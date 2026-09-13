using System.Text.Json.Serialization;

namespace KSR.Diagnostics;

public sealed record DiagnosticDto(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("col")] int Col,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("sourceFile")] string SourceFile)
{
    public static DiagnosticDto FromDiagnostic(KsrDiagnostic diagnostic) => new(
        diagnostic.Message,
        diagnostic.Line,
        diagnostic.Column,
        diagnostic.Severity.ToString().ToLowerInvariant(),
        diagnostic.SourceFile);
}
