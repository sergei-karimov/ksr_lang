using System.Text.Json;
using KSR.CodeGen;
using KSR.Parser;

// ─────────────────────────────────────────────────────────────────────────────
//  KSR — Kotlin-Style Runtime language
//
//  USAGE
//    ksr <file.ksr> [--debug]     compile and run a single .ksr file
//    ksr check <file>             output JSON diagnostics (for editors)
//    ksr lsp                      start Language Server (JSON-RPC over stdio)
//
//  PROJECT WORKFLOW  (standard .NET commands)
//    dotnet new ksr-console -n MyApp
//    dotnet add package Raylib-cs
//    dotnet run / dotnet build / dotnet publish
// ─────────────────────────────────────────────────────────────────────────────

bool debugMode = args.Contains("--debug");
var  positional = args.Where(a => !a.StartsWith("--")).ToArray();

if (positional.Length == 0)
{
    PrintHelp();
    Environment.Exit(0);
}

try
{
    switch (positional[0])
    {
        case "check":
            CheckFile(positional.ElementAtOrDefault(1) ?? "");
            break;

        case "lsp":
            KSR.LSP.LspServer.Run();
            break;

        default:
        {
            var path = positional[0];
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"ksr: file not found: {path}");
                PrintHelp();
                Environment.Exit(1);
            }
            RunSingleFile(path, debugMode);
            break;
        }
    }
}
catch (KSR.Lexer.KsrLexException   ex) { Fail(ex.Message); }
catch (KsrParseException            ex) { Fail(ex.Message); }
catch (KsrCompileException          ex) { Fail(ex.Message); }
catch (Exception                    ex) { Fail($"Internal error: {ex}"); }

// ── helpers ───────────────────────────────────────────────────────────────────

static void CheckFile(string path)
{
    if (!File.Exists(path))
    {
        Console.WriteLine("[]");
        return;
    }

    try
    {
        var source = File.ReadAllText(path);
        var tokens = new KSR.Lexer.Lexer(source).Tokenize();
        new KSR.Parser.Parser(tokens).Parse();
        Console.WriteLine("[]");
    }
    catch (KSR.Lexer.KsrLexException ex)
    {
        Console.WriteLine(JsonSerializer.Serialize(new[]
        {
            new { message = ex.Message, line = ex.Line, col = ex.Col }
        }));
    }
    catch (KsrParseException ex)
    {
        Console.WriteLine(JsonSerializer.Serialize(new[]
        {
            new { message = ex.Message, line = ex.Line, col = ex.Col }
        }));
    }
    catch (Exception ex)
    {
        Console.WriteLine(JsonSerializer.Serialize(new[]
        {
            new { message = ex.Message, line = 1, col = 1 }
        }));
    }
}

static void RunSingleFile(string path, bool debugMode)
{
    // Ensure KSR.StdLib assembly is loaded into the AppDomain so that Roslyn
    // (strategy 3 in ResolveReferences) can find it when compiling ksr.io / ksr.text.
    _ = typeof(KSR.Io.IO).Assembly;

    var source    = File.ReadAllText(path);
    var tokens    = new KSR.Lexer.Lexer(source).Tokenize();
    var program   = new Parser(tokens, Path.GetFullPath(path)).Parse();
    var csharpSrc = new KSR.CodeGen.CodeGenerator().Generate(program);
    KsrCompiler.CompileAndRun(csharpSrc, debugMode);
}

static void Fail(string msg)
{
    Console.Error.WriteLine($"error: {msg}");
    Environment.Exit(1);
}

static void PrintHelp()
{
    Console.WriteLine("""
        KSR — Kotlin-Style Runtime language

        SINGLE-FILE MODE
          ksr <file.ksr>               Compile and run a .ksr file
          ksr <file.ksr> --debug       Also print the generated C# source

        EDITOR INTEGRATION
          ksr check <file>             Output JSON diagnostics
          ksr lsp                      Language Server (JSON-RPC/stdio)

        PROJECT WORKFLOW  (standard .NET)
          dotnet new ksr-console -n MyApp
          cd MyApp
          dotnet add package Raylib-cs
          dotnet run
          dotnet build
          dotnet publish

        LANGUAGE FEATURES
          data class Point(x: Int, y: Int)       value type
          val x = 42                             immutable binding
          var n = 0                              mutable binding
          fun add(a: Int, b: Int): Int { ... }   function
          fun Point.len(): Int { ... }           extension function
          use Raylib_cs                          namespace import
          "Hello, ${name}!"                      string template
          if / while / for (i in 1..10) { }      control flow
          when (x) { 1 -> "one"  else -> "?" }  pattern matching
          user?.name ?: "default"                null-safe access + elvis
          new Bool[size]                         array allocation
          cells[i] = v                           array write

        TYPES
          Int   String   Bool   Double   Float   Long   Unit
          Append ? for nullable:  String?   User?
        """);
}
