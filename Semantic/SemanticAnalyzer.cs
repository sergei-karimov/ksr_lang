using KSR.AST;
using KSR.Diagnostics;

namespace KSR.Semantic;

public class SemanticAnalyzer : IAstVisitor<object?>
{
    private readonly SymbolTable _symbols = new();
    private readonly List<KsrDiagnostic> _diagnostics = new();
    private readonly HashSet<string> _usedNamespaces = new(StringComparer.Ordinal);
    private string _currentFile = "";
    private TypeRef? _currentReturnType;
    private bool _isAsyncFunction;
    private readonly List<ExtFunctionDecl> _extensions = new();
    private readonly List<ImplBlock> _implementations = new();
    private readonly Dictionary<string, string> _sealedBases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _externalTypes = new(StringComparer.Ordinal);
    private readonly HashSet<WhenExpr> _statementWhens = new(ReferenceEqualityComparer.Instance);

    private record MemberInfo(TypeRef Type, IReadOnlyList<Parameter>? Parameters = null,
        IReadOnlyList<string>? TypeParams = null);

    public IReadOnlyList<KsrDiagnostic> Diagnostics => _diagnostics;

    // Transitional one-way projection for existing compiler and LSP callers.
    public IReadOnlyList<string> Errors => _diagnostics.Select(FormatDiagnostic).ToArray();

    public SemanticAnalyzer()
    {
        // Register built-ins
        _symbols.Declare("println", SymbolKind.Function, false, 
            new FunctionDecl("println", [], [new Parameter("text", new TypeRef("Any", true))], null, new Block([])));
    }

    public void Analyze(ProgramNode program, string sourceFile = "")
    {
        _currentFile = sourceFile;
        _diagnostics.Clear();
        _usedNamespaces.Clear();
        foreach (var use in program.Declarations.OfType<UseDecl>())
            _usedNamespaces.Add(use.Namespace);
        program.Accept(this);
    }

    private void Error(AstNode node, string message)
    {
        var sourceFile = !string.IsNullOrEmpty(node.SourceFile)
            ? node.SourceFile
            : _currentFile;
        var line = node.Line > 0 ? node.Line : 1;
        var column = node.Column > 0 ? node.Column : 1;
        _diagnostics.Add(new KsrDiagnostic(message, sourceFile, line, column, DiagnosticSeverity.Error));
    }

    private static string FormatDiagnostic(KsrDiagnostic diagnostic) =>
        $"{diagnostic.SourceFile}({diagnostic.Line},{diagnostic.Column}): error: {diagnostic.Message}";

    public object? Visit(ProgramNode node)
    {
        _extensions.AddRange(node.Declarations.OfType<ExtFunctionDecl>());
        _implementations.AddRange(node.Declarations.OfType<ImplBlock>());
        // Pass 1: Register all top-level names
        foreach (var decl in node.Declarations)
        {
            switch (decl)
            {
                case FunctionDecl fd:
                    if (!_symbols.Declare(fd.Name, SymbolKind.Function, false, fd))
                        Error(fd, $"Redeclaration of function '{fd.Name}'");
                    break;
                case StructDecl sd:
                    if (!_symbols.Declare(sd.Name, SymbolKind.Struct, false, sd))
                        Error(sd, $"Redeclaration of struct '{sd.Name}'");
                    break;
                case SealedDecl sd:
                    if (!_symbols.Declare(sd.Name, SymbolKind.Sealed, false, sd))
                        Error(sd, $"Redeclaration of sealed type '{sd.Name}'");
                    foreach (var v in sd.Variants)
                    {
                        _sealedBases.TryAdd(v.Name, sd.Name);
                        if (!_symbols.Declare(v.Name, SymbolKind.Struct, false, v))
                            Error(v, $"Redeclaration of struct '{v.Name}' (in sealed '{sd.Name}')");
                    }
                    break;
                case InterfaceDecl id:
                    if (!_symbols.Declare(id.Name, SymbolKind.Interface, false, id))
                        Error(id, $"Redeclaration of interface '{id.Name}'");
                    break;
            }
        }

        // Pass 2: Internal analysis
        foreach (var decl in node.Declarations)
        {
            decl.Accept(this);
        }
        return null;
    }

    public object? Visit(InterfaceDecl id) => null;
    public object? Visit(ImplBlock node)
    {
        _symbols.EnterScope();
        _symbols.Declare("this", SymbolKind.Parameter, false, new TypeRef(node.TypeName, false));
        foreach (var m in node.Methods) m.Accept(this);
        _symbols.ExitScope();
        return null;
    }
    public object? Visit(UseDecl node) => null;
    public object? Visit(StructDecl node) => null;
    public object? Visit(SealedDecl node) => null;

