using KSR.AST;
using KSR.Parser;
using Xunit;

namespace KSR.Tests;

public class ParserTests
{
    private static ProgramNode Parse(string src) => KsrHelper.Parse(src);

    private static T SingleDecl<T>(string src) where T : AstNode =>
        Assert.IsType<T>(Parse(src).Declarations.Single());

    private static T SingleStmt<T>(string funBody) where T : Stmt
    {
        var fd = Assert.IsType<FunctionDecl>(Parse($"fun f() {{ {funBody} }}").Declarations.Single());
        return Assert.IsType<T>(fd.Body.Statements.Single());
    }

    // ── use declarations ──────────────────────────────────────────────────────

    [Fact]
    public void UseDecl_SimpleNamespace()
    {
        var ud = SingleDecl<UseDecl>("use Raylib_cs");
        Assert.Equal("Raylib_cs", ud.Namespace);
    }

    [Fact]
    public void UseDecl_DottedNamespace()
    {
        var ud = SingleDecl<UseDecl>("use System.Collections.Generic");
        Assert.Equal("System.Collections.Generic", ud.Namespace);
    }

    // ── structs ──────────────────────────────────────────────────────────

    [Fact]
    public void DataClass_NoProperties()
    {
        var dc = SingleDecl<StructDecl>("struct Empty()");
        Assert.Equal("Empty", dc.Name);
        Assert.Empty(dc.Properties);
    }

    [Fact]
    public void DataClass_TwoProperties()
    {
        var dc = SingleDecl<StructDecl>("struct Point(x: Int, y: Int)");
        Assert.Equal("Point", dc.Name);
        Assert.Equal(2, dc.Properties.Count);
        Assert.Equal("x",   dc.Properties[0].Name);
        Assert.Equal("Int", dc.Properties[0].Type.Name);
        Assert.Equal("y",   dc.Properties[1].Name);
    }

    [Fact]
    public void DataClass_NullableProperty()
    {
        var dc = SingleDecl<StructDecl>("struct User(name: String?)");
        Assert.True(dc.Properties[0].Type.Nullable);
    }

    // ── function declarations ─────────────────────────────────────────────────

    [Fact]
    public void FunctionDecl_NoParams_NoReturn()
    {
        var fd = SingleDecl<FunctionDecl>("fun hello() { }");
        Assert.Equal("hello", fd.Name);
        Assert.Empty(fd.Parameters);
        Assert.Null(fd.ReturnType);
    }

    [Fact]
    public void FunctionDecl_WithParams()
    {
        var fd = SingleDecl<FunctionDecl>("fun add(a: Int, b: Int): Int { return a }");
        Assert.Equal(2, fd.Parameters.Count);
        Assert.Equal("a",   fd.Parameters[0].Name);
        Assert.Equal("Int", fd.Parameters[0].Type.Name);
        Assert.NotNull(fd.ReturnType);
        Assert.Equal("Int", fd.ReturnType!.Name);
    }

    // ── extension functions ───────────────────────────────────────────────────

    [Fact]
    public void ExtFunction_ParsedCorrectly()
    {
        var efd = SingleDecl<ExtFunctionDecl>("fun Point.len(): Int { return 0 }");
        Assert.Equal("Point", efd.ReceiverType);
        Assert.Equal("len",   efd.MethodName);
        Assert.Equal("Int",   efd.ReturnType!.Name);
    }

    // ── variable declarations ─────────────────────────────────────────────────

    [Fact]
    public void ValDecl_InferredType()
    {
        var vd = SingleStmt<ValDecl>("val x = 42");
        Assert.Equal("x", vd.Name);
        Assert.Null(vd.Type);
        Assert.IsType<IntLiteral>(vd.Value);
    }

    [Fact]
    public void VarDecl_ExplicitType()
    {
        var vd = SingleStmt<VarDecl>("var n: Int = 0");
        Assert.Equal("n",   vd.Name);
        Assert.Equal("Int", vd.Type!.Name);
    }

