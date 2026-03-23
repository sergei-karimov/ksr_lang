using KSR.AST;
using KSR.Lexer;
using Xunit;

namespace KSR.Tests;

public class WhenTests
{
    private static string Gen(string src)  => KsrHelper.Generate(src);
    private static string Flat(string src) => KsrHelper.Flatten(Gen(src));

    // ── lexer ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Lexer_RecognisesWhenKeyword()
    {
        var tok = KsrHelper.Lex("when").First(t => t.Type == TokenType.When);
        Assert.Equal("when", tok.Value);
    }

    // ── parser ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parser_WhenExpr_SubjectForm_Parsed()
    {
        var prog = KsrHelper.Parse("fun f(x: Int): String { return when (x) { 1 -> \"one\" else -> \"other\" } }");
        var fd   = Assert.IsType<FunctionDecl>(prog.Declarations.Single());
        var rs   = Assert.IsType<ReturnStmt>(fd.Body.Statements.Single());
        var we   = Assert.IsType<WhenExpr>(rs.Value);
        Assert.NotNull(we.Subject);
        Assert.Equal(2, we.Arms.Count);
    }

    [Fact]
    public void Parser_WhenExpr_SubjectLessForm_Parsed()
    {
        var prog = KsrHelper.Parse("fun f(x: Int): String { return when { x > 0 -> \"pos\" else -> \"neg\" } }");
        var fd   = Assert.IsType<FunctionDecl>(prog.Declarations.Single());
        var rs   = Assert.IsType<ReturnStmt>(fd.Body.Statements.Single());
        var we   = Assert.IsType<WhenExpr>(rs.Value);
        Assert.Null(we.Subject);
        Assert.Equal(2, we.Arms.Count);
    }

    [Fact]
    public void Parser_WhenExpr_ElseArmHasNullPattern()
    {
        var prog = KsrHelper.Parse("fun f(x: Int): Int { return when (x) { 1 -> 10 else -> 0 } }");
        var fd   = Assert.IsType<FunctionDecl>(prog.Declarations.Single());
        var rs   = Assert.IsType<ReturnStmt>(fd.Body.Statements.Single());
        var we   = Assert.IsType<WhenExpr>(rs.Value);
        var elseArm = we.Arms.Last();
        Assert.Null(elseArm.Pattern);
    }

    [Fact]
    public void Parser_WhenExpr_ThreeArms()
    {
        var prog = KsrHelper.Parse("""
            fun classify(x: Int): String {
                return when (x) {
                    1 -> "one"
                    2 -> "two"
                    else -> "other"
                }
            }
            """);
        var fd = Assert.IsType<FunctionDecl>(prog.Declarations.Single());
        var rs = Assert.IsType<ReturnStmt>(fd.Body.Statements.Single());
        var we = Assert.IsType<WhenExpr>(rs.Value);
        Assert.Equal(3, we.Arms.Count);
    }

    [Fact]
    public void Parser_WhenExpr_BodyIsExpr()
    {
        var prog = KsrHelper.Parse("fun f(x: Int): Int { return when (x) { 1 -> 100 else -> 0 } }");
        var fd   = Assert.IsType<FunctionDecl>(prog.Declarations.Single());
        var rs   = Assert.IsType<ReturnStmt>(fd.Body.Statements.Single());
        var we   = Assert.IsType<WhenExpr>(rs.Value);
        Assert.IsType<IntLiteral>(we.Arms[0].Body);
    }

    [Fact]
    public void Parser_WhenExpr_UsedAsStatement()
    {
        // when used as a standalone statement (not returned)
        var prog = KsrHelper.Parse("""
            fun greet(x: Int) {
                when (x) {
                    1 -> println("one")
                    else -> println("other")
                }
            }
            """);
        var fd   = Assert.IsType<FunctionDecl>(prog.Declarations.Single());
        var es   = Assert.IsType<ExprStmt>(fd.Body.Statements.Single());
        Assert.IsType<WhenExpr>(es.Expression);
    }

