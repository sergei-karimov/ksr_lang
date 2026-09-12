using KSR.Diagnostics;
using Xunit;

namespace KSR.Tests;

public class DiagnosticsTests
{
    [Fact]
    public void DiagnosticPreservesMessageLocationAndSeverity()
    {
        var diagnostic = new KsrDiagnostic(
            "Unexpected token",
            "src/main.ksr",
            4,
            12,
            DiagnosticSeverity.Error);

        Assert.Equal("Unexpected token", diagnostic.Message);
        Assert.Equal("src/main.ksr", diagnostic.SourceFile);
        Assert.Equal(4, diagnostic.Line);
        Assert.Equal(12, diagnostic.Column);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void AnalysisResultReportsWhetherItHasErrors()
    {
        var result = new KsrAnalysisResult(
            Program: null,
            Diagnostics: new[]
            {
                new KsrDiagnostic("warning", "a.ksr", 2, 3, DiagnosticSeverity.Warning)
            });

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void AnalysisResultReportsErrors()
    {
        var result = new KsrAnalysisResult(
            Program: null,
            Diagnostics: new[]
            {
                new KsrDiagnostic("error", "a.ksr", 2, 3, DiagnosticSeverity.Error)
            });

        Assert.True(result.HasErrors);
    }

    [Fact]
    public void AnalysisResultDoesNotChangeWhenInputDiagnosticsAreMutated()
    {
        var diagnostics = new List<KsrDiagnostic>
        {
            new("error", "a.ksr", 2, 3, DiagnosticSeverity.Error)
        };
        var result = new KsrAnalysisResult(null, diagnostics);

        diagnostics.Clear();

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("error", diagnostic.Message);
    }
}