    [Fact]
    public void ValDecl_StringValue()
    {
        var vd = SingleStmt<ValDecl>("val s = \"hello\"");
        var sl = Assert.IsType<StringLiteral>(vd.Value);
        Assert.Equal("hello", sl.Value);
    }

    [Fact]
    public void ValDecl_BoolValue()
    {
        var vd = SingleStmt<ValDecl>("val b = true");
        var bl = Assert.IsType<BoolLiteral>(vd.Value);
        Assert.True(bl.Value);
    }

    [Fact]
    public void ValDecl_NullValue()
    {
        var vd = SingleStmt<ValDecl>("val n = null");
        Assert.IsType<NullLiteral>(vd.Value);
    }

    // ── assignment ────────────────────────────────────────────────────────────

    [Fact]
    public void AssignStmt()
    {
        var fd = Assert.IsType<FunctionDecl>(
            Parse("fun f() { var x = 0\n x = 5 }").Declarations.Single());
        var a = Assert.IsType<AssignStmt>(fd.Body.Statements[1]);
        Assert.Equal("x", a.Name);
        Assert.IsType<IntLiteral>(a.Value);
    }

    [Fact]
    public void CompoundAssign_PlusEq()
    {
        // Need a function with two statements; grab the second
        var fd = Assert.IsType<FunctionDecl>(
            Parse("fun f() { var x = 0\n x += 1 }").Declarations.Single());
        var ca = Assert.IsType<CompoundAssignStmt>(fd.Body.Statements[1]);
        Assert.Equal("x",  ca.Name);
        Assert.Equal("+=", ca.Op);
    }

    [Fact]
    public void IndexAssignStmt()
    {
        var fd = Assert.IsType<FunctionDecl>(
            Parse("fun f() { val a = new Int[5]\n a[0] = 1 }").Declarations.Single());
        var ia = Assert.IsType<IndexAssignStmt>(fd.Body.Statements[1]);
        Assert.Equal("a", ia.Name);
    }

    // ── control flow ──────────────────────────────────────────────────────────

    [Fact]
    public void IfStmt_WithoutElse()
    {
        var ifs = SingleStmt<IfStmt>("if (x > 0) { }");
        Assert.IsType<BinaryExpr>(ifs.Condition);
        Assert.Null(ifs.Else);
    }

    [Fact]
    public void IfStmt_WithElse()
    {
        var ifs = SingleStmt<IfStmt>("if (x > 0) { } else { }");
        Assert.NotNull(ifs.Else);
    }

    [Fact]
    public void WhileStmt()
    {
        var ws = SingleStmt<WhileStmt>("while (i > 0) { }");
        Assert.IsType<BinaryExpr>(ws.Condition);
    }

    [Fact]
    public void ForInStmt_InclusiveRange()
    {
        var fs = SingleStmt<ForInStmt>("for (i in 1..10) { }");
        Assert.Equal("i", fs.VarName);
        var range = Assert.IsType<RangeExpr>(fs.Iterable);
        Assert.True(range.Inclusive);
    }

    [Fact]
    public void ForInStmt_ExclusiveRange()
    {
        var fs = SingleStmt<ForInStmt>("for (i in 0..<n) { }");
        var range = Assert.IsType<RangeExpr>(fs.Iterable);
        Assert.False(range.Inclusive);
    }

    [Fact]
    public void ForInStmt_Collection()
    {
        var fs = SingleStmt<ForInStmt>("for (item in items) { }");
        Assert.IsType<IdentifierExpr>(fs.Iterable);
    }

    [Fact]
    public void ReturnStmt_WithValue()
    {
        var rs = SingleStmt<ReturnStmt>("return 42");
        Assert.NotNull(rs.Value);
        Assert.IsType<IntLiteral>(rs.Value);
    }

