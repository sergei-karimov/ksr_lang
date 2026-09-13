using KSR.Diagnostics;
using KSR.LSP;
using System.Text.Json;
using Xunit;

namespace KSR.Tests;

public class DiagnosticsTests
{
    [Fact]
    public void CliDiagnosticSerializationPreservesStructuredFieldsAndEscapesMessage()
    {
        var diagnostics = new[]
        {
            new KsrDiagnostic("Bad \u03c0\nsecond line", "src/main.ksr", 4, 12, DiagnosticSeverity.Error)
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(diagnostics.Select(DiagnosticDto.FromDiagnostic)));
        var diagnostic = json.RootElement[0];

        Assert.Equal("Bad \u03c0\nsecond line", diagnostic.GetProperty("message").GetString());
        Assert.Equal("src/main.ksr", diagnostic.GetProperty("sourceFile").GetString());
        Assert.Equal(4, diagnostic.GetProperty("line").GetInt32());
        Assert.Equal(12, diagnostic.GetProperty("col").GetInt32());
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
    }

    [Fact]
    public void LspDiagnosticMappingConvertsOneBasedCoreLocationToZeroBasedRange()
    {
        var lsp = LspServer.ToLspDiagnostic(new KsrDiagnostic(
            "Bad \u03c0\nsecond line", "file:///src/main.ksr", 4, 12, DiagnosticSeverity.Warning));

        Assert.Equal(3, lsp.Range.Start.Line);
        Assert.Equal(11, lsp.Range.Start.Character);
        Assert.Equal(3, lsp.Range.End.Line);
        Assert.Equal(11, lsp.Range.End.Character);
        Assert.Equal(2, lsp.Severity);
        Assert.Equal("Bad \u03c0\nsecond line", lsp.Message);
        Assert.Equal("kestrel", lsp.Source);
    }

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

    [Fact]
    public void ParserDiagnosticExposesStructuredMessageAndLocation()
    {
        var parser = new KSR.Parser.Parser(
            new KSR.Lexer.Lexer("fun f() { val = 1 }").Tokenize(),
            "src/broken.ksr");

        parser.Parse();

        var diagnostic = Assert.Single(parser.Diagnostics);
        Assert.Equal("Expected 'Identifier' but found '=' (Equals)", diagnostic.Message);
        Assert.Equal("src/broken.ksr", diagnostic.SourceFile);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(15, diagnostic.Column);
    }

    [Fact]
    public void StringTemplateParserErrorUsesParentSourceLocation()
    {
        var parser = new KSR.Parser.Parser(
            new KSR.Lexer.Lexer("fun f() { val message = \"${1 + }\" }").Tokenize(),
            "src/template.ksr");

        parser.Parse();

        var diagnostic = Assert.Single(parser.Diagnostics);
        Assert.Equal("Unexpected token '' (Eof) in expression", diagnostic.Message);
        Assert.Equal("src/template.ksr", diagnostic.SourceFile);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(32, diagnostic.Column);
    }

    [Fact]
    public void StringTemplateLexerErrorUsesParentSourceLocation()
    {
        var parser = new KSR.Parser.Parser(
            new KSR.Lexer.Lexer("fun f() { val message = \"${1 & 2}\" }").Tokenize(),
            "src/template.ksr");

        parser.Parse();

        var diagnostic = Assert.Single(parser.Diagnostics);
        Assert.Equal("Expected '&&'", diagnostic.Message);
        Assert.Equal("src/template.ksr", diagnostic.SourceFile);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(30, diagnostic.Column);
    }
}
