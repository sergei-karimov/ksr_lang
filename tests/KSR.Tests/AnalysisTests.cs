using KSR.Analysis;
using KSR.Diagnostics;
using Xunit;

namespace KSR.Tests;

public class AnalysisTests
{
    [Fact]
    public void AnalyzeValidSourceReturnsProgramWithoutErrors()
    {
        var result = KsrAnalyzer.Analyze("fun main() { }", "main.ksr");

        Assert.NotNull(result.Program);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void AnalyzeLexicalErrorReturnsDiagnosticWithSourceLocation()
    {
        var result = KsrAnalyzer.Analyze("fun main() { & }", "main.ksr");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("main.ksr", diagnostic.SourceFile);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(14, diagnostic.Column);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(result.HasErrors);
        Assert.Null(result.Program);
    }

    [Fact]
    public void AnalyzeSyntaxErrorReturnsDiagnosticWithSourceLocation()
    {
        var result = KsrAnalyzer.Analyze("fun main( { }", "main.ksr");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("main.ksr", diagnostic.SourceFile);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(11, diagnostic.Column);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(result.HasErrors);
        Assert.Null(result.Program);
    }

    [Fact]
    public void AnalyzeSemanticErrorReturnsDiagnosticWithoutThrowing()
    {
        var result = KsrAnalyzer.Analyze("fun main() { val x = y }", "main.ksr");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("Undefined identifier 'y'", diagnostic.Message);
        Assert.Equal("main.ksr", diagnostic.SourceFile);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(result.HasErrors);
        Assert.NotNull(result.Program);
    }

    [Fact]
    public void AnalyzeSemanticStatementErrorUsesOneBasedSourceLocation()
    {
        var result = KsrAnalyzer.Analyze("fun main() {\nval x: Int = \"text\"\n}", "main.ksr");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(2, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
    }

    [Fact]
    public void AnalyzeSemanticExpressionErrorUsesIdentifierSourcePosition()
    {
        var result = KsrAnalyzer.Analyze("fun main() {\n    val x = missing\n}", "main.ksr");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(2, diagnostic.Line);
        Assert.Equal(13, diagnostic.Column);
    }

    [Fact]
    public void AnalyzeSemanticDeclarationErrorUsesDeclarationSourcePosition()
    {
        var result = KsrAnalyzer.Analyze("struct Item()\nstruct Item()", "main.ksr");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(2, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
    }
}
