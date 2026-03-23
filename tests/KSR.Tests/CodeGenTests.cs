using Xunit;

namespace KSR.Tests;

public class CodeGenTests {
    private static string Gen(string src) => KsrHelper.Generate(src);
    private static string Flat(string src) => KsrHelper.Flatten(Gen(src));

    // ── preamble ──────────────────────────────────────────────────────────────

    [Fact]
    public void Output_ContainsNullableEnable() =>
        Assert.Contains("#nullable enable", Gen("fun main() { }"));

    [Fact]
    public void Output_ContainsSystemLinq() =>
        Assert.Contains("using System.Linq;", Gen("fun main() { }"));

    [Fact]
    public void UseDecl_EmitsUsingDirective() =>
        Assert.Contains("using Raylib_cs;", Gen("use Raylib_cs\nfun main() { }"));

    // ── type mapping ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Int", "int")]
    [InlineData("String", "string")]
    [InlineData("Bool", "bool")]
    [InlineData("Double", "double")]
    [InlineData("Float", "float")]
    [InlineData("Long", "long")]
    public void TypeMapping_PrimitiveTypes(string ksrType, string csType) {
        var cs = Gen($"fun f(x: {ksrType}) {{ }}");
        Assert.Contains(csType, cs);
        Assert.DoesNotContain(ksrType + " ", cs); // original type not leaked
    }

    [Fact]
    public void TypeMapping_Unit_MapsToVoid() =>
        Assert.Contains("void f(", Gen("fun f(): Unit { }"));

    [Fact]
    public void TypeMapping_NullableString() =>
        Assert.Contains("string?", Gen("fun f(x: String?) { }"));

    [Fact]
    public void TypeMapping_ArrayType() =>
        Assert.Contains("bool[]", Gen("fun f(a: Bool[]) { }"));

    [Fact]
    public void TypeMapping_ListOfInt() =>
        Assert.Contains("IReadOnlyList<int>", Gen("fun f(xs: List<Int>) { }"));

    [Fact]
    public void TypeMapping_MutableListOfInt() =>
        Assert.Contains("List<int>", Gen("fun f(xs: MutableList<Int>) { }"));

    [Fact]
    public void TypeMapping_MapStringInt() =>
        Assert.Contains("IReadOnlyDictionary<string, int>", Gen("fun f(m: Map<String, Int>) { }"));

    [Fact]
    public void TypeMapping_MutableMapStringInt() =>
        Assert.Contains("Dictionary<string, int>", Gen("fun f(m: MutableMap<String, Int>) { }"));

    [Fact]
    public void TypeMapping_NestedGeneric() =>
        Assert.Contains("IReadOnlyDictionary<string, IReadOnlyList<int>>",
            Gen("fun f(m: Map<String, List<Int>>) { }"));

    // ── structs ──────────────────────────────────────────────────────────

    [Fact]
    public void DataClass_EmitsRecord() {
        var cs = Gen("struct Point(x: Int, y: Int)");
        Assert.Contains("record Point(", cs);
        Assert.Contains("int X", cs);
        Assert.Contains("int Y", cs);
    }

    [Fact]
    public void DataClass_PropertiesArePascalCase() {
        var cs = Gen("struct User(firstName: String)");
        Assert.Contains("string FirstName", cs);
    }

    // ── functions ─────────────────────────────────────────────────────────────

    [Fact]
    public void Function_EmitsStaticMethod() =>
        Assert.Contains("static int add(", Gen("fun add(a: Int, b: Int): Int { return a }"));

    [Fact]
    public void Function_MainIsCapitalised() =>
        Assert.Contains("static void Main(", Gen("fun main() { }"));

    [Fact]
    public void Function_NoReturn_EmitsVoid() =>
        Assert.Contains("static void greet(", Gen("fun greet() { }"));

    // ── extension functions ───────────────────────────────────────────────────

