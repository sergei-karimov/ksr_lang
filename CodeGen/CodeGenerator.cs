using System.Text;
using KSR.AST;

namespace KSR.CodeGen;

/// <summary>
/// Walks a KSR AST and emits valid C# source code.
///
/// Design notes (Rust-like, no inheritance):
///   • data class  → C# positional record  (sealed value type, no base classes)
///   • Extension functions go into KsrProgram as public static extension methods
///   • 'this' inside extension bodies compiles to 'self' (the receiver parameter)
///   • val / var both compile to 'var' (C# has no readonly locals)
/// </summary>
public class CodeGenerator
{
    private readonly StringBuilder _out = new();
    private int _indent = 0;

    // Names declared as data classes — used to emit 'new Foo(…)'
    private readonly HashSet<string> _dataClasses = new();

    // Names declared as interfaces — used to prefix with 'I' in type references
    private readonly HashSet<string> _interfaces = new();

    // impl blocks grouped by target type — collected before emitting
    private readonly Dictionary<string, List<ImplBlock>> _implsByType = new();

    // True while emitting a record method body (this → "this", not "self")
    private bool _inRecordMethod = false;

    // Type parameter names of the function currently being emitted — prevents
    // MapType from accidentally prefixing 'I' on a single-letter type param.
    private HashSet<string> _currentTypeParams = new();

    // ── async state ───────────────────────────────────────────────────────────

    /// <summary>Global default: Task (default) or ValueTask (--async-return=valuetask).</summary>
    private readonly AsyncReturnKind _globalAsyncReturn;

    /// <summary>True while emitting the body of an async function.</summary>
    private bool _inAsyncFunction = false;

    /// <summary>
    /// Effective async return kind for the function currently being emitted.
    /// Per-function @ValueTask annotation wins; global flag is the tiebreaker.
    /// </summary>
    private AsyncReturnKind _currentAsyncReturn = AsyncReturnKind.Task;

    public CodeGenerator(AsyncReturnKind globalAsyncReturn = AsyncReturnKind.Task)
    {
        _globalAsyncReturn = globalAsyncReturn;
    }

    // ── public entry ─────────────────────────────────────────────────────────

    public string Generate(ProgramNode program)
    {
        // First pass: collect data class names, interface names, and group impl blocks by type
        foreach (var d in program.Declarations)
        {
            if (d is DataClassDecl dc) _dataClasses.Add(dc.Name);
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
        foreach (var d in program.Declarations)
        {
            if (d is UseDecl ud)
                Line($"using {MapUseNamespace(ud.Namespace)};");
        }
        Blank();

        // Interfaces
        foreach (var d in program.Declarations)
            if (d is InterfaceDecl id) EmitInterface(id);

        // Records for data classes (with optional interface implementations)
        foreach (var d in program.Declarations)
            if (d is DataClassDecl dc) EmitDataClass(dc);

        // Single static class that holds all functions + extension methods
        Line("static class KsrProgram");
        Line("{");
        _indent++;

        foreach (var d in program.Declarations)
        {
            if (d is FunctionDecl fd) EmitFunction(fd);
            if (d is ExtFunctionDecl efd) EmitExtFunction(efd);
        }

        _indent--;
        Line("}");

        return _out.ToString();
    }

    // ── declarations ─────────────────────────────────────────────────────────

    private void EmitInterface(InterfaceDecl id)
    {
        Line($"interface I{id.Name}");
        Line("{");
        _indent++;
        foreach (var m in id.Methods)
        {
            // C# interface method signatures do NOT use the 'async' keyword;
            // the return type alone signals the async contract.
            var ret = m.IsAsync
                ? BuildAsyncReturnType(m.ReturnType, EffectiveAsyncReturn(m.AsyncReturn))
                : (m.ReturnType is null ? "void" : MapType(m.ReturnType));
            var parms = string.Join(", ", m.Parameters.Select(p => $"{MapType(p.Type)} {p.Name}"));
            Line($"{ret} {Pascal(m.Name)}({parms});");
        }
        _indent--;
        Line("}");
        Blank();
    }

    private void EmitDataClass(DataClassDecl dc)
    {
        var props = string.Join(", ",
            dc.Properties.Select(p => $"{MapType(p.Type)} {Pascal(p.Name)}"));

        if (_implsByType.TryGetValue(dc.Name, out var impls))
        {
            // Record implements one or more interfaces
            var ifaces = string.Join(", ", impls.Select(b => "I" + b.InterfaceName));
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
    }

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
            fd.Parameters.Select(p => $"{MapType(p.Type)} {p.Name}"));

        Line($"public {asyncMod}{ret} {Pascal(fd.Name)}{typeParamStr}({parms})");
        Line("{");
        _indent++;
        EmitBlock(fd.Body);
        if (HasSourceInfo(fd.Body)) Line("#line default");
        _indent--;
        Line("}");
        Blank();

        _inAsyncFunction    = prevAsync;
        _currentAsyncReturn = prevKind;
        _currentTypeParams  = prevTypeParams;
    }