    [Fact]
    public void Parser_WhenExpr_UsedInValDecl()
    {
        var prog = KsrHelper.Parse("fun f(x: Int) { val s = when (x) { 1 -> \"one\" else -> \"?\" } }");
        var fd   = Assert.IsType<FunctionDecl>(prog.Declarations.Single());
        var vd   = Assert.IsType<ValDecl>(fd.Body.Statements.Single());
        Assert.IsType<WhenExpr>(vd.Value);
    }

    // ── code generation — expression context ─────────────────────────────────

    [Fact]
    public void CodeGen_When_SubjectForm_UsesSwitchExpression()
    {
        var cs = Flat("fun f(x: Int): String { return when (x) { 1 -> \"one\" else -> \"other\" } }");
        Assert.Contains("switch", cs);
        Assert.Contains("1 =>", cs);
        Assert.Contains("_ =>", cs);
    }

    [Fact]
    public void CodeGen_When_SubjectForm_SubjectEmitted()
    {
        var cs = Gen("fun f(x: Int): String { return when (x) { 1 -> \"one\" else -> \"other\" } }");
        Assert.Contains("x switch", cs);
    }

    [Fact]
    public void CodeGen_When_SubjectForm_ArmsEmitted()
    {
        var cs = Gen("""
            fun classify(n: Int): String {
                return when (n) {
                    1    -> "one"
                    2    -> "two"
                    else -> "many"
                }
            }
            """);
        Assert.Contains("1 =>", cs);
        Assert.Contains("2 =>", cs);
        Assert.Contains("_ =>", cs);
        Assert.Contains("\"many\"", cs);
    }

    [Fact]
    public void CodeGen_When_SubjectLess_UsesTernary()
    {
        var cs = Gen("""
            fun sign(x: Int): String {
                return when {
                    x > 0 -> "pos"
                    x < 0 -> "neg"
                    else  -> "zero"
                }
            }
            """);
        // Subject-less → ternary chain
        Assert.Contains("? \"pos\"", cs);
        Assert.Contains(": (", cs);     // nested ternary
    }

    [Fact]
    public void CodeGen_When_SubjectLess_ElseIsLastTernary()
    {
        var cs = Flat("""
            fun f(x: Int): String {
                return when {
                    x > 0 -> "pos"
                    else  -> "neg"
                }
            }
            """);
        // ternary: (x > 0) ? "pos" : "neg"
        Assert.Contains("? \"pos\"", cs);
        Assert.Contains(": \"neg\"", cs);
    }

    [Fact]
    public void CodeGen_When_ValueUsedInValDecl()
    {
        var cs = Gen("fun f(x: Int) { val label = when (x) { 1 -> \"one\" else -> \"other\" } }");
        Assert.Contains("switch", cs);
        Assert.Contains("label", cs);
    }

    // ── code generation — statement context ──────────────────────────────────

    [Fact]
    public void CodeGen_When_Statement_EmitsIfElseChain()
    {
        var cs = Gen("""
            fun greet(x: Int) {
                when (x) {
                    1 -> println("one")
                    else -> println("other")
                }
            }
            """);
        Assert.Contains("if (x == 1)", cs);
        Assert.Contains("else", cs);
        // Does NOT use switch expression (invalid as statement)
        Assert.DoesNotContain("x switch", cs);
    }

    [Fact]
    public void CodeGen_When_Statement_SubjectLess_EmitsIfElse()
    {
        var cs = Gen("""
            fun greet(x: Int) {
                when {
                    x > 0 -> println("pos")
                    else  -> println("neg")
                }
            }
            """);
        Assert.Contains("if ((x > 0))", cs);
        Assert.Contains("else", cs);
    }

    [Fact]
    public void CodeGen_When_Statement_ThreeArmsEmittedAsElseIf()
    {
        var cs = Gen("""
            fun f(x: Int) {
                when (x) {
                    1 -> println("one")
                    2 -> println("two")
                    else -> println("other")
                }
            }
            """);
        Assert.Contains("if (x == 1)",      cs);
        Assert.Contains("else if (x == 2)", cs);
        Assert.Contains("else",             cs);
    }

