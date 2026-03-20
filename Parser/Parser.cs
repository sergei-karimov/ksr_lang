using KSR.AST;
using KSR.Lexer;

namespace KSR.Parser;

/// <summary>
/// Recursive-descent parser.
///
/// Expression precedence (lowest → highest):
///   elvis       ?:
///   logicalOr   ||
///   logicalAnd  &&
///   equality    == !=
///   comparison  &lt; &gt; &lt;= &gt;=
///   range       .. ..(less than)
///   additive    + -
///   multiplicative * / %
///   unary       ! -
///   postfix     call()  .member  ?.member
///   primary     literals / identifiers / (expr)
/// </summary>
public class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;

    public Parser(List<Token> tokens) => _tokens = tokens;

    // ── token helpers ─────────────────────────────────────────────────────────

    private Token Current => _tokens[_pos];
    private Token Peek(int offset = 1) => _tokens[Math.Min(_pos + offset, _tokens.Count - 1)];

    private Token Consume() => _tokens[_pos++];

    private Token Expect(TokenType type)
    {
        if (Current.Type != type)
            throw new KsrParseException(
                $"Expected '{type}' but found '{Current.Value}' ({Current.Type})",
                Current.Line, Current.Col);
        return Consume();
    }

    private bool Check(TokenType type) => Current.Type == type;

    private bool Match(TokenType type)
    {
        if (!Check(type)) return false;
        Consume();
        return true;
    }

    // ── entry point ───────────────────────────────────────────────────────────

    public ProgramNode Parse()
    {
        var decls = new List<AstNode>();
        while (!Check(TokenType.Eof))
            decls.Add(ParseDeclaration());
        return new ProgramNode(decls);
    }

    // ── declarations ──────────────────────────────────────────────────────────

    private AstNode ParseDeclaration()
    {
        if (Check(TokenType.Use))  return ParseUseDecl();
        if (Check(TokenType.Data)) return ParseDataClass();
        if (Check(TokenType.Fun))  return ParseFunctionOrExtension();

        throw new KsrParseException(
            $"Unexpected token '{Current.Value}' — expected 'use', 'data' or 'fun'",
            Current.Line, Current.Col);
    }

    private UseDecl ParseUseDecl()
    {
        Expect(TokenType.Use);
        // Namespace may be dotted: Raylib_cs  or  System.Collections.Generic
        var sb = new System.Text.StringBuilder();
        sb.Append(Expect(TokenType.Identifier).Value);
        while (Check(TokenType.Dot))
        {
            Consume();
            sb.Append('.');
            sb.Append(Expect(TokenType.Identifier).Value);
        }
        return new UseDecl(sb.ToString());
    }

    private DataClassDecl ParseDataClass()
    {
        Expect(TokenType.Data);
        Expect(TokenType.Class);
        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LParen);
        var props = ParseParamList();
        Expect(TokenType.RParen);
        return new DataClassDecl(name, props);
    }

    /// <summary>
    /// Handles both:
    ///   fun name(...)           → FunctionDecl
    ///   fun TypeName.method(...)  → ExtFunctionDecl
    /// </summary>
    private AstNode ParseFunctionOrExtension()
    {
        Expect(TokenType.Fun);
        var firstName = Expect(TokenType.Identifier).Value;

        // Extension function: fun ReceiverType.methodName(...)
        if (Check(TokenType.Dot))
        {
            Consume(); // .
            var methodName = Expect(TokenType.Identifier).Value;
            return ParseExtFunctionTail(firstName, methodName);
        }

        return ParseFunctionTail(firstName);
    }

    private FunctionDecl ParseFunctionTail(string name)
    {
        Expect(TokenType.LParen);
        var parms = new List<Parameter>();
        if (!Check(TokenType.RParen)) parms = ParseParamList();
        Expect(TokenType.RParen);

        TypeRef? retType = null;
        if (Match(TokenType.Colon)) retType = ParseTypeRef();

        return new FunctionDecl(name, parms, retType, ParseBlock());
    }

    private ExtFunctionDecl ParseExtFunctionTail(string receiverType, string methodName)
    {
        Expect(TokenType.LParen);
        var parms = new List<Parameter>();
        if (!Check(TokenType.RParen)) parms = ParseParamList();
        Expect(TokenType.RParen);

        TypeRef? retType = null;
        if (Match(TokenType.Colon)) retType = ParseTypeRef();

        return new ExtFunctionDecl(receiverType, methodName, parms, retType, ParseBlock());
    }

    // ── parameters / types ────────────────────────────────────────────────────

    private List<Parameter> ParseParamList()
    {
        var list = new List<Parameter> { ParseParam() };
        while (Match(TokenType.Comma))
            list.Add(ParseParam());
        return list;
    }

    private Parameter ParseParam()
    {
        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.Colon);
        return new Parameter(name, ParseTypeRef());
    }

    private TypeRef ParseTypeRef()
    {
        var name     = Expect(TokenType.Identifier).Value;
        var nullable = Match(TokenType.Question);   // '?' but not ?. or ?:
        return new TypeRef(name, nullable);
    }

    // ── block & statements ────────────────────────────────────────────────────

    private Block ParseBlock()
    {
        Expect(TokenType.LBrace);
        var stmts = new List<Stmt>();
        while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
            stmts.Add(ParseStatement());
        Expect(TokenType.RBrace);
        return new Block(stmts);
    }

    private Stmt ParseStatement()
    {
        if (Check(TokenType.Val))    return ParseValDecl();
        if (Check(TokenType.Var))    return ParseVarDecl();
        if (Check(TokenType.Return)) return ParseReturnStmt();
        if (Check(TokenType.If))     return ParseIfStmt();
        if (Check(TokenType.While))  return ParseWhileStmt();
        if (Check(TokenType.For))    return ParseForStmt();

        // x = expr   or   x += expr / x -= expr
        if (Check(TokenType.Identifier))
        {
            var next = Peek().Type;
            if (next == TokenType.Equals)   return ParseAssign();
            if (next == TokenType.PlusEq ||
                next == TokenType.MinusEq)  return ParseCompoundAssign();
        }

        return new ExprStmt(ParseExpr());
    }

    private ValDecl ParseValDecl()
    {
        Expect(TokenType.Val);
        var name = Expect(TokenType.Identifier).Value;
        TypeRef? type = null;
        if (Match(TokenType.Colon)) type = ParseTypeRef();
        Expect(TokenType.Equals);
        return new ValDecl(name, type, ParseExpr());
    }

    private VarDecl ParseVarDecl()
    {
        Expect(TokenType.Var);
        var name = Expect(TokenType.Identifier).Value;
        TypeRef? type = null;
        if (Match(TokenType.Colon)) type = ParseTypeRef();
        Expect(TokenType.Equals);
        return new VarDecl(name, type, ParseExpr());
    }

    private AssignStmt ParseAssign()
    {
        var name = Consume().Value; // identifier
        Expect(TokenType.Equals);
        return new AssignStmt(name, ParseExpr());
    }

    private CompoundAssignStmt ParseCompoundAssign()
    {
        var name = Consume().Value; // identifier
        var op   = Consume().Value; // += or -=
        return new CompoundAssignStmt(name, op, ParseExpr());
    }

    private ReturnStmt ParseReturnStmt()
    {
        Expect(TokenType.Return);
        Expr? value = null;
        if (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
            value = ParseExpr();
        return new ReturnStmt(value);
    }

    private IfStmt ParseIfStmt()
    {
        Expect(TokenType.If);
        Expect(TokenType.LParen);
        var cond = ParseExpr();
        Expect(TokenType.RParen);
        var then = ParseBlock();
        Block? else_ = null;
        if (Match(TokenType.Else)) else_ = ParseBlock();
        return new IfStmt(cond, then, else_);
    }

    private WhileStmt ParseWhileStmt()
    {
        Expect(TokenType.While);
        Expect(TokenType.LParen);
        var cond = ParseExpr();
        Expect(TokenType.RParen);
        return new WhileStmt(cond, ParseBlock());
    }

    private ForInStmt ParseForStmt()
    {
        Expect(TokenType.For);
        Expect(TokenType.LParen);
        var varName = Expect(TokenType.Identifier).Value;
        Expect(TokenType.In);
        var iterable = ParseExpr(); // naturally handles RangeExpr via ParseRange()
        Expect(TokenType.RParen);
        return new ForInStmt(varName, iterable, ParseBlock());
    }

    // ── expressions (public so sub-parsers for string templates can call it) ──

    public Expr ParseExpr() => ParseElvis();

    private Expr ParseElvis()
    {
        var left = ParseLogicalOr();
        while (Check(TokenType.Elvis))
        {
            Consume();
            left = new ElvisExpr(left, ParseLogicalOr());
        }
        return left;
    }

    private Expr ParseLogicalOr()
    {
        var left = ParseLogicalAnd();
        while (Check(TokenType.PipePipe))
        {
            var op = Consume().Value;
            left = new BinaryExpr(left, op, ParseLogicalAnd());
        }
        return left;
    }

    private Expr ParseLogicalAnd()
    {
        var left = ParseEquality();
        while (Check(TokenType.AmpAmp))
        {
            var op = Consume().Value;
            left = new BinaryExpr(left, op, ParseEquality());
        }
        return left;
    }

    private Expr ParseEquality()
    {
        var left = ParseComparison();
        while (Check(TokenType.EqEq) || Check(TokenType.BangEq))
        {
            var op = Consume().Value;
            left = new BinaryExpr(left, op, ParseComparison());
        }
        return left;
    }

    private Expr ParseComparison()
    {
        var left = ParseRange();
        while (Check(TokenType.Lt) || Check(TokenType.Gt) ||
               Check(TokenType.LtEq) || Check(TokenType.GtEq))
        {
            var op = Consume().Value;
            left = new BinaryExpr(left, op, ParseRange());
        }
        return left;
    }

    private Expr ParseRange()
    {
        var left = ParseAdditive();
        if (Check(TokenType.DotDot))
        {
            Consume();
            return new RangeExpr(left, ParseAdditive(), Inclusive: true);
        }
        if (Check(TokenType.DotDotLt))
        {
            Consume();
            return new RangeExpr(left, ParseAdditive(), Inclusive: false);
        }
        return left;
    }

    private Expr ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Check(TokenType.Plus) || Check(TokenType.Minus))
        {
            var op = Consume().Value;
            left = new BinaryExpr(left, op, ParseMultiplicative());
        }
        return left;
    }

    private Expr ParseMultiplicative()
    {
        var left = ParseUnary();
        while (Check(TokenType.Star) || Check(TokenType.Slash) || Check(TokenType.Percent))
        {
            var op = Consume().Value;
            left = new BinaryExpr(left, op, ParseUnary());
        }
        return left;
    }

    private Expr ParseUnary()
    {
        if (Check(TokenType.Bang))
        {
            var op = Consume().Value;
            return new UnaryExpr(op, ParseUnary());
        }
        if (Check(TokenType.Minus))
        {
            var op = Consume().Value;
            return new UnaryExpr(op, ParseUnary());
        }
        return ParsePostfix();
    }

    private Expr ParsePostfix()
    {
        var expr = ParsePrimary();
        while (true)
        {
            if (Check(TokenType.LParen))
            {
                Consume();
                var args = new List<Expr>();
                if (!Check(TokenType.RParen))
                {
                    args.Add(ParseExpr());
                    while (Match(TokenType.Comma))
                        args.Add(ParseExpr());
                }
                Expect(TokenType.RParen);
                expr = new CallExpr(expr, args);
            }
            else if (Check(TokenType.Dot))
            {
                Consume();
                var member = Expect(TokenType.Identifier).Value;
                expr = new MemberAccessExpr(expr, member);
            }
            else if (Check(TokenType.SafeCall))
            {
                Consume();
                var member = Expect(TokenType.Identifier).Value;
                expr = new SafeCallExpr(expr, member);
            }
            else break;
        }
        return expr;
    }

    private Expr ParsePrimary()
    {
        var tok = Current;

        switch (tok.Type)
        {
            case TokenType.IntLiteral:
                Consume();
                return new IntLiteral(int.Parse(tok.Value));

            case TokenType.StringLiteral:
                Consume();
                return new StringLiteral(tok.Value);

            case TokenType.StringTemplate:
                Consume();
                return ParseStringTemplate(tok.Value);

            case TokenType.True:  Consume(); return new BoolLiteral(true);
            case TokenType.False: Consume(); return new BoolLiteral(false);
            case TokenType.Null:  Consume(); return new NullLiteral();
            case TokenType.This:  Consume(); return new ThisExpr();

            case TokenType.Identifier:
                Consume();
                return new IdentifierExpr(tok.Value);

            case TokenType.LParen:
                Consume();
                var inner = ParseExpr();
                Expect(TokenType.RParen);
                return inner;

            default:
                throw new KsrParseException(
                    $"Unexpected token '{tok.Value}' ({tok.Type}) in expression",
                    tok.Line, tok.Col);
        }
    }

    // ── string template parsing ───────────────────────────────────────────────

    /// <summary>
    /// Splits the raw template value (which uses the <c>${...}</c> convention
    /// normalised by the lexer) into literal and expression parts.
    /// Each expression part is parsed by a fresh sub-parser.
    /// </summary>
    private StringTemplateExpr ParseStringTemplate(string rawValue)
    {
        var parts = new List<StringPart>();
        int i = 0;

        while (i < rawValue.Length)
        {
            int dollarPos = rawValue.IndexOf("${", i);
            if (dollarPos == -1)
            {
                if (i < rawValue.Length)
                    parts.Add(new LiteralPart(rawValue[i..]));
                break;
            }

            // Literal text before ${
            if (dollarPos > i)
                parts.Add(new LiteralPart(rawValue[i..dollarPos]));

            // Find matching }
            int exprStart = dollarPos + 2;
            int j = exprStart;
            int depth = 1;
            while (j < rawValue.Length && depth > 0)
            {
                if      (rawValue[j] == '{') depth++;
                else if (rawValue[j] == '}') depth--;
                j++;
            }

            var exprText = rawValue[exprStart..(j - 1)];

            try
            {
                var subTokens = new KSR.Lexer.Lexer(exprText).Tokenize();
                var subParser = new Parser(subTokens);
                parts.Add(new ExprPart(subParser.ParseExpr()));
            }
            catch
            {
                // Fallback: emit as opaque literal so we still produce valid C#
                parts.Add(new LiteralPart("${" + exprText + "}"));
            }

            i = j;
        }

        return new StringTemplateExpr(parts);
    }
}
