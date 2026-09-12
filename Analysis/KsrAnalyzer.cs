using System.Text.RegularExpressions;
using KSR.AST;
using KSR.Diagnostics;
using KSR.Lexer;
using KSR.Parser;
using KSR.Semantic;

namespace KSR.Analysis;

public static class KsrAnalyzer
{
    private static readonly Regex SemanticErrorPattern = new(
        @"^.*\((?<line>\d+),(?<column>\d+)\): error: (?<message>.*)$",
        RegexOptions.Compiled);

    public static KsrAnalysisResult Analyze(string source, string sourceFile)
    {
        ProgramNode? program;

        try
        {
            var tokens = new Lexer.Lexer(source).Tokenize();
            program = new Parser.Parser(tokens, sourceFile, throwOnError: true).Parse();
        }
        catch (KsrLexException exception)
        {
            return Failure(exception.Message, sourceFile, exception.Line, exception.Col);
        }
        catch (KsrParseException exception)
        {
            return Failure(exception.Message, sourceFile, exception.Line, exception.Col);
        }

        var semanticAnalyzer = new SemanticAnalyzer();
        semanticAnalyzer.Analyze(program, sourceFile);

        var diagnostics = semanticAnalyzer.Errors
            .Select(error => ToDiagnostic(error, sourceFile));
        return new KsrAnalysisResult(program, diagnostics);
    }

    private static KsrAnalysisResult Failure(string message, string sourceFile, int line, int column) =>
        new(null, [new KsrDiagnostic(message, sourceFile, line, column, DiagnosticSeverity.Error)]);

    private static KsrDiagnostic ToDiagnostic(string error, string sourceFile)
    {
        var match = SemanticErrorPattern.Match(error);
        if (!match.Success)
            return new KsrDiagnostic(error, sourceFile, 0, 0, DiagnosticSeverity.Error);

        return new KsrDiagnostic(
            match.Groups["message"].Value,
            sourceFile,
            int.Parse(match.Groups["line"].Value),
            int.Parse(match.Groups["column"].Value),
            DiagnosticSeverity.Error);
    }
}
