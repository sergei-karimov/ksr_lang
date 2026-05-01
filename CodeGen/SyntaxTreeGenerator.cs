using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KSR.AST;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace KSR.CodeGen;

/// <summary>
/// Walks a KSR AST and builds a Roslyn <see cref="SyntaxTree"/> directly via
/// <see cref="SyntaxFactory"/>, skipping the intermediate C# text representation.
///
/// This eliminates the text generation + ParseText round-trip that
/// <see cref="CodeGenerator"/> requires, reducing compilation overhead.
///
/// Semantics mirror CodeGenerator exactly:
///   • struct  → C# positional record
///   • Extension functions → public static extension methods in KsrProgram
///   • 'this' inside extension bodies → 'self' (the receiver parameter)
///   • #line source-mapping is intentionally omitted (use --debug for C# inspection)
/// </summary>
public class SyntaxTreeGenerator
{
    private readonly HashSet<string> _structs     = new();
    private readonly HashSet<string> _interfaces  = new();
    private readonly HashSet<string> _sealedTypes = new();
    private readonly Dictionary<string, List<ImplBlock>> _implsByType = new();

    private bool             _inRecordMethod    = false;
    private HashSet<string>  _currentTypeParams = new();
    private readonly AsyncReturnKind _globalAsyncReturn;
    private bool             _inAsyncFunction   = false;
    private AsyncReturnKind  _currentAsyncReturn = AsyncReturnKind.Task;

    public SyntaxTreeGenerator(AsyncReturnKind globalAsyncReturn = AsyncReturnKind.Task)
    {
        _globalAsyncReturn = globalAsyncReturn;
    }

    // ── public entry ─────────────────────────────────────────────────────────

    public SyntaxTree Generate(ProgramNode program)
    {
        // First pass: collect struct / sealed / interface names; group impl blocks
        foreach (var d in program.Declarations)
        {
            if (d is StructDecl dc) _structs.Add(dc.Name);
            if (d is SealedDecl sd)
            {
                _sealedTypes.Add(sd.Name);
                foreach (var v in sd.Variants) _structs.Add(v.Name);
            }
            if (d is InterfaceDecl ifd) _interfaces.Add(ifd.Name);
            if (d is ImplBlock ib)
            {
                if (!_implsByType.TryGetValue(ib.TypeName, out var list))
                    _implsByType[ib.TypeName] = list = [];
                list.Add(ib);
            }
        }

        // Using directives
        var usings = new List<UsingDirectiveSyntax>
        {
            UsingDirective(ParseName("System")),
            UsingDirective(ParseName("System.Collections.Generic")),
            UsingDirective(ParseName("System.Linq")),
        };
        foreach (var d in program.Declarations)
            if (d is UseDecl ud)
                usings.Add(UsingDirective(ParseName(MapUseNamespace(ud.Namespace))));

        // Attach #nullable enable as leading trivia on the first using
        usings[0] = usings[0].WithLeadingTrivia(TriviaList(
            Trivia(NullableDirectiveTrivia(Token(SyntaxKind.EnableKeyword), true)),
            EndOfLine(Environment.NewLine)));

        // Top-level type declarations
        var members = new List<MemberDeclarationSyntax>();
        foreach (var d in program.Declarations)
            if (d is InterfaceDecl id) members.Add(BuildInterface(id));
        foreach (var d in program.Declarations)
            if (d is SealedDecl sd)    members.AddRange(BuildSealed(sd));
        foreach (var d in program.Declarations)
            if (d is StructDecl dc)    members.Add(BuildStruct(dc));

        // static class KsrProgram { all functions + extension methods }
        var programMembers = new List<MemberDeclarationSyntax>();
        foreach (var d in program.Declarations)
        {
            if (d is FunctionDecl fd)   programMembers.Add(BuildFunction(fd));
            if (d is ExtFunctionDecl efd) programMembers.Add(BuildExtFunction(efd));
        }
        members.Add(
            ClassDeclaration("KsrProgram")
                .WithModifiers(TokenList(Token(SyntaxKind.StaticKeyword)))
                .WithMembers(List<MemberDeclarationSyntax>(programMembers)));

        return CompilationUnit()
            .WithUsings(List(usings))
            .WithMembers(List(members))
            .SyntaxTree;
    }

    // ── declarations ─────────────────────────────────────────────────────────

