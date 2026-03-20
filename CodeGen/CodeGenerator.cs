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
    private readonly StringBuilder _out    = new();
    private int                    _indent = 0;

    // Names declared as data classes — used to emit 'new Foo(…)'
    private readonly HashSet<string> _dataClasses = new();

    // ── public entry ─────────────────────────────────────────────────────────

    public string Generate(ProgramNode program)
    {
        // First pass: collect data class names
        foreach (var d in program.Declarations)
            if (d is DataClassDecl dc) _dataClasses.Add(dc.Name);

        // Preamble
        Line("#nullable enable");
        Line("using System;");
        Line("using System.Linq;");
        foreach (var d in program.Declarations)
            if (d is UseDecl ud) Line($"using {ud.Namespace};");
        Blank();

        // Records for data classes (sealed by default in C# record syntax)
        foreach (var d in program.Declarations)
            if (d is DataClassDecl dc) EmitDataClass(dc);

        // Single static class that holds all functions + extension methods
        Line("static class KsrProgram");
        Line("{");
        _indent++;

        foreach (var d in program.Declarations)
        {
            if (d is FunctionDecl fd)    EmitFunction(fd);
            if (d is ExtFunctionDecl efd) EmitExtFunction(efd);
        }

        _indent--;
        Line("}");

        return _out.ToString();
    }

    // ── declarations ─────────────────────────────────────────────────────────

    private void EmitDataClass(DataClassDecl dc)
    {
        var props = string.Join(", ",
            dc.Properties.Select(p => $"{MapType(p.Type)} {Pascal(p.Name)}"));
        Line($"record {dc.Name}({props});");
        Blank();
    }

    private void EmitFunction(FunctionDecl fd)
    {
        var ret    = fd.ReturnType is null ? "void" : MapType(fd.ReturnType);
        var method = fd.Name == "main" ? "Main" : fd.Name;
        var parms  = string.Join(", ",
            fd.Parameters.Select(p => $"{MapType(p.Type)} {p.Name}"));

        Line($"static {ret} {method}({parms})");
        Line("{");
        _indent++;
        EmitBlock(fd.Body);
        _indent--;
        Line("}");
        Blank();
    }

    /// <summary>
    /// fun Point.distanceSq(other: Point): Int  →
    ///   public static int distanceSq(this Point self, Point other)
    /// </summary>
    private void EmitExtFunction(ExtFunctionDecl efd)
    {
        var ret      = efd.ReturnType is null ? "void" : MapType(efd.ReturnType);
        var csType   = MapType(new TypeRef(efd.ReceiverType, false));

        // First parameter is the receiver: "this TypeName self"
        var paramParts = new List<string> { $"this {csType} self" };
        paramParts.AddRange(efd.Parameters.Select(p => $"{MapType(p.Type)} {p.Name}"));
        var parms = string.Join(", ", paramParts);

        Line($"public static {ret} {Pascal(efd.MethodName)}({parms})");
        Line("{");
        _indent++;
        EmitBlock(efd.Body);
        _indent--;
        Line("}");
        Blank();
    }

    // ── block & statements ────────────────────────────────────────────────────

    private void EmitBlock(Block block)
    {
        foreach (var s in block.Statements)
            EmitStmt(s);
    }

    private void EmitStmt(Stmt stmt)
    {
        switch (stmt)
        {
            // val / var  →  var (C# infers type; mutability tracked at KSR level only)
            case ValDecl vd:
            {
                var t = vd.Type is null ? "var" : MapType(vd.Type);
                Line($"{t} {vd.Name} = {EmitExpr(vd.Value)};");
                break;
            }
            case VarDecl vd:
            {
                var t = vd.Type is null ? "var" : MapType(vd.Type);
                Line($"{t} {vd.Name} = {EmitExpr(vd.Value)};");
                break;
            }

            case AssignStmt ass:
                Line($"{ass.Name} = {EmitExpr(ass.Value)};");
                break;

            case CompoundAssignStmt cas:
                Line($"{cas.Name} {cas.Op} {EmitExpr(cas.Value)};");
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
        IntLiteral    il => il.Value.ToString(),
        StringLiteral sl => $"\"{Escape(sl.Value)}\"",
        BoolLiteral   bl => bl.Value ? "true" : "false",
        NullLiteral      => "null",
        ThisExpr         => "self",                      // receiver in ext functions

        IdentifierExpr id => id.Name,

        BinaryExpr be  => $"({EmitExpr(be.Left)} {be.Op} {EmitExpr(be.Right)})",
        UnaryExpr  ue  => $"({ue.Op}{EmitExpr(ue.Operand)})",

        MemberAccessExpr ma => $"{EmitExpr(ma.Target)}.{Pascal(ma.Member)}",
        SafeCallExpr     sc => $"{EmitExpr(sc.Target)}?.{Pascal(sc.Member)}",
        ElvisExpr        ev => $"({EmitExpr(ev.Left)} ?? {EmitExpr(ev.Right)})",

        // Range used as a value (outside for-in) → Enumerable.Range
        RangeExpr re when re.Inclusive  =>
            $"Enumerable.Range({EmitExpr(re.Start)}, {EmitExpr(re.End)} - {EmitExpr(re.Start)} + 1)",
        RangeExpr re =>
            $"Enumerable.Range({EmitExpr(re.Start)}, {EmitExpr(re.End)} - {EmitExpr(re.Start)})",

        StringTemplateExpr ste => EmitStringTemplate(ste),
        CallExpr           ce  => EmitCall(ce),

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
                    .Replace("{",  "{{")
                    .Replace("}",  "}}")
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

    // ── type mapping ──────────────────────────────────────────────────────────

    private static string MapType(TypeRef t)
    {
        var base_ = t.Name switch
        {
            "Int"    => "int",
            "String" => "string",
            "Bool"   => "bool",
            "Double" => "double",
            "Float"  => "float",
            "Long"   => "long",
            "Unit"   => "void",
            _        => t.Name
        };
        return t.Nullable ? $"{base_}?" : base_;
    }

    // ── output helpers ────────────────────────────────────────────────────────

    private void Line(string text) =>
        _out.AppendLine(new string(' ', _indent * 4) + text);

    private void Blank() => _out.AppendLine();

    // ── misc ──────────────────────────────────────────────────────────────────

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