    public object? Visit(FunctionDecl fd)
    {
        var previousReturnType = _currentReturnType;
        var previousAsync = _isAsyncFunction;
        _isAsyncFunction = fd.IsAsync;
        _currentReturnType = fd.ReturnType ?? new TypeRef("Unit", false);

        _symbols.EnterScope();
        foreach (var p in fd.Parameters)
        {
            if (!_symbols.Declare(p.Name, SymbolKind.Parameter, false, p.Type))
                Error(fd, $"Duplicate parameter name '{p.Name}' in function '{fd.Name}'");
        }
        VisitFunctionBody(fd.Body);
        CheckReturnPaths(fd, fd.Body, _currentReturnType);
        _symbols.ExitScope();

        _currentReturnType = previousReturnType;
        _isAsyncFunction = previousAsync;
        return null;
    }

    public object? Visit(ExtFunctionDecl efd)
    {
        var previousReturnType = _currentReturnType;
        var previousAsync = _isAsyncFunction;
        _isAsyncFunction = efd.IsAsync;
        _currentReturnType = efd.ReturnType ?? new TypeRef("Unit", false);

        _symbols.EnterScope();
        _symbols.Declare("this", SymbolKind.Parameter, false, new TypeRef(efd.ReceiverType, false));
        foreach (var p in efd.Parameters)
        {
            if (!_symbols.Declare(p.Name, SymbolKind.Parameter, false, p.Type))
                Error(efd, $"Duplicate parameter name '{p.Name}' in extension function '{efd.MethodName}'");
        }
        VisitFunctionBody(efd.Body);
        CheckReturnPaths(efd, efd.Body, _currentReturnType);
        _symbols.ExitScope();

        _currentReturnType = previousReturnType;
        _isAsyncFunction = previousAsync;
        return null;
    }

    private void VisitFunctionBody(Block body)
    {
        foreach (var s in body.Statements) s.Accept(this);
    }

    public object? Visit(Block node)
    {
        _symbols.EnterScope();
        foreach (var s in node.Statements) s.Accept(this);
        _symbols.ExitScope();
        return null;
    }

    // ── Statements ───────────────────────────────────────────────────────────

    public object? Visit(ValDecl node)
    {
        var valueType = (TypeRef?)node.Value.Accept(this);
        if (node.Value is ListLiteralExpr { Elements.Count: 0 } && node.Type is not null)
            valueType = node.Type;
        if (node.Type != null && valueType != null)
        {
            if (!IsCompatible(node.Type, valueType))
                Error(node, $"Type mismatch: cannot assign '{valueType.Name}' to '{node.Type.Name}'");
        }
        
        if (!_symbols.Declare(node.Name, SymbolKind.Variable, false, node.Type ?? valueType))
            Error(node, $"Variable '{node.Name}' is already defined in this scope");

        return null;
    }

    public object? Visit(VarDecl node)
    {
        var valueType = (TypeRef?)node.Value.Accept(this);
        if (node.Value is ListLiteralExpr { Elements.Count: 0 } && node.Type is not null)
            valueType = node.Type;
        if (node.Type != null && valueType != null)
        {
            if (!IsCompatible(node.Type, valueType))
                Error(node, $"Type mismatch: cannot assign '{valueType.Name}' to '{node.Type.Name}'");
        }

        if (!_symbols.Declare(node.Name, SymbolKind.Variable, true, node.Type ?? valueType))
            Error(node, $"Variable '{node.Name}' is already defined in this scope");

        return null;
    }

    public object? Visit(AssignStmt node)
    {
        var valueType = (TypeRef?)node.Value.Accept(this);
        var sym = _symbols.Resolve(node.Name);
        if (sym == null)
            Error(node, $"Undefined variable '{node.Name}'");
        else
        {
            if (!sym.IsMutable && sym.Kind != SymbolKind.Parameter)
                Error(node, $"Cannot reassign to immutable variable '{node.Name}' (declared with 'val')");
            else if (sym.Kind == SymbolKind.Parameter)
                Error(node, $"Cannot reassign to immutable variable '{node.Name}'");

            var targetType = sym.Metadata as TypeRef;
            if (targetType != null && valueType != null)
            {
                if (!IsCompatible(targetType, valueType))
                    Error(node, $"Type mismatch: cannot assign '{valueType.Name}' to '{targetType.Name}'");
            }
        }
        return null;
    }

    public object? Visit(CompoundAssignStmt node)
    {
        var valueType = (TypeRef?)node.Value.Accept(this);
        var sym = _symbols.Resolve(node.Name);
        if (sym == null)
            Error(node, $"Undefined variable '{node.Name}'");
        else if (!sym.IsMutable)
            Error(node, $"Cannot use compound assignment on immutable variable '{node.Name}'");
        return null;
    }

    public object? Visit(IndexAssignStmt node)
    {
        node.Index.Accept(this);
        node.Value.Accept(this);
        var sym = _symbols.Resolve(node.Name);
        if (sym == null)
            Error(node, $"Undefined variable '{node.Name}'");
        return null;
    }

