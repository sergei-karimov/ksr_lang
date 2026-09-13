using KSR.Diagnostics;

namespace KSR.Parser;

public class KsrParseException : Exception
{
    public int Line { get; }
    public int Col  { get; }
    public KsrDiagnostic Diagnostic { get; }

    public KsrParseException(string message, int line, int col, string sourceFile = "")
        : base($"Parse error at {line}:{col} — {message}")
    {
        Line = line;
        Col  = col;
        Diagnostic = new KsrDiagnostic(message, sourceFile, line, col, DiagnosticSeverity.Error);
    }
}
