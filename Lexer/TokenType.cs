namespace KSR.Lexer;

public enum TokenType
{
    // ── Literals ──────────────────────────────────────────────────────────────
    IntLiteral,
    FloatLiteral,   // numeric literal with a decimal point: 3.14, 1.0
    StringLiteral,
    StringTemplate,     // string containing ${...} interpolations

    // ── Keywords ──────────────────────────────────────────────────────────────
    Use,        // use  (namespace import)
    Val,        // val  (immutable binding)
    Var,        // var  (mutable binding)
    Fun,        // fun
    Struct,     // struct
    New,        // new
    If,         // if
    Else,       // else
    Return,     // return
    While,      // while
    For,        // for
    In,         // in
    This,       // this (receiver in extension functions)
    True,       // true
    False,      // false
    Null,       // null
    Interface,  // interface
    Implement,  // implement
    When,       // when   (pattern-matching expression)
    Async,      // async  (async function modifier)
    Await,      // await  (await expression)
    At,         // @      (annotation sigil: @ValueTask)

    // ── Identifier ────────────────────────────────────────────────────────────
    Identifier,

    // ── Assignment ────────────────────────────────────────────────────────────
    Equals,     // =
    PlusEq,     // +=
    MinusEq,    // -=
    Arrow,      // ->  (lambda parameter separator)

    // ── Arithmetic ────────────────────────────────────────────────────────────
    Plus,       // +
    Minus,      // -
    Star,       // *
    Slash,      // /
    Percent,    // %

    // ── Comparison ────────────────────────────────────────────────────────────
    EqEq,       // ==
    BangEq,     // !=
    Lt,         // <
    Gt,         // >
    LtEq,       // <=
    GtEq,       // >=

    // ── Logical ───────────────────────────────────────────────────────────────
    Bang,       // !
    AmpAmp,     // &&
    PipePipe,   // ||

    // ── Null-safety ───────────────────────────────────────────────────────────
    Question,   // ?   (nullable type marker)
    SafeCall,   // ?.  (safe member access)
    Elvis,      // ?:  (null coalescing)

    // ── Range ─────────────────────────────────────────────────────────────────
    DotDot,     // ..  (inclusive range)
    DotDotLt,   // ..< (exclusive range)

    // ── Punctuation ───────────────────────────────────────────────────────────
    Colon,      // :
    Comma,      // ,
    Dot,        // .
    LParen,     // (
    RParen,     // )
    LBrace,     // {
    RBrace,     // }
    LBracket,   // [
    RBracket,   // ]

    // ── Meta ──────────────────────────────────────────────────────────────────
    Eof
}