    private InterfaceDeclarationSyntax BuildInterface(InterfaceDecl id)
    {
        var prevTypeParams = _currentTypeParams;
        _currentTypeParams = id.TypeParams.Count > 0 ? new HashSet<string>(id.TypeParams) : new();

        var decl = InterfaceDeclaration("I" + id.Name);

        if (id.TypeParams.Count > 0)
            decl = decl.WithTypeParameterList(
                TypeParameterList(SeparatedList(id.TypeParams.Select(tp => TypeParameter(tp)))));

        if (id.Constraints.Count > 0)
            decl = decl.WithConstraintClauses(List(id.Constraints.Select(c =>
                TypeParameterConstraintClause(IdentifierName(c.TypeParam))
                    .WithConstraints(SeparatedList<TypeParameterConstraintSyntax>(
                        c.Bounds.Select(b => TypeConstraint(MapTypeSyntax(new TypeRef(b, false)))))))));

        var methods = id.Methods.Select(m =>
        {
            TypeSyntax ret = m.IsAsync
                ? BuildAsyncReturnTypeSyntax(m.ReturnType, EffectiveAsyncReturn(m.AsyncReturn))
                : m.ReturnType is null
                    ? PredefinedType(Token(SyntaxKind.VoidKeyword))
                    : MapTypeSyntax(m.ReturnType);
            return (MemberDeclarationSyntax)MethodDeclaration(ret, Pascal(m.Name))
                .WithParameterList(ParameterList(SeparatedList(m.Parameters.Select(p => BuildParam(p)))))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
        });

        decl = decl.WithMembers(List(methods));
        _currentTypeParams = prevTypeParams;
        return decl;
    }