    [Fact]
    public void CodeGen_When_SubjectLess_ThreeArmsWithElseIf()
    {
        var cs = Gen("""
            fun f(x: Int) {
                when {
                    x > 0  -> println("pos")
                    x < 0  -> println("neg")
                    else   -> println("zero")
                }
            }
            """);
        Assert.Contains("else if",  cs);
        Assert.Contains("else",     cs);
    }

    // ── sealed + exhaustive when ──────────────────────────────────────────────

    [Fact]
    public void Sealed_EmitsAbstractRecord()
    {
        var cs = Flat("""
            sealed Shape {
                struct Circle(r: Double)
                struct Rect(w: Double, h: Double)
            }
            """);
        Assert.Contains("abstract record Shape;", cs);
    }

    [Fact]
    public void Sealed_EmitsVariantsWithBase()
    {
        var cs = Flat("""
            sealed Shape {
                struct Circle(r: Double)
                struct Rect(w: Double, h: Double)
            }
            """);
        Assert.Contains("record Circle(double R) : Shape;", cs);
        Assert.Contains("record Rect(double W, double H) : Shape;", cs);
    }

    [Fact]
    public void Sealed_EmptyVariant_NoParens()
    {
        var cs = Flat("""
            sealed Option {
                struct Some(value: Int)
                struct None
            }
            """);
        Assert.Contains("abstract record Option;", cs);
        Assert.Contains("record None : Option;", cs);
    }

    [Fact]
    public void IsPattern_WithBinding_ExprForm()
    {
        var cs = Flat("""
            sealed Shape {
                struct Circle(r: Double)
                struct Rect(w: Double, h: Double)
            }
            fun area(s: Shape): Double {
                return when (s) {
                    is Circle(c) -> c.r
                    is Rect(r)   -> r.w
                }
            }
            """);
        Assert.Contains("Circle c => c.R", cs);
        Assert.Contains("Rect r => r.W", cs);
    }

    [Fact]
    public void IsPattern_NoBinding_ExprForm()
    {
        var cs = Flat("""
            sealed Color {
                struct Red
                struct Green
                struct Blue
            }
            fun label(c: Color): String {
                return when (c) {
                    is Red   -> "red"
                    is Green -> "green"
                    is Blue  -> "blue"
                }
            }
            """);
        Assert.Contains("Red => \"red\"", cs);
        Assert.Contains("Green => \"green\"", cs);
        Assert.Contains("Blue => \"blue\"", cs);
    }

    [Fact]
    public void IsPattern_WithBinding_StmtForm()
    {
        var cs = Flat("""
            sealed Shape {
                struct Circle(r: Double)
                struct Rect(w: Double, h: Double)
            }
            fun describe(s: Shape) {
                when (s) {
                    is Circle(c) -> println(c.r)
                    is Rect(r)   -> println(r.w)
                }
            }
            """);
        Assert.Contains("if (s is Circle c)", cs);
        Assert.Contains("else if (s is Rect r)", cs);
    }

    [Fact]
    public void IsPattern_WithElse_ExprForm()
    {
        var cs = Flat("""
            sealed Shape {
                struct Circle(r: Double)
            }
            fun area(s: Shape): Double {
                return when (s) {
                    is Circle(c) -> c.r
                    else         -> 0.0
                }
            }
            """);
        Assert.Contains("Circle c => c.R", cs);
        Assert.Contains("_ =>", cs);
    }

    [Fact]
    public void Sealed_VariantUsedAsConstructor()
    {
        var cs = Flat("""
            sealed Shape {
                struct Circle(r: Double)
            }
            fun main() {
                val s = Circle(3.0)
            }
            """);
        Assert.Contains("new Circle(", cs);
    }

    [Fact]
    public void Lexer_RecognisesSealedKeyword()
    {
        var tok = KsrHelper.Lex("sealed").First(t => t.Type == TokenType.Sealed);
        Assert.Equal("sealed", tok.Value);
    }

    [Fact]
    public void Lexer_RecognisesIsKeyword()
    {
        var tok = KsrHelper.Lex("is").First(t => t.Type == TokenType.Is);
        Assert.Equal("is", tok.Value);
    }
}