    public object? Visit(ReturnStmt node)
    {
        var valueType = (TypeRef?)node.Value?.Accept(this);
        if (_currentReturnType is null)
            return null;

        if (node.Value is null)
        {
            if (_currentReturnType.Name != "Unit")
                Error(node, $"Return type mismatch: expected '{_currentReturnType.Name}' but found 'Unit'");
            return null;
        }

        if (valueType != null && !IsCompatible(_currentReturnType, valueType))
            Error(node, $"Type mismatch: cannot assign '{valueType.Name}' to '{_currentReturnType.Name}'");

        return null;
    }

    public object? Visit(IfStmt node)
    {
        var condType = (TypeRef?)node.Condition.Accept(this);
        if (condType != null && condType.Name != "Bool")
            Error(node, $"Condition must be Bool, but found '{condType.Name}'");
        node.Then.Accept(this);
        node.Else?.Accept(this);
        return null;
    }

    public object? Visit(WhileStmt node)
    {
        var condType = (TypeRef?)node.Condition.Accept(this);
        if (condType != null && condType.Name != "Bool")
            Error(node, $"Condition must be Bool, but found '{condType.Name}'");
        node.Body.Accept(this);
        return null;
    }

    public object? Visit(ForInStmt node)
    {
        var iterType = (TypeRef?)node.Iterable.Accept(this);
        _symbols.EnterScope();
        
        TypeRef? elemType = null;
        if (iterType != null)
        {
            if (iterType.Name.EndsWith("[]"))
                elemType = new TypeRef(iterType.Name[..^2], false);
            else if (iterType.Name.StartsWith("List<"))
                elemType = new TypeRef(iterType.Name[5..^1], false);
        }

        _symbols.Declare(node.VarName, SymbolKind.Variable, false, elemType); 
        node.Body.Accept(this);
        _symbols.ExitScope();
        return null;
    }

    public object? Visit(ExprStmt node)
    {
        if (node.Expression is WhenExpr when)
        {
            _statementWhens.Add(when);
            try { when.Accept(this); }
            finally { _statementWhens.Remove(when); }
        }
        else node.Expression.Accept(this);
        return null;
    }

    // ── Expressions ──────────────────────────────────────────────────────────

    public object? Visit(IntLiteral node) => new TypeRef("Int", false);
    public object? Visit(DoubleLiteral node) => new TypeRef("Double", false);
    public object? Visit(StringLiteral node) => new TypeRef("String", false);
    public object? Visit(BoolLiteral node) => new TypeRef("Bool", false);
    public object? Visit(NullLiteral node) => new TypeRef("Any", true); // Dynamic null
    public object? Visit(ThisExpr node)
    {
        var sym = _symbols.Resolve("this");
        if (sym == null)
        {
            Error(node, "'this' is only available in extension functions or record methods");
            return new TypeRef("Any", false);
        }
        return sym.Metadata as TypeRef ?? new TypeRef("Any", false);
    }

    public object? Visit(IdentifierExpr node)
    {
        var sym = _symbols.Resolve(node.Name);
        if (sym == null)
        {
            Error(node, $"Undefined identifier '{node.Name}'");
            return new TypeRef("Any", false);
        }
        if (sym.Kind == SymbolKind.Function) return new TypeRef("Function", false);
        if (sym.Metadata is StructDecl) return new TypeRef(node.Name, false);
        if (sym.Metadata is TypeRef tr) return tr;
        return new TypeRef("Any", false);
    }

    public object? Visit(StringTemplateExpr node)
    {
        foreach (var p in node.Parts)
        {
            if (p is ExprPart ep) ep.Expression.Accept(this);
        }
        return new TypeRef("String", false);
    }

    public object? Visit(CallExpr node)
    {
        if (node.Callee is IdentifierExpr id)
        {
            var sym = _symbols.Resolve(id.Name);
            if (sym?.Metadata is FunctionDecl fd)
            {
                return CheckArguments(node, node.Arguments, new MemberInfo(
                    fd.ReturnType ?? new TypeRef("Unit", false), fd.Parameters, fd.TypeParams));
            }
            if (sym?.Metadata is StructDecl sd)
                return CheckArguments(node, node.Arguments, new MemberInfo(new TypeRef(id.Name, false), sd.Properties));
        }
        if (node.Callee is MemberAccessExpr member)
            return CheckArguments(node, node.Arguments, AnalyzeMember(member, member.Target, member.Member, false, isCall: true));
        if (node.Callee is SafeCallExpr safe)
            return CheckArguments(node, node.Arguments, AnalyzeMember(safe, safe.Target, safe.Member, true, isCall: true));

        node.Callee.Accept(this);
        return CheckArguments(node, node.Arguments, null);
    }

