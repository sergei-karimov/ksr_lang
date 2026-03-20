namespace KSR.Parser;

public class KsrParseException : Exception
{
    public int Line { get; }
    public int Col  { get; }

    public KsrParseException(string message, int line, int col)
        : base($"Parse error at {line}:{col} — {message}")
    {
        Line = line;
        Col  = col;
    }
}
