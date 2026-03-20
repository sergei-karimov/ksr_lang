namespace KSR.Lexer;

public class KsrLexException : Exception
{
    public int Line { get; }
    public int Col  { get; }

    public KsrLexException(string message, int line, int col)
        : base($"Lexer error at {line}:{col} — {message}")
    {
        Line = line;
        Col  = col;
    }
}
