using KSR.AST;
using KSR.Lexer;
using Xunit;

namespace KSR.Tests;

public class InterfaceTests
{
    private static string Gen(string src) => KsrHelper.Generate(src);
    private static string Flat(string src) => KsrHelper.Flatten(Gen(src));

    // ── lexer ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Lexer_RecognisesInterfaceKeyword()
    {
        var tok = KsrHelper.Lex("interface").First(t => t.Type == TokenType.Interface);
        Assert.Equal("interface", tok.Value);
    }

    [Fact]
    public void Lexer_RecognisesImplementKeyword()
    {
        var tok = KsrHelper.Lex("implement").First(t => t.Type == TokenType.Implement);
        Assert.Equal("implement", tok.Value);
    }

    [Fact]
    public void Lexer_FloatLiteral()
    {
        var tok = KsrHelper.Lex("3.14").First(t => t.Type == TokenType.FloatLiteral);
        Assert.Equal("3.14", tok.Value);
    }

    [Fact]
    public void Lexer_FloatLiteral_OneDotZero()
    {
        var tok = KsrHelper.Lex("1.0").First(t => t.Type == TokenType.FloatLiteral);
        Assert.Equal("1.0", tok.Value);
    }

    [Fact]
    public void Lexer_IntegerFollowedByDot_IsIntThenDot()
    {
        // "x.y" — identifier, dot, identifier — not a float
        var tokens = KsrHelper.Lex("x.y").Where(t => t.Type != TokenType.Eof).ToList();
        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenType.Identifier, tokens[0].Type);
        Assert.Equal(TokenType.Dot,        tokens[1].Type);
        Assert.Equal(TokenType.Identifier, tokens[2].Type);
    }

    // ── parser ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parser_InterfaceDecl_NoMethods()
    {
        var prog = KsrHelper.Parse("interface Empty { }");
        var id = Assert.IsType<InterfaceDecl>(prog.Declarations.Single());
        Assert.Equal("Empty", id.Name);
        Assert.Empty(id.Methods);
    }

    [Fact]
    public void Parser_InterfaceDecl_TwoMethods()
    {
        var prog = KsrHelper.Parse("""
            interface Shape {
                fun area(): Double
                fun perimeter(): Double
            }
            """);
        var id = Assert.IsType<InterfaceDecl>(prog.Declarations.Single());
        Assert.Equal(2, id.Methods.Count);
        Assert.Equal("area",      id.Methods[0].Name);
        Assert.Equal("Double",    id.Methods[0].ReturnType!.Name);
        Assert.Equal("perimeter", id.Methods[1].Name);
    }

    [Fact]
    public void Parser_InterfaceMethod_WithParams()
    {
        var prog = KsrHelper.Parse("""
            interface Drawable {
                fun draw(x: Int, y: Int): Bool
            }
            """);
        var id = Assert.IsType<InterfaceDecl>(prog.Declarations.Single());
        var m  = id.Methods.Single();
        Assert.Equal(2, m.Parameters.Count);
        Assert.Equal("x", m.Parameters[0].Name);
    }

    [Fact]
    public void Parser_ImplBlock_ParsedCorrectly()
    {
        var prog = KsrHelper.Parse("""
            struct Point(x: Int)
            interface Named { fun name(): String }
            implement Named for Point {
                fun name(): String { return "point" }
            }
            """);
        var ib = Assert.IsType<ImplBlock>(
            prog.Declarations.OfType<ImplBlock>().Single());
        Assert.Equal("Named", ib.InterfaceName);
        Assert.Equal("Point", ib.TypeName);
        Assert.Single(ib.Methods);
        Assert.Equal("name", ib.Methods[0].Name);
    }

    [Fact]
    public void Parser_FloatLiteral_ProducesDoubleLiteral()
    {
        var prog = KsrHelper.Parse("fun f() { val x = 3.14 }");
        var fd   = Assert.IsType<FunctionDecl>(prog.Declarations.Single());
        var vd   = Assert.IsType<ValDecl>(fd.Body.Statements.Single());
        var dl   = Assert.IsType<DoubleLiteral>(vd.Value);
        Assert.Equal(3.14, dl.Value, precision: 5);
    }

    // ── code generation ───────────────────────────────────────────────────────

    [Fact]
    public void CodeGen_Interface_EmitsIPrefix()
    {
        var cs = Gen("interface Shape { fun area(): Double }");
        Assert.Contains("interface IShape", cs);
    }

    [Fact]
    public void CodeGen_Interface_MethodSignatureCorrect()
    {
        var cs = Gen("interface Shape { fun area(): Double }");
        Assert.Contains("double Area();", cs);
    }

    [Fact]
    public void CodeGen_Interface_NoBody()
    {
        var cs = Gen("interface Marker { }");
        Assert.Contains("interface IMarker", cs);
    }

    [Fact]
    public void CodeGen_Record_ImplementsInterface()
    {
        var cs = Gen("""
            interface Shape { fun area(): Double }
            struct Circle(r: Double)
            implement Shape for Circle {
                fun area(): Double { return 1.0 }
            }
            """);
        Assert.Contains("record Circle(double R) : IShape", cs);
    }

    [Fact]
    public void CodeGen_ImplMethod_IsPublic()
    {
        var cs = Gen("""
            interface Shape { fun area(): Double }
            struct Circle(r: Double)
            implement Shape for Circle {
                fun area(): Double { return 1.0 }
            }
            """);
        Assert.Contains("public double Area()", cs);
    }

    [Fact]
    public void CodeGen_ImplMethod_ThisResolvesToThis()
    {
        var cs = Gen("""
            interface Shape { fun area(): Double }
            struct Circle(r: Double)
            implement Shape for Circle {
                fun area(): Double { return this.r }
            }
            """);
        Assert.Contains("this.R", cs);
        Assert.DoesNotContain("self.R", cs);
    }

    [Fact]
    public void CodeGen_InterfaceTypeName_PrefixedWithI()
    {
        var cs = Gen("""
            interface Shape { fun area(): Double }
            fun printArea(s: Shape) { println("hi") }
            """);
        Assert.Contains("IShape s", cs);
    }

    [Fact]
    public void CodeGen_MultipleInterfaces_AllApplied()
    {
        var cs = Gen("""
            interface Shape { fun area(): Double }
            interface Named { fun name(): String }
            struct Circle(r: Double)
            implement Shape for Circle {
                fun area(): Double { return 1.0 }
            }
            implement Named for Circle {
                fun name(): String { return "circle" }
            }
            """);
        Assert.Contains("IShape", cs);
        Assert.Contains("INamed", cs);
        Assert.Contains(": IShape, INamed", cs);
    }

    [Fact]
    public void CodeGen_FloatLiteral_EmitsDecimalPoint()
    {
        var cs = Flat("fun f() { val x = 3.14 }");
        Assert.Contains("3.14", cs);
    }

    [Fact]
    public void CodeGen_FloatLiteral_UsesInvariantCulture()
    {
        // Must use '.' not ',' regardless of system locale
        var cs = Gen("fun f() { val pi = 3.14159 }");
        Assert.Contains("3.14159", cs);
        Assert.DoesNotContain("3,14159", cs);
    }

    [Fact]
    public void CodeGen_DataClass_WithoutImpl_StillEmitsSemicolon()
    {
        var cs = Gen("struct Point(x: Int, y: Int)");
        Assert.Contains("record Point(int X, int Y);", cs);
    }

    [Fact]
    public void CodeGen_ExtFunction_ThisStillEmitsSelf()
    {
        // Extension functions must still use 'self', not 'this'
        var cs = Gen("""
            struct Point(x: Int)
            fun Point.getX(): Int { return this.x }
            """);
        Assert.Contains("self.X", cs);
        Assert.DoesNotContain("this.X", cs);
    }

    // ── generic interfaces ────────────────────────────────────────────────────

    [Fact]
    public void GenericInterface_EmitsTypeParam()
    {
        var cs = Flat("""
            interface Container<T> {
                fun get(): T
            }
            """);
        Assert.Contains("interface IContainer<T>", cs);
        Assert.Contains("T Get();", cs);
    }

    [Fact]
    public void GenericInterface_TypeParamNotIPrefixed()
    {
        // T is a type param — must not become IT
        var cs = Flat("""
            interface Box<T> {
                fun value(): T
            }
            """);
        Assert.DoesNotContain("IT", cs);
        Assert.Contains("T Value();", cs);
    }

    [Fact]
    public void GenericInterface_WhereClause_SingleConstraint()
    {
        var cs = Flat("""
            interface Comparable<T> {
                fun compareTo(other: T): Int
            }
            interface Sortable<E> where E : Comparable<E> {
                fun sort(): E
            }
            """);
        Assert.Contains("interface ISortable<E> where E : IComparable<E>", cs);
    }

    [Fact]
    public void GenericInterface_WhereClause_MultipleConstraints()
    {
        var cs = Flat("""
            interface Enum<E> where E : Enum<E> {
                fun name(): String
            }
            interface IndexDbEnum<E> where E : Enum<E>, E : IndexDbEnum<E> {
                fun key(): String
            }
            """);
        Assert.Contains("interface IEnum<E> where E : IEnum<E>", cs);
        Assert.Contains("interface IIndexDbEnum<E> where E : IEnum<E>, E : IIndexDbEnum<E>", cs);
    }

    [Fact]
    public void GenericInterface_Implement_WithTypeArg()
    {
        var cs = Flat("""
            interface Container<T> {
                fun get(): T
            }
            struct Box(value: Int)
            implement Container<Int> for Box {
                fun get(): Int { return this.value }
            }
            """);
        Assert.Contains("record Box", cs);
        Assert.Contains("IContainer<int>", cs);
    }

    [Fact]
    public void GenericInterface_SelfReferential_Roundtrip()
    {
        // Full pattern: interface Enum<E> where E : Enum<E>
        // implement Enum<Color> for Color
        var cs = Flat("""
            interface Enum<E> where E : Enum<E> {
                fun name(): String
                fun ordinal(): Int
            }
            struct Color(label: String, idx: Int)
            implement Enum<Color> for Color {
                fun name(): String { return this.label }
                fun ordinal(): Int { return this.idx }
            }
            """);
        Assert.Contains("interface IEnum<E> where E : IEnum<E>", cs);
        Assert.Contains("record Color", cs);
        Assert.Contains("IEnum<Color>", cs);
    }
}