    public object? Visit(MemberAccessExpr node)
    {
        var member = AnalyzeMember(node, node.Target, node.Member, false);
        return member?.Parameters != null ? new TypeRef("Function", false) : member?.Type;
    }

    public object? Visit(SafeCallExpr node)
    {
        var member = AnalyzeMember(node, node.Target, node.Member, true);
        return member?.Parameters != null ? new TypeRef("Function", true) : member?.Type;
    }

    public object? Visit(ElvisExpr node)
    {
        var left = (TypeRef?)node.Left.Accept(this);
        var right = (TypeRef?)node.Right.Accept(this);
        return left != null ? left with { Nullable = false } : right;
    }

    public object? Visit(BinaryExpr node)
    {
        var left = (TypeRef?)node.Left.Accept(this);
        var right = (TypeRef?)node.Right.Accept(this);
        
        if (node.Op is "==" or "!=" or "<" or ">" or "<=" or ">=")
            return new TypeRef("Bool", false);
        
        return left ?? right ?? new TypeRef("Any", false);
    }

    public object? Visit(IndexExpr node)
    {
        var target = (TypeRef?)node.Target.Accept(this);
        node.Index.Accept(this);
        if (target != null && target.Name.EndsWith("[]"))
            return new TypeRef(target.Name[..^2], false);
        return new TypeRef("Any", false);
    }

    public object? Visit(NewArrayExpr node)
    {
        node.Size.Accept(this);
        return new TypeRef(node.ElementType.Name + "[]", false);
    }

    public object? Visit(LambdaExpr node)
    {
        var previousReturnType = _currentReturnType;
        var previousAsync = _isAsyncFunction;
        _isAsyncFunction = false; // Lambda syntax currently has no async modifier.
        _currentReturnType = null;

        try
        {
            _symbols.EnterScope();
            foreach (var p in node.Params) _symbols.Declare(p, SymbolKind.Parameter, false);
            if (node.IsBlockBody)
                node.BlockBody?.Accept(this);
            else
                node.Body?.Accept(this);
            _symbols.ExitScope();
            return new TypeRef("Function", false);
        }
        finally
        {
            _currentReturnType = previousReturnType;
            _isAsyncFunction = previousAsync;
        }
    }

    public object? Visit(NewObjectExpr node)
    {
        if (_symbols.ResolveType(node.TypeName)?.Metadata is StructDecl sd)
            return CheckArguments(node, node.Arguments, new MemberInfo(new TypeRef(sd.Name, false), sd.Properties));
        // CLR constructor signatures are resolved later by the C# compiler.
        _externalTypes.Add(node.TypeName);
        foreach (var a in node.Arguments) a.Accept(this);
        return new TypeRef(node.TypeName, false);
    }

    public object? Visit(UnaryExpr node)
    {
        return node.Operand.Accept(this);
    }

    public object? Visit(RangeExpr node)
    {
        node.Start.Accept(this);
        node.End.Accept(this);
        return new TypeRef("Range", false);
    }

    public object? Visit(ListLiteralExpr node)
    {
        TypeRef? first = null;
        foreach (var e in node.Elements)
        {
            var t = (TypeRef?)e.Accept(this);
            first ??= t;
        }
        var elemName = first?.Name ?? "Any";
        return new TypeRef($"List<{elemName}>", false);
    }

    public object? Visit(WhenExpr node)
    {
        var subjectType = (TypeRef?)node.Subject?.Accept(this);
        if (!_statementWhens.Contains(node) && subjectType != null
            && _symbols.ResolveType(subjectType.Name)?.Metadata is SealedDecl sealedType
            && !node.Arms.Any(a => a.Pattern == null))
        {
            var covered = node.Arms.Select(a => a.Pattern).OfType<IsPatternExpr>()
                .Select(p => p.TypeName).ToHashSet(StringComparer.Ordinal);
            var missing = sealedType.Variants.Where(v => !covered.Contains(v.Name)
                && !covered.Contains(sealedType.Name)).Select(v => v.Name).ToList();
            if (subjectType.Nullable && !node.Arms.Any(a => a.Pattern is NullLiteral)) missing.Add("null");
            if (missing.Count > 0)
                Error(node, $"Non-exhaustive when: missing {string.Join(", ", missing)}");
        }
        TypeRef? first = null;
        foreach (var arm in node.Arms)
        {
            _symbols.EnterScope();
            if (arm.Pattern is IsPatternExpr { Binding: not null } pattern)
                _symbols.Declare(pattern.Binding, SymbolKind.Variable, false, new TypeRef(pattern.TypeName, false));
            arm.Pattern?.Accept(this);
            // Only the arm's direct expression inherits statement context.
            var statementWhen = _statementWhens.Contains(node) ? arm.Body as WhenExpr : null;
            if (statementWhen != null) _statementWhens.Add(statementWhen);
            TypeRef? t;
            try { t = (TypeRef?)arm.Body.Accept(this); }
            finally
            {
                if (statementWhen != null) _statementWhens.Remove(statementWhen);
                _symbols.ExitScope();
            }
            first ??= t;
        }
        return first ?? new TypeRef("Any", false);
    }

