using KSR.AST;
using KSR.Lexer;
using Xunit;

namespace KSR.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  AsyncTests  —  async/await keyword support
//
//  Sections:
//    1. Lexer
//    2. Parser — AST shape
//    3. Parser — error cases
//    4. CodeGen — generated C# content
// ─────────────────────────────────────────────────────────────────────────────

public class AsyncLexerTests
{
    [Fact]
    public void Lex_AsyncKeyword()
    {
        var tokens = KsrHelper.Lex("async");
        Assert.Equal(TokenType.Async, tokens[0].Type);
    }

    [Fact]
    public void Lex_AwaitKeyword()
    {
        var tokens = KsrHelper.Lex("await");
        Assert.Equal(TokenType.Await, tokens[0].Type);
    }

    [Fact]
    public void Lex_AtSign()
    {
        var tokens = KsrHelper.Lex("@ValueTask");
        Assert.Equal(TokenType.At,         tokens[0].Type);
        Assert.Equal(TokenType.Identifier, tokens[1].Type);
        Assert.Equal("ValueTask",          tokens[1].Value);
    }

    [Fact]
    public void Lex_AsyncDoesNotBreakIdentifierStartingWithAsync()
    {
        // "asyncFoo" should still be an identifier, not async + Foo
        var tokens = KsrHelper.Lex("asyncFoo");
        Assert.Equal(TokenType.Identifier, tokens[0].Type);
        Assert.Equal("asyncFoo",           tokens[0].Value);
    }
}

public class AsyncParserTests
{
    // ── FunctionDecl async flag ───────────────────────────────────────────────

    [Fact]
    public void Parse_AsyncFun_Void_SetsIsAsync()
    {
        var prog = KsrHelper.Parse("async fun f() {}");
        var fd   = Assert.IsType<FunctionDecl>(prog.Declarations[0]);
        Assert.True(fd.IsAsync);
        Assert.Null(fd.ReturnType);
        Assert.Equal(AsyncReturnKind.Task, fd.AsyncReturn);
    }

    [Fact]
    public void Parse_AsyncFun_WithInnerReturnType()
    {
        var prog = KsrHelper.Parse("async fun fetchName(): String {}");
        var fd   = Assert.IsType<FunctionDecl>(prog.Declarations[0]);
        Assert.True(fd.IsAsync);
        Assert.Equal("String", fd.ReturnType!.Name);
    }

    [Fact]
    public void Parse_NonAsyncFun_IsAsyncFalse()
    {
        var prog = KsrHelper.Parse("fun f() {}");
        var fd   = Assert.IsType<FunctionDecl>(prog.Declarations[0]);
        Assert.False(fd.IsAsync);
    }

    // ── @ValueTask annotation ─────────────────────────────────────────────────

    [Fact]
    public void Parse_ValueTaskAnnotation_SetsAsyncReturn()
    {
        var prog = KsrHelper.Parse("@ValueTask async fun f(): Int {}");
        var fd   = Assert.IsType<FunctionDecl>(prog.Declarations[0]);
        Assert.True(fd.IsAsync);
        Assert.Equal(AsyncReturnKind.ValueTask, fd.AsyncReturn);
    }

    // ── await expression ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_AwaitExpr_InsideAsyncFun()
    {
        var prog = KsrHelper.Parse("async fun f() { val x = await someTask() }");
        var fd   = Assert.IsType<FunctionDecl>(prog.Declarations[0]);
        var vd   = Assert.IsType<ValDecl>(fd.Body.Statements[0]);
        var ae   = Assert.IsType<AwaitExpr>(vd.Value);
        var call = Assert.IsType<CallExpr>(ae.Operand);
        Assert.IsType<IdentifierExpr>(call.Callee);
    }

    [Fact]
    public void Parse_AwaitExpr_IsRightAssociative()
    {
        // await await x  →  AwaitExpr(AwaitExpr(x))
        var prog = KsrHelper.Parse("async fun f() { val x = await await g() }");
        var fd   = Assert.IsType<FunctionDecl>(prog.Declarations[0]);
        var vd   = Assert.IsType<ValDecl>(fd.Body.Statements[0]);
        var outer = Assert.IsType<AwaitExpr>(vd.Value);
        Assert.IsType<AwaitExpr>(outer.Operand);
    }

    [Fact]
    public void Parse_AwaitExpr_AsStatement()
    {
        var prog = KsrHelper.Parse("async fun f() { await doSomething() }");
        var fd   = Assert.IsType<FunctionDecl>(prog.Declarations[0]);
        var es   = Assert.IsType<ExprStmt>(fd.Body.Statements[0]);
        Assert.IsType<AwaitExpr>(es.Expression);
    }

    // ── extension function ────────────────────────────────────────────────────

    [Fact]
    public void Parse_AsyncExtFun_SetsIsAsync()
    {
        var prog = KsrHelper.Parse("async fun Foo.bar(): String {}");
        var efd  = Assert.IsType<ExtFunctionDecl>(prog.Declarations[0]);
        Assert.True(efd.IsAsync);
    }

    // ── interface method ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_AsyncInterfaceMethod()
    {
        var prog = KsrHelper.Parse("interface Fetcher { async fun fetch(): String }");
        var id   = Assert.IsType<InterfaceDecl>(prog.Declarations[0]);
        var m    = id.Methods[0];
        Assert.True(m.IsAsync);
        Assert.Equal("String", m.ReturnType!.Name);
    }

    // ── impl block ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_AsyncImplMethod()
    {
        var prog = KsrHelper.Parse(
            "struct Foo()\n" +
            "interface Bar { async fun run(): Int }\n" +
            "implement Bar for Foo { async fun run(): Int { return 1 } }");
        var impl = (ImplBlock)prog.Declarations[2];
        Assert.True(impl.Methods[0].IsAsync);
    }
}