    [Fact]
    public void ReturnStmt_Void()
    {
        var rs = SingleStmt<ReturnStmt>("return");
        Assert.Null(rs.Value);
    }

    // ── expressions ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a + b",  "+")]
    [InlineData("a - b",  "-")]
    [InlineData("a * b",  "*")]
    [InlineData("a / b",  "/")]
    [InlineData("a % b",  "%")]
    [InlineData("a == b", "==")]
    [InlineData("a != b", "!=")]
    [InlineData("a < b",  "<")]
    [InlineData("a > b",  ">")]
    [InlineData("a <= b", "<=")]
    [InlineData("a >= b", ">=")]
    [InlineData("a && b", "&&")]
    [InlineData("a || b", "||")]
    public void BinaryExpr_Operators(string expr, string op)
    {
        var es = SingleStmt<ExprStmt>($"{expr}");
        var be = Assert.IsType<BinaryExpr>(es.Expression);
        Assert.Equal(op, be.Op);
    }

    [Fact]
    public void UnaryExpr_Not()
    {
        var es = SingleStmt<ExprStmt>("!flag");
        var ue = Assert.IsType<UnaryExpr>(es.Expression);
        Assert.Equal("!", ue.Op);
    }

    [Fact]
    public void UnaryExpr_Negate()
    {
        var es = SingleStmt<ExprStmt>("-x");
        var ue = Assert.IsType<UnaryExpr>(es.Expression);
        Assert.Equal("-", ue.Op);
    }

    [Fact]
    public void MemberAccessExpr()
    {
        var es = SingleStmt<ExprStmt>("user.name");
        var ma = Assert.IsType<MemberAccessExpr>(es.Expression);
        Assert.Equal("name", ma.Member);
    }

    [Fact]
    public void SafeCallExpr()
    {
        var es = SingleStmt<ExprStmt>("user?.name");
        Assert.IsType<SafeCallExpr>(es.Expression);
    }

    [Fact]
    public void ElvisExpr()
    {
        var es = SingleStmt<ExprStmt>("user?.name ?: \"default\"");
        Assert.IsType<ElvisExpr>(es.Expression);
    }

    [Fact]
    public void CallExpr_NoArgs()
    {
        var es = SingleStmt<ExprStmt>("foo()");
        var ce = Assert.IsType<CallExpr>(es.Expression);
        Assert.Empty(ce.Arguments);
    }

    [Fact]
    public void CallExpr_WithArgs()
    {
        var es = SingleStmt<ExprStmt>("add(1, 2)");
        var ce = Assert.IsType<CallExpr>(es.Expression);
        Assert.Equal(2, ce.Arguments.Count);
    }

    [Fact]
    public void NewArrayExpr()
    {
        var vd = SingleStmt<ValDecl>("val a = new Bool[10]");
        var na = Assert.IsType<NewArrayExpr>(vd.Value);
        Assert.Equal("Bool", na.ElementType.Name);
    }

    [Fact]
    public void NewObjectExpr()
    {
        var vd = SingleStmt<ValDecl>("val r = new Random()");
        var no = Assert.IsType<NewObjectExpr>(vd.Value);
        Assert.Equal("Random", no.TypeName);
    }

    // ── lambdas ───────────────────────────────────────────────────────────────

    [Fact]
    public void Lambda_ImplicitIt()
    {
        var es = SingleStmt<ExprStmt>("items.count { it }");
        var ce = Assert.IsType<CallExpr>(es.Expression);
        var le = Assert.IsType<LambdaExpr>(ce.Arguments.Last());
        Assert.Equal(["it"], le.Params);
    }

    [Fact]
    public void Lambda_NamedParam()
    {
        var es = SingleStmt<ExprStmt>("items.select { x -> x }");
        var ce = Assert.IsType<CallExpr>(es.Expression);
        var le = Assert.IsType<LambdaExpr>(ce.Arguments.Last());
        Assert.Equal(["x"], le.Params);
    }