    public object? Visit(MapLiteralExpr node)
    {
        foreach (var (k, v) in node.Entries)
        {
            k.Accept(this);
            v.Accept(this);
        }
        return new TypeRef("Map<Any, Any>", false);
    }

    public object? Visit(AwaitExpr node)
    {
        if (!_isAsyncFunction) Error(node, "'await' is only allowed inside an async function");
        return node.Operand.Accept(this);
    }

    public object? Visit(NamedArgExpr node)
    {
        return node.Value.Accept(this);
    }

    public object? Visit(IsPatternExpr node)
    {
        return new TypeRef("Bool", false);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void CheckReturnPaths(AstNode declaration, Block body, TypeRef returnType)
    {
        if (returnType.Name != "Unit" && !AlwaysReturns(body))
            Error(declaration, $"Not all paths return a value of type '{returnType.Name}'");
    }

    private static bool AlwaysReturns(AstNode node) => node switch
    {
        ReturnStmt => true,
        Block block => block.Statements.Any(AlwaysReturns),
        IfStmt conditional => AlwaysReturns(conditional.Then)
            && conditional.Else != null && AlwaysReturns(conditional.Else),
        // Loops may execute zero times; lambda returns belong to the lambda.
        _ => false,
    };

    private TypeRef? CheckArguments(AstNode call, IReadOnlyList<Expr> arguments, MemberInfo? signature)
    {
        var parameters = signature?.Parameters;
        var supplied = new HashSet<int>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var substitutions = new Dictionary<string, TypeRef>(StringComparer.Ordinal);
        var sawNamed = false;
        var malformed = false;
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var actual = (TypeRef?)argument.Accept(this);
            var index = i;
            if (argument is NamedArgExpr named)
            {
                sawNamed = true;
                if (!names.Add(named.Name))
                {
                    Error(argument, $"Duplicate argument '{named.Name}'");
                    malformed = true;
                    continue;
                }
                if (parameters != null)
                {
                    index = -1;
                    for (var p = 0; p < parameters.Count; p++)
                        if (parameters[p].Name == named.Name) { index = p; break; }
                    if (index < 0)
                    {
                        Error(argument, $"Unknown named argument '{named.Name}'");
                        malformed = true;
                        continue;
                    }
                }
            }
            else if (sawNamed)
            {
                Error(argument, "Positional argument cannot follow a named argument");
                malformed = true;
            }

            if (parameters == null || index >= parameters.Count) continue;
            var parameter = parameters[index];
            if (!supplied.Add(index))
            {
                Error(argument, $"Duplicate argument '{parameter.Name}'");
                malformed = true;
                continue;
            }
            if (actual == null) continue;
            InferTypeArguments(parameter.Type, actual, signature?.TypeParams, substitutions);
            var expected = SubstituteType(parameter.Type, substitutions);
            if (!IsCompatible(expected, actual))
                Error(argument, $"Type mismatch for argument '{parameter.Name}': expected '{DisplayType(expected)}' but found '{DisplayType(actual)}'");
        }

        if (parameters != null && !malformed
            && (arguments.Count > parameters.Count
                || parameters.Where((p, i) => p.Default == null && !supplied.Contains(i)).Any()))
            Error(call, $"Expected {parameters.Count} arguments but found {arguments.Count} (required parameters must be supplied)");
        return signature == null ? null : SubstituteType(signature.Type, substitutions);
    }

    private static string DisplayType(TypeRef type) => type.Name + (type.Nullable ? "?" : "");

    private static TypeRef SubstituteType(TypeRef type, IReadOnlyDictionary<string, TypeRef> substitutions)
    {
        if (substitutions.TryGetValue(type.Name, out var replacement))
            return replacement with { Nullable = type.Nullable || replacement.Nullable };
        if (type.Name.EndsWith("[]"))
            return type with { Name = DisplayType(SubstituteType(new TypeRef(type.Name[..^2], false), substitutions)) + "[]" };
        var (name, arguments) = TypeShape(type);
        return arguments.Count == 0 ? type : type with
        {
            Name = $"{name}<{string.Join(", ", arguments.Select(a => DisplayType(SubstituteType(a, substitutions))))}>"
        };
    }