    [Fact]
    public void ExtFunction_EmitsStaticExtensionMethod() {
        var cs = Gen("fun Point.len(): Int { return 0 }");
        Assert.Contains("public static int Len(this Point self", cs);
    }

    [Fact]
    public void ExtFunction_ThisCompilesAsSelf() {
        var cs = Gen("struct Point(x: Int)\nfun Point.getX(): Int { return this.x }");
        Assert.Contains("self.X", cs);
    }

    // ── statements ────────────────────────────────────────────────────────────

    [Fact]
    public void Println_EmitsConsoleWriteLine() =>
        Assert.Contains("Console.WriteLine(", Gen("fun main() { println(\"hi\") }"));

    [Fact]
    public void ValDecl_EmitsVar() =>
        Assert.Contains("var x = 42", Flat("fun main() { val x = 42 }"));

    [Fact]
    public void AssignStmt_EmitsCorrectly() =>
        Assert.Contains("x = 5;", Flat("fun main() { var x = 0\n x = 5 }"));

    [Fact]
    public void CompoundAssign_EmitsCorrectly() =>
        Assert.Contains("x += 1;", Flat("fun main() { var x = 0\n x += 1 }"));

    [Fact]
    public void ReturnStmt_EmitsReturn() =>
        Assert.Contains("return 42;", Flat("fun f(): Int { return 42 }"));

    // ── control flow ──────────────────────────────────────────────────────────

    [Fact]
    public void IfStmt_EmitsCorrectly() =>
        Assert.Contains("if ((x > 0))", Flat("fun f() { if (x > 0) { } }"));

    [Fact]
    public void IfElseStmt_EmitsElse() =>
        Assert.Contains("else", Gen("fun f() { if (x > 0) { } else { } }"));

    [Fact]
    public void WhileStmt_EmitsCorrectly() =>
        Assert.Contains("while ((i > 0))", Flat("fun f() { while (i > 0) { } }"));

    [Fact]
    public void ForInStmt_InclusiveRange_EmitsForLoop() {
        var flat = Flat("fun f() { for (i in 1..10) { } }");
        Assert.Contains("for (var i = 1; i <= 10; i++)", flat);
    }

    [Fact]
    public void ForInStmt_ExclusiveRange_EmitsForLoop() {
        var flat = Flat("fun f() { for (i in 0..<n) { } }");
        Assert.Contains("for (var i = 0; i < n; i++)", flat);
    }

    [Fact]
    public void ForInStmt_Collection_EmitsForeach() {
        var flat = Flat("fun f() { for (item in items) { } }");
        Assert.Contains("foreach (var item in items)", flat);
    }

    // ── expressions ───────────────────────────────────────────────────────────

    [Fact]
    public void BinaryExpr_EmitsParenthesised() =>
        Assert.Contains("(a + b)", Flat("fun f(): Int { return a + b }"));

    [Fact]
    public void MemberAccess_IsPascalCased() {
        var cs = Gen("fun f() { val n = user.firstName }");
        Assert.Contains(".FirstName", cs);
    }

    [Fact]
    public void SafeCallExpr_EmitsQuestionDot() =>
        Assert.Contains("?.Name", Gen("fun f() { val n = user?.name }"));

    [Fact]
    public void ElvisExpr_EmitsNullCoalescing() =>
        Assert.Contains("??", Gen("fun f() { val n = user?.name ?: \"default\" }"));

    [Fact]
    public void NewArrayExpr_EmitsNewArray() =>
        Assert.Contains("new bool[10]", Flat("fun f() { val a = new Bool[10] }"));

    // ── string templates ──────────────────────────────────────────────────────

    [Fact]
    public void StringTemplate_EmitsCsharpInterpolation() =>
        Assert.Contains("$\"Hello, {name}!\"",
            Flat("fun f() { println(\"Hello, ${name}!\") }"));

    [Fact]
    public void StringTemplate_EscapesBraces() {
        // A string template with a literal '{' in the non-interpolated part must
        // have it doubled to {{ so it doesn't trigger C# interpolation syntax.
        var cs = Gen("fun f() { println(\"count: { ${x} }\") }");
        Assert.Contains("{{", cs);
        Assert.Contains("}}", cs);
    }

