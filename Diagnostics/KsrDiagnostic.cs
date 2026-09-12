namespace KSR.Diagnostics;

public sealed record KsrDiagnostic(
    string Message,
    string SourceFile,
    int Line,
    int Column,
    DiagnosticSeverity Severity);
