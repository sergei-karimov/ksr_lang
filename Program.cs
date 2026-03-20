using KSR.CodeGen;
using KSR.Parser;

// ─────────────────────────────────────────────────────────────────────────────
//  KSR — Kotlin-Style Runtime language
//  Usage:
//      dotnet run                       # runs the built-in demo
//      dotnet run -- file.ksr           # compiles + runs a .ksr source file
//      dotnet run -- file.ksr --debug   # also prints generated C#
// ─────────────────────────────────────────────────────────────────────────────

bool   debugMode = args.Contains("--debug");
string source;

if (args.Length > 0 && !args[0].StartsWith("--"))
{
    var path = args[0];
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"ksr: file not found: {path}");
        Environment.Exit(1);
    }
    source = File.ReadAllText(path);
}
else
{
    PrintHelp();
    Environment.Exit(0);
    source = ""; // unreachable
}

try
{
    // ── Lex ──────────────────────────────────────────────────────────────────
    var lexer  = new KSR.Lexer.Lexer(source);
    var tokens = lexer.Tokenize();

    // ── Parse ─────────────────────────────────────────────────────────────────
    var parser  = new Parser(tokens);
    var program = parser.Parse();

    // ── Generate C# ──────────────────────────────────────────────────────────
    var codegen  = new CodeGenerator();
    var csharpSrc = codegen.Generate(program);

    // ── Compile & Run via Roslyn ─────────────────────────────────────────────
    KsrCompiler.CompileAndRun(csharpSrc, debugMode);
}
catch (KSR.Lexer.KsrLexException   ex) { Fail(ex.Message); }
catch (KsrParseException            ex) { Fail(ex.Message); }
catch (KsrCompileException          ex) { Fail(ex.Message); }
catch (Exception                    ex) { Fail($"Internal error: {ex}"); }

static void Fail(string msg)
{
    Console.Error.WriteLine($"error: {msg}");
    Environment.Exit(1);
}

static void PrintHelp()
{
    Console.WriteLine("""
        KSR — Kotlin-Style Runtime language

        USAGE
          ksr <file.ksr> [--debug]

        ARGUMENTS
          file.ksr      Path to the KSR source file to compile and run
          --debug       Print the generated C# before executing

        EXAMPLES
          ksr hello.ksr
          ksr hello.ksr --debug

        LANGUAGE FEATURES
          data class Point(x: Int, y: Int)       value type (no inheritance)

          val x = 42                             immutable binding
          var n = 0                              mutable binding
          n += 1                                 compound assignment

          fun add(a: Int, b: Int): Int { ... }   function
          fun Point.len(): Int { ... }           extension function (this = receiver)

          "Hello, ${name}!"                      string template
          if (x > 0) { ... } else { ... }        conditional
          while (n < 10) { n += 1 }              while loop
          for (i in 1..10) { ... }               inclusive range loop
          for (i in 0..<10) { ... }              exclusive range loop
          for (item in list) { ... }             collection loop

          user?.name ?: "default"                null-safe access + elvis

        TYPES
          Int   String   Bool   Double   Float   Long
          Append ? for nullable:  String?  User?
        """);
}