    // Keep the existing TypeRef representation while reading nested generic shapes
    // in one place for inference, substitution and member lookup.
    private static (string Name, List<TypeRef> Arguments) TypeShape(TypeRef type)
    {
        var open = type.Name.IndexOf('<');
        if (open < 0 || !type.Name.EndsWith('>')) return (type.Name, []);
        var arguments = new List<TypeRef>();
        var depth = 0;
        var start = open + 1;
        for (var i = start; i < type.Name.Length; i++)
        {
            var ch = type.Name[i];
            if ((ch == ',' && depth == 0) || i == type.Name.Length - 1)
            {
                var argument = type.Name[start..i].Trim();
                arguments.Add(new TypeRef(argument.TrimEnd('?'), argument.EndsWith('?')));
                start = i + 1;
            }
            else if (ch == '<') depth++;
            else if (ch == '>') depth--;
        }
        return (type.Name[..open], arguments);
    }

    private static void InferTypeArguments(TypeRef expected, TypeRef actual, IReadOnlyList<string>? typeParams,
        Dictionary<string, TypeRef> substitutions)
    {
        if (typeParams == null) return;
        if (typeParams.Contains(expected.Name))
        {
            substitutions.TryAdd(expected.Name, actual with { Nullable = actual.Nullable && !expected.Nullable });
            return;
        }
        if (expected.Name.EndsWith("[]") && actual.Name.EndsWith("[]"))
        {
            InferTypeArguments(new TypeRef(expected.Name[..^2], false), new TypeRef(actual.Name[..^2], false), typeParams, substitutions);
            return;
        }
        var expectedShape = TypeShape(expected);
        var actualShape = TypeShape(actual);
        if (expectedShape.Name != actualShape.Name || expectedShape.Arguments.Count != actualShape.Arguments.Count) return;
        for (var i = 0; i < expectedShape.Arguments.Count; i++)
            InferTypeArguments(expectedShape.Arguments[i], actualShape.Arguments[i], typeParams, substitutions);
    }

    private MemberInfo? AnalyzeMember(AstNode node, Expr target, string name, bool safe, bool isCall = false)
    {
        if (LooksLikeExternalStaticAccess(target)) return new MemberInfo(new TypeRef("Any", safe));
        var targetType = (TypeRef?)target.Accept(this);
        if (targetType == null) return null; // A prior diagnostic already explains the unresolved target.
        var member = ResolveMember(targetType, name, isCall);
        if (member == null)
        {
            Error(node, $"Unknown member '{name}' on type '{targetType.Name}'");
            return null;
        }
        return safe ? member with { Type = member.Type with { Nullable = true } } : member;
    }

