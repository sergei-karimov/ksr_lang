using KSR.AST;
using KSR.Diagnostics;
using KSR.Lexer;
using KSR.Parser;
using KSR.Semantic;

namespace KSR.Analysis;

public sealed record KsrSource(string Source, string SourceFile);

public static class KsrAnalyzer
{
    public static KsrAnalysisResult Analyze(string source, string sourceFile) =>
        Analyze([new KsrSource(source, sourceFile)]);

    public static KsrAnalysisResult Analyze(IEnumerable<KsrSource> sources)
    {
        var declarations = new List<AstNode>();
        var diagnostics = new List<KsrDiagnostic>();

        foreach (var source in sources)
        {
            try
            {
                var tokens = new Lexer.Lexer(source.Source, source.SourceFile).Tokenize();
                var program = new Parser.Parser(tokens, source.SourceFile, throwOnError: true).Parse();
                declarations.AddRange(program.Declarations);
            }
            catch (KsrLexException exception)
            {
                diagnostics.Add(exception.Diagnostic);
            }
            catch (KsrParseException exception)
            {
                diagnostics.Add(exception.Diagnostic);
            }
        }

        if (diagnostics.Count > 0)
            return new KsrAnalysisResult(null, diagnostics);

        return Analyze(new ProgramNode(declarations));
    }

    public static KsrAnalysisResult Analyze(ProgramNode program, string sourceFile = "")
    {
        var semanticAnalyzer = new SemanticAnalyzer();
        semanticAnalyzer.Analyze(program, sourceFile);

        return new KsrAnalysisResult(program, semanticAnalyzer.Diagnostics);
    }
}
