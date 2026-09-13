using KSR.Diagnostics;

namespace KSR.Lexer;

public class KsrLexException : Exception
{
    public int Line { get; }
    public int Col  { get; }
    public KsrDiagnostic Diagnostic { get; }

    public KsrLexException(string message, int line, int col, string sourceFile = "")
        : base($"Lexer error at {line}:{col} — {message}")
    {
        Line = line;
        Col  = col;
        Diagnostic = new KsrDiagnostic(message, sourceFile, line, col, DiagnosticSeverity.Error);
    }
}