    private MemberInfo? ResolveMember(TypeRef target, string member, bool isCall)
    {
        // Dynamic values are explicit or originate at an external API boundary.
        // Neither an import nor generic/array syntax makes a typed value dynamic.
        if (target.Name == "Any") return new MemberInfo(new TypeRef("Any", false));
        var targetShape = TypeShape(target);
        var declaration = _symbols.ResolveType(targetShape.Name)?.Metadata;
        if (declaration is StructDecl sd)
        {
            var property = sd.Properties.FirstOrDefault(p => p.Name == member);
            if (property != null) return new MemberInfo(property.Type);
        }
        if (declaration is InterfaceDecl id)
        {
            var method = id.Methods.FirstOrDefault(m => m.Name == member);
            if (method != null)
            {
                var bindings = id.TypeParams.Zip(targetShape.Arguments)
                    .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
                return new MemberInfo(SubstituteType(method.ReturnType ?? new TypeRef("Unit", false), bindings),
                    method.Parameters.Select(p => p with { Type = SubstituteType(p.Type, bindings) }).ToArray());
            }
        }
        var implementation = _implementations.Where(i => i.TypeName == target.Name)
            .SelectMany(i => i.Methods).FirstOrDefault(m => m.Name == member);
        if (implementation != null)
            return new MemberInfo(implementation.ReturnType ?? new TypeRef("Unit", false),
                implementation.Parameters, implementation.TypeParams);
        foreach (var extension in _extensions.Where(e => e.MethodName == member))
        {
            var bindings = new Dictionary<string, TypeRef>(StringComparer.Ordinal);
            var receiver = new TypeRef(extension.ReceiverType, false);
            InferTypeArguments(receiver, target, extension.TypeParams, bindings);
            if (!IsCompatible(SubstituteType(receiver, bindings), target with { Nullable = false })) continue;
            return new MemberInfo(SubstituteType(extension.ReturnType ?? new TypeRef("Unit", false), bindings),
                extension.Parameters.Select(p => p with { Type = SubstituteType(p.Type, bindings) }).ToArray(), extension.TypeParams);
        }
        // Language records and interfaces retain the standard object methods.
        if (declaration != null)
            return member switch
            {
                "toString" or "ToString" => new MemberInfo(new TypeRef("String", false), []),
                "getHashCode" or "GetHashCode" => new MemberInfo(new TypeRef("Int", false), []),
                "equals" or "Equals" => new MemberInfo(new TypeRef("Bool", false), [new Parameter("obj", new TypeRef("Any", true))]),
                _ => null,
            };

        var runtimeType = targetShape.Name switch
        {
            "String" => typeof(string), "Int" => typeof(int), "Double" => typeof(double),
            "Bool" => typeof(bool), "Long" => typeof(long), "Float" => typeof(float),
            "List" when targetShape.Arguments.Count == 1 => typeof(IReadOnlyList<>),
            "MutableList" when targetShape.Arguments.Count == 1 => typeof(List<>),
            "Map" when targetShape.Arguments.Count == 2 => typeof(IReadOnlyDictionary<,>),
            "MutableMap" when targetShape.Arguments.Count == 2 => typeof(Dictionary<,>),
            _ when target.Name.EndsWith("[]") => typeof(Array),
            _ => null,
        };
        if (runtimeType != null)
        {
            var clrName = char.ToUpperInvariant(member[0]) + member[1..];
            if (isCall)
            {
                var extension = ResolveCollectionExtension(target, clrName);
                if (extension != null) return extension;
                // System.Linq is always imported by the generator. Array Count
                // (including its predicate overload) is a known Enumerable API.
                if (target.Name.EndsWith("[]") && clrName == "Count")
                    return new MemberInfo(new TypeRef("Int", false));
            }
            var bindings = runtimeType.GetGenericArguments().Zip(targetShape.Arguments)
                .ToDictionary(pair => pair.First.Name, pair => pair.Second, StringComparer.Ordinal);
            var surfaces = runtimeType.IsInterface
                ? runtimeType.GetInterfaces().Prepend(runtimeType).Append(typeof(object)) : [runtimeType];
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            var property = surfaces.SelectMany(t => t.GetProperties(flags)).FirstOrDefault(p => p.Name == clrName);
            if (property != null)
                return new MemberInfo(RuntimeTypeRef(property.PropertyType, bindings));
            var methods = surfaces.SelectMany(t => t.GetMethods(flags)).Where(m => m.Name == clrName).ToArray();
            if (methods.Length > 0)
            {
                // Overload argument validation is still deferred, but a concrete
                // shared return type must not become Any (e.g. String.Trim).
                var results = methods.Select(m => RuntimeTypeRef(m.ReturnType, bindings)).Distinct().ToArray();
                if (results.Length == 1) return new MemberInfo(results[0]);
            }
            return ResolveCollectionExtension(target, clrName);
        }

        // Only constructor-observed external types retain opaque member lookup.
        if (_externalTypes.Contains(target.Name))
            return new MemberInfo(new TypeRef("Any", false));
        return null;
    }

