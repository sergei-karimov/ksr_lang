using System.Text;

namespace KSR.Lexer;

/// <summary>
/// Converts KSR source text into a flat list of tokens.
///
/// New in this version:
///   • arithmetic / comparison / logical operators
///   • compound assignment  +=  -=
///   • range operators  ..  ..<
///   • keywords: var, while, for, in, this
///   • string templates  "Hello, ${name}!"  and  "$name"  shorthand
/// </summary>
public class Lexer
{
    private readonly string _src;
    private int    _pos  = 0;
    private int    _line = 1;
    private int    _col  = 1;

    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        ["use"]    = TokenType.Use,
        ["new"]    = TokenType.New,
        ["val"]    = TokenType.Val,
        ["var"]    = TokenType.Var,
        ["fun"]    = TokenType.Fun,
        ["data"]   = TokenType.Data,
        ["class"]  = TokenType.Class,
        ["if"]     = TokenType.If,
        ["else"]   = TokenType.Else,
        ["return"] = TokenType.Return,
        ["while"]  = TokenType.While,
        ["for"]    = TokenType.For,
        ["in"]     = TokenType.In,
        ["this"]   = TokenType.This,
        ["true"]      = TokenType.True,
        ["false"]     = TokenType.False,
        ["null"]      = TokenType.Null,
        ["interface"] = TokenType.Interface,
        ["implement"] = TokenType.Implement,
    };

    public Lexer(string source) => _src = source;

    // ── helpers ───────────────────────────────────────────────────────────────

    private char Current   => _pos     < _src.Length ? _src[_pos]     : '\0';
    private char LookAhead => _pos + 1 < _src.Length ? _src[_pos + 1] : '\0';

    private void Step()
    {
        if (Current == '\n') { _line++; _col = 1; } else { _col++; }
        _pos++;
    }

    // ── entry point ───────────────────────────────────────────────────────────

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (_pos < _src.Length)
        {
            SkipWhitespaceAndComments();
            if (_pos >= _src.Length) break;
            tokens.Add(Next());
        }
        tokens.Add(new Token(TokenType.Eof, "", _line, _col));
        return tokens;
    }

    // ── internal helpers ──────────────────────────────────────────────────────

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _src.Length)
        {
            if (Current == '/' && LookAhead == '/')
            {
                while (_pos < _src.Length && Current != '\n') Step();
                continue;
            }
            if (Current is ' ' or '\t' or '\r' or '\n') { Step(); continue; }
            break;
        }
    }

    private Token Next()
    {
        int line = _line, col = _col;
        char c = Current;

        if (char.IsLetter(c) || c == '_') return ReadWord(line, col);
        if (char.IsDigit(c))              return ReadInt(line, col);
        if (c == '"')                     return ReadString(line, col);

        Step(); // consume c

        return c switch
        {
            // ── single-char with possible compound forms ──────────────────────
            '+' => Current == '=' ? StepTok(TokenType.PlusEq,   "+=", line, col)
                                  : Tok(TokenType.Plus,          "+",  line, col),

            '-' => Current == '=' ? StepTok(TokenType.MinusEq,  "-=", line, col)
                 : Current == '>' ? StepTok(TokenType.Arrow,    "->", line, col)
                 :                  Tok(TokenType.Minus,         "-",  line, col),

            '!' => Current == '=' ? StepTok(TokenType.BangEq,   "!=", line, col)
                                  : Tok(TokenType.Bang,          "!",  line, col),

            '=' => Current == '=' ? StepTok(TokenType.EqEq,     "==", line, col)
                                  : Tok(TokenType.Equals,        "=",  line, col),

            '<' => Current == '=' ? StepTok(TokenType.LtEq,     "<=", line, col)
                                  : Tok(TokenType.Lt,            "<",  line, col),

            '>' => Current == '=' ? StepTok(TokenType.GtEq,     ">=", line, col)
                                  : Tok(TokenType.Gt,            ">",  line, col),

            '&' => Current == '&' ? StepTok(TokenType.AmpAmp,   "&&", line, col)
                                  : throw new KsrLexException("Expected '&&'", line, col),

            '|' => Current == '|' ? StepTok(TokenType.PipePipe, "||", line, col)
                                  : throw new KsrLexException("Expected '||'", line, col),

            // ── dot / range ───────────────────────────────────────────────────
            '.' when Current == '.' =>
                LookAhead == '<'
                    ? Step2Tok(TokenType.DotDotLt, "..<", line, col)  // ..< (step over second . and <)
                    : StepTok(TokenType.DotDot,    "..",  line, col),  // ..

            '.' => Tok(TokenType.Dot, ".", line, col),

            // ── null-safety ───────────────────────────────────────────────────
            '?' => Current == '.' ? StepTok(TokenType.SafeCall, "?.", line, col)
                 : Current == ':' ? StepTok(TokenType.Elvis,    "?:", line, col)
                 :                  Tok(TokenType.Question,     "?",  line, col),

            // ── single-char symbols ───────────────────────────────────────────
            '*' => Tok(TokenType.Star,    "*",  line, col),
            '/' => Tok(TokenType.Slash,   "/",  line, col),
            '%' => Tok(TokenType.Percent, "%",  line, col),
            ':' => Tok(TokenType.Colon,   ":",  line, col),
            ',' => Tok(TokenType.Comma,   ",",  line, col),
            '(' => Tok(TokenType.LParen,  "(",  line, col),
            ')' => Tok(TokenType.RParen,  ")",  line, col),
            '{' => Tok(TokenType.LBrace,    "{",  line, col),
            '}' => Tok(TokenType.RBrace,    "}",  line, col),
            '[' => Tok(TokenType.LBracket,  "[",  line, col),
            ']' => Tok(TokenType.RBracket,  "]",  line, col),

            _ => throw new KsrLexException($"Unexpected character '{c}'", line, col)
        };
    }

    private Token Tok(TokenType t, string v, int line, int col) => new(t, v, line, col);

    /// Consume one more character, then return the token.
    private Token StepTok(TokenType t, string v, int line, int col)
    { Step(); return new Token(t, v, line, col); }

    /// Consume two more characters (e.g. for "..<": we already consumed first '.', now step '.' and '<').
    private Token Step2Tok(TokenType t, string v, int line, int col)
    { Step(); Step(); return new Token(t, v, line, col); }

    // ── word / identifier ─────────────────────────────────────────────────────

    private Token ReadWord(int line, int col)
    {
        int start = _pos;
        while (_pos < _src.Length && (char.IsLetterOrDigit(Current) || Current == '_'))
            Step();
        var text = _src[start.._pos];
        var type = Keywords.TryGetValue(text, out var kw) ? kw : TokenType.Identifier;
        return new Token(type, text, line, col);
    }

    // ── integer literal ───────────────────────────────────────────────────────

    private Token ReadInt(int line, int col)
    {
        int start = _pos;
        while (_pos < _src.Length && char.IsDigit(Current)) Step();

        // Float literal: digits followed by '.' and more digits (e.g. 3.14, 1.0)
        if (Current == '.' && _pos + 1 < _src.Length && char.IsDigit(_src[_pos + 1]))
        {
            Step(); // consume '.'
            while (_pos < _src.Length && char.IsDigit(Current)) Step();
            return new Token(TokenType.FloatLiteral, _src[start.._pos], line, col);
        }

        return new Token(TokenType.IntLiteral, _src[start.._pos], line, col);
    }

    // ── string / string template ──────────────────────────────────────────────

    /// <summary>
    /// Reads a double-quoted string.  If any <c>${...}</c> or <c>$identifier</c>
    /// interpolations are found the token type becomes <see cref="TokenType.StringTemplate"/>
    /// and the raw value preserves all <c>${...}</c> markers so the parser can
    /// split and parse each embedded expression.
    /// </summary>
    private Token ReadString(int line, int col)
    {
        Step(); // opening "
        var sb = new StringBuilder();
        bool isTemplate = false;

        while (_pos < _src.Length && Current != '"')
        {
            if (Current == '\\')
            {
                Step();
                char esc = Current switch
                {
                    'n'  => '\n',
                    't'  => '\t',
                    '"'  => '"',
                    '\\' => '\\',
                    '$'  => '$',
                    _    => Current
                };
                sb.Append(esc);
                Step();
            }
            else if (Current == '$')
            {
                if (LookAhead == '{')
                {
                    // ${...} template  — capture until matching '}'
                    isTemplate = true;
                    sb.Append("${");
                    Step(); // $
                    Step(); // {
                    int depth = 1;
                    while (_pos < _src.Length && depth > 0)
                    {
                        if      (Current == '{') depth++;
                        else if (Current == '}') { depth--; if (depth == 0) break; }
                        sb.Append(Current);
                        Step();
                    }
                    sb.Append('}');
                    if (Current == '}') Step(); // consume the closing '}'
                }
                else if (char.IsLetter(LookAhead) || LookAhead == '_')
                {
                    // $name shorthand — normalise to ${name}
                    isTemplate = true;
                    sb.Append("${");
                    Step(); // $
                    while (_pos < _src.Length && (char.IsLetterOrDigit(Current) || Current == '_'))
                    {
                        sb.Append(Current);
                        Step();
                    }
                    sb.Append('}');
                }
                else
                {
                    sb.Append(Current);
                    Step();
                }
            }
            else
            {
                sb.Append(Current);
                Step();
            }
        }

        if (Current != '"')
            throw new KsrLexException("Unterminated string literal", line, col);
        Step(); // closing "

        var type = isTemplate ? TokenType.StringTemplate : TokenType.StringLiteral;
        return new Token(type, sb.ToString(), line, col);
    }
}