    private IEnumerable<MemberDeclarationSyntax> BuildSealed(SealedDecl sd)
    {
        // abstract record Shape;
        yield return RecordDeclaration(Token(SyntaxKind.RecordKeyword), sd.Name)
            .WithModifiers(TokenList(Token(SyntaxKind.AbstractKeyword)))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

        foreach (var v in sd.Variants)
        {
            // Base list always includes the sealed parent
            var baseTypes = new List<BaseTypeSyntax> { SimpleBaseType(IdentifierName(sd.Name)) };

            RecordDeclarationSyntax variantDecl;
            if (_implsByType.TryGetValue(v.Name, out var impls))
            {
                baseTypes.AddRange(impls.Select(b => (BaseTypeSyntax)SimpleBaseType(BuildImplTypeSyntax(b))));

                var prev = _inRecordMethod;
                _inRecordMethod = true;
                var methodMembers = impls.SelectMany(b => b.Methods)
                                        .Select(m => (MemberDeclarationSyntax)BuildRecordMethod(m))
                                        .ToList();
                _inRecordMethod = prev;

                variantDecl = RecordDeclaration(Token(SyntaxKind.RecordKeyword), v.Name)
                    .WithParameterList(BuildRecordParams(v.Properties))
                    .WithBaseList(BaseList(SeparatedList(baseTypes)))
                    .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                    .WithMembers(List(methodMembers))
                    .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));
            }
            else
            {
                variantDecl = RecordDeclaration(Token(SyntaxKind.RecordKeyword), v.Name)
                    .WithParameterList(BuildRecordParams(v.Properties))
                    .WithBaseList(BaseList(SeparatedList(baseTypes)))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
            }
            yield return variantDecl;
        }
    }

    private MemberDeclarationSyntax BuildStruct(StructDecl dc)
    {
        if (_implsByType.TryGetValue(dc.Name, out var impls))
        {
            var baseTypes = impls.Select(b => (BaseTypeSyntax)SimpleBaseType(BuildImplTypeSyntax(b)));

            var prev = _inRecordMethod;
            _inRecordMethod = true;
            var methodMembers = impls.SelectMany(b => b.Methods)
                                     .Select(m => (MemberDeclarationSyntax)BuildRecordMethod(m))
                                     .ToList();
            _inRecordMethod = prev;

            return RecordDeclaration(Token(SyntaxKind.RecordKeyword), dc.Name)
                .WithParameterList(BuildRecordParams(dc.Properties))
                .WithBaseList(BaseList(SeparatedList(baseTypes)))
                .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                .WithMembers(List(methodMembers))
                .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));
        }

        return RecordDeclaration(Token(SyntaxKind.RecordKeyword), dc.Name)
            .WithParameterList(BuildRecordParams(dc.Properties))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }

    /// <summary>Positional parameter list for record declarations; omits list when empty.</summary>
    private ParameterListSyntax BuildRecordParams(List<Parameter> props) =>
        ParameterList(props.Count > 0
            ? SeparatedList(props.Select(p => BuildParam(p, pascalName: true)))
            : default);

    private TypeSyntax BuildImplTypeSyntax(ImplBlock b)
    {
        if (b.InterfaceTypeArgs.Count > 0)
            return GenericName("I" + b.InterfaceName)
                .WithTypeArgumentList(TypeArgumentList(SeparatedList(
                    b.InterfaceTypeArgs.Select(a => MapTypeSyntax(new TypeRef(a, false))))));
        return IdentifierName("I" + b.InterfaceName);
    }

    private MethodDeclarationSyntax BuildRecordMethod(FunctionDecl fd)
    {
        var (prevAsync, prevKind, prevTypeParams) = SaveAsyncState();
        _inAsyncFunction    = fd.IsAsync;
        _currentAsyncReturn = EffectiveAsyncReturn(fd.AsyncReturn);
        _currentTypeParams  = fd.TypeParams.Count > 0 ? new HashSet<string>(fd.TypeParams) : new();

        var ret = GetReturnTypeSyntax(fd.IsAsync, fd.ReturnType, _currentAsyncReturn);
        var mods = new List<SyntaxToken> { Token(SyntaxKind.PublicKeyword) };
        if (fd.IsAsync) mods.Add(Token(SyntaxKind.AsyncKeyword));

        var method = MethodDeclaration(ret, Pascal(fd.Name))
            .WithModifiers(TokenList(mods))
            .WithParameterList(ParameterList(SeparatedList(fd.Parameters.Select(p => BuildParam(p)))))
            .WithBody(BuildBlock(fd.Body));

        if (fd.TypeParams.Count > 0)
            method = method.WithTypeParameterList(
                TypeParameterList(SeparatedList(fd.TypeParams.Select(tp => TypeParameter(tp)))));

        RestoreAsyncState(prevAsync, prevKind, prevTypeParams);
        return method;
    }

    private MethodDeclarationSyntax BuildFunction(FunctionDecl fd)
    {
        var (prevAsync, prevKind, prevTypeParams) = SaveAsyncState();
        _inAsyncFunction    = fd.IsAsync;
        _currentAsyncReturn = EffectiveAsyncReturn(fd.AsyncReturn);
        _currentTypeParams  = fd.TypeParams.Count > 0 ? new HashSet<string>(fd.TypeParams) : new();

        var ret        = GetReturnTypeSyntax(fd.IsAsync, fd.ReturnType, _currentAsyncReturn);
        var methodName = fd.Name == "main" ? "Main" : fd.Name;
        var mods       = new List<SyntaxToken> { Token(SyntaxKind.StaticKeyword) };
        if (fd.IsAsync) mods.Add(Token(SyntaxKind.AsyncKeyword));

        var method = MethodDeclaration(ret, methodName)
            .WithModifiers(TokenList(mods))
            .WithParameterList(ParameterList(SeparatedList(fd.Parameters.Select(p => BuildParam(p)))))
            .WithBody(BuildBlock(fd.Body));

        if (fd.TypeParams.Count > 0)
            method = method.WithTypeParameterList(
                TypeParameterList(SeparatedList(fd.TypeParams.Select(tp => TypeParameter(tp)))));

        RestoreAsyncState(prevAsync, prevKind, prevTypeParams);
        return method;
    }

    private MethodDeclarationSyntax BuildExtFunction(ExtFunctionDecl efd)
    {
        var (prevAsync, prevKind, prevTypeParams) = SaveAsyncState();
        _inAsyncFunction    = efd.IsAsync;
        _currentAsyncReturn = EffectiveAsyncReturn(efd.AsyncReturn);
        _currentTypeParams  = efd.TypeParams.Count > 0 ? new HashSet<string>(efd.TypeParams) : new();

        var ret          = GetReturnTypeSyntax(efd.IsAsync, efd.ReturnType, _currentAsyncReturn);
        var receiverType = MapTypeSyntax(new TypeRef(efd.ReceiverType, false));
        var receiverParam = Parameter(Identifier("self"))
            .WithType(receiverType)
            .WithModifiers(TokenList(Token(SyntaxKind.ThisKeyword)));
        var allParams = new[] { receiverParam }.Concat(efd.Parameters.Select(p => BuildParam(p)));

        var mods = new List<SyntaxToken>
            { Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword) };
        if (efd.IsAsync) mods.Add(Token(SyntaxKind.AsyncKeyword));

        var method = MethodDeclaration(ret, Pascal(efd.MethodName))
            .WithModifiers(TokenList(mods))
            .WithParameterList(ParameterList(SeparatedList(allParams)))
            .WithBody(BuildBlock(efd.Body));

        if (efd.TypeParams.Count > 0)
            method = method.WithTypeParameterList(
                TypeParameterList(SeparatedList(efd.TypeParams.Select(tp => TypeParameter(tp)))));

        RestoreAsyncState(prevAsync, prevKind, prevTypeParams);
        return method;
    }

    // ── block & statements ────────────────────────────────────────────────────

    private BlockSyntax BuildBlock(Block block) =>
        Block(block.Statements.Select(BuildStmt));

    private StatementSyntax BuildStmt(Stmt stmt) => stmt switch
    {
        ValDecl vd => BuildLocalDecl(vd.Name, vd.Type, BuildExprWithHint(vd.Value, vd.Type)),
        VarDecl vd => BuildLocalDecl(vd.Name, vd.Type, BuildExprWithHint(vd.Value, vd.Type)),

        AssignStmt ass => ExpressionStatement(
            AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                IdentifierName(ass.Name), BuildExpr(ass.Value))),

        CompoundAssignStmt cas => ExpressionStatement(
            AssignmentExpression(MapCompoundOp(cas.Op),
                IdentifierName(cas.Name), BuildExpr(cas.Value))),

        IndexAssignStmt ias => ExpressionStatement(
            AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                ElementAccessExpression(IdentifierName(ias.Name))
                    .WithArgumentList(BracketedArgumentList(
                        SingletonSeparatedList(Argument(BuildExpr(ias.Index))))),
                BuildExpr(ias.Value))),

        ExprStmt { Expression: WhenExpr we } => BuildWhenStmt(we),
        ExprStmt es => ExpressionStatement(BuildExpr(es.Expression)),

        ReturnStmt rs => rs.Value is null
            ? ReturnStatement()
            : ReturnStatement(BuildExpr(rs.Value)),

        IfStmt ifs => BuildIf(ifs),
        WhileStmt ws => WhileStatement(BuildExpr(ws.Condition), BuildBlock(ws.Body)),
        ForInStmt fis => BuildForIn(fis),

        _ => throw new InvalidOperationException($"Unknown statement: {stmt.GetType().Name}")
    };

    private LocalDeclarationStatementSyntax BuildLocalDecl(
        string name, TypeRef? typeHint, ExpressionSyntax init)
    {
        TypeSyntax typeSyntax = typeHint is null ? IdentifierName("var") : MapTypeSyntax(typeHint);
        return LocalDeclarationStatement(
            VariableDeclaration(typeSyntax)
                .WithVariables(SingletonSeparatedList(
                    VariableDeclarator(Identifier(name))
                        .WithInitializer(EqualsValueClause(init)))));
    }

    private IfStatementSyntax BuildIf(IfStmt ifs)
    {
        var s = IfStatement(BuildExpr(ifs.Condition), BuildBlock(ifs.Then));
        return ifs.Else is null ? s : s.WithElse(ElseClause(BuildBlock(ifs.Else)));
    }

    private StatementSyntax BuildForIn(ForInStmt fis)
    {
        if (fis.Iterable is RangeExpr re)
        {
            var op = re.Inclusive
                ? SyntaxKind.LessThanOrEqualExpression
                : SyntaxKind.LessThanExpression;
            return ForStatement(
                VariableDeclaration(IdentifierName("var"))
                    .WithVariables(SingletonSeparatedList(
                        VariableDeclarator(Identifier(fis.VarName))
                            .WithInitializer(EqualsValueClause(BuildExpr(re.Start))))),
                default,
                BinaryExpression(op, IdentifierName(fis.VarName), BuildExpr(re.End)),
                SingletonSeparatedList<ExpressionSyntax>(
                    PostfixUnaryExpression(SyntaxKind.PostIncrementExpression,
                        IdentifierName(fis.VarName))),
                BuildBlock(fis.Body));
        }

        return ForEachStatement(
            IdentifierName("var"),
            Identifier(fis.VarName),
            BuildExpr(fis.Iterable),
            BuildBlock(fis.Body));
    }

    /// <summary>
    /// Emits <c>when</c> as a C# statement (if / else-if / else chain).
    /// Arms are processed in reverse so we can nest ElseClauses.
    /// </summary>
    private StatementSyntax BuildWhenStmt(WhenExpr we)
    {
        var subjectExpr = we.Subject is not null ? BuildExpr(we.Subject) : null;
        StatementSyntax? result = null;

        foreach (var arm in Enumerable.Reverse(we.Arms))
        {
            var body = (StatementSyntax)Block(ExpressionStatement(BuildExpr(arm.Body)));

            if (arm.Pattern is null) // else arm
            {
                result = body;
            }
            else
            {
                ExpressionSyntax cond;
                if (arm.Pattern is IsPatternExpr ip)
                {
                    cond = IsPatternExpression(subjectExpr!, BuildIsPattern(ip));
                }
                else
                {
                    cond = subjectExpr is not null
                        ? BinaryExpression(SyntaxKind.EqualsExpression,
                            subjectExpr, BuildExpr(arm.Pattern))
                        : BuildExpr(arm.Pattern);
                }

                var ifStmt = IfStatement(cond, body);
                if (result is not null) ifStmt = ifStmt.WithElse(ElseClause(result));
                result = ifStmt;
            }
        }

        return result ?? Block();
    }

    // ── expressions ──────────────────────────────────────────────────────────

    private ExpressionSyntax BuildExpr(Expr expr) => expr switch
    {
        IntLiteral il    => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(il.Value)),
        DoubleLiteral dl => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(dl.Value)),
        BoolLiteral bl   => bl.Value
                                ? LiteralExpression(SyntaxKind.TrueLiteralExpression)
                                : LiteralExpression(SyntaxKind.FalseLiteralExpression),
        NullLiteral      => LiteralExpression(SyntaxKind.NullLiteralExpression),
        StringLiteral sl => BuildStringLiteral(sl),

        ThisExpr => _inRecordMethod
            ? (ExpressionSyntax)ThisExpression()
            : IdentifierName("self"),

        IdentifierExpr id => IdentifierName(id.Name),

        IndexExpr ie => ElementAccessExpression(BuildExpr(ie.Target))
            .WithArgumentList(BracketedArgumentList(
                SingletonSeparatedList(Argument(BuildExpr(ie.Index))))),

        NewArrayExpr na => ArrayCreationExpression(
            ArrayType(MapTypeSyntax(na.ElementType))
                .WithRankSpecifiers(SingletonList(
                    ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(BuildExpr(na.Size)))))),

        NewObjectExpr no => ObjectCreationExpression(IdentifierName(no.TypeName))
            .WithArgumentList(ArgumentList(
                SeparatedList(no.Arguments.Select(a => Argument(BuildExpr(a)))))),

        LambdaExpr le => BuildLambda(le),

        BinaryExpr be => ParenthesizedExpression(
            BinaryExpression(MapBinaryOp(be.Op), BuildExpr(be.Left), BuildExpr(be.Right))),

        UnaryExpr ue => ParenthesizedExpression(
            PrefixUnaryExpression(MapUnaryOp(ue.Op), BuildExpr(ue.Operand))),

        MemberAccessExpr ma => MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            BuildExpr(ma.Target), IdentifierName(Pascal(ma.Member))),

        SafeCallExpr sc => ConditionalAccessExpression(
            BuildExpr(sc.Target),
            MemberBindingExpression(IdentifierName(Pascal(sc.Member)))),

        ElvisExpr ev => ParenthesizedExpression(
            BinaryExpression(SyntaxKind.CoalesceExpression,
                BuildExpr(ev.Left), BuildExpr(ev.Right))),

        RangeExpr re      => BuildRangeAsValue(re),
        StringTemplateExpr ste => BuildStringTemplate(ste),
        CallExpr ce       => BuildCall(ce),
        ListLiteralExpr ll => BuildListLiteral(ll),
        MapLiteralExpr ml  => BuildMapLiteral(ml),
        WhenExpr we        => BuildWhenExpr(we),

        AwaitExpr ae => ParenthesizedExpression(AwaitExpression(BuildExpr(ae.Operand))),

        NamedArgExpr na => BuildExpr(na.Value), // handled as named arg in BuildCall

        IsPatternExpr => throw new InvalidOperationException(
            "IsPatternExpr must be handled in pattern context, not as a standalone expression"),

        _ => throw new InvalidOperationException($"Unknown expression: {expr.GetType().Name}")
    };

    private ExpressionSyntax BuildStringLiteral(StringLiteral sl)
    {
        if (sl.IsRaw)
        {
            var escaped = sl.Value.Replace("\"", "\"\"");
            return LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                Token(default, SyntaxKind.StringLiteralToken,
                    $"@\"{escaped}\"", sl.Value, default));
        }
        return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(sl.Value));
    }

    private ExpressionSyntax BuildLambda(LambdaExpr le)
    {
        var body = BuildExpr(le.Body);
        if (le.Params.Count == 0)
            return ParenthesizedLambdaExpression().WithBody(body);
        if (le.Params.Count == 1)
            return SimpleLambdaExpression(Parameter(Identifier(le.Params[0]))).WithBody(body);
        return ParenthesizedLambdaExpression()
            .WithParameterList(ParameterList(SeparatedList(le.Params.Select(p => Parameter(Identifier(p))))))
            .WithBody(body);
    }

    private ExpressionSyntax BuildCall(CallExpr ce)
    {
        // SafeCallExpr as callee: target?.Method(args)
        if (ce.Callee is SafeCallExpr sc)
        {
            return ConditionalAccessExpression(
                BuildExpr(sc.Target),
                InvocationExpression(MemberBindingExpression(IdentifierName(Pascal(sc.Member))))
                    .WithArgumentList(ArgumentList(
                        SeparatedList(ce.Arguments.Select(BuildCallArg)))));
        }

        // Built-in: println → Console.WriteLine
        if (ce.Callee is IdentifierExpr { Name: "println" })
        {
            return InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("Console"), IdentifierName("WriteLine")))
                .WithArgumentList(ArgumentList(
                    SeparatedList(ce.Arguments.Select(BuildCallArg))));
        }

        // Constructor call for known structs: Foo(…) → new Foo(…)
        if (ce.Callee is IdentifierExpr id && _structs.Contains(id.Name))
        {
            return ObjectCreationExpression(IdentifierName(id.Name))
                .WithArgumentList(ArgumentList(
                    SeparatedList(ce.Arguments.Select(BuildCallArg))));
        }

        // Regular invocation
        return InvocationExpression(BuildExpr(ce.Callee))
            .WithArgumentList(ArgumentList(
                SeparatedList(ce.Arguments.Select(BuildCallArg))));
    }

    private ArgumentSyntax BuildCallArg(Expr expr)
    {
        if (expr is NamedArgExpr na)
            return Argument(BuildExpr(na.Value))
                .WithNameColon(NameColon(IdentifierName(na.Name)));
        return Argument(BuildExpr(expr));
    }

    private ExpressionSyntax BuildStringTemplate(StringTemplateExpr ste)
    {
        var startToken = ste.IsRaw
            ? Token(SyntaxKind.InterpolatedVerbatimStringStartToken)
            : Token(SyntaxKind.InterpolatedStringStartToken);

        var contents = ste.Parts.Select<StringPart, InterpolatedStringContentSyntax>(part =>
        {
            if (part is LiteralPart lp)
            {
                var srcText = ste.IsRaw
                    ? lp.Text.Replace("\"", "\"\"").Replace("{", "{{").Replace("}", "}}")
                    : lp.Text.Replace("{", "{{").Replace("}", "}}");
                return InterpolatedStringText(
                    Token(default, SyntaxKind.InterpolatedStringTextToken,
                        srcText, lp.Text, default));
            }
            if (part is ExprPart ep)
                return Interpolation(BuildExpr(ep.Expression));
            throw new InvalidOperationException($"Unknown StringPart: {part.GetType().Name}");
        });

        return InterpolatedStringExpression(startToken)
            .WithContents(List(contents))
            .WithStringEndToken(Token(SyntaxKind.InterpolatedStringEndToken));
    }

    private ExpressionSyntax BuildRangeAsValue(RangeExpr re)
    {
        // Enumerable.Range(start, end - start + 1)  or  Enumerable.Range(start, end - start)
        ExpressionSyntax count = re.Inclusive
            ? BinaryExpression(SyntaxKind.AddExpression,
                BinaryExpression(SyntaxKind.SubtractExpression, BuildExpr(re.End), BuildExpr(re.Start)),
                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(1)))
            : BinaryExpression(SyntaxKind.SubtractExpression, BuildExpr(re.End), BuildExpr(re.Start));

        return InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("Enumerable"), IdentifierName("Range")))
            .WithArgumentList(ArgumentList(SeparatedList(new[]
            {
                Argument(BuildExpr(re.Start)),
                Argument(count),
            })));
    }

    private ExpressionSyntax BuildListLiteral(ListLiteralExpr ll)
    {
        if (ll.Elements.Count == 0)
            return InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("Array"),
                        GenericName("Empty").WithTypeArgumentList(TypeArgumentList(
                            SingletonSeparatedList<TypeSyntax>(IdentifierName("object"))))))
                .WithArgumentList(ArgumentList());

        return ImplicitArrayCreationExpression(
            InitializerExpression(SyntaxKind.ArrayInitializerExpression,
                SeparatedList(ll.Elements.Select(BuildExpr))));
    }

    private ExpressionSyntax BuildMapLiteral(MapLiteralExpr ml)
    {
        var keyType = (TypeSyntax)(InferTypeSyntax(ml.Entries.Count > 0 ? ml.Entries[0].Key : null) ?? IdentifierName("object"));
        var valType = (TypeSyntax)(InferTypeSyntax(ml.Entries.Count > 0 ? ml.Entries[0].Value : null) ?? IdentifierName("object"));

        var dictType = GenericName("Dictionary")
            .WithTypeArgumentList(TypeArgumentList(SeparatedList<TypeSyntax>(new[] { keyType, valType })));

        if (ml.Entries.Count == 0)
            return ObjectCreationExpression(dictType).WithArgumentList(ArgumentList());

        var entries = ml.Entries.Select(e =>
            (ExpressionSyntax)AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                ImplicitElementAccess()
                    .WithArgumentList(BracketedArgumentList(
                        SingletonSeparatedList(Argument(BuildExpr(e.Key))))),
                BuildExpr(e.Value)));

        return ObjectCreationExpression(dictType)
            .WithArgumentList(ArgumentList())
            .WithInitializer(InitializerExpression(
                SyntaxKind.ObjectInitializerExpression,
                SeparatedList(entries)));
    }

    private ExpressionSyntax BuildWhenExpr(WhenExpr we)
    {
        if (we.Subject is not null)
        {
            var arms = we.Arms.Select(arm =>
            {
                PatternSyntax pattern = arm.Pattern switch
                {
                    null                => DiscardPattern(),
                    IsPatternExpr ip    => BuildSwitchPattern(ip),
                    _                   => ConstantPattern(BuildExpr(arm.Pattern))
                };
                return SwitchExpressionArm(pattern, BuildExpr(arm.Body));
            });

            return ParenthesizedExpression(
                SwitchExpression(BuildExpr(we.Subject))
                    .WithArms(SeparatedList(arms)));
        }

        // Subject-less → ternary chain
        return BuildWhenTernary(we.Arms, 0);
    }

    private ExpressionSyntax BuildWhenTernary(List<WhenArm> arms, int i)
    {
        if (i >= arms.Count)
            return ThrowExpression(
                ObjectCreationExpression(IdentifierName("InvalidOperationException"))
                    .WithArgumentList(ArgumentList(SingletonSeparatedList(
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                            Literal("when: no arm matched")))))));

        var arm = arms[i];
        return arm.Pattern is null
            ? BuildExpr(arm.Body)
            : ConditionalExpression(BuildExpr(arm.Pattern),
                BuildExpr(arm.Body),
                BuildWhenTernary(arms, i + 1));
    }

    /// <summary>
    /// Like <see cref="BuildExpr"/> but uses type context so collection literals
    /// get the correct concrete or interface type (MutableList vs IReadOnlyList, etc.).
    /// </summary>
    private ExpressionSyntax BuildExprWithHint(Expr expr, TypeRef? hint)
    {
        if (hint is not null)
        {
            var outerName = hint.Name.Contains('<')
                ? hint.Name[..hint.Name.IndexOf('<')]
                : hint.Name;

            if (expr is ListLiteralExpr ll)
            {
                if (ll.Elements.Count == 0)
                {
                    if (outerName == "MutableList")
                        return ObjectCreationExpression(MapTypeForNewSyntax(hint))
                            .WithArgumentList(ArgumentList());

                    if (outerName == "List")
                    {
                        var argStr = hint.Name[(hint.Name.IndexOf('<') + 1)..^1];
                        var csArg  = MapTypeSyntax(new TypeRef(argStr.Trim(), false));
                        return InvocationExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName("Array"),
                                    GenericName("Empty").WithTypeArgumentList(
                                        TypeArgumentList(SingletonSeparatedList(csArg)))))
                            .WithArgumentList(ArgumentList());
                    }

                    if (outerName is "Map" or "MutableMap")
                        return ObjectCreationExpression(MapTypeForNewSyntax(hint))
                            .WithArgumentList(ArgumentList());
                }
                else if (outerName == "MutableList")
                {
                    return ObjectCreationExpression(MapTypeForNewSyntax(hint))
                        .WithArgumentList(ArgumentList())
                        .WithInitializer(InitializerExpression(
                            SyntaxKind.CollectionInitializerExpression,
                            SeparatedList(ll.Elements.Select(BuildExpr))));
                }
            }

            if (expr is MapLiteralExpr { Entries.Count: 0 })
                return ObjectCreationExpression(MapTypeForNewSyntax(hint))
                    .WithArgumentList(ArgumentList());
        }

        return BuildExpr(expr);
    }

    // ── pattern helpers ───────────────────────────────────────────────────────

    /// <summary>Pattern for a switch expression arm: <c>Circle c</c> or <c>Circle</c>.</summary>
    private PatternSyntax BuildSwitchPattern(IsPatternExpr ip) =>
        ip.Binding is not null
            ? DeclarationPattern(IdentifierName(ip.TypeName),
                SingleVariableDesignation(Identifier(ip.Binding)))
            : TypePattern(IdentifierName(ip.TypeName));

    /// <summary>Pattern for an <c>is</c> check inside a <c>when</c> condition: <c>s is Circle c</c>.</summary>
    private PatternSyntax BuildIsPattern(IsPatternExpr ip) => BuildSwitchPattern(ip);

    // ── type mapping ──────────────────────────────────────────────────────────

    private TypeSyntax MapTypeSyntax(TypeRef t)
    {
        // Array types: Bool[] → bool[]
        if (t.Name.EndsWith("[]"))
        {
            var elem    = MapTypeSyntax(new TypeRef(t.Name[..^2], false));
            var arrType = ArrayType(elem).WithRankSpecifiers(SingletonList(
                ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(
                    OmittedArraySizeExpression()))));
            return t.Nullable ? NullableType(arrType) : (TypeSyntax)arrType;
        }

        // Generic types: List<Int> → IReadOnlyList<int>
        var angleIdx = t.Name.IndexOf('<');
        if (angleIdx >= 0)
        {
            var outer   = t.Name[..angleIdx];
            var argsStr = t.Name[(angleIdx + 1)..^1];
            var args    = SplitTypeArgs(argsStr).Select(a => MapTypeSyntax(new TypeRef(a.Trim(), false)));
            var csOuter = outer switch
            {
                "List"        => "IReadOnlyList",
                "MutableList" => "List",
                "Map"         => "IReadOnlyDictionary",
                "MutableMap"  => "Dictionary",
                _ => _currentTypeParams.Contains(outer) ? outer
                   : _interfaces.Contains(outer)        ? "I" + outer
                   : outer
            };
            TypeSyntax genType = GenericName(csOuter)
                .WithTypeArgumentList(TypeArgumentList(SeparatedList(args)));
            return t.Nullable ? NullableType(genType) : genType;
        }

        TypeSyntax baseTy = t.Name switch
        {
            "Int"    => PredefinedType(Token(SyntaxKind.IntKeyword)),
            "String" => PredefinedType(Token(SyntaxKind.StringKeyword)),
            "Bool"   => PredefinedType(Token(SyntaxKind.BoolKeyword)),
            "Double" => PredefinedType(Token(SyntaxKind.DoubleKeyword)),
            "Float"  => PredefinedType(Token(SyntaxKind.FloatKeyword)),
            "Long"   => PredefinedType(Token(SyntaxKind.LongKeyword)),
            "Unit"   => PredefinedType(Token(SyntaxKind.VoidKeyword)),
            _ => _currentTypeParams.Contains(t.Name) ? IdentifierName(t.Name)
               : _interfaces.Contains(t.Name)        ? (TypeSyntax)IdentifierName("I" + t.Name)
               : IdentifierName(t.Name)
        };
        return t.Nullable ? NullableType(baseTy) : baseTy;
    }

    /// <summary>
    /// Maps to the concrete (constructible) C# type used in <c>new T()</c>.
    /// IReadOnlyList/IReadOnlyDictionary are interfaces; use List/Dictionary for construction.
    /// </summary>
    private TypeSyntax MapTypeForNewSyntax(TypeRef t)
    {
        var angleIdx = t.Name.IndexOf('<');
        if (angleIdx >= 0)
        {
            var outer   = t.Name[..angleIdx];
            var argsStr = t.Name[(angleIdx + 1)..^1];
            var args    = SplitTypeArgs(argsStr).Select(a => MapTypeSyntax(new TypeRef(a.Trim(), false)));
            var csOuter = outer switch
            {
                "List" or "MutableList" => "List",
                "Map"  or "MutableMap"  => "Dictionary",
                _                       => outer
            };
            return GenericName(csOuter)
                .WithTypeArgumentList(TypeArgumentList(SeparatedList(args)));
        }
        return MapTypeSyntax(t);
    }

    // ── async helpers ─────────────────────────────────────────────────────────

    private AsyncReturnKind EffectiveAsyncReturn(AsyncReturnKind perFunction) =>
        perFunction == AsyncReturnKind.ValueTask || _globalAsyncReturn == AsyncReturnKind.ValueTask
            ? AsyncReturnKind.ValueTask
            : AsyncReturnKind.Task;

    private TypeSyntax BuildAsyncReturnTypeSyntax(TypeRef? innerType, AsyncReturnKind kind)
    {
        var wrapper = kind == AsyncReturnKind.ValueTask ? "ValueTask" : "Task";
        if (innerType is null) return IdentifierName(wrapper);
        return GenericName(wrapper)
            .WithTypeArgumentList(TypeArgumentList(
                SingletonSeparatedList(MapTypeSyntax(innerType))));
    }

    private TypeSyntax GetReturnTypeSyntax(bool isAsync, TypeRef? returnType, AsyncReturnKind kind) =>
        isAsync
            ? BuildAsyncReturnTypeSyntax(returnType, kind)
            : returnType is null
                ? PredefinedType(Token(SyntaxKind.VoidKeyword))
                : MapTypeSyntax(returnType);

    // ── state save / restore helpers ─────────────────────────────────────────

    private (bool, AsyncReturnKind, HashSet<string>) SaveAsyncState() =>
        (_inAsyncFunction, _currentAsyncReturn, _currentTypeParams);

    private void RestoreAsyncState(bool prevAsync, AsyncReturnKind prevKind, HashSet<string> prevTypeParams)
    {
        _inAsyncFunction    = prevAsync;
        _currentAsyncReturn = prevKind;
        _currentTypeParams  = prevTypeParams;
    }

    // ── misc helpers ──────────────────────────────────────────────────────────

    private ParameterSyntax BuildParam(Parameter p, bool pascalName = false)
    {
        var name  = pascalName ? Pascal(p.Name) : p.Name;
        var param = Parameter(Identifier(name)).WithType(MapTypeSyntax(p.Type));
        if (p.Default is not null)
            param = param.WithDefault(EqualsValueClause(BuildExpr(p.Default)));
        return param;
    }

    private static SyntaxKind MapBinaryOp(string op) => op switch
    {
        "+"  => SyntaxKind.AddExpression,
        "-"  => SyntaxKind.SubtractExpression,
        "*"  => SyntaxKind.MultiplyExpression,
        "/"  => SyntaxKind.DivideExpression,
        "%"  => SyntaxKind.ModuloExpression,
        "==" => SyntaxKind.EqualsExpression,
        "!=" => SyntaxKind.NotEqualsExpression,
        "<"  => SyntaxKind.LessThanExpression,
        ">"  => SyntaxKind.GreaterThanExpression,
        "<=" => SyntaxKind.LessThanOrEqualExpression,
        ">=" => SyntaxKind.GreaterThanOrEqualExpression,
        "&&" => SyntaxKind.LogicalAndExpression,
        "||" => SyntaxKind.LogicalOrExpression,
        _    => throw new NotSupportedException($"Unsupported binary operator: {op}")
    };

    private static SyntaxKind MapUnaryOp(string op) => op switch
    {
        "!" => SyntaxKind.LogicalNotExpression,
        "-" => SyntaxKind.UnaryMinusExpression,
        _   => throw new NotSupportedException($"Unsupported unary operator: {op}")
    };

    private static SyntaxKind MapCompoundOp(string op) => op switch
    {
        "+=" => SyntaxKind.AddAssignmentExpression,
        "-=" => SyntaxKind.SubtractAssignmentExpression,
        "*=" => SyntaxKind.MultiplyAssignmentExpression,
        "/=" => SyntaxKind.DivideAssignmentExpression,
        "%=" => SyntaxKind.ModuloAssignmentExpression,
        _    => throw new NotSupportedException($"Unsupported compound operator: {op}")
    };

    private static TypeSyntax? InferTypeSyntax(Expr? e) => e switch
    {
        IntLiteral           => PredefinedType(Token(SyntaxKind.IntKeyword)),
        DoubleLiteral        => PredefinedType(Token(SyntaxKind.DoubleKeyword)),
        BoolLiteral          => PredefinedType(Token(SyntaxKind.BoolKeyword)),
        StringLiteral        => PredefinedType(Token(SyntaxKind.StringKeyword)),
        StringTemplateExpr   => PredefinedType(Token(SyntaxKind.StringKeyword)),
        _                    => null
    };

    private static IEnumerable<string> SplitTypeArgs(string args)
    {
        var result = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if      (args[i] == '<') depth++;
            else if (args[i] == '>') depth--;
            else if (args[i] == ',' && depth == 0) { result.Add(args[start..i]); start = i + 1; }
        }
        result.Add(args[start..]);
        return result;
    }

    private static string MapUseNamespace(string ns) => ns switch
    {
        "ksr.io"          => "KSR.Io",
        "ksr.text"        => "KSR.Text",
        "ksr.collections" => "KSR.Collections",
        _                 => ns,
    };

    private static string Pascal(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
