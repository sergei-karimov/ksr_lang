using KSR.AST;
using KSR.Lexer;

namespace KSR.Parser;

/// <summary>
/// Recursive-descent parser with error recovery.
/// </summary>
public class Parser
{
    private readonly List<Token> _tokens;
    private readonly string      _sourceFile;
    private int _pos;
    private readonly List<string> _errors = new();
    private readonly bool _throwOnError;

    public Parser(List<Token> tokens, string sourceFile = "", bool throwOnError = false)
    {
        _tokens     = tokens;
        _sourceFile = sourceFile;
        _throwOnError = throwOnError;
    }

    public IReadOnlyList<string> Errors => _errors;

    // ── token helpers ─────────────────────────────────────────────────────────

    private Token Current => _pos < _tokens.Count ? _tokens[_pos] : _tokens[^1];
    private Token Peek(int offset = 1) => _tokens[Math.Min(_pos + offset, _tokens.Count - 1)];

    private Token Consume() => _pos < _tokens.Count ? _tokens[_pos++] : _tokens[^1];

    private Token Expect(TokenType type)
    {
        if (Current.Type != type)
        {
            Error($"Expected '{type}' but found '{Current.Value}' ({Current.Type})");
            // If we're at EOF, don't try to consume
            if (Current.Type == TokenType.Eof) throw new KsrParseException("EOF", Current.Line, Current.Col);
            return Consume(); // Pseudo-consume to avoid infinite loops in some callers
        }
        return Consume();
    }

    private void Error(string message)
    {
        var err = $"{_sourceFile}({Current.Line},{Current.Col}): error: {message}";
        _errors.Add(err);
        // We still throw internally to trigger Synchronize() in caller loops,
        // or to satisfy tests that expect a fatal error.
        throw new KsrParseException(message, Current.Line, Current.Col);
    }

    private bool Check(TokenType type) => Current.Type == type;

    private bool Match(TokenType type)
    {
        if (!Check(type)) return false;
        Consume();
        return true;
    }

    /// <summary>
    /// Skips tokens until we find a reliable recovery point (start of a new declaration).
    /// </summary>
    private void Synchronize()
    {
        if (_throwOnError) return; // Don't synchronize if we want to fail fast

        Consume();

        while (!Check(TokenType.Eof))
        {
            switch (Current.Type)
            {
                case TokenType.Use:
                case TokenType.Struct:
                case TokenType.Sealed:
                case TokenType.Fun:
                case TokenType.Interface:
                case TokenType.Implement:
                case TokenType.Val:
                case TokenType.Var:
                case TokenType.If:
                case TokenType.While:
                case TokenType.For:
                case TokenType.Return:
                    return;
            }

            Consume();
        }
    }

    // ── entry point ───────────────────────────────────────────────────────────

    public ProgramNode Parse()
    {
        var decls = new List<AstNode>();
        while (!Check(TokenType.Eof))
        {
            try
            {
                decls.Add(ParseDeclaration());
            }
            catch (KsrParseException)
            {
                if (_throwOnError) throw;
                Synchronize();
            }
        }
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
            {
                // Note: Error() throws, so this triggers Synchronize() in Parse()
                Error($"Unknown annotation '@{ann}' — only '@ValueTask' is supported");
            }
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
            Error("@ValueTask can only be used on async functions");

        if (Check(TokenType.Use))
        {
            if (isAsync) Error("'async' cannot be applied to 'use'");
            return ParseUseDecl();
        }
        if (Check(TokenType.Struct))    return ParseStruct();
        if (Check(TokenType.Sealed))    return ParseSealedDecl();
        if (Check(TokenType.Fun))       return ParseFunctionOrExtension(isAsync, asyncReturn);
        if (Check(TokenType.Interface)) return ParseInterfaceDecl();
        if (Check(TokenType.Implement)) return ParseImplBlock();

        Error($"Unexpected token '{Current.Value}' — expected 'use', 'struct', 'sealed', 'fun', 'interface' or 'implement'");
        return null!; // Unreachable due to Error throwing
    }

    private UseDecl ParseUseDecl()
    {
        Expect(TokenType.Use);
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

    private StructDecl ParseStruct()
    {
        Expect(TokenType.Struct);
        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LParen);
        var props = Check(TokenType.RParen) ? [] : ParseParamList();
        Expect(TokenType.RParen);
        return new StructDecl(name, props);
    }

    private SealedDecl ParseSealedDecl()
    {
        Expect(TokenType.Sealed);
        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LBrace);