    [Fact]
    public void Lambda_ZeroParams()
    {
        var vd = SingleStmt<ValDecl>("val f = { -> 42 }");
        var le = Assert.IsType<LambdaExpr>(vd.Value);
        Assert.Empty(le.Params);
    }

    // ── collection literals ───────────────────────────────────────────────────

    [Fact]
    public void ListLiteral_NonEmpty()
    {
        var vd = SingleStmt<ValDecl>("val nums = [1, 2, 3]");
        var ll = Assert.IsType<ListLiteralExpr>(vd.Value);
        Assert.Equal(3, ll.Elements.Count);
    }

    [Fact]
    public void ListLiteral_Empty()
    {
        var vd = SingleStmt<ValDecl>("val nums: List<Int> = []");
        Assert.IsType<ListLiteralExpr>(vd.Value);
    }

    [Fact]
    public void MapLiteral_NonEmpty()
    {
        var vd = SingleStmt<ValDecl>("val m = [\"a\": 1, \"b\": 2]");
        var ml = Assert.IsType<MapLiteralExpr>(vd.Value);
        Assert.Equal(2, ml.Entries.Count);
    }

    [Fact]
    public void MapLiteral_TrailingComma()
    {
        var vd = SingleStmt<ValDecl>("val m = [\"a\": 1,]");
        var ml = Assert.IsType<MapLiteralExpr>(vd.Value);
        Assert.Single(ml.Entries);
    }

    // ── type refs ─────────────────────────────────────────────────────────────

    [Fact]
    public void TypeRef_Nullable()
    {
        var fd = SingleDecl<FunctionDecl>("fun f(x: String?) { }");
        Assert.True(fd.Parameters[0].Type.Nullable);
    }

    [Fact]
    public void TypeRef_Array()
    {
        var fd = SingleDecl<FunctionDecl>("fun f(a: Int[]) { }");
        Assert.Equal("Int[]", fd.Parameters[0].Type.Name);
    }

    [Fact]
    public void TypeRef_Generic_List()
    {
        var vd = SingleStmt<VarDecl>("var xs: List<Int> = []");
        Assert.Equal("List<Int>", vd.Type!.Name);
    }

    [Fact]
    public void TypeRef_Generic_Map()
    {
        var vd = SingleStmt<VarDecl>("var m: Map<String, Int> = []");
        Assert.Equal("Map<String, Int>", vd.Type!.Name);
    }

    // ── source positions ──────────────────────────────────────────────────────

    [Fact]
    public void Stmt_HasCorrectLineNumber()
    {
        var fd = Assert.IsType<FunctionDecl>(
            KsrHelper.Parse("fun f() {\n    val x = 1\n    var y = 2\n}", "test.ksr")
                .Declarations.Single());
        Assert.Equal(2, fd.Body.Statements[0].Line);
        Assert.Equal(3, fd.Body.Statements[1].Line);
    }

    [Fact]
    public void Stmt_HasCorrectSourceFile()
    {
        var fd = Assert.IsType<FunctionDecl>(
            KsrHelper.Parse("fun f() { val x = 1 }", "hello.ksr").Declarations.Single());
        Assert.Equal("hello.ksr", fd.Body.Statements[0].SourceFile);
    }

    // ── error cases ───────────────────────────────────────────────────────────

    [Fact]
    public void UnexpectedToken_ThrowsKsrParseException()
    {
        // 'val' without a name is valid KSR lex but invalid parse
        Assert.Throws<KsrParseException>(() => Parse("fun f() { val = 1 }"));
    }

    [Fact]
    public void MissingClosingBrace_ThrowsKsrParseException()
    {
        Assert.Throws<KsrParseException>(() => Parse("fun f() {"));
    }

    [Fact]
    public void ParseException_CarriesLineAndCol()
    {
        var ex = Assert.Throws<KsrParseException>(() =>
            Parse("fun f() {\n    val = 1\n}"));
        Assert.True(ex.Line > 0);
    }
}