    // ── collection literals ───────────────────────────────────────────────────

    [Fact]
    public void ListLiteral_NonEmpty_EmitsArray() =>
        Assert.Contains("new[] { 1, 2, 3 }",
            Flat("fun f() { val nums = [1, 2, 3] }"));

    [Fact]
    public void ListLiteral_StringElements() =>
        Assert.Contains("new[] { \"Alice\", \"Bob\" }",
            Flat("fun f() { val names = [\"Alice\", \"Bob\"] }"));

    [Fact]
    public void ListLiteral_EmptyWithTypeHint() =>
        Assert.Contains("Array.Empty<int>()",
            Flat("fun f() { val xs: List<Int> = [] }"));

    [Fact]
    public void ListLiteral_EmptyMutableListHint() =>
        Assert.Contains("new List<int>()",
            Flat("fun f() { val xs: MutableList<Int> = [] }"));

    [Fact]
    public void ListLiteral_NonEmptyMutableList() =>
        Assert.Contains("new List<int> { 1, 2, 3 }",
            Flat("fun f() { val xs: MutableList<Int> = [1, 2, 3] }"));

    [Fact]
    public void MapLiteral_NonEmpty_EmitsDictionary() {
        var flat = Flat("fun f() { val m = [\"a\": 1, \"b\": 2] }");
        Assert.Contains("new Dictionary<string, int>", flat);
        Assert.Contains("[\"a\"] = 1", flat);
        Assert.Contains("[\"b\"] = 2", flat);
    }

    [Fact]
    public void MapLiteral_EmptyWithTypeHint() =>
        Assert.Contains("new Dictionary<string, int>()",
            Flat("fun f() { val m: Map<String, Int> = [] }"));

    // ── lambdas ───────────────────────────────────────────────────────────────

    [Fact]
    public void Lambda_ImplicitIt_EmitsArrowLambda() =>
        Assert.Contains("(it =>", Flat("fun f() { val n = xs.count { it } }"));

    [Fact]
    public void Lambda_NamedParam() =>
        Assert.Contains("(x =>", Flat("fun f() { val n = xs.select { x -> x } }"));

    [Fact]
    public void Lambda_ZeroParams() =>
        Assert.Contains("(() =>", Flat("fun f() { val fn = { -> 42 } }"));

    // ── #line directives ──────────────────────────────────────────────────────

    [Fact]
    public void LineDirective_EmittedWhenSourceFileProvided() {
        var cs = KsrHelper.Generate("fun f() { val x = 1 }", "test.ksr");
        Assert.Contains("#line", cs);
        Assert.Contains("test.ksr", cs);
    }

    [Fact]
    public void LineDirective_NotEmittedWithoutSourceFile() {
        var cs = KsrHelper.Generate("fun f() { val x = 1 }");
        Assert.DoesNotContain("#line", cs);
    }

    [Fact]
    public void LineDirective_DefaultAtEndOfFunction() {
        var cs = KsrHelper.Generate("fun f() { val x = 1 }", "test.ksr");
        Assert.Contains("#line default", cs);
    }

    [Fact]
    public void LineDirective_ContainsCorrectLineNumber() {
        var cs = KsrHelper.Generate("fun f() {\n    val x = 1\n}", "test.ksr");
        Assert.Contains("#line 2", cs);
    }

    // ── constructor calls ─────────────────────────────────────────────────────

    [Fact]
    public void DataClassConstructor_EmitsNewKeyword() {
        var cs = Gen("struct Point(x: Int, y: Int)\nfun f() { val p = Point(1, 2) }");
        Assert.Contains("new Point(", cs);
    }

    [Fact]
    public void KnownDataClass_CallEmitsNew() {
        var flat = Flat("struct Vec(x: Int)\nfun f() { val v = Vec(0) }");
        Assert.Contains("new Vec(0)", flat);
    }

