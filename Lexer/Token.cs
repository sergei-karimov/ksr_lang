namespace KSR.Lexer;

/// <summary>A single token produced by the lexer.</summary>
public record Token(TokenType Type, string Value, int Line, int Col)
{
    public override string ToString() => $"{Type}({Value}) @{Line}:{Col}";
}
