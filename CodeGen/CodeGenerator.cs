using System.Text;
using KSR.AST;

namespace KSR.CodeGen;

/// <summary>
/// Walks a KSR AST and emits valid C# source code using the Visitor pattern.
/// </summary>
public class CodeGenerator : IAstVisitor<string>
{
    private readonly StringBuilder _out = new();
    private int _indent = 0;

    // Names declared as structs — used to emit 'new Foo(…)'
    private readonly HashSet<string> _structs = new();

    // Names declared as interfaces — used to prefix with 'I' in type references
    private readonly HashSet<string> _interfaces = new();

    // Sealed type names — abstract base records; never I-prefixed
    private readonly HashSet<string> _sealedTypes = new();

    // impl blocks grouped by target type — collected before emitting
    private readonly Dictionary<string, List<ImplBlock>> _implsByType = new();

    // True while emitting a record method body (this → "this", not "self")
    private bool _inRecordMethod = false;

    // Type parameter names of the function currently being emitted
    private HashSet<string> _currentTypeParams = new();

    private readonly AsyncReturnKind _globalAsyncReturn;
    private bool _inAsyncFunction = false;
    private AsyncReturnKind _currentAsyncReturn = AsyncReturnKind.Task;

    public CodeGenerator(AsyncReturnKind globalAsyncReturn = AsyncReturnKind.Task)
    {
        _globalAsyncReturn = globalAsyncReturn;
    }

    // ── public entry ─────────────────────────────────────────────────────────

    public string Generate(ProgramNode program)
    {
        return program.Accept(this);
    }

    // ── Visit Implementations ────────────────────────────────────────────────

