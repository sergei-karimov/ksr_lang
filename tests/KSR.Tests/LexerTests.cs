using KSR.Lexer;
using Xunit;

namespace KSR.Tests;

public class LexerTests
{
    private static List<Token> Lex(string src) => KsrHelper.Lex(src);

    private static TokenType Type(string src) =>
        Lex(src).First(t => t.Type != TokenType.Eof).Type;

    // ── keywords ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("val",    TokenType.Val)]
    [InlineData("var",    TokenType.Var)]
    [InlineData("fun",    TokenType.Fun)]
    [InlineData("data",   TokenType.Data)]
    [InlineData("class",  TokenType.Class)]
    [InlineData("use",    TokenType.Use)]
    [InlineData("if",     TokenType.If)]
    [InlineData("else",   TokenType.Else)]
    [InlineData("while",  TokenType.While)]
    [InlineData("for",    TokenType.For)]
    [InlineData("in",     TokenType.In)]
    [InlineData("return", TokenType.Return)]
    [InlineData("new",    TokenType.New)]
    [InlineData("this",   TokenType.This)]
    [InlineData("true",   TokenType.True)]
    [InlineData("false",  TokenType.False)]
    [InlineData("null",   TokenType.Null)]
    public void Keywords_AreRecognised(string src, TokenType expected) =>
        Assert.Equal(expected, Type(src));

    // ── operators & punctuation ───────────────────────────────────────────────

    [Theory]
    [InlineData("==",  TokenType.EqEq)]
    [InlineData("!=",  TokenType.BangEq)]
    [InlineData("<=",  TokenType.LtEq)]
    [InlineData(">=",  TokenType.GtEq)]
    [InlineData("<",   TokenType.Lt)]
    [InlineData(">",   TokenType.Gt)]
    [InlineData("&&",  TokenType.AmpAmp)]
    [InlineData("||",  TokenType.PipePipe)]
    [InlineData("!",   TokenType.Bang)]
    [InlineData("+=",  TokenType.PlusEq)]
    [InlineData("-=",  TokenType.MinusEq)]
    [InlineData("->",  TokenType.Arrow)]
    [InlineData("?.",  TokenType.SafeCall)]
    [InlineData("?:",  TokenType.Elvis)]
    [InlineData("?",   TokenType.Question)]
    [InlineData("..",  TokenType.DotDot)]
    [InlineData("..<", TokenType.DotDotLt)]
    [InlineData("=",   TokenType.Equals)]
    [InlineData("+",   TokenType.Plus)]
    [InlineData("-",   TokenType.Minus)]
    [InlineData("*",   TokenType.Star)]
    [InlineData("/",   TokenType.Slash)]
    [InlineData("%",   TokenType.Percent)]
    [InlineData(":",   TokenType.Colon)]
    [InlineData(",",   TokenType.Comma)]
    [InlineData(".",   TokenType.Dot)]
    [InlineData("(",   TokenType.LParen)]
    [InlineData(")",   TokenType.RParen)]
    [InlineData("{",   TokenType.LBrace)]
    [InlineData("}",   TokenType.RBrace)]
    [InlineData("[",   TokenType.LBracket)]
    [InlineData("]",   TokenType.RBracket)]
    public void Operators_AreRecognised(string src, TokenType expected) =>
        Assert.Equal(expected, Type(src));

    // ── literals ──────────────────────────────────────────────────────────────

    [Fact]
    public void IntLiteral_SingleDigit()
    {
        var t = Lex("7").First(t => t.Type == TokenType.IntLiteral);
        Assert.Equal("7", t.Value);
    }

    [Fact]
    public void IntLiteral_MultiDigit()
    {
        var t = Lex("1234").First(t => t.Type == TokenType.IntLiteral);
        Assert.Equal("1234", t.Value);
    }

    [Fact]
    public void StringLiteral_Empty()
    {
        var t = Lex("\"\"").First(t => t.Type == TokenType.StringLiteral);
        Assert.Equal("", t.Value);
    }

    [Fact]
    public void StringLiteral_Simple()
    {
        var t = Lex("\"hello\"").First(t => t.Type == TokenType.StringLiteral);
        Assert.Equal("hello", t.Value);
    }

    [Fact]
    public void StringTemplate_ProducesStringTemplateToken()
    {
        var t = Lex("\"Hello, ${name}!\"").First(t => t.Type == TokenType.StringTemplate);
        Assert.Contains("${name}", t.Value);
    }

    [Fact]
    public void Identifier_Simple()
    {
        var t = Lex("myVar").First(t => t.Type == TokenType.Identifier);
        Assert.Equal("myVar", t.Value);
    }

    [Fact]
    public void Identifier_WithUnderscore()
    {
        var t = Lex("my_var").First(t => t.Type == TokenType.Identifier);
        Assert.Equal("my_var", t.Value);
    }

    // ── comments ──────────────────────────────────────────────────────────────

    [Fact]
    public void LineComment_IsSkipped()
    {
        var tokens = Lex("// this is a comment\nval x = 1");
        Assert.DoesNotContain(tokens, t => t.Value.Contains("comment"));
        Assert.Contains(tokens, t => t.Type == TokenType.Val);
    }

    [Fact]
    public void InlineComment_IsSkipped()
    {
        var tokens = Lex("val x = 1 // inline");
        Assert.Contains(tokens, t => t.Type == TokenType.Val);
        Assert.DoesNotContain(tokens, t => t.Value == "inline");
    }

    // ── positions ─────────────────────────────────────────────────────────────

    [Fact]
    public void Token_HasCorrectLineNumber()
    {
        var tokens = Lex("val x = 1\nvar y = 2");
        var varTok = tokens.First(t => t.Type == TokenType.Var);
        Assert.Equal(2, varTok.Line);
    }

    [Fact]
    public void Token_HasCorrectColumnNumber()
    {
        var tokens = Lex("val x = 1");
        var xTok = tokens.First(t => t.Type == TokenType.Identifier && t.Value == "x");
        Assert.Equal(5, xTok.Col);
    }

    // ── EOF ───────────────────────────────────────────────────────────────────

    [Fact]
    public void EmptySource_ProducesOnlyEof()
    {
        var tokens = Lex("");
        Assert.Single(tokens);
        Assert.Equal(TokenType.Eof, tokens[0].Type);
    }

    [Fact]
    public void LastToken_IsAlwaysEof()
    {
        var tokens = Lex("val x = 1");
        Assert.Equal(TokenType.Eof, tokens.Last().Type);
    }

    // ── multiple tokens ───────────────────────────────────────────────────────

    [Fact]
    public void ValDecl_ProducesCorrectTokenSequence()
    {
        var types = Lex("val x = 42")
            .Where(t => t.Type != TokenType.Eof)
            .Select(t => t.Type)
            .ToList();

        Assert.Equal(
        [
            TokenType.Val,
            TokenType.Identifier,
            TokenType.Equals,
            TokenType.IntLiteral
        ], types);
    }

    // ── errors ────────────────────────────────────────────────────────────────

    [Fact]
    public void UnterminatedString_ThrowsKsrLexException()
    {
        Assert.Throws<KsrLexException>(() => Lex("\"unterminated"));
    }

    [Fact]
    public void UnexpectedCharacter_ThrowsKsrLexException()
    {
        Assert.Throws<KsrLexException>(() => Lex("val x = @bad"));
    }

    [Fact]
    public void LexException_CarriesLineAndCol()
    {
        var ex = Assert.Throws<KsrLexException>(() => Lex("val x = @bad"));
        Assert.True(ex.Line > 0);
        Assert.True(ex.Col  > 0);
    }
}
