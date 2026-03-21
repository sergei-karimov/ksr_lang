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
    private readonly string      _sourceFile;
    private int _pos;

    public Parser(List<Token> tokens, string sourceFile = "")
    {
        _tokens     = tokens;
        _sourceFile = sourceFile;
    }

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
        // ── optional @ValueTask annotation ────────────────────────────────────
        var asyncReturn = AsyncReturnKind.Task;
        if (Check(TokenType.At))
        {
            Consume(); // @
            var ann = Expect(TokenType.Identifier).Value;
            if (ann != "ValueTask")
                throw new KsrParseException(
                    $"Unknown annotation '@{ann}' — only '@ValueTask' is supported",
                    Current.Line, Current.Col);
            asyncReturn = AsyncReturnKind.ValueTask;
        }

        // ── optional async modifier ───────────────────────────────────────────
        bool isAsync = false;
        if (Check(TokenType.Async))
        {
            isAsync = true;
            Consume();
        }

        // @ValueTask without async is invalid
        if (asyncReturn == AsyncReturnKind.ValueTask && !isAsync)
            throw new KsrParseException(
                "@ValueTask can only be used on async functions",
                Current.Line, Current.Col);

        if (Check(TokenType.Use))
        {
            if (isAsync) throw new KsrParseException(
                "'async' cannot be applied to 'use'", Current.Line, Current.Col);
            return ParseUseDecl();
        }
        if (Check(TokenType.Data))      return ParseDataClass();
        if (Check(TokenType.Fun))       return ParseFunctionOrExtension(isAsync, asyncReturn);
        if (Check(TokenType.Interface)) return ParseInterfaceDecl();
        if (Check(TokenType.Implement)) return ParseImplBlock();

        throw new KsrParseException(
            $"Unexpected token '{Current.Value}' — expected 'use', 'data', 'fun', 'interface' or 'implement'",
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
        var props = Check(TokenType.RParen) ? [] : ParseParamList();
        Expect(TokenType.RParen);
        return new DataClassDecl(name, props);
    }

    /// <summary>
    /// interface Shape { fun area(): Double \n async fun fetch(): String }
    /// </summary>
    private InterfaceDecl ParseInterfaceDecl()
    {
        Expect(TokenType.Interface);
        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LBrace);

        var methods = new List<InterfaceMethod>();
        while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
        {
            // Optional async modifier on interface methods
            var mAsyncReturn = AsyncReturnKind.Task;
            if (Check(TokenType.At))
            {
                Consume();
                var ann = Expect(TokenType.Identifier).Value;
                if (ann != "ValueTask")
                    throw new KsrParseException(
                        $"Unknown annotation '@{ann}'", Current.Line, Current.Col);
                mAsyncReturn = AsyncReturnKind.ValueTask;
            }
            bool mIsAsync = false;
            if (Check(TokenType.Async)) { mIsAsync = true; Consume(); }

            Expect(TokenType.Fun);
            var mname = Expect(TokenType.Identifier).Value;
            Expect(TokenType.LParen);
            var parms = Check(TokenType.RParen) ? [] : ParseParamList();
            Expect(TokenType.RParen);
            TypeRef? ret = null;
            if (Match(TokenType.Colon)) ret = ParseTypeRef();
            if (mIsAsync) ValidateAsyncReturnType(ret, mname);
            methods.Add(new InterfaceMethod(mname, parms, ret, mIsAsync, mAsyncReturn));
        }

        Expect(TokenType.RBrace);
        return new InterfaceDecl(name, methods);
    }

    /// <summary>
    /// implement Shape for Circle { fun area(): Double { … } … }
    /// </summary>
    private ImplBlock ParseImplBlock()
    {
        Expect(TokenType.Implement);
        var interfaceName = Expect(TokenType.Identifier).Value;
        Expect(TokenType.For);
        var typeName = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LBrace);

        var methods = new List<FunctionDecl>();
        while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
        {
            var mAsyncReturn = AsyncReturnKind.Task;
            if (Check(TokenType.At))
            {
                Consume();
                var ann = Expect(TokenType.Identifier).Value;
                if (ann != "ValueTask")
                    throw new KsrParseException(
                        $"Unknown annotation '@{ann}'", Current.Line, Current.Col);
                mAsyncReturn = AsyncReturnKind.ValueTask;
            }
            bool mIsAsync = false;
            if (Check(TokenType.Async)) { mIsAsync = true; Consume(); }

            Expect(TokenType.Fun);
            var mname = Expect(TokenType.Identifier).Value;
            methods.Add(ParseFunctionTail(mname, mIsAsync, mAsyncReturn));
        }

        Expect(TokenType.RBrace);
        return new ImplBlock(interfaceName, typeName, methods);
    }

    /// <summary>
    /// Handles both:
    ///   fun name(...)             → FunctionDecl
    ///   fun TypeName.method(...)  → ExtFunctionDecl
    /// Both may be preceded by async / @ValueTask async.
    /// </summary>
    private AstNode ParseFunctionOrExtension(
        bool isAsync = false,
        AsyncReturnKind asyncReturn = AsyncReturnKind.Task)
    {
        Expect(TokenType.Fun);
        var firstName = Expect(TokenType.Identifier).Value;

        // Extension function: fun ReceiverType.methodName(...)
        if (Check(TokenType.Dot))
        {
            Consume(); // .
            var methodName = Expect(TokenType.Identifier).Value;
            return ParseExtFunctionTail(firstName, methodName, isAsync, asyncReturn);
        }

        return ParseFunctionTail(firstName, isAsync, asyncReturn);
    }

    private FunctionDecl ParseFunctionTail(
        string name,
        bool isAsync = false,
        AsyncReturnKind asyncReturn = AsyncReturnKind.Task)
    {
        Expect(TokenType.LParen);
        var parms = new List<Parameter>();
        if (!Check(TokenType.RParen)) parms = ParseParamList();
        Expect(TokenType.RParen);

        TypeRef? retType = null;
        if (Match(TokenType.Colon)) retType = ParseTypeRef();

        if (isAsync) ValidateAsyncReturnType(retType, name);

        return new FunctionDecl(name, parms, retType, ParseBlock(), isAsync, asyncReturn);
    }

    private ExtFunctionDecl ParseExtFunctionTail(
        string receiverType,
        string methodName,
        bool isAsync = false,
        AsyncReturnKind asyncReturn = AsyncReturnKind.Task)
    {
        Expect(TokenType.LParen);
        var parms = new List<Parameter>();
        if (!Check(TokenType.RParen)) parms = ParseParamList();
        Expect(TokenType.RParen);

        TypeRef? retType = null;
        if (Match(TokenType.Colon)) retType = ParseTypeRef();

        if (isAsync) ValidateAsyncReturnType(retType, $"{receiverType}.{methodName}");

        return new ExtFunctionDecl(receiverType, methodName, parms, retType,
                                   ParseBlock(), isAsync, asyncReturn);
    }

    /// <summary>
    /// Rejects explicit Task/ValueTask wrapper types in async return annotations.
    /// The annotation should be the inner type only; the compiler adds the wrapper.
    /// </summary>
    private void ValidateAsyncReturnType(TypeRef? ret, string funcName)
    {
        if (ret is null) return;
        var n = ret.Name;
        if (n == "Task" || n.StartsWith("Task<") ||
            n == "ValueTask" || n.StartsWith("ValueTask<"))
            throw new KsrParseException(
                $"async function '{funcName}': write the inner return type " +
                $"(e.g. 'String'), not the Task wrapper ('{n}'). " +
                "The compiler adds Task/ValueTask automatically.",
                Current.Line, Current.Col);
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
        var name = Expect(TokenType.Identifier).Value;

        // Generic type: List<T>  Map<K, V>
        if (Check(TokenType.Lt))
        {
            Consume(); // <
            var args = new List<string> { ParseTypeRef().Name };
            while (Match(TokenType.Comma))
                args.Add(ParseTypeRef().Name);
            Expect(TokenType.Gt); // >
            name = $"{name}<{string.Join(", ", args)}>";
        }

        // Array type: Bool[]  Int[]  etc.
        if (Check(TokenType.LBracket))
        {
            Consume(); // [
            Expect(TokenType.RBracket); // ]
            name += "[]";
        }

        var nullable = Match(TokenType.Question);
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
        int line = Current.Line;

        Stmt stmt;
        if (Check(TokenType.Val))    stmt = ParseValDecl();
        else if (Check(TokenType.Var))    stmt = ParseVarDecl();
        else if (Check(TokenType.Return)) stmt = ParseReturnStmt();
        else if (Check(TokenType.If))     stmt = ParseIfStmt();
        else if (Check(TokenType.While))  stmt = ParseWhileStmt();
        else if (Check(TokenType.For))    stmt = ParseForStmt();
        else if (Check(TokenType.Identifier))
        {
            var next = Peek().Type;
            if (next == TokenType.Equals)    stmt = ParseAssign();
            else if (next == TokenType.PlusEq ||
                     next == TokenType.MinusEq)   stmt = ParseCompoundAssign();
            else if (next == TokenType.LBracket)  stmt = ParseIndexAssign();
            else stmt = new ExprStmt(ParseExpr());
        }
        else stmt = new ExprStmt(ParseExpr());

        return stmt with { Line = line, SourceFile = _sourceFile };
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

    private IndexAssignStmt ParseIndexAssign()
    {
        var name = Consume().Value;          // identifier
        Expect(TokenType.LBracket);
        var index = ParseExpr();
        Expect(TokenType.RBracket);
        Expect(TokenType.Equals);
        return new IndexAssignStmt(name, index, ParseExpr());
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
        if (Check(TokenType.Await))
        {
            Consume();
            return new AwaitExpr(ParseUnary()); // right-associative: await await x
        }
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
            else if (Check(TokenType.LBracket))
            {
                Consume(); // [
                var index = ParseExpr();
                Expect(TokenType.RBracket);
                expr = new IndexExpr(expr, index);
            }
            else if (Check(TokenType.SafeCall))
            {
                Consume();
                var member = Expect(TokenType.Identifier).Value;
                expr = new SafeCallExpr(expr, member);
            }
            // Trailing lambda: expr { ... }  or  expr.method { ... }
            // Appended as the last argument to the preceding call.
            else if (Check(TokenType.LBrace))
            {
                var lambda = ParseLambdaExpr();
                // Wrap in a call if not already one, or append to existing call
                expr = expr is CallExpr ce
                    ? new CallExpr(ce.Callee, [..ce.Arguments, lambda])
                    : new CallExpr(expr, [lambda]);
            }
            else break;
        }
        return expr;
    }

    // ── lambda ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a lambda literal starting at <c>{</c>.
    /// <code>
    ///   { expr }           — implicit "it" parameter
    ///   { x → expr }       — single named parameter
    ///   { x, y → expr }    — multiple named parameters
    ///   { → expr }         — zero parameters (e.g. for Func&lt;T&gt;)
    /// </code>
    /// </summary>
    private LambdaExpr ParseLambdaExpr()
    {
        Expect(TokenType.LBrace);
        var parms = new List<string>();

        if (Check(TokenType.Arrow))
        {
            // { -> expr }  — zero parameters
            Consume();
        }
        else if (Check(TokenType.Identifier) &&
                 (Peek().Type == TokenType.Arrow || Peek().Type == TokenType.Comma))
        {
            // { x -> expr }  or  { x, y -> expr }
            parms.Add(Consume().Value);
            while (Match(TokenType.Comma))
                parms.Add(Expect(TokenType.Identifier).Value);
            Expect(TokenType.Arrow);
        }
        else
        {
            // { expr }  — implicit "it"
            parms.Add("it");
        }

        var body = ParseExpr();
        Expect(TokenType.RBrace);
        return new LambdaExpr(parms, body);
    }

    private Expr ParsePrimary()
    {
        var tok = Current;

        switch (tok.Type)
        {
            case TokenType.IntLiteral:
                Consume();
                return new IntLiteral(int.Parse(tok.Value));

            case TokenType.FloatLiteral:
                Consume();
                return new DoubleLiteral(double.Parse(tok.Value,
                    System.Globalization.CultureInfo.InvariantCulture));

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

            case TokenType.New:
            {
                Consume(); // new
                var typeName = Expect(TokenType.Identifier).Value;
                if (Check(TokenType.LBracket))
                {
                    // new Bool[size]  →  array creation
                    Consume(); // [
                    var size = ParseExpr();
                    Expect(TokenType.RBracket);
                    return new NewArrayExpr(new TypeRef(typeName, false), size);
                }
                else
                {
                    // new Random(args)  →  object creation
                    Expect(TokenType.LParen);
                    var args = new List<Expr>();
                    if (!Check(TokenType.RParen))
                    {
                        args.Add(ParseExpr());
                        while (Match(TokenType.Comma))
                            args.Add(ParseExpr());
                    }
                    Expect(TokenType.RParen);
                    return new NewObjectExpr(typeName, args);
                }
            }

            // List / Map literal: [a, b, c]  or  ["k1": v1, "k2": v2]
            case TokenType.LBracket:
                return ParseCollectionLiteral();

            // when expression
            case TokenType.When:
                return ParseWhenExpr();

            // Lambda literal  { expr }  /  { x -> expr }  /  { x, y -> expr }  /  { -> expr }
            case TokenType.LBrace:
                return ParseLambdaExpr();

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

    // ── when expression ───────────────────────────────────────────────────────

    /// <summary>
    /// when (expr) { arm* }   — subject form (match by value)
    /// when { arm* }          — subject-less form (guard conditions)
    ///
    /// arm ::= expr -> expr
    ///       | else -> expr
    /// </summary>
    private WhenExpr ParseWhenExpr()
    {
        Expect(TokenType.When);

        Expr? subject = null;
        if (Match(TokenType.LParen))
        {
            subject = ParseExpr();
            Expect(TokenType.RParen);
        }

        Expect(TokenType.LBrace);
        var arms = new List<WhenArm>();

        while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
        {
            Expr? pattern;
            if (Check(TokenType.Else))
            {
                Consume(); // else
                pattern = null;
            }
            else
            {
                pattern = ParseExpr();
            }
            Expect(TokenType.Arrow);
            var body = ParseExpr();
            arms.Add(new WhenArm(pattern, body));
        }

        Expect(TokenType.RBrace);
        return new WhenExpr(subject, arms);
    }

    // ── collection literals ───────────────────────────────────────────────────

    /// <summary>
    /// Parses a list or map literal starting at <c>[</c>.
    /// <code>
    ///   [1, 2, 3]            — list literal
    ///   ["k1": v1, "k2": v2] — map literal  (distinguished by ':' after first key)
    ///   []                   — empty list
    /// </code>
    /// </summary>
    private Expr ParseCollectionLiteral()
    {
        Expect(TokenType.LBracket);

        // Empty list
        if (Check(TokenType.RBracket))
        {
            Consume();
            return new ListLiteralExpr(new List<Expr>());
        }

        var first = ParseExpr();

        // Map literal: first key is followed by ':'
        if (Check(TokenType.Colon))
        {
            Consume(); // :
            var firstVal = ParseExpr();
            var entries = new List<(Expr, Expr)> { (first, firstVal) };
            while (Match(TokenType.Comma))
            {
                if (Check(TokenType.RBracket)) break; // trailing comma
                var k = ParseExpr();
                Expect(TokenType.Colon);
                var v = ParseExpr();
                entries.Add((k, v));
            }
            Expect(TokenType.RBracket);
            return new MapLiteralExpr(entries);
        }

        // List literal
        var elems = new List<Expr> { first };
        while (Match(TokenType.Comma))
        {
            if (Check(TokenType.RBracket)) break; // trailing comma
            elems.Add(ParseExpr());
        }
        Expect(TokenType.RBracket);
        return new ListLiteralExpr(elems);
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
