using KSR.AST;
using KSR.CodeGen;
using KSR.Lexer;
using KSR.Parser;

namespace KSR.Tests;

internal static class KsrHelper
{
    public static List<Token> Lex(string src) =>
        new Lexer.Lexer(src).Tokenize();

    public static ProgramNode Parse(string src, string file = "") =>
        new Parser.Parser(new Lexer.Lexer(src).Tokenize(), file).Parse();

    public static string Generate(string src, string file = "") =>
        new CodeGenerator().Generate(Parse(src, file));

    /// <summary>Normalise whitespace so generated-C# assertions are indentation-agnostic.</summary>
    public static string Flatten(string s) =>
        string.Join(' ', s.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
}