    public string Visit(ProgramNode node)
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
                    _implsByType[ib.TypeName] = list = new List<ImplBlock>();
                list.Add(ib);
            }
        }

        // Preamble
        Line("#nullable enable");
        Line("using System;");
        Line("using System.Collections.Generic;");
        Line("using System.Linq;");
        foreach (var d in node.Declarations)
        {
            if (d is UseDecl ud)
                Line($"using {MapUseNamespace(ud.Namespace)};");
        }
        Blank();

        // Interfaces
        foreach (var d in node.Declarations)
            if (d is InterfaceDecl id) id.Accept(this);

        // Sealed types
        foreach (var d in node.Declarations)
            if (d is SealedDecl sd) sd.Accept(this);

        // Standalone structs
        foreach (var d in node.Declarations)
            if (d is StructDecl dc) dc.Accept(this);

        // Single static class that holds all functions + extension methods
        Line("static class KsrProgram");
        Line("{");
        _indent++;

        foreach (var d in node.Declarations)
        {
            if (d is FunctionDecl fd) fd.Accept(this);
            if (d is ExtFunctionDecl efd) efd.Accept(this);
        }

        _indent--;
        Line("}");

        return _out.ToString();
    }

    public string Visit(InterfaceDecl id)
    {
        var prevTypeParams = _currentTypeParams;
        _currentTypeParams = id.TypeParams.Count > 0 ? new HashSet<string>(id.TypeParams) : new();

        var typeParamStr = id.TypeParams.Count > 0
            ? $"<{string.Join(", ", id.TypeParams)}>"
            : "";

        var whereClause = "";
        if (id.Constraints.Count > 0)
        {
            var parts = id.Constraints.Select(c =>
                $"{c.TypeParam} : {string.Join(", ", c.Bounds.Select(b => MapType(new TypeRef(b, false))))}");
            whereClause = $" where {string.Join(", ", parts)}";
        }

        Line($"interface I{id.Name}{typeParamStr}{whereClause}");
        Line("{");
        _indent++;
        foreach (var m in id.Methods)
        {
            var ret = m.IsAsync
                ? BuildAsyncReturnType(m.ReturnType, EffectiveAsyncReturn(m.AsyncReturn))
                : (m.ReturnType is null ? "void" : MapType(m.ReturnType));
            var parms = string.Join(", ", m.Parameters.Select(p => EmitParam(p)));
            Line($"{ret} {Pascal(m.Name)}({parms});");
        }
        _indent--;
        Line("}");
        Blank();

        _currentTypeParams = prevTypeParams;
        return "";
    }

    public string Visit(SealedDecl sd)
    {
        Line($"abstract record {sd.Name};");
        Blank();

        foreach (var v in sd.Variants)
        {
            var props = v.Properties.Count > 0
                ? $"({string.Join(", ", v.Properties.Select(p => EmitParam(p, pascalName: true)))})"
                : "";

            if (_implsByType.TryGetValue(v.Name, out var impls))
            {
                var ifaces = string.Join(", ", impls.Select(b =>
                {
                    var args = b.InterfaceTypeArgs.Count > 0
                        ? $"<{string.Join(", ", b.InterfaceTypeArgs.Select(a => MapType(new TypeRef(a, false))))}>"
                        : "";
                    return $"I{b.InterfaceName}{args}";
                }));
                Line($"record {v.Name}{props} : {sd.Name}, {ifaces}");
                Line("{");
                _indent++;
                _inRecordMethod = true;
                foreach (var method in impls.SelectMany(b => b.Methods))
                    EmitRecordMethod(method);
                _inRecordMethod = false;
                _indent--;
                Line("}");
            }
            else
            {
                Line($"record {v.Name}{props} : {sd.Name};");
            }
        }
        Blank();
        return "";
    }

    public string Visit(StructDecl dc)
    {
        var props = string.Join(", ",
            dc.Properties.Select(p => EmitParam(p, pascalName: true)));

        if (_implsByType.TryGetValue(dc.Name, out var impls))
        {
            var ifaces = string.Join(", ", impls.Select(b =>
            {
                var args = b.InterfaceTypeArgs.Count > 0
                    ? $"<{string.Join(", ", b.InterfaceTypeArgs.Select(a => MapType(new TypeRef(a, false))))}>"
                    : "";
                return $"I{b.InterfaceName}{args}";
            }));
            Line($"record {dc.Name}({props}) : {ifaces}");
            Line("{");
            _indent++;
            _inRecordMethod = true;
            foreach (var method in impls.SelectMany(b => b.Methods))
                EmitRecordMethod(method);
            _inRecordMethod = false;
            _indent--;
            Line("}");
        }
        else
        {
            Line($"record {dc.Name}({props});");
        }
        Blank();
        return "";
    }

    public string Visit(FunctionDecl fd)
    {
        var prevAsync      = _inAsyncFunction;
        var prevKind       = _currentAsyncReturn;
        var prevTypeParams = _currentTypeParams;
        _inAsyncFunction    = fd.IsAsync;
        _currentAsyncReturn = EffectiveAsyncReturn(fd.AsyncReturn);
        _currentTypeParams  = fd.TypeParams.Count > 0 ? new HashSet<string>(fd.TypeParams) : new();

        var ret = fd.IsAsync
            ? BuildAsyncReturnType(fd.ReturnType, _currentAsyncReturn)
            : (fd.ReturnType is null ? "void" : MapType(fd.ReturnType));
        var asyncMod     = fd.IsAsync ? "async " : "";
        var method       = fd.Name == "main" ? "Main" : fd.Name;
        var typeParamStr = fd.TypeParams.Count > 0 ? $"<{string.Join(", ", fd.TypeParams)}>" : "";
        var parms = string.Join(", ",
            fd.Parameters.Select(p => EmitParam(p)));

        Line($"static {asyncMod}{ret} {method}{typeParamStr}({parms})");
        Line("{");
        _indent++;
        fd.Body.Accept(this);
        if (HasSourceInfo(fd.Body)) Line("#line default");
        _indent--;
        Line("}");
        Blank();

        _inAsyncFunction    = prevAsync;
        _currentAsyncReturn = prevKind;
        _currentTypeParams  = prevTypeParams;
        return "";
    }

    public string Visit(ExtFunctionDecl efd)
    {
        var prevAsync      = _inAsyncFunction;
        var prevKind       = _currentAsyncReturn;
        var prevTypeParams = _currentTypeParams;
        _inAsyncFunction    = efd.IsAsync;
        _currentAsyncReturn = EffectiveAsyncReturn(efd.AsyncReturn);
        _currentTypeParams  = efd.TypeParams.Count > 0 ? new HashSet<string>(efd.TypeParams) : new();

        var ret = efd.IsAsync
            ? BuildAsyncReturnType(efd.ReturnType, _currentAsyncReturn)
            : (efd.ReturnType is null ? "void" : MapType(efd.ReturnType));
        var asyncMod     = efd.IsAsync ? "async " : "";
        var typeParamStr = efd.TypeParams.Count > 0 ? $"<{string.Join(", ", efd.TypeParams)}>" : "";
        var csType       = MapType(new TypeRef(efd.ReceiverType, false));

        var paramParts = new List<string> { $"this {csType} self" };
        paramParts.AddRange(efd.Parameters.Select(p => EmitParam(p)));
        var parms = string.Join(", ", paramParts);

        Line($"public static {asyncMod}{ret} {Pascal(efd.MethodName)}{typeParamStr}({parms})");
        Line("{");
        _indent++;
        efd.Body.Accept(this);
        if (HasSourceInfo(efd.Body)) Line("#line default");
        _indent--;
        Line("}");
        Blank();

        _inAsyncFunction    = prevAsync;
        _currentAsyncReturn = prevKind;
        _currentTypeParams  = prevTypeParams;
        return "";
    }

    public string Visit(Block node)
    {
        foreach (var s in node.Statements)
            s.Accept(this);
        return "";
    }

    public string Visit(UseDecl node) => ""; // Handled in preamble

    public string Visit(ImplBlock node) => ""; // Handled in metadata pass

    // ── Statements ───────────────────────────────────────────────────────────

    private void MarkLine(Stmt stmt)
    {
        if (stmt.Line > 0 && !string.IsNullOrEmpty(stmt.SourceFile))
        {
            var path = stmt.SourceFile.Replace('\\', '/');
            Line($"#line {stmt.Line} \"{path}\"");
        }
    }

    public string Visit(ValDecl node)
    {
        MarkLine(node);
        var t = node.Type is null ? "var" : MapType(node.Type);
        Line($"{t} {NameUtils.Escape(node.Name)} = {EmitExprWithHint(node.Value, node.Type)};");
        return "";
    }

    public string Visit(VarDecl node)
    {
        MarkLine(node);
        var t = node.Type is null ? "var" : MapType(node.Type);
        Line($"{t} {NameUtils.Escape(node.Name)} = {EmitExprWithHint(node.Value, node.Type)};");
        return "";
    }

    public string Visit(AssignStmt node)
    {
        MarkLine(node);
        Line($"{NameUtils.Escape(node.Name)} = {node.Value.Accept(this)};");
        return "";
    }

    public string Visit(CompoundAssignStmt node)
    {
        MarkLine(node);
        Line($"{NameUtils.Escape(node.Name)} {node.Op} {node.Value.Accept(this)};");
        return "";
    }

    public string Visit(IndexAssignStmt node)
    {
        MarkLine(node);
        Line($"{NameUtils.Escape(node.Name)}[{node.Index.Accept(this)}] = {node.Value.Accept(this)};");
        return "";
    }

    public string Visit(ReturnStmt node)
    {
        MarkLine(node);
        Line(node.Value is null ? "return;" : $"return {node.Value.Accept(this)};");
        return "";
    }

    public string Visit(IfStmt node)
    {
        MarkLine(node);
        Line($"if ({node.Condition.Accept(this)})");
        Line("{");
        _indent++;
        node.Then.Accept(this);
        _indent--;
        Line("}");
        if (node.Else is not null)
        {
            Line("else");
            Line("{");
            _indent++;
            node.Else.Accept(this);
            _indent--;
            Line("}");
        }
        return "";
    }

    public string Visit(WhileStmt node)
    {
        MarkLine(node);
        Line($"while ({node.Condition.Accept(this)})");
        Line("{");
        _indent++;
        node.Body.Accept(this);
        _indent--;
        Line("}");
        return "";
    }

    public string Visit(ForInStmt node)
    {
        MarkLine(node);
        var escapedVar = NameUtils.Escape(node.VarName);
        if (node.Iterable is RangeExpr re)
        {
            var op = re.Inclusive ? "<=" : "<";
            Line($"for (var {escapedVar} = {re.Start.Accept(this)}; {escapedVar} {op} {re.End.Accept(this)}; {escapedVar}++)");
        }
        else
        {
            Line($"foreach (var {escapedVar} in {node.Iterable.Accept(this)})");
        }
        Line("{");
        _indent++;
        node.Body.Accept(this);
        _indent--;
        Line("}");
        return "";
    }

    public string Visit(ExprStmt node)
    {
        MarkLine(node);
        if (node.Expression is WhenExpr we)
        {
            EmitWhenStmt(we);
        }
        else
        {
            Line($"{node.Expression.Accept(this)};");
        }
        return "";
    }

    // ── Expressions ──────────────────────────────────────────────────────────

    public string Visit(IntLiteral node) => node.Value.ToString();
    public string Visit(DoubleLiteral node) => node.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string Visit(StringLiteral node) => node.IsRaw
        ? $"@\"{node.Value.Replace("\"", "\"\"")}\""
        : $"\"{Escape(node.Value)}\"";
    public string Visit(BoolLiteral node) => node.Value ? "true" : "false";
    public string Visit(NullLiteral node) => "null";
    public string Visit(ThisExpr node) => _inRecordMethod ? "this" : "self";
    public string Visit(IdentifierExpr node) => NameUtils.Escape(node.Name);

    public string Visit(IndexExpr node) => $"{node.Target.Accept(this)}[{node.Index.Accept(this)}]";
    public string Visit(NewArrayExpr node) => $"new {MapType(node.ElementType)}[{node.Size.Accept(this)}]";
    public string Visit(NewObjectExpr node) => $"new {NameUtils.Escape(node.TypeName)}({string.Join(", ", node.Arguments.Select(a => a.Accept(this)))})";

    public string Visit(LambdaExpr node)
    {
        var parameters = node.Params.Count switch
        {
            0 => "()",
            1 => NameUtils.Escape(node.Params[0]),
            _ => $"({string.Join(", ", node.Params.Select(NameUtils.Escape))})",
        };

        if (!node.IsBlockBody)
        {
            var body = node.Body?.Accept(this)
                ?? throw new InvalidOperationException("Expression lambda is missing a body.");
            return $"({parameters} => {body})";
        }

        var previous = _out.Length;
        var savedIndent = _indent;
        var block = node.BlockBody
            ?? throw new InvalidOperationException("Block lambda is missing a block body.");

        var sb = new StringBuilder();
        sb.Append('(').Append(parameters).AppendLine(" =>");
        sb.AppendLine(new string(' ', savedIndent * 4) + "{");
        _indent = savedIndent + 1;
        block.Accept(this);
        var bodyText = _out.ToString(previous, _out.Length - previous);
        _out.Length = previous;
        _indent = savedIndent;
        sb.Append(bodyText);
        sb.Append(new string(' ', savedIndent * 4)).Append("})");
        return sb.ToString();
    }

    public string Visit(BinaryExpr node) => $"({node.Left.Accept(this)} {node.Op} {node.Right.Accept(this)})";
    public string Visit(UnaryExpr node) => $"({node.Op}{node.Operand.Accept(this)})";
    public string Visit(MemberAccessExpr node) => $"{node.Target.Accept(this)}.{Pascal(node.Member)}";
    public string Visit(SafeCallExpr node) => $"{node.Target.Accept(this)}?.{Pascal(node.Member)}";
    public string Visit(ElvisExpr node) => $"({node.Left.Accept(this)} ?? {node.Right.Accept(this)})";

    public string Visit(RangeExpr node)
    {
        var start = node.Start.Accept(this);
        var end = node.End.Accept(this);
        return node.Inclusive
            ? $"Enumerable.Range({start}, {end} - {start} + 1)"
            : $"Enumerable.Range({start}, {end} - {start})";
    }

    public string Visit(StringTemplateExpr node)
    {
        var prefix = node.IsRaw ? "$@\"" : "$\"";
        var sb = new StringBuilder(prefix);
        foreach (var part in node.Parts)
        {
            if (part is LiteralPart lp)
            {
                if (node.IsRaw)
                {
                    sb.Append(lp.Text.Replace("\"", "\"\"").Replace("{", "{{").Replace("}", "}}"));
                }
                else
                {
                    sb.Append(lp.Text.Replace("{", "{{").Replace("}", "}}").Replace("\"", "\\\""));
                }
            }
            else if (part is ExprPart ep)
            {
                sb.Append($"{{{ep.Expression.Accept(this)}}}");
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    public string Visit(CallExpr node)
    {
        var args = string.Join(", ", node.Arguments.Select(a => a.Accept(this)));

        if (node.Callee is IdentifierExpr { Name: "println" })
            return $"Console.WriteLine({args})";

        if (node.Callee is IdentifierExpr id && _structs.Contains(id.Name))
            return $"new {NameUtils.Escape(id.Name)}({args})";

        return $"{node.Callee.Accept(this)}({args})";
    }

    public string Visit(ListLiteralExpr node)
    {
        if (node.Elements.Count == 0)
            return "Array.Empty<object>()";

        var items = string.Join(", ", node.Elements.Select(e => e.Accept(this)));
        return $"new[] {{ {items} }}";
    }

    public string Visit(MapLiteralExpr node)
    {
        if (node.Entries.Count == 0)
            return "new Dictionary<object, object>()";

        var keyType = InferPrimitiveType(node.Entries[0].Key) ?? "object";
        var valType = InferPrimitiveType(node.Entries[0].Value) ?? "object";
        var entries = string.Join(", ",
            node.Entries.Select(e => $"[{e.Key.Accept(this)}] = {e.Value.Accept(this)}"));
        return $"new Dictionary<{keyType}, {valType}> {{ {entries} }}";
    }

    public string Visit(WhenExpr node)
    {
        if (node.Subject is not null)
        {
            var subject = node.Subject.Accept(this);
            var arms = node.Arms.Select(arm =>
            {
                var body = arm.Body.Accept(this);
                return arm.Pattern switch
                {
                    null                                       => $"_ => {body}",
                    IsPatternExpr { Binding: not null } ip    => $"{NameUtils.Escape(ip.TypeName)} {NameUtils.Escape(ip.Binding)} => {body}",
                    IsPatternExpr ip                          => $"{NameUtils.Escape(ip.TypeName)} => {body}",
                    _                                         => $"{arm.Pattern.Accept(this)} => {body}"
                };
            });
            return $"({subject} switch {{ {string.Join(", ", arms)} }})";
        }
        return EmitWhenTernary(node.Arms, 0);
    }

    public string Visit(AwaitExpr node)
    {
        if (!_inAsyncFunction)
            Line("#error KSR: 'await' used outside an async function");
        return $"(await {node.Operand.Accept(this)})";
    }

    public string Visit(NamedArgExpr node) => $"{NameUtils.Escape(node.Name)}: {node.Value.Accept(this)}";

    public string Visit(IsPatternExpr node) => node.Binding is not null
        ? $"{NameUtils.Escape(node.TypeName)} {NameUtils.Escape(node.Binding)}"
        : NameUtils.Escape(node.TypeName);

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void EmitRecordMethod(FunctionDecl fd)
    {
        var prevAsync      = _inAsyncFunction;
        var prevKind       = _currentAsyncReturn;
        var prevTypeParams = _currentTypeParams;
        _inAsyncFunction    = fd.IsAsync;
        _currentAsyncReturn = EffectiveAsyncReturn(fd.AsyncReturn);
        _currentTypeParams  = fd.TypeParams.Count > 0 ? new HashSet<string>(fd.TypeParams) : new();

        var ret = fd.IsAsync
            ? BuildAsyncReturnType(fd.ReturnType, _currentAsyncReturn)
            : (fd.ReturnType is null ? "void" : MapType(fd.ReturnType));
        var asyncMod    = fd.IsAsync ? "async " : "";
        var typeParamStr = fd.TypeParams.Count > 0 ? $"<{string.Join(", ", fd.TypeParams)}>" : "";
        var parms = string.Join(", ",
            fd.Parameters.Select(p => EmitParam(p)));

        Line($"public {asyncMod}{ret} {Pascal(fd.Name)}{typeParamStr}({parms})");
        Line("{");
        _indent++;
        fd.Body.Accept(this);
        if (HasSourceInfo(fd.Body)) Line("#line default");
        _indent--;
        Line("}");
        Blank();

        _inAsyncFunction    = prevAsync;
        _currentAsyncReturn = prevKind;
        _currentTypeParams  = prevTypeParams;
    }

    private void EmitWhenStmt(WhenExpr we)
    {
        bool first = true;
        var subject = we.Subject is not null ? we.Subject.Accept(this) : null;

        foreach (var arm in we.Arms)
        {
            if (arm.Pattern is null) // else arm
            {
                Line("else");
            }
            else
            {
                string cond;
                if (arm.Pattern is IsPatternExpr ip)
                {
                    cond = ip.Binding is not null
                        ? $"{subject} is {NameUtils.Escape(ip.TypeName)} {NameUtils.Escape(ip.Binding)}"
                        : $"{subject} is {NameUtils.Escape(ip.TypeName)}";
                }
                else
                {
                    cond = subject is not null
                        ? $"{subject} == {arm.Pattern.Accept(this)}"
                        : arm.Pattern.Accept(this);
                }
                Line(first ? $"if ({cond})" : $"else if ({cond})");
                first = false;
            }
            Line("{");
            _indent++;
            Line($"{arm.Body.Accept(this)};");
            _indent--;
            Line("}");
        }
    }

    private string EmitWhenTernary(List<WhenArm> arms, int i)
    {
        if (i >= arms.Count)
            return "throw new InvalidOperationException(\"when: no arm matched\")";
        var arm = arms[i];
        if (arm.Pattern is null) return arm.Body.Accept(this); // else arm
        return $"({arm.Pattern.Accept(this)} ? {arm.Body.Accept(this)} : {EmitWhenTernary(arms, i + 1)})";
    }

    private string EmitExprWithHint(Expr expr, TypeRef? hint)
    {
        if (hint is not null)
        {
            var outerName = hint.Name.Contains('<') ? hint.Name[..hint.Name.IndexOf('<')] : hint.Name;

            if (expr is ListLiteralExpr ll)
            {
                if (ll.Elements.Count == 0)
                {
                    if (outerName == "MutableList")
                        return $"new {MapType(hint)}()";
                    if (outerName == "List")
                    {
                        var argStr = hint.Name[(hint.Name.IndexOf('<') + 1)..^1];
                        var csArg = MapType(new TypeRef(argStr.Trim(), false));
                        return $"Array.Empty<{csArg}>()";
                    }
                    if (outerName == "Map" || outerName == "MutableMap")
                        return $"new {MapTypeForNew(hint)}()";
                }
                else if (outerName == "MutableList")
                {
                    var items = string.Join(", ", ll.Elements.Select(e => e.Accept(this)));
                    return $"new {MapType(hint)} {{ {items} }}";
                }
            }

            if (expr is MapLiteralExpr { Entries.Count: 0 })
                return $"new {MapTypeForNew(hint)}()";
        }
        return expr.Accept(this);
    }

    private string MapType(TypeRef t)
    {
        if (t.Name.EndsWith("[]"))
        {
            var elem = MapType(new TypeRef(t.Name[..^2], false));
            return t.Nullable ? $"{elem}[]?" : $"{elem}[]";
        }

        var angleIdx = t.Name.IndexOf('<');
        if (angleIdx >= 0)
        {
            var outer = t.Name[..angleIdx];
            var argsStr = t.Name[(angleIdx + 1)..^1];
            var args = SplitTypeArgs(argsStr)
                               .Select(a => MapType(new TypeRef(a.Trim(), false)));
            var csOuter = outer switch
            {
                "List"        => "IReadOnlyList",
                "MutableList" => "List",
                "Map"         => "IReadOnlyDictionary",
                "MutableMap"  => "Dictionary",
                _ => _currentTypeParams.Contains(outer) ? NameUtils.Escape(outer)
                   : _interfaces.Contains(outer) ? "I" + NameUtils.Escape(outer)
                   : NameUtils.Escape(outer)
            };
            var result = $"{csOuter}<{string.Join(", ", args)}>";
            return t.Nullable ? $"{result}?" : result;
        }

        var base_ = t.Name switch
        {
            "Int"    => "int",
            "String" => "string",
            "Bool"   => "bool",
            "Double" => "double",
            "Float"  => "float",
            "Long"   => "long",
            "Unit"   => "void",
            _ => _currentTypeParams.Contains(t.Name) ? NameUtils.Escape(t.Name)
               : _interfaces.Contains(t.Name) ? "I" + NameUtils.Escape(t.Name)
               : NameUtils.Escape(t.Name)
        };
        return t.Nullable ? $"{base_}?" : base_;
    }

    private string MapTypeForNew(TypeRef t)
    {
        var angleIdx = t.Name.IndexOf('<');
        if (angleIdx >= 0)
        {
            var outer = t.Name[..angleIdx];
            var argsStr = t.Name[(angleIdx + 1)..^1];
            var args = SplitTypeArgs(argsStr).Select(a => MapType(new TypeRef(a.Trim(), false)));
            var csOuter = outer switch
            {
                "List" or "MutableList" => "List",
                "Map" or "MutableMap"   => "Dictionary",
                _                       => NameUtils.Escape(outer)
            };
            var result = $"{csOuter}<{string.Join(", ", args)}>";
            return t.Nullable ? $"{result}?" : result;
        }
        return MapType(t);
    }

    private IEnumerable<string> SplitTypeArgs(string args)
    {
        var result = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == '<') depth++;
            else if (args[i] == '>') depth--;
            else if (args[i] == ',' && depth == 0)
            {
                result.Add(args[start..i]);
                start = i + 1;
            }
        }
        result.Add(args[start..]);
        return result;
    }

    private static bool HasSourceInfo(Block block) =>
        block.Statements.Any(s => s.Line > 0 && !string.IsNullOrEmpty(s.SourceFile));

    private void Line(string text) =>
        _out.AppendLine(new string(' ', _indent * 4) + text);

    private void Blank() => _out.AppendLine();

    private static string MapUseNamespace(string ns) => ns switch
    {
        "ksr.io"          => "KSR.Io",
        "ksr.text"        => "KSR.Text",
        "ksr.collections" => "KSR.Collections",
        _                 => ns,
    };

    private static string Pascal(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private string EmitParam(Parameter p, bool pascalName = false)
    {
        var name = pascalName ? Pascal(p.Name) : NameUtils.Escape(p.Name);
        var type = MapType(p.Type);
        if (p.Default is null) return $"{type} {name}";
        return $"{type} {name} = {p.Default.Accept(this)}";
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\n", "\\n")
         .Replace("\r", "\\r")
         .Replace("\t", "\\t");


    private static string? InferPrimitiveType(Expr e) => e switch
    {
        IntLiteral => "int",
        DoubleLiteral => "double",
        BoolLiteral => "bool",
        StringLiteral => "string",
        StringTemplateExpr => "string",
        _ => null
    };

    private AsyncReturnKind EffectiveAsyncReturn(AsyncReturnKind perFunction) =>
        perFunction == AsyncReturnKind.ValueTask || _globalAsyncReturn == AsyncReturnKind.ValueTask
            ? AsyncReturnKind.ValueTask
            : AsyncReturnKind.Task;

    private string BuildAsyncReturnType(TypeRef? innerType, AsyncReturnKind kind)
    {
        var wrapper = kind == AsyncReturnKind.ValueTask ? "ValueTask" : "Task";
        return innerType is null ? wrapper : $"{wrapper}<{MapType(innerType)}>";
    }
}