    private static TypeRef RuntimeTypeRef(Type type, IReadOnlyDictionary<string, TypeRef> bindings)
    {
        if (type.IsGenericParameter)
            return bindings.TryGetValue(type.Name, out var bound) ? bound : new TypeRef(type.Name, false);
        if (type.IsArray) return new TypeRef(DisplayType(RuntimeTypeRef(type.GetElementType()!, bindings)) + "[]", false);
        if (Nullable.GetUnderlyingType(type) is Type inner) return RuntimeTypeRef(inner, bindings) with { Nullable = true };
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var name = definition == typeof(IReadOnlyList<>) ? "List"
                : definition == typeof(List<>) ? "MutableList"
                : definition == typeof(IReadOnlyDictionary<,>) ? "Map"
                : definition == typeof(Dictionary<,>) ? "MutableMap" : type.Name.Split('`')[0];
            return new TypeRef($"{name}<{string.Join(", ", type.GetGenericArguments().Select(a => DisplayType(RuntimeTypeRef(a, bindings))))}>", false);
        }
        return new TypeRef(type == typeof(string) ? "String" : type == typeof(int) ? "Int"
            : type == typeof(bool) ? "Bool" : type == typeof(double) ? "Double"
            : type == typeof(long) ? "Long" : type == typeof(float) ? "Float"
            : type == typeof(void) ? "Unit" : type == typeof(object) ? "Any" : type.Name, false);
    }

    private MemberInfo? ResolveCollectionExtension(TypeRef target, string member)
    {
        if (!_usedNamespaces.Contains("ksr.collections")) return null;
        var (name, arguments) = TypeShape(target);
        // These named APIs are declared in sdk/KSR.StdLib/Collections.cs. Unknown
        // members never get an interop fallback. Lambda-dependent generic results
        // retain their container shape while only the uninferred element is Any.
        if ((name is "List" or "MutableList" && arguments.Count == 1) || target.Name.EndsWith("[]"))
        {
            var element = arguments.Count == 1 ? arguments[0] : new TypeRef(target.Name[..^2], false);
            var list = new TypeRef($"List<{DisplayType(element)}>", false);
            TypeRef? result = member switch
            {
                "Filter" or "Sorted" or "SortedBy" or "SortedByDescending" or "Reversed"
                    or "Take" or "Drop" or "TakeWhile" or "DropWhile" or "Distinct" or "Concat" or "Plus" => list,
                "First" or "Last" or "Get" => element,
                "Find" => element with { Nullable = true },
                "Size" or "Count" or "Sum" => new TypeRef("Int", false),
                "SumLong" => new TypeRef("Long", false),
                "SumDouble" => new TypeRef("Double", false),
                "Min" or "Max" => new TypeRef("Int", true),
                "MinDouble" or "MaxDouble" => new TypeRef("Double", true),
                "Any" or "All" or "None" or "IsEmpty" or "Contains" => new TypeRef("Bool", false),
                "ForEach" or "ForEachIndexed" => new TypeRef("Unit", false),
                "JoinToString" => new TypeRef("String", false),
                "ToMutable" => new TypeRef($"MutableList<{DisplayType(element)}>", false),
                "Map" or "FlatMap" => new TypeRef("List<Any>", false),
                "Flatten" when TypeShape(element) is { Name: "List", Arguments.Count: 1 } => element,
                "GroupBy" => new TypeRef($"Map<Any, {list.Name}>", false),
                "Zip" => new TypeRef($"List<ValueTuple<{DisplayType(element)}, Any>>", false),
                "Fold" => new TypeRef("Any", false), // Result depends on the callback/initial value.
                _ => null,
            };
            return result == null ? null : new MemberInfo(result);
        }
        if (name is "Map" or "MutableMap" && arguments.Count == 2)
        {
            var key = arguments[0];
            var value = arguments[1];
            TypeRef? result = member switch
            {
                "Keys" => new TypeRef($"List<{DisplayType(key)}>", false),
                "Values" => new TypeRef($"List<{DisplayType(value)}>", false),
                "ContainsKey" or "IsEmpty" => new TypeRef("Bool", false),
                "Get" => value with { Nullable = true },
                "GetOrDefault" => value,
                "Size" => new TypeRef("Int", false),
                "Filter" => new TypeRef($"Map<{DisplayType(key)}, {DisplayType(value)}>", false),
                "MapValues" => new TypeRef($"Map<{DisplayType(key)}, Any>", false),
                "ToMutable" => new TypeRef($"MutableMap<{DisplayType(key)}, {DisplayType(value)}>", false),
                "ForEach" => new TypeRef("Unit", false),
                _ => null,
            };
            return result == null ? null : new MemberInfo(result);
        }
        return null;
    }

    private bool LooksLikeExternalStaticAccess(Expr target)
    {
        if (_usedNamespaces.Count == 0)
            return false;

        return target switch
        {
            IdentifierExpr id => IsKnownExternalRoot(id.Name) && _symbols.Resolve(id.Name) is null,
            MemberAccessExpr ma => LooksLikeExternalStaticAccess(ma.Target),
            SafeCallExpr sc => LooksLikeExternalStaticAccess(sc.Target),
            _ => false,
        };
    }

    private bool IsKnownExternalRoot(string name) =>
        IsUppercaseIdentifier(name) ||
        (name == "draw" && _usedNamespaces.Contains("KSR.Creative"));

    private static bool IsUppercaseIdentifier(string name) =>
        name.Length > 0 && char.IsUpper(name[0]);

    private bool IsCompatible(TypeRef target, TypeRef source)
    {
        if (target.Name == "Any") return true;
        if (source.Name == "Any")
        {
            // 'null' (Any?) is compatible with any nullable target
            if (source.Nullable && target.Nullable) return true;
            // Non-null Any is the intentional dynamic representation for external
            // calls and untyped lambda parameters; Any? represents the null literal.
            return !source.Nullable;
        }

        if (target.Name == source.Name)
        {
            if (!target.Nullable && source.Nullable) return false;
            return true;
        }
        if (!target.Nullable && source.Nullable) return false;

        var targetShape = TypeShape(target);
        var sourceShape = TypeShape(source);
        if (targetShape.Name == sourceShape.Name
            && targetShape.Arguments.Count == sourceShape.Arguments.Count
            && targetShape.Name == "List")
        {
            return targetShape.Arguments.Zip(sourceShape.Arguments)
                .All(pair => IsCompatible(pair.First, pair.Second));
        }

        if (_sealedBases.TryGetValue(source.Name, out var sealedBase) && sealedBase == target.Name) return true;

        if (_implementations.Any(i =>
        {
            if (i.TypeName != source.Name || i.InterfaceName != targetShape.Name)
                return false;
            if (i.InterfaceTypeArgs.Count == 0)
                return targetShape.Arguments.Count == 0;
            return i.InterfaceTypeArgs.Count == targetShape.Arguments.Count
                && i.InterfaceTypeArgs.Zip(targetShape.Arguments)
                    .All(pair => pair.First == DisplayType(pair.Second));
        })) return true;

        return false;
    }
}
