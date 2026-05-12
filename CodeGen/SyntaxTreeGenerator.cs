using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KSR.AST;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace KSR.CodeGen;

/// <summary>
/// Walks a KSR AST and builds a Roslyn <see cref="SyntaxTree"/> directly via
/// <see cref="SyntaxFactory"/> using the Visitor pattern.
/// </summary>
public class SyntaxTreeGenerator : IAstVisitor<CSharpSyntaxNode>
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
        return program.Accept(this).SyntaxTree;
    }

    // ── Visit Implementations ────────────────────────────────────────────────

    public CSharpSyntaxNode Visit(ProgramNode node)
    {
        // First pass: collect metadata
        foreach (var d in node.Declarations)
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
        foreach (var d in node.Declarations)
            if (d is UseDecl ud)
                usings.Add(UsingDirective(ParseName(MapUseNamespace(ud.Namespace))));

        // Attach #nullable enable
        usings[0] = usings[0].WithLeadingTrivia(TriviaList(
            Trivia(NullableDirectiveTrivia(Token(SyntaxKind.EnableKeyword), true)),
            EndOfLine(Environment.NewLine)));

        // Top-level declarations
        var members = new List<MemberDeclarationSyntax>();
        foreach (var d in node.Declarations)
        {
            if (d is InterfaceDecl id) members.Add((MemberDeclarationSyntax)id.Accept(this));
            if (d is SealedDecl sd)    members.AddRange(VisitSealed(sd));
            if (d is StructDecl dc)    members.Add((MemberDeclarationSyntax)dc.Accept(this));
        }

        // static class KsrProgram
        var programMembers = new List<MemberDeclarationSyntax>();
        foreach (var d in node.Declarations)
        {
            if (d is FunctionDecl fd)     programMembers.Add((MemberDeclarationSyntax)fd.Accept(this));
            if (d is ExtFunctionDecl efd) programMembers.Add((MemberDeclarationSyntax)efd.Accept(this));
        }

        members.Add(
            ClassDeclaration("KsrProgram")
                .WithModifiers(TokenList(Token(SyntaxKind.StaticKeyword)))
                .WithMembers(List(programMembers)));

        return CompilationUnit()
            .WithUsings(List(usings))
            .WithMembers(List(members));
    }

    public CSharpSyntaxNode Visit(InterfaceDecl id)
    {
        var prevTypeParams = _currentTypeParams;
        _currentTypeParams = id.TypeParams.Count > 0 ? new HashSet<string>(id.TypeParams) : new();

        var decl = InterfaceDeclaration("I" + NameUtils.Escape(id.Name));

        if (id.TypeParams.Count > 0)
            decl = decl.WithTypeParameterList(
                TypeParameterList(SeparatedList(id.TypeParams.Select(tp => TypeParameter(NameUtils.Escape(tp))))));

        if (id.Constraints.Count > 0)
            decl = decl.WithConstraintClauses(List(id.Constraints.Select(c =>
                TypeParameterConstraintClause(IdentifierName(NameUtils.Escape(c.TypeParam)))
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

    public CSharpSyntaxNode Visit(SealedDecl sd)
    {
        // This is a special case since it returns multiple members.
        // The standard Visit(SealedDecl) will return the base record.
        return RecordDeclaration(Token(SyntaxKind.RecordKeyword), NameUtils.Escape(sd.Name))
            .WithModifiers(TokenList(Token(SyntaxKind.AbstractKeyword)))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }

    private IEnumerable<MemberDeclarationSyntax> VisitSealed(SealedDecl sd)
    {
        yield return (MemberDeclarationSyntax)Visit(sd);

        foreach (var v in sd.Variants)
        {
            var baseTypes = new List<BaseTypeSyntax> { SimpleBaseType(IdentifierName(NameUtils.Escape(sd.Name))) };

            RecordDeclarationSyntax variantDecl;
            if (_implsByType.TryGetValue(v.Name, out var impls))
            {
                baseTypes.AddRange(impls.Select(b => (BaseTypeSyntax)SimpleBaseType(BuildImplTypeSyntax(b))));

                var prev = _inRecordMethod;
                _inRecordMethod = true;
                var methodMembers = impls.SelectMany(b => b.Methods)
                                        .Select(m => (MemberDeclarationSyntax)m.Accept(this))
                                        .ToList();
                _inRecordMethod = prev;

                variantDecl = RecordDeclaration(Token(SyntaxKind.RecordKeyword), NameUtils.Escape(v.Name))
                    .WithParameterList(BuildRecordParams(v.Properties))
                    .WithBaseList(BaseList(SeparatedList(baseTypes)))
                    .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                    .WithMembers(List(methodMembers))
                    .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));
            }
            else
            {
                variantDecl = RecordDeclaration(Token(SyntaxKind.RecordKeyword), NameUtils.Escape(v.Name))
                    .WithParameterList(BuildRecordParams(v.Properties))
                    .WithBaseList(BaseList(SeparatedList(baseTypes)))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
            }
            yield return variantDecl;
        }
    }

    public CSharpSyntaxNode Visit(StructDecl dc)
    {
        if (_implsByType.TryGetValue(dc.Name, out var impls))
        {
            var baseTypes = impls.Select(b => (BaseTypeSyntax)SimpleBaseType(BuildImplTypeSyntax(b)));

            var prev = _inRecordMethod;
            _inRecordMethod = true;
            var methodMembers = impls.SelectMany(b => b.Methods)
                                     .Select(m => (MemberDeclarationSyntax)m.Accept(this))
                                     .ToList();
            _inRecordMethod = prev;

            return RecordDeclaration(Token(SyntaxKind.RecordKeyword), NameUtils.Escape(dc.Name))
                .WithParameterList(BuildRecordParams(dc.Properties))
                .WithBaseList(BaseList(SeparatedList(baseTypes)))
                .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                .WithMembers(List(methodMembers))
                .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));
        }

        return RecordDeclaration(Token(SyntaxKind.RecordKeyword), NameUtils.Escape(dc.Name))
            .WithParameterList(BuildRecordParams(dc.Properties))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }

    public CSharpSyntaxNode Visit(FunctionDecl fd)
    {
        var (prevAsync, prevKind, prevTypeParams) = SaveAsyncState();
        _inAsyncFunction    = fd.IsAsync;
        _currentAsyncReturn = EffectiveAsyncReturn(fd.AsyncReturn);
        _currentTypeParams  = fd.TypeParams.Count > 0 ? new HashSet<string>(fd.TypeParams) : new();

        var ret = GetReturnTypeSyntax(fd.IsAsync, fd.ReturnType, _currentAsyncReturn);
        var mods = new List<SyntaxToken>();

        if (_inRecordMethod)
            mods.Add(Token(SyntaxKind.PublicKeyword));
        else
            mods.Add(Token(SyntaxKind.StaticKeyword));

        if (fd.IsAsync) mods.Add(Token(SyntaxKind.AsyncKeyword));

        var methodName = (!_inRecordMethod && fd.Name == "main") ? "Main" : fd.Name;

        var method = MethodDeclaration(ret, Pascal(methodName))
            .WithModifiers(TokenList(mods))
            .WithParameterList(ParameterList(SeparatedList(fd.Parameters.Select(p => BuildParam(p)))))
            .WithBody((BlockSyntax)fd.Body.Accept(this));

        if (fd.TypeParams.Count > 0)
            method = method.WithTypeParameterList(
                TypeParameterList(SeparatedList(fd.TypeParams.Select(tp => TypeParameter(NameUtils.Escape(tp))))));

        RestoreAsyncState(prevAsync, prevKind, prevTypeParams);
        return method;
    }

    public CSharpSyntaxNode Visit(ExtFunctionDecl efd)
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
            .WithBody((BlockSyntax)efd.Body.Accept(this));

        if (efd.TypeParams.Count > 0)
            method = method.WithTypeParameterList(
                TypeParameterList(SeparatedList(efd.TypeParams.Select(tp => TypeParameter(NameUtils.Escape(tp))))));

        RestoreAsyncState(prevAsync, prevKind, prevTypeParams);
        return method;
    }

    public CSharpSyntaxNode Visit(Block node) =>
        Block(node.Statements.Select(s => (StatementSyntax)s.Accept(this)));

    public CSharpSyntaxNode Visit(UseDecl node) => EmptyStatement(); // Handled in preamble

    public CSharpSyntaxNode Visit(ImplBlock node) => EmptyStatement(); // Handled in metadata pass

    // ── Statements ───────────────────────────────────────────────────────────

    public CSharpSyntaxNode Visit(ValDecl node) =>
        BuildLocalDecl(node.Name, node.Type, (ExpressionSyntax)BuildExprWithHint(node.Value, node.Type));

    public CSharpSyntaxNode Visit(VarDecl node) =>
        BuildLocalDecl(node.Name, node.Type, (ExpressionSyntax)BuildExprWithHint(node.Value, node.Type));

    public CSharpSyntaxNode Visit(AssignStmt node) =>
        ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
            IdentifierName(NameUtils.Escape(node.Name)), (ExpressionSyntax)node.Value.Accept(this)));

    public CSharpSyntaxNode Visit(CompoundAssignStmt node) =>
        ExpressionStatement(AssignmentExpression(MapCompoundOp(node.Op),
            IdentifierName(NameUtils.Escape(node.Name)), (ExpressionSyntax)node.Value.Accept(this)));

    public CSharpSyntaxNode Visit(IndexAssignStmt node) =>
        ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
            ElementAccessExpression(IdentifierName(NameUtils.Escape(node.Name)))
                .WithArgumentList(BracketedArgumentList(
                    SingletonSeparatedList(Argument((ExpressionSyntax)node.Index.Accept(this))))),
            (ExpressionSyntax)node.Value.Accept(this)));

    public CSharpSyntaxNode Visit(ReturnStmt node) =>
        node.Value is null
            ? ReturnStatement()
            : ReturnStatement((ExpressionSyntax)node.Value.Accept(this));

    public CSharpSyntaxNode Visit(IfStmt node)
    {
        var s = IfStatement((ExpressionSyntax)node.Condition.Accept(this), (BlockSyntax)node.Then.Accept(this));
        return node.Else is null ? s : s.WithElse(ElseClause((BlockSyntax)node.Else.Accept(this)));
    }

    public CSharpSyntaxNode Visit(WhileStmt node) =>
        WhileStatement((ExpressionSyntax)node.Condition.Accept(this), (BlockSyntax)node.Body.Accept(this));

    public CSharpSyntaxNode Visit(ForInStmt node)
    {
        var escapedVar = NameUtils.Escape(node.VarName);
        if (node.Iterable is RangeExpr re)
        {
            var op = re.Inclusive
                ? SyntaxKind.LessThanOrEqualExpression
                : SyntaxKind.LessThanExpression;
            return ForStatement(
                VariableDeclaration(IdentifierName("var"))
                    .WithVariables(SingletonSeparatedList(
                        VariableDeclarator(Identifier(escapedVar))
                            .WithInitializer(EqualsValueClause((ExpressionSyntax)re.Start.Accept(this))))),
                default,
                BinaryExpression(op, IdentifierName(escapedVar), (ExpressionSyntax)re.End.Accept(this)),
                SingletonSeparatedList<ExpressionSyntax>(
                    PostfixUnaryExpression(SyntaxKind.PostIncrementExpression,
                        IdentifierName(escapedVar))),
                (BlockSyntax)node.Body.Accept(this));
        }

        return ForEachStatement(
            IdentifierName("var"),
            Identifier(escapedVar),
            (ExpressionSyntax)node.Iterable.Accept(this),
            (BlockSyntax)node.Body.Accept(this));
    }

    public CSharpSyntaxNode Visit(ExprStmt node)
    {
        if (node.Expression is WhenExpr we)
            return BuildWhenStmt(we);
        return ExpressionStatement((ExpressionSyntax)node.Expression.Accept(this));
    }

    // ── Expressions ──────────────────────────────────────────────────────────

    public CSharpSyntaxNode Visit(IntLiteral node) =>
        LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(node.Value));

    public CSharpSyntaxNode Visit(DoubleLiteral node) =>
        LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(node.Value));

    public CSharpSyntaxNode Visit(StringLiteral node)
    {
        if (node.IsRaw)
        {
            var escaped = node.Value.Replace("\"", "\"\"");
            return LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                Token(default, SyntaxKind.StringLiteralToken,
                    $"@\"{escaped}\"", node.Value, default));
        }
        return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(node.Value));
    }

    public CSharpSyntaxNode Visit(BoolLiteral node) =>
        node.Value
            ? LiteralExpression(SyntaxKind.TrueLiteralExpression)
            : LiteralExpression(SyntaxKind.FalseLiteralExpression);

    public CSharpSyntaxNode Visit(NullLiteral node) =>
        LiteralExpression(SyntaxKind.NullLiteralExpression);

    public CSharpSyntaxNode Visit(ThisExpr node) =>
        _inRecordMethod
            ? (ExpressionSyntax)ThisExpression()
            : IdentifierName("self");

    public CSharpSyntaxNode Visit(IdentifierExpr node) => IdentifierName(NameUtils.Escape(node.Name));

    public CSharpSyntaxNode Visit(IndexExpr node) =>
        ElementAccessExpression((ExpressionSyntax)node.Target.Accept(this))
            .WithArgumentList(BracketedArgumentList(
                SingletonSeparatedList(Argument((ExpressionSyntax)node.Index.Accept(this)))));

    public CSharpSyntaxNode Visit(NewArrayExpr node) =>
        ArrayCreationExpression(
            ArrayType(MapTypeSyntax(node.ElementType))
                .WithRankSpecifiers(SingletonList(
                    ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>((ExpressionSyntax)node.Size.Accept(this))))));

    public CSharpSyntaxNode Visit(NewObjectExpr node) =>
        ObjectCreationExpression(IdentifierName(NameUtils.Escape(node.TypeName)))
            .WithArgumentList(ArgumentList(
                SeparatedList(node.Arguments.Select(a => Argument((ExpressionSyntax)a.Accept(this))))));

    public CSharpSyntaxNode Visit(LambdaExpr node)
    {
        CSharpSyntaxNode body = node.IsBlockBody
            ? (BlockSyntax)(node.BlockBody?.Accept(this)
                ?? throw new InvalidOperationException("Block lambda is missing a block body."))
            : (ExpressionSyntax)(node.Body?.Accept(this)
                ?? throw new InvalidOperationException("Expression lambda is missing a body."));

        if (node.Params.Count == 0)
            return ParenthesizedLambdaExpression().WithBody(body);
        if (node.Params.Count == 1)
            return SimpleLambdaExpression(Parameter(Identifier(NameUtils.Escape(node.Params[0])))).WithBody(body);
        return ParenthesizedLambdaExpression()
            .WithParameterList(ParameterList(SeparatedList(node.Params.Select(p => Parameter(Identifier(NameUtils.Escape(p)))))))
            .WithBody(body);
    }


    public CSharpSyntaxNode Visit(BinaryExpr node) =>
        ParenthesizedExpression(
            BinaryExpression(MapBinaryOp(node.Op), (ExpressionSyntax)node.Left.Accept(this), (ExpressionSyntax)node.Right.Accept(this)));

    public CSharpSyntaxNode Visit(UnaryExpr node) =>
        ParenthesizedExpression(
            PrefixUnaryExpression(MapUnaryOp(node.Op), (ExpressionSyntax)node.Operand.Accept(this)));

    public CSharpSyntaxNode Visit(MemberAccessExpr node) =>
        MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            (ExpressionSyntax)node.Target.Accept(this), IdentifierName(Pascal(node.Member)));

    public CSharpSyntaxNode Visit(SafeCallExpr node) =>
        ConditionalAccessExpression(
            (ExpressionSyntax)node.Target.Accept(this),
            MemberBindingExpression(IdentifierName(Pascal(node.Member))));

    public CSharpSyntaxNode Visit(ElvisExpr node) =>
        ParenthesizedExpression(
            BinaryExpression(SyntaxKind.CoalesceExpression,
                (ExpressionSyntax)node.Left.Accept(this), (ExpressionSyntax)node.Right.Accept(this)));

    public CSharpSyntaxNode Visit(RangeExpr node)
    {
        var start = (ExpressionSyntax)node.Start.Accept(this);
        var end = (ExpressionSyntax)node.End.Accept(this);
        ExpressionSyntax count = node.Inclusive
            ? BinaryExpression(SyntaxKind.AddExpression,
                BinaryExpression(SyntaxKind.SubtractExpression, end, start),
                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(1)))
            : BinaryExpression(SyntaxKind.SubtractExpression, end, start);

        return InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("Enumerable"), IdentifierName("Range")))
            .WithArgumentList(ArgumentList(SeparatedList(new[]
            {
                Argument(start),
                Argument(count),
            })));
    }

    public CSharpSyntaxNode Visit(StringTemplateExpr node)
    {
        var startToken = node.IsRaw
            ? Token(SyntaxKind.InterpolatedVerbatimStringStartToken)
            : Token(SyntaxKind.InterpolatedStringStartToken);

        var contents = node.Parts.Select<StringPart, InterpolatedStringContentSyntax>(part =>
        {
            if (part is LiteralPart lp)
            {
                var srcText = node.IsRaw
                    ? lp.Text.Replace("\"", "\"\"").Replace("{", "{{").Replace("}", "}}")
                    : lp.Text.Replace("{", "{{").Replace("}", "}}");
                return InterpolatedStringText(
                    Token(default, SyntaxKind.InterpolatedStringTextToken,
                        srcText, lp.Text, default));
            }
            if (part is ExprPart ep)
                return Interpolation((ExpressionSyntax)ep.Expression.Accept(this));
            throw new InvalidOperationException($"Unknown StringPart: {part.GetType().Name}");
        });

        return InterpolatedStringExpression(startToken)
            .WithContents(List(contents))
            .WithStringEndToken(Token(SyntaxKind.InterpolatedStringEndToken));
    }

    public CSharpSyntaxNode Visit(CallExpr node)
    {
        if (node.Callee is SafeCallExpr sc)
        {
            return ConditionalAccessExpression(
                (ExpressionSyntax)sc.Target.Accept(this),
                InvocationExpression(MemberBindingExpression(IdentifierName(Pascal(sc.Member))))
                    .WithArgumentList(ArgumentList(
                        SeparatedList(node.Arguments.Select(BuildCallArg)))));
        }

        if (node.Callee is IdentifierExpr { Name: "println" })
        {
            return InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("Console"), IdentifierName("WriteLine")))
                .WithArgumentList(ArgumentList(
                    SeparatedList(node.Arguments.Select(BuildCallArg))));
        }

        if (node.Callee is IdentifierExpr id && _structs.Contains(id.Name))
        {
            return ObjectCreationExpression(IdentifierName(id.Name))
                .WithArgumentList(ArgumentList(
                    SeparatedList(node.Arguments.Select(BuildCallArg))));
        }

        return InvocationExpression((ExpressionSyntax)node.Callee.Accept(this))
            .WithArgumentList(ArgumentList(
                SeparatedList(node.Arguments.Select(BuildCallArg))));
    }

    public CSharpSyntaxNode Visit(ListLiteralExpr node)
    {
        if (node.Elements.Count == 0)
            return InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("Array"),
                        GenericName("Empty").WithTypeArgumentList(TypeArgumentList(
                            SingletonSeparatedList<TypeSyntax>(IdentifierName("object"))))))
                .WithArgumentList(ArgumentList());

        return ImplicitArrayCreationExpression(
            InitializerExpression(SyntaxKind.ArrayInitializerExpression,
                SeparatedList(node.Elements.Select(e => (ExpressionSyntax)e.Accept(this)))));
    }

    public CSharpSyntaxNode Visit(MapLiteralExpr node)
    {
        var keyType = (TypeSyntax)(InferTypeSyntax(node.Entries.Count > 0 ? node.Entries[0].Key : null) ?? IdentifierName("object"));
        var valType = (TypeSyntax)(InferTypeSyntax(node.Entries.Count > 0 ? node.Entries[0].Value : null) ?? IdentifierName("object"));

        var dictType = GenericName("Dictionary")
            .WithTypeArgumentList(TypeArgumentList(SeparatedList<TypeSyntax>(new[] { keyType, valType })));

        if (node.Entries.Count == 0)
            return ObjectCreationExpression(dictType).WithArgumentList(ArgumentList());

        var entries = node.Entries.Select(e =>
            (ExpressionSyntax)AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                ImplicitElementAccess()
                    .WithArgumentList(BracketedArgumentList(
                        SingletonSeparatedList(Argument((ExpressionSyntax)e.Key.Accept(this))))),
                (ExpressionSyntax)e.Value.Accept(this)));

        return ObjectCreationExpression(dictType)
            .WithArgumentList(ArgumentList())
            .WithInitializer(InitializerExpression(
                SyntaxKind.ObjectInitializerExpression,
                SeparatedList(entries)));
    }

    public CSharpSyntaxNode Visit(WhenExpr node)
    {
        if (node.Subject is not null)
        {
            var arms = node.Arms.Select(arm =>
            {
                PatternSyntax pattern = arm.Pattern switch
                {
                    null                => DiscardPattern(),
                    IsPatternExpr ip    => BuildSwitchPattern(ip),
                    _                   => ConstantPattern((ExpressionSyntax)arm.Pattern.Accept(this))
                };
                return SwitchExpressionArm(pattern, (ExpressionSyntax)arm.Body.Accept(this));
            });

            return ParenthesizedExpression(
                SwitchExpression((ExpressionSyntax)node.Subject.Accept(this))
                    .WithArms(SeparatedList(arms)));
        }

        return BuildWhenTernary(node.Arms, 0);
    }

    public CSharpSyntaxNode Visit(AwaitExpr node) =>
        ParenthesizedExpression(AwaitExpression((ExpressionSyntax)node.Operand.Accept(this)));

    public CSharpSyntaxNode Visit(NamedArgExpr node) =>
        (ExpressionSyntax)node.Value.Accept(this);

    public CSharpSyntaxNode Visit(IsPatternExpr node) =>
        throw new InvalidOperationException("IsPatternExpr must be handled in pattern context");

    // ── Helpers ──────────────────────────────────────────────────────────────

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

    private StatementSyntax BuildWhenStmt(WhenExpr we)
    {
        var subjectExpr = we.Subject is not null ? (ExpressionSyntax)we.Subject.Accept(this) : null;
        StatementSyntax? result = null;

        foreach (var arm in Enumerable.Reverse(we.Arms))
        {
            var body = (StatementSyntax)Block(ExpressionStatement((ExpressionSyntax)arm.Body.Accept(this)));

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
                            subjectExpr, (ExpressionSyntax)arm.Pattern.Accept(this))
                        : (ExpressionSyntax)arm.Pattern.Accept(this);
                }

                var ifStmt = IfStatement(cond, body);
                if (result is not null) ifStmt = ifStmt.WithElse(ElseClause(result));
                result = ifStmt;
            }
        }

        return result ?? Block();
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
            ? (ExpressionSyntax)arm.Body.Accept(this)
            : ConditionalExpression((ExpressionSyntax)arm.Pattern.Accept(this),
                (ExpressionSyntax)arm.Body.Accept(this),
                BuildWhenTernary(arms, i + 1));
    }

    private CSharpSyntaxNode BuildExprWithHint(Expr expr, TypeRef? hint)
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
                            SeparatedList(ll.Elements.Select(e => (ExpressionSyntax)e.Accept(this)))));
                }
            }

            if (expr is MapLiteralExpr { Entries.Count: 0 })
                return ObjectCreationExpression(MapTypeForNewSyntax(hint))
                    .WithArgumentList(ArgumentList());
        }

        return expr.Accept(this);
    }

    private PatternSyntax BuildSwitchPattern(IsPatternExpr ip) =>
        ip.Binding is not null
            ? DeclarationPattern(IdentifierName(ip.TypeName),
                SingleVariableDesignation(Identifier(ip.Binding)))
            : TypePattern(IdentifierName(ip.TypeName));

    private PatternSyntax BuildIsPattern(IsPatternExpr ip) => BuildSwitchPattern(ip);

    private TypeSyntax MapTypeSyntax(TypeRef t)
    {
        if (t.Name.EndsWith("[]"))
        {
            var elem    = MapTypeSyntax(new TypeRef(t.Name[..^2], false));
            var arrType = ArrayType(elem).WithRankSpecifiers(SingletonList(
                ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(
                    OmittedArraySizeExpression()))));
            return t.Nullable ? NullableType(arrType) : (TypeSyntax)arrType;
        }

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

    private (bool, AsyncReturnKind, HashSet<string>) SaveAsyncState() =>
        (_inAsyncFunction, _currentAsyncReturn, _currentTypeParams);

    private void RestoreAsyncState(bool prevAsync, AsyncReturnKind prevKind, HashSet<string> prevTypeParams)
    {
        _inAsyncFunction    = prevAsync;
        _currentAsyncReturn = prevKind;
        _currentTypeParams  = prevTypeParams;
    }

    private ParameterSyntax BuildParam(Parameter p, bool pascalName = false)
    {
        var name  = pascalName ? Pascal(p.Name) : p.Name;
        var param = Parameter(Identifier(name)).WithType(MapTypeSyntax(p.Type));
        if (p.Default is not null)
            param = param.WithDefault(EqualsValueClause((ExpressionSyntax)p.Default.Accept(this)));
        return param;
    }

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

    private ArgumentSyntax BuildCallArg(Expr expr)
    {
        if (expr is NamedArgExpr na)
            return Argument((ExpressionSyntax)na.Value.Accept(this))
                .WithNameColon(NameColon(IdentifierName(na.Name)));
        return Argument((ExpressionSyntax)expr.Accept(this));
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