        var variants = new List<StructDecl>();
        while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
        {
            try
            {
                Expect(TokenType.Struct);
                var vName = Expect(TokenType.Identifier).Value;
                List<Parameter> props = [];
                if (Check(TokenType.LParen))
                {
                    Consume(); // (
                    props = Check(TokenType.RParen) ? [] : ParseParamList();
                    Expect(TokenType.RParen);
                }
                variants.Add(new StructDecl(vName, props));
            }
            catch (KsrParseException)
            {
                // Local recovery inside sealed block: skip to next 'struct' or '}'
                while (!Check(TokenType.Struct) && !Check(TokenType.RBrace) && !Check(TokenType.Eof))
                    Consume();
            }
        }

        Expect(TokenType.RBrace);
        return new SealedDecl(name, variants);
    }

    private InterfaceDecl ParseInterfaceDecl()
    {
        Expect(TokenType.Interface);
        var name = Expect(TokenType.Identifier).Value;

        var typeParams = new List<string>();
        if (Check(TokenType.Lt))
        {
            Consume(); // <
            typeParams.Add(Expect(TokenType.Identifier).Value);
            while (Match(TokenType.Comma))
                typeParams.Add(Expect(TokenType.Identifier).Value);
            Expect(TokenType.Gt);
        }

        var constraints = new List<WhereConstraint>();
        if (Current.Type == TokenType.Identifier && Current.Value == "where")
        {
            Consume(); // "where"
            do
            {
                var tp = Expect(TokenType.Identifier).Value;
                Expect(TokenType.Colon);
                var bounds = new List<string> { ParseTypeRef().Name };
                constraints.Add(new WhereConstraint(tp, bounds));
            }
            while (Match(TokenType.Comma) && !(Current.Type == TokenType.LBrace));
        }

        Expect(TokenType.LBrace);

        var methods = new List<InterfaceMethod>();
        while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
        {
            try
            {
                var mAsyncReturn = AsyncReturnKind.Task;
                if (Check(TokenType.At))
                {
                    Consume();
                    var ann = Expect(TokenType.Identifier).Value;
                    if (ann != "ValueTask") Error($"Unknown annotation '@{ann}'");
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
            catch (KsrParseException)
            {
                while (!Check(TokenType.Fun) && !Check(TokenType.Async) && !Check(TokenType.At) && !Check(TokenType.RBrace) && !Check(TokenType.Eof))
                    Consume();
            }
        }

        Expect(TokenType.RBrace);
        return new InterfaceDecl(name, typeParams, constraints, methods);
    }

    private ImplBlock ParseImplBlock()
    {
        Expect(TokenType.Implement);
        var interfaceName = Expect(TokenType.Identifier).Value;

        var interfaceTypeArgs = new List<string>();
        if (Check(TokenType.Lt))
        {
            Consume(); // <
            interfaceTypeArgs.Add(ParseTypeRef().Name);
            while (Match(TokenType.Comma))
                interfaceTypeArgs.Add(ParseTypeRef().Name);
            Expect(TokenType.Gt);
        }

        Expect(TokenType.For);
        var typeName = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LBrace);

        var methods = new List<FunctionDecl>();
        while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
        {
            try
            {
                var mAsyncReturn = AsyncReturnKind.Task;
                if (Check(TokenType.At))
                {
                    Consume();
                    var ann = Expect(TokenType.Identifier).Value;
                    if (ann != "ValueTask") Error($"Unknown annotation '@{ann}'");
                    mAsyncReturn = AsyncReturnKind.ValueTask;
                }
                bool mIsAsync = false;
                if (Check(TokenType.Async)) { mIsAsync = true; Consume(); }

                Expect(TokenType.Fun);
                var mname = Expect(TokenType.Identifier).Value;
                methods.Add(ParseFunctionTail(mname, [], mIsAsync, mAsyncReturn));
            }
            catch (KsrParseException)
            {
                while (!Check(TokenType.Fun) && !Check(TokenType.Async) && !Check(TokenType.At) && !Check(TokenType.RBrace) && !Check(TokenType.Eof))
                    Consume();
            }
        }

        Expect(TokenType.RBrace);
        return new ImplBlock(interfaceName, interfaceTypeArgs, typeName, methods);
    }

    private AstNode ParseFunctionOrExtension(
        bool isAsync = false,
        AsyncReturnKind asyncReturn = AsyncReturnKind.Task)
    {
        Expect(TokenType.Fun);

        var typeParams = new List<string>();
        if (Check(TokenType.Lt))
        {
            Consume(); // <
            typeParams.Add(Expect(TokenType.Identifier).Value);
            while (Match(TokenType.Comma))
                typeParams.Add(Expect(TokenType.Identifier).Value);
            Expect(TokenType.Gt);
        }

        var firstName = ParseTypeRef().Name;

        if (Check(TokenType.Dot))
        {
            Consume(); // .
            var methodName = Expect(TokenType.Identifier).Value;
            return ParseExtFunctionTail(firstName, methodName, typeParams, isAsync, asyncReturn);
        }

        return ParseFunctionTail(firstName, typeParams, isAsync, asyncReturn);
    }

    private FunctionDecl ParseFunctionTail(
        string name,
        List<string> typeParams,
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

        return new FunctionDecl(name, typeParams, parms, retType, ParseBlock(), isAsync, asyncReturn);
    }

    private ExtFunctionDecl ParseExtFunctionTail(
        string receiverType,
        string methodName,
        List<string> typeParams,
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

        return new ExtFunctionDecl(receiverType, methodName, typeParams, parms, retType,
                                   ParseBlock(), isAsync, asyncReturn);
    }

    private void ValidateAsyncReturnType(TypeRef? ret, string funcName)
    {
        if (ret is null) return;
        var n = ret.Name;
        if (n == "Task" || n.StartsWith("Task<") ||
            n == "ValueTask" || n.StartsWith("ValueTask<"))
            Error($"async function '{funcName}': write the inner return type " +
                $"(e.g. 'String'), not the Task wrapper ('{n}'). " +
                "The compiler adds Task/ValueTask automatically.");
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
        var type = ParseTypeRef();
        Expr? defaultValue = null;
        if (Match(TokenType.Equals))
            defaultValue = ParseExpr();
        return new Parameter(name, type, defaultValue);
    }

    private Expr ParseCallArg()
    {
        if (Current.Type == TokenType.Identifier && Peek().Type == TokenType.Equals)
            return new NamedArgExpr(Consume().Value, (Consume(), ParseExpr()).Item2);
        return ParseExpr();
    }

    private TypeRef ParseTypeRef()
    {
        var name = Expect(TokenType.Identifier).Value;

        if (Check(TokenType.Lt))
        {
            Consume(); // <
            var args = new List<string> { ParseTypeRef().Name };
            while (Match(TokenType.Comma))
                args.Add(ParseTypeRef().Name);
            Expect(TokenType.Gt); // >
            name = $"{name}<{string.Join(", ", args)}>";
        }

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
        {
            try
            {
                stmts.Add(ParseStatement());
            }
            catch (KsrParseException)
            {
                if (_throwOnError) throw;

                // Local recovery inside block: skip to next statement or '}'
                while (!Check(TokenType.Val) && !Check(TokenType.Var) && !Check(TokenType.Return) &&
                       !Check(TokenType.If) && !Check(TokenType.While) && !Check(TokenType.For) &&
                       !Check(TokenType.RBrace) && !Check(TokenType.Eof))
                {
                    Consume();
                }
            }
        }
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
        var iterable = ParseExpr();
        Expect(TokenType.RParen);
        return new ForInStmt(varName, iterable, ParseBlock());
    }

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
            return new AwaitExpr(ParseUnary());
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
                    args.Add(ParseCallArg());
                    while (Match(TokenType.Comma))
                        args.Add(ParseCallArg());
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
            else if (Check(TokenType.LBrace))
            {
                var lambda = ParseLambdaExpr();
                expr = expr is CallExpr ce
                    ? new CallExpr(ce.Callee, [..ce.Arguments, lambda])
                    : new CallExpr(expr, [lambda]);
            }
            else break;
        }
        return expr;
    }

    private LambdaExpr ParseLambdaExpr()
    {
        Expect(TokenType.LBrace);
        var parms = new List<string>();
        var explicitParams = false;

        if (Check(TokenType.Arrow))
        {
            Consume();
            explicitParams = true;
        }
        else if (Check(TokenType.Identifier) &&
                 (Peek().Type == TokenType.Arrow || Peek().Type == TokenType.Comma))
        {
            parms.Add(Consume().Value);
            while (Match(TokenType.Comma))
                parms.Add(Expect(TokenType.Identifier).Value);
            Expect(TokenType.Arrow);
            explicitParams = true;
        }
        else
        {
            if (LooksLikeStatementStart(Current.Type))
                explicitParams = true;
            else
                parms.Add("it");
        }

        if (LooksLikeStatementStart(Current.Type))
        {
            var body = ParseLambdaBlockBody();
            return new LambdaExpr(parms, null, body);
        }

        var expr = ParseExpr();
        if (Check(TokenType.RBrace))
        {
            Consume();
            return new LambdaExpr(parms, expr);
        }

        var stmts = new List<Stmt> { new ExprStmt(expr) };
        stmts.AddRange(ParseLambdaStatementsUntilBrace());

        if (!explicitParams && parms.Count == 1 && parms[0] == "it")
            parms.Clear();

        return new LambdaExpr(parms, null, new Block(stmts));
    }

    private Block ParseLambdaBlockBody() =>
        new(ParseLambdaStatementsUntilBrace());

    private List<Stmt> ParseLambdaStatementsUntilBrace()
    {
        var stmts = new List<Stmt>();
        while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
            stmts.Add(ParseStatement());
        Expect(TokenType.RBrace);
        return stmts;
    }

    private static bool LooksLikeStatementStart(TokenType type) => type is
        TokenType.Val or
        TokenType.Var or
        TokenType.Return or
        TokenType.If or
        TokenType.While or
        TokenType.For;

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

            case TokenType.RawStringLiteral:
                Consume();
                return new StringLiteral(tok.Value, IsRaw: true);

            case TokenType.RawStringTemplate:
                Consume();
                return ParseStringTemplate(tok.Value, isRaw: true);

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
                    Consume(); // [
                    var size = ParseExpr();
                    Expect(TokenType.RBracket);
                    return new NewArrayExpr(new TypeRef(typeName, false), size);
                }
                else
                {
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

            case TokenType.LBracket:
                return ParseCollectionLiteral();

            case TokenType.When:
                return ParseWhenExpr();

            case TokenType.LBrace:
                return ParseLambdaExpr();

            case TokenType.LParen:
                Consume();
                var inner = ParseExpr();
                Expect(TokenType.RParen);
                return inner;

            default:
                Error($"Unexpected token '{tok.Value}' ({tok.Type}) in expression");
                return null!;
        }
    }

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
            try
            {
                Expr? pattern;
                if (Check(TokenType.Else))
                {
                    Consume(); // else
                    pattern = null;
                }
                else if (Check(TokenType.Is))
                {
                    Consume(); // is
                    var typeName = Expect(TokenType.Identifier).Value;
                    string? binding = null;
                    if (Check(TokenType.LParen))
                    {
                        Consume(); // (
                        binding = Expect(TokenType.Identifier).Value;
                        Expect(TokenType.RParen);
                    }
                    pattern = new IsPatternExpr(typeName, binding);
                }
                else
                {
                    pattern = ParseExpr();
                }
                Expect(TokenType.Arrow);
                var body = ParseExpr();
                arms.Add(new WhenArm(pattern, body));
            }
            catch (KsrParseException)
            {
                while (!Check(TokenType.Else) && !Check(TokenType.Is) && !Check(TokenType.Arrow) &&
                       !Check(TokenType.RBrace) && !Check(TokenType.Eof))
                {
                    Consume();
                }
                if (Match(TokenType.Arrow)) ParseExpr(); // try to skip body
            }
        }

        Expect(TokenType.RBrace);
        return new WhenExpr(subject, arms);
    }

    private Expr ParseCollectionLiteral()
    {
        Expect(TokenType.LBracket);

        if (Check(TokenType.RBracket))
        {
            Consume();
            return new ListLiteralExpr(new List<Expr>());
        }

        var first = ParseExpr();

        if (Check(TokenType.Colon))
        {
            Consume(); // :
            var firstVal = ParseExpr();
            var entries = new List<(Expr, Expr)> { (first, firstVal) };
            while (Match(TokenType.Comma))
            {
                if (Check(TokenType.RBracket)) break;
                var k = ParseExpr();
                Expect(TokenType.Colon);
                var v = ParseExpr();
                entries.Add((k, v));
            }
            Expect(TokenType.RBracket);
            return new MapLiteralExpr(entries);
        }

        var elems = new List<Expr> { first };
        while (Match(TokenType.Comma))
        {
            if (Check(TokenType.RBracket)) break;
            elems.Add(ParseExpr());
        }
        Expect(TokenType.RBracket);
        return new ListLiteralExpr(elems);
    }

    private StringTemplateExpr ParseStringTemplate(string rawValue, bool isRaw = false)
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

            if (dollarPos > i)
                parts.Add(new LiteralPart(rawValue[i..dollarPos]));

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
                _errors.AddRange(subParser.Errors); // Bubble up errors from templates
            }
            catch
            {
                parts.Add(new LiteralPart("${" + exprText + "}"));
            }

            i = j;
        }

        return new StringTemplateExpr(parts, isRaw);
    }
}