public class AsyncParserErrorTests
{
    [Fact]
    public void Parse_TaskWrapperInReturnType_Throws()
    {
        // Writing Task<String> directly is rejected — inner type only
        Assert.Throws<KSR.Parser.KsrParseException>(() =>
            KsrHelper.Parse("async fun f(): Task<String> {}"));
    }

    [Fact]
    public void Parse_ValueTaskWrapperInReturnType_Throws()
    {
        Assert.Throws<KSR.Parser.KsrParseException>(() =>
            KsrHelper.Parse("async fun f(): ValueTask<Int> {}"));
    }

    [Fact]
    public void Parse_ValueTaskAnnotationWithoutAsync_Throws()
    {
        Assert.Throws<KSR.Parser.KsrParseException>(() =>
            KsrHelper.Parse("@ValueTask fun f(): Int {}"));
    }
}

public class AsyncCodeGenTests
{
    // ── void async function ───────────────────────────────────────────────────

    [Fact]
    public void CodeGen_AsyncFun_Void_EmitsAsyncTask()
    {
        var cs = KsrHelper.Generate("async fun f() {}");
        Assert.Contains("async Task f()", cs);
    }

    [Fact]
    public void CodeGen_AsyncFun_Void_EmitsAsyncModifier()
    {
        var cs = KsrHelper.Generate("async fun f() {}");
        Assert.Contains("async ", cs);
    }

    // ── typed async function ──────────────────────────────────────────────────

    [Fact]
    public void CodeGen_AsyncFun_WithReturnType_EmitsTaskT()
    {
        var cs = KsrHelper.Generate("async fun greet(): String {}");
        // Top-level fun names are not Pascal-cased (only extension/record methods are)
        Assert.Contains("async Task<string> greet()", cs);
    }

    [Fact]
    public void CodeGen_AsyncFun_IntReturn_EmitsTaskInt()
    {
        var cs = KsrHelper.Generate("async fun count(): Int {}");
        Assert.Contains("async Task<int> count()", cs);
    }

    // ── @ValueTask annotation ─────────────────────────────────────────────────

    [Fact]
    public void CodeGen_ValueTaskAnnotation_EmitsValueTask()
    {
        var cs = KsrHelper.Generate("@ValueTask async fun f(): Int {}");
        Assert.Contains("async ValueTask<int> f()", cs);
    }

    [Fact]
    public void CodeGen_ValueTaskAnnotation_VoidEmitsValueTask()
    {
        var cs = KsrHelper.Generate("@ValueTask async fun f() {}");
        Assert.Contains("async ValueTask f()", cs);
    }

    // ── global --async-return=valuetask flag ──────────────────────────────────

    [Fact]
    public void CodeGen_GlobalValueTaskFlag_OverridesDefault()
    {
        var cs = new KSR.CodeGen.CodeGenerator(AsyncReturnKind.ValueTask)
            .Generate(KsrHelper.Parse("async fun f(): String {}"));
        Assert.Contains("async ValueTask<string>", cs);
    }

    [Fact]
    public void CodeGen_GlobalValueTaskFlag_PerFunctionValueTaskWins()
    {
        // Global=Task, per-function=ValueTask → ValueTask
        var cs = new KSR.CodeGen.CodeGenerator(AsyncReturnKind.Task)
            .Generate(KsrHelper.Parse("@ValueTask async fun f(): Int {}"));
        Assert.Contains("async ValueTask<int>", cs);
    }

    // ── await expression ──────────────────────────────────────────────────────

    [Fact]
    public void CodeGen_AwaitExpr_EmitsAwait()
    {
        var cs = KsrHelper.Generate("async fun f() { val x = await g() }");
        // Bare function calls are not Pascal-cased
        Assert.Contains("await g()", cs);
    }

    [Fact]
    public void CodeGen_AwaitExpr_OutsideAsync_EmitsHashError()
    {
        // Should inject a #error directive so Roslyn fails with a clear message
        var cs = KsrHelper.Generate("fun f() { val x = await g() }");
        Assert.Contains("#error", cs);
        Assert.Contains("await", cs);
    }

    // ── non-async functions unchanged ─────────────────────────────────────────

    [Fact]
    public void CodeGen_NonAsync_NoTaskWrapper()
    {
        var cs = KsrHelper.Generate("fun add(a: Int, b: Int): Int { return a }");
        Assert.DoesNotContain("Task", cs);
        Assert.DoesNotContain("async", cs);
    }

    // ── interface method ──────────────────────────────────────────────────────

    [Fact]
    public void CodeGen_AsyncInterfaceMethod_NoAsyncKeyword_HasTaskReturnType()
    {
        // C# interface signatures don't use 'async' keyword; return type carries Task
        var cs = KsrHelper.Generate("interface Fetcher { async fun fetch(): String }");
        Assert.Contains("Task<string> Fetch()", cs);
        // The 'async' keyword must NOT appear in the interface body
        var interfaceBody = cs[(cs.IndexOf("interface IFetcher"))..];
        var closingBrace  = interfaceBody.IndexOf('}');
        var body = interfaceBody[..closingBrace];
        Assert.DoesNotContain("async Task", body);
    }

    // ── async main ────────────────────────────────────────────────────────────

    [Fact]
    public void CodeGen_AsyncMain_EmitsAsyncTaskMain()
    {
        var cs = KsrHelper.Generate("async fun main() {}");
        Assert.Contains("async Task Main()", cs);
    }
}