    private void EmitFunction(FunctionDecl fd)
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
            fd.Parameters.Select(p => $"{MapType(p.Type)} {p.Name}"));

        Line($"static {asyncMod}{ret} {method}{typeParamStr}({parms})");
        Line("{");
        _indent++;
        EmitBlock(fd.Body);
        if (HasSourceInfo(fd.Body)) Line("#line default");
        _indent--;
        Line("}");
        Blank();

        _inAsyncFunction    = prevAsync;
        _currentAsyncReturn = prevKind;
        _currentTypeParams  = prevTypeParams;
    }

    /// <summary>
    /// fun Point.distanceSq(other: Point): Int  →
    ///   public static int distanceSq(this Point self, Point other)
    /// </summary>
    private void EmitExtFunction(ExtFunctionDecl efd)
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

        // First parameter is the receiver: "this TypeName self"
        var paramParts = new List<string> { $"this {csType} self" };
        paramParts.AddRange(efd.Parameters.Select(p => $"{MapType(p.Type)} {p.Name}"));
        var parms = string.Join(", ", paramParts);

        Line($"public static {asyncMod}{ret} {Pascal(efd.MethodName)}{typeParamStr}({parms})");
        Line("{");
        _indent++;
        EmitBlock(efd.Body);
        if (HasSourceInfo(efd.Body)) Line("#line default");
        _indent--;
        Line("}");
        Blank();

        _inAsyncFunction    = prevAsync;
        _currentAsyncReturn = prevKind;
        _currentTypeParams  = prevTypeParams;
    }

    // ── block & statements ────────────────────────────────────────────────────

    private void EmitBlock(Block block)
    {
        foreach (var s in block.Statements)
            EmitStmt(s);
    }

    private void EmitStmt(Stmt stmt)
    {
        if (stmt.Line > 0 && !string.IsNullOrEmpty(stmt.SourceFile))
        {
            var path = stmt.SourceFile.Replace('\\', '/');
            Line($"#line {stmt.Line} \"{path}\"");
        }

        switch (stmt)
        {
            // val / var  →  var (C# infers type; mutability tracked at KSR level only)
            case ValDecl vd:
                {
                    var t = vd.Type is null ? "var" : MapType(vd.Type);
                    Line($"{t} {vd.Name} = {EmitExprWithHint(vd.Value, vd.Type)};");
                    break;
                }
            case VarDecl vd:
                {
                    var t = vd.Type is null ? "var" : MapType(vd.Type);
                    Line($"{t} {vd.Name} = {EmitExprWithHint(vd.Value, vd.Type)};");
                    break;
                }

            case AssignStmt ass:
                Line($"{ass.Name} = {EmitExpr(ass.Value)};");
                break;

            case CompoundAssignStmt cas:
                Line($"{cas.Name} {cas.Op} {EmitExpr(cas.Value)};");
                break;

            case IndexAssignStmt ias:
                Line($"{ias.Name}[{EmitExpr(ias.Index)}] = {EmitExpr(ias.Value)};");
                break;

            // when used as a statement — emit if/else chain (switch expression not valid as stmt)
            case ExprStmt { Expression: WhenExpr we }:
                EmitWhenStmt(we);
                break;

            case ExprStmt es:
                Line($"{EmitExpr(es.Expression)};");
                break;

            case ReturnStmt rs:
                Line(rs.Value is null ? "return;" : $"return {EmitExpr(rs.Value)};");
                break;

            case IfStmt ifs:
                Line($"if ({EmitExpr(ifs.Condition)})");
                Line("{");
                _indent++;
                EmitBlock(ifs.Then);
                _indent--;
                Line("}");
                if (ifs.Else is not null)
                {
                    Line("else");
                    Line("{");
                    _indent++;
                    EmitBlock(ifs.Else);
                    _indent--;
                    Line("}");
                }
                break;

            case WhileStmt ws:
                Line($"while ({EmitExpr(ws.Condition)})");
                Line("{");
                _indent++;
                EmitBlock(ws.Body);
                _indent--;
                Line("}");
                break;

            case ForInStmt fis:
                if (fis.Iterable is RangeExpr re)
                {
                    // for (i in 0..10)  →  for (var i = 0; i <= 10; i++)
                    var op = re.Inclusive ? "<=" : "<";
                    Line($"for (var {fis.VarName} = {EmitExpr(re.Start)}; {fis.VarName} {op} {EmitExpr(re.End)}; {fis.VarName}++)");
                }
                else
                {
                    // for (item in collection)  →  foreach (var item in collection)
                    Line($"foreach (var {fis.VarName} in {EmitExpr(fis.Iterable)})");
                }
                Line("{");
                _indent++;
                EmitBlock(fis.Body);
                _indent--;
                Line("}");
                break;

            default:
                throw new InvalidOperationException($"Unknown statement: {stmt.GetType().Name}");
        }
    }

    // ── expressions ──────────────────────────────────────────────────────────

    private string EmitExpr(Expr expr) => expr switch
    {
        IntLiteral il => il.Value.ToString(),
        DoubleLiteral dl => dl.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        StringLiteral sl => $"\"{Escape(sl.Value)}\"",
        BoolLiteral bl => bl.Value ? "true" : "false",
        NullLiteral => "null",
        ThisExpr => _inRecordMethod ? "this" : "self",

        IdentifierExpr id => id.Name,

        IndexExpr ie => $"{EmitExpr(ie.Target)}[{EmitExpr(ie.Index)}]",
        NewArrayExpr na => $"new {MapType(na.ElementType)}[{EmitExpr(na.Size)}]",
        NewObjectExpr no => $"new {no.TypeName}({string.Join(", ", no.Arguments.Select(EmitExpr))})",

        LambdaExpr le when le.Params.Count == 0
            => $"(() => {EmitExpr(le.Body)})",
        LambdaExpr le when le.Params.Count == 1
            => $"({le.Params[0]} => {EmitExpr(le.Body)})",
        LambdaExpr le
            => $"(({string.Join(", ", le.Params)}) => {EmitExpr(le.Body)})",

        BinaryExpr be => $"({EmitExpr(be.Left)} {be.Op} {EmitExpr(be.Right)})",
        UnaryExpr ue => $"({ue.Op}{EmitExpr(ue.Operand)})",

        MemberAccessExpr ma => $"{EmitExpr(ma.Target)}.{Pascal(ma.Member)}",
        SafeCallExpr sc => $"{EmitExpr(sc.Target)}?.{Pascal(sc.Member)}",
        ElvisExpr ev => $"({EmitExpr(ev.Left)} ?? {EmitExpr(ev.Right)})",

        // Range used as a value (outside for-in) → Enumerable.Range
        RangeExpr re when re.Inclusive =>
            $"Enumerable.Range({EmitExpr(re.Start)}, {EmitExpr(re.End)} - {EmitExpr(re.Start)} + 1)",
        RangeExpr re =>
            $"Enumerable.Range({EmitExpr(re.Start)}, {EmitExpr(re.End)} - {EmitExpr(re.Start)})",

        StringTemplateExpr ste => EmitStringTemplate(ste),
        CallExpr ce => EmitCall(ce),
        ListLiteralExpr ll => EmitListLiteral(ll),
        MapLiteralExpr ml => EmitMapLiteral(ml),
        WhenExpr we => EmitWhenExpr(we),
        AwaitExpr ae => EmitAwait(ae),

        _ => throw new InvalidOperationException($"Unknown expression: {expr.GetType().Name}")
    };

    private string EmitCall(CallExpr ce)
    {
        var args = string.Join(", ", ce.Arguments.Select(EmitExpr));

        // Built-in: println → Console.WriteLine
        if (ce.Callee is IdentifierExpr { Name: "println" })
            return $"Console.WriteLine({args})";

        // Constructor call for known data classes
        if (ce.Callee is IdentifierExpr id && _dataClasses.Contains(id.Name))
            return $"new {id.Name}({args})";

        return $"{EmitExpr(ce.Callee)}({args})";
    }

    private string EmitStringTemplate(StringTemplateExpr ste)
    {
        var sb = new StringBuilder("$\"");
        foreach (var part in ste.Parts)
        {
            if (part is LiteralPart lp)
            {
                // Escape { } in literal text so they don't trigger C# interpolation
                sb.Append(lp.Text
                    .Replace("{", "{{")
                    .Replace("}", "}}")
                    .Replace("\"", "\\\""));
            }
            else if (part is ExprPart ep)
            {
                sb.Append($"{{{EmitExpr(ep.Expression)}}}");
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// Like EmitExpr but passes type context for collection literals so the correct
    /// concrete or interface type is used (IReadOnlyList vs List, etc.).
    /// </summary>
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
                        return $"new {MapType(hint)}()"; // new List<T>()
                    if (outerName == "List")
                    {
                        var argStr = hint.Name[(hint.Name.IndexOf('<') + 1)..^1];
                        var csArg = MapType(new TypeRef(argStr.Trim(), false));
                        return $"Array.Empty<{csArg}>()";
                    }
                    // Empty [] with a Map/MutableMap hint → concrete Dictionary
                    if (outerName == "Map" || outerName == "MutableMap")
                        return $"new {MapTypeForNew(hint)}()";
                }
                else if (outerName == "MutableList")
                {
                    // MutableList<T> non-empty literal → new List<T> { items }
                    var items = string.Join(", ", ll.Elements.Select(EmitExpr));
                    return $"new {MapType(hint)} {{ {items} }}";
                }
            }

            if (expr is MapLiteralExpr { Entries.Count: 0 })
                return $"new {MapTypeForNew(hint)}()";
        }
        return EmitExpr(expr);
    }

    private string EmitListLiteral(ListLiteralExpr ll)
    {
        if (ll.Elements.Count == 0)
            return "Array.Empty<object>()";

        var items = string.Join(", ", ll.Elements.Select(EmitExpr));
        return $"new[] {{ {items} }}";
    }

    private string EmitMapLiteral(MapLiteralExpr ml)
    {
        if (ml.Entries.Count == 0)
            return "new Dictionary<object, object>()";

        var keyType = InferPrimitiveType(ml.Entries[0].Key) ?? "object";
        var valType = InferPrimitiveType(ml.Entries[0].Value) ?? "object";
        var entries = string.Join(", ",
            ml.Entries.Select(e => $"[{EmitExpr(e.Key)}] = {EmitExpr(e.Value)}"));
        return $"new Dictionary<{keyType}, {valType}> {{ {entries} }}";
    }

    /// <summary>Infer a C# primitive type name from a literal expression, or null if unknown.</summary>
    private static string? InferPrimitiveType(Expr e) => e switch
    {
        IntLiteral => "int",
        DoubleLiteral => "double",
        BoolLiteral => "bool",
        StringLiteral => "string",
        StringTemplateExpr => "string",
        _ => null
    };

    // ── async helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the effective async return kind for one function:
    /// per-function @ValueTask OR global --async-return=valuetask wins over the default Task.
    /// </summary>
    private AsyncReturnKind EffectiveAsyncReturn(AsyncReturnKind perFunction) =>
        perFunction == AsyncReturnKind.ValueTask || _globalAsyncReturn == AsyncReturnKind.ValueTask
            ? AsyncReturnKind.ValueTask
            : AsyncReturnKind.Task;

    /// <summary>
    /// Builds the C# async return type.
    ///   innerType=null   → "Task"  or "ValueTask"
    ///   innerType=String → "Task&lt;string&gt;"  or "ValueTask&lt;string&gt;"
    /// </summary>
    private string BuildAsyncReturnType(TypeRef? innerType, AsyncReturnKind kind)
    {
        var wrapper = kind == AsyncReturnKind.ValueTask ? "ValueTask" : "Task";
        return innerType is null ? wrapper : $"{wrapper}<{MapType(innerType)}>";
    }

    /// <summary>
    /// Emits an await expression.  If used outside an async function a C# #error
    /// directive is injected so the Roslyn compile step surfaces a clear message.
    /// </summary>
    private string EmitAwait(AwaitExpr ae)
    {
        if (!_inAsyncFunction)
            Line("#error KSR: 'await' used outside an async function");
        return $"(await {EmitExpr(ae.Operand)})";
    }

    // ── when expression ──────────────────────────────────────────────────────

    /// <summary>
    /// Emit <c>when</c> as a C# expression value.
    /// Subject form   → switch expression:  (x switch { 1 =&gt; a, 2 =&gt; b, _ =&gt; c })
    /// Subject-less   → ternary chain:      (cond1 ? a : (cond2 ? b : c))
    /// </summary>
    private string EmitWhenExpr(WhenExpr we)
    {
        if (we.Subject is not null)
        {
            var subject = EmitExpr(we.Subject);
            var arms = we.Arms.Select(arm =>
                arm.Pattern is null
                    ? $"_ => {EmitExpr(arm.Body)}"
                    : $"{EmitExpr(arm.Pattern)} => {EmitExpr(arm.Body)}");
            return $"({subject} switch {{ {string.Join(", ", arms)} }})";
        }
        return EmitWhenTernary(we.Arms, 0);
    }

    private string EmitWhenTernary(List<WhenArm> arms, int i)
    {
        if (i >= arms.Count)
            return "throw new InvalidOperationException(\"when: no arm matched\")";
        var arm = arms[i];
        if (arm.Pattern is null) return EmitExpr(arm.Body); // else arm
        return $"({EmitExpr(arm.Pattern)} ? {EmitExpr(arm.Body)} : {EmitWhenTernary(arms, i + 1)})";
    }

    /// <summary>
    /// Emit <c>when</c> as a C# statement (if / else-if / else chain).
    /// Used when <c>when</c> appears as an <see cref="ExprStmt"/>.
    /// </summary>
    private void EmitWhenStmt(WhenExpr we)
    {
        bool first = true;
        var subject = we.Subject is not null ? EmitExpr(we.Subject) : null;

        foreach (var arm in we.Arms)
        {
            if (arm.Pattern is null) // else arm
            {
                Line("else");
            }
            else
            {
                var cond = subject is not null
                    ? $"{subject} == {EmitExpr(arm.Pattern)}"
                    : EmitExpr(arm.Pattern);
                Line(first ? $"if ({cond})" : $"else if ({cond})");
                first = false;
            }
            Line("{");
            _indent++;
            Line($"{EmitExpr(arm.Body)};");
            _indent--;
            Line("}");
        }
    }

    // ── type mapping ──────────────────────────────────────────────────────────

    private string MapType(TypeRef t)
    {
        // Array types: Bool[] → bool[], Int[] → int[], etc.
        if (t.Name.EndsWith("[]"))
        {
            var elem = MapType(new TypeRef(t.Name[..^2], false));
            return t.Nullable ? $"{elem}[]?" : $"{elem}[]";
        }

        // Generic types: List<Int> → IReadOnlyList<int>, MutableList<Int> → List<int>, etc.
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
                _             => outer
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
            // Type parameters pass through as-is; only prefix 'I' for declared interfaces.
            _ => _currentTypeParams.Contains(t.Name) ? t.Name
               : _interfaces.Contains(t.Name) ? "I" + t.Name
               : t.Name
        };
        return t.Nullable ? $"{base_}?" : base_;
    }

    /// <summary>
    /// Maps a KSR type to the concrete (constructible) C# type used in <c>new T()</c> or <c>new T { }</c>.
    /// IReadOnlyList/IReadOnlyDictionary are interfaces; use List/Dictionary for construction instead.
    /// </summary>
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
                _                       => outer
            };
            var result = $"{csOuter}<{string.Join(", ", args)}>";
            return t.Nullable ? $"{result}?" : result;
        }
        return MapType(t);
    }

    /// <summary>
    /// Splits top-level type arguments by comma, respecting nested angle brackets.
    /// e.g. "String, Map&lt;Int, Bool&gt;" → ["String", "Map&lt;Int, Bool&gt;"]
    /// </summary>
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

    // ── output helpers ────────────────────────────────────────────────────────

    private void Line(string text) =>
        _out.AppendLine(new string(' ', _indent * 4) + text);

    private void Blank() => _out.AppendLine();

    // ── misc ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a KSR <c>use</c> namespace to the C# <c>using</c> directive target.
    /// Standard library modules use short KSR names (ksr.io, ksr.text) that map
    /// to the C# namespaces defined in KSR.StdLib.
    /// </summary>
    private static string MapUseNamespace(string ns) => ns switch
    {
        "ksr.io"          => "KSR.Io",
        "ksr.text"        => "KSR.Text",
        "ksr.collections" => "KSR.Collections",
        _                 => ns,
    };

    /// KSR camelCase → C# PascalCase for property/member names
    private static string Pascal(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\n", "\\n")
         .Replace("\r", "\\r")
         .Replace("\t", "\\t");
}
