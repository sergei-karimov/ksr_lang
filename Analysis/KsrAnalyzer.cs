using KSR.AST;
using KSR.Diagnostics;
using KSR.Lexer;
using KSR.Parser;
using KSR.Semantic;

namespace KSR.Analysis;

public static class KsrAnalyzer
{
    public static KsrAnalysisResult Analyze(string source, string sourceFile)
    {
        ProgramNode? program;

        try
        {
            var tokens = new Lexer.Lexer(source, sourceFile).Tokenize();
            program = new Parser.Parser(tokens, sourceFile, throwOnError: true).Parse();
        }
        catch (KsrLexException exception)
        {
            return new KsrAnalysisResult(null, [exception.Diagnostic]);
        }
        catch (KsrParseException exception)
        {
            return new KsrAnalysisResult(null, [exception.Diagnostic]);
        }

        var semanticAnalyzer = new SemanticAnalyzer();
        semanticAnalyzer.Analyze(program, sourceFile);

        return new KsrAnalysisResult(program, semanticAnalyzer.Diagnostics);
    }
}