    // ── generic type parameters ───────────────────────────────────────────────

    [Fact]
    public void GenericFunction_EmitsTypeParams()
    {
        var cs = Gen("fun <T> identity(x: T): T { return x }");
        Assert.Contains("identity<T>(", cs);
        Assert.Contains("T x", cs);
    }

    [Fact]
    public void GenericFunction_MultipleTypeParams()
    {
        var cs = Gen("fun <T, U> pair(a: T, b: U): T { return a }");
        Assert.Contains("pair<T, U>(", cs);
    }

    [Fact]
    public void GenericExtensionFunction_EmitsTypeParams()
    {
        var cs = Gen("fun <T, U> List<T>.myMap(f: T): U { return f }");
        Assert.Contains("MyMap<T, U>(", cs);
        Assert.Contains("this IReadOnlyList<T> self", cs);
    }

    [Fact]
    public void GenericExtensionFunction_ReceiverTypeIsGeneric()
    {
        var cs = Gen("fun <T> List<T>.first(): T { return this[0] }");
        Assert.Contains("IReadOnlyList<T>", cs);
        Assert.Contains("First<T>(", cs);
    }

    [Fact]
    public void GenericFunction_TypeParamInReturnType()
    {
        var cs = Gen("fun <T> wrap(x: T): List<T> { return x }");
        Assert.Contains("IReadOnlyList<T>", cs);
        Assert.Contains("wrap<T>(", cs);
    }

    [Fact]
    public void GenericFunction_TypeParamNotPrefixedWithI()
    {
        // Type params must NOT get the 'I' interface prefix.
        var cs = Gen("fun <T> id(x: T): T { return x }");
        Assert.Contains("T id<T>(T x)", cs.Replace("  ", " ").Split('\n')
            .Select(l => l.Trim()).First(l => l.Contains("id<T>")));
    }

    // ── default arguments ─────────────────────────────────────────────────────

    [Fact]
    public void DefaultArg_EmittedInSignature()
    {
        var cs = Flat("fun greet(name: String, greeting: String = \"Hello\") { }");
        Assert.Contains("string greeting = \"Hello\"", cs);
    }

    [Fact]
    public void DefaultArg_IntLiteral()
    {
        var cs = Flat("fun repeat(s: String, n: Int = 1) { }");
        Assert.Contains("int n = 1", cs);
    }

    [Fact]
    public void DefaultArg_BoolLiteral()
    {
        var cs = Flat("fun log(msg: String, verbose: Bool = false) { }");
        Assert.Contains("bool verbose = false", cs);
    }

    [Fact]
    public void DefaultArg_NullLiteral()
    {
        var cs = Flat("fun find(name: String? = null) { }");
        Assert.Contains("string? name = null", cs);
    }

    [Fact]
    public void DefaultArg_Struct_WithDefault()
    {
        var cs = Flat("struct Point(x: Int = 0, y: Int = 0)");
        Assert.Contains("int X = 0", cs);
        Assert.Contains("int Y = 0", cs);
    }

    // ── named arguments ───────────────────────────────────────────────────────

    [Fact]
    public void NamedArg_EmittedWithColon()
    {
        var cs = Flat("fun f(x: Int, y: Int) { } fun main() { f(y = 2, x = 1) }");
        Assert.Contains("f(y: 2, x: 1)", cs);
    }

    [Fact]
    public void NamedArg_Mixed_PositionalAndNamed()
    {
        var cs = Flat("fun f(x: Int, y: Int) { } fun main() { f(1, y = 2) }");
        Assert.Contains("f(1, y: 2)", cs);
    }

    [Fact]
    public void NamedArg_OnlyNamed()
    {
        var cs = Flat("fun greet(name: String, greeting: String = \"Hi\") { } fun main() { greet(name = \"Alice\") }");
        Assert.Contains("greet(name: \"Alice\")", cs);
    }
}
