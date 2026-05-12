using KSR.AST;

namespace KSR.Semantic;

public class SemanticAnalyzer : IAstVisitor<object?>
{
    private readonly SymbolTable _symbols = new();
    private readonly List<string> _errors = new();
    private readonly HashSet<string> _usedNamespaces = new(StringComparer.Ordinal);
    private string _currentFile = "";
    private TypeRef? _currentReturnType;

    public IReadOnlyList<string> Errors => _errors;

    public SemanticAnalyzer()
    {
        // Register built-ins
        _symbols.Declare("println", SymbolKind.Function, false, 
            new FunctionDecl("println", [], [new Parameter("text", new TypeRef("Any", true))], null, new Block([])));
    }

    public void Analyze(ProgramNode program, string sourceFile = "")
    {
        _currentFile = sourceFile;
        _usedNamespaces.Clear();
        foreach (var use in program.Declarations.OfType<UseDecl>())
            _usedNamespaces.Add(use.Namespace);
        program.Accept(this);
    }

    private void Error(AstNode node, string message)
    {
        int line = 0;
        if (node is Stmt s) line = s.Line;
        _errors.Add($"{_currentFile}({line},0): error: {message}");
    }

    public object? Visit(ProgramNode node)
    {
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
        foreach (var m in node.Methods) m.Accept(this);
        return null;
    }
    public object? Visit(UseDecl node) => null;
    public object? Visit(StructDecl node) => null;
    public object? Visit(SealedDecl node) => null;

    public object? Visit(FunctionDecl fd)
    {
        var previousReturnType = _currentReturnType;
        _currentReturnType = fd.ReturnType ?? new TypeRef("Unit", false);

        _symbols.EnterScope();
        foreach (var p in fd.Parameters)
        {
            if (!_symbols.Declare(p.Name, SymbolKind.Parameter, false, p.Type))
                Error(fd, $"Duplicate parameter name '{p.Name}' in function '{fd.Name}'");
        }
        VisitFunctionBody(fd.Body);
        _symbols.ExitScope();

        _currentReturnType = previousReturnType;
        return null;
    }

    public object? Visit(ExtFunctionDecl efd)
    {
        var previousReturnType = _currentReturnType;
        _currentReturnType = efd.ReturnType ?? new TypeRef("Unit", false);

        _symbols.EnterScope();
        _symbols.Declare("this", SymbolKind.Parameter, false, new TypeRef(efd.ReceiverType, false));
        foreach (var p in efd.Parameters)
        {
            if (!_symbols.Declare(p.Name, SymbolKind.Parameter, false, p.Type))
                Error(efd, $"Duplicate parameter name '{p.Name}' in extension function '{efd.MethodName}'");
        }
        VisitFunctionBody(efd.Body);
        _symbols.ExitScope();

        _currentReturnType = previousReturnType;
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
        node.Expression.Accept(this);
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
            Error(new ExprStmt(node) { Line = 0 }, "'this' is only available in extension functions or record methods");
            return new TypeRef("Any", false);
        }
        return sym.Metadata as TypeRef ?? new TypeRef("Any", false);
    }

    public object? Visit(IdentifierExpr node)
    {
        var sym = _symbols.Resolve(node.Name);
        if (sym == null)
        {
            Error(new ExprStmt(node) { Line = 0 }, $"Undefined identifier '{node.Name}'");
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
        foreach (var a in node.Arguments) a.Accept(this);

        if (node.Callee is IdentifierExpr id)
        {
            var sym = _symbols.Resolve(id.Name);
            if (sym?.Metadata is FunctionDecl fd)
            {
                if (node.Arguments.Count != fd.Parameters.Count)
                    Error(new ExprStmt(node) { Line = 0 }, $"Expected {fd.Parameters.Count} arguments but found {node.Arguments.Count}");
                return fd.ReturnType;
            }
            if (sym?.Metadata is StructDecl) return new TypeRef(id.Name, false);
            if (sym?.Metadata is InterfaceDecl) return new TypeRef(id.Name, false);
        }
        
        node.Callee.Accept(this);
        return new TypeRef("Any", false);
    }

    public object? Visit(MemberAccessExpr node)
    {
        if (LooksLikeExternalStaticAccess(node.Target))
            return new TypeRef("Any", false);

        var targetType = (TypeRef?)node.Target.Accept(this);
        if (targetType != null)
        {
            var memberType = ResolveMemberType(targetType, node.Member);
            return memberType ?? new TypeRef("Any", false);
        }
        return new TypeRef("Any", false); 
    }

    public object? Visit(SafeCallExpr node)
    {
        if (LooksLikeExternalStaticAccess(node.Target))
            return new TypeRef("Any", true);

        var targetType = (TypeRef?)node.Target.Accept(this);
        if (targetType != null)
        {
            var memberType = ResolveMemberType(targetType, node.Member);
            return memberType != null ? memberType with { Nullable = true } : new TypeRef("Any", true);
        }
        return new TypeRef("Any", true);
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
        }
    }

    public object? Visit(NewObjectExpr node)
    {
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
        node.Subject?.Accept(this);
        TypeRef? first = null;
        foreach (var arm in node.Arms)
        {
            arm.Pattern?.Accept(this);
            var t = (TypeRef?)arm.Body.Accept(this);
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

    private TypeRef? ResolveMemberType(TypeRef target, string member)
    {
        var sym = _symbols.Resolve(target.Name);
        if (sym?.Metadata is StructDecl sd)
        {
            var prop = sd.Properties.FirstOrDefault(p => p.Name == member);
            if (prop != null) return prop.Type;
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
            // 'Any' (non-null) is only compatible if target is 'Any'
            return target.Name == "Any";
        }

        if (target.Name == source.Name)
        {
            if (!target.Nullable && source.Nullable) return false;
            return true;
        }
        return false;
    }
}
