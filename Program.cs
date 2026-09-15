using System.Text.Json;
using KSR.Analysis;
using KSR.CodeGen;
using KSR.Diagnostics;
using KSR.Parser;

// ─────────────────────────────────────────────────────────────────────────────
//  Kestrel — Kotlin-style language for .NET
//
//  USAGE
//    kestrel <file.ksr> [--debug] compile and run a single .ksr file
//    kestrel check <file>         output JSON diagnostics (for editors)
//    kestrel lsp                  start Language Server (JSON-RPC over stdio)
//
//  PROJECT WORKFLOW  (standard .NET commands)
//    dotnet new kestrel-console -n MyApp
//    dotnet add package Raylib-cs
//    dotnet run / dotnet build / dotnet publish
// ─────────────────────────────────────────────────────────────────────────────

bool debugMode = args.Contains("--debug");
var asyncReturn = args.Contains("--async-return=valuetask")
    ? KSR.AST.AsyncReturnKind.ValueTask
    : KSR.AST.AsyncReturnKind.Task;
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
            Environment.ExitCode = CheckFile(positional.ElementAtOrDefault(1) ?? "") ? 1 : 0;
            break;

        case "lsp":
            KSR.LSP.LspServer.Run();
            break;

        default:
        {
            var path = positional[0];
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"kestrel: file not found: {path}");
                PrintHelp();
                Environment.Exit(1);
            }
            RunSingleFile(path, debugMode, asyncReturn);
            break;
        }
    }
}
catch (KSR.Lexer.KsrLexException   ex) { Fail(ex.Message); }
catch (KsrParseException            ex) { Fail(ex.Message); }
catch (KsrCompileException          ex) { Fail(ex.Message); }
catch (Exception                    ex) { Fail($"Internal error: {ex}"); }

// ── helpers ───────────────────────────────────────────────────────────────────

static bool CheckFile(string path)
{
    var fullPath = Path.GetFullPath(path);
    if (!File.Exists(path))
    {
        Console.WriteLine("[]");
        return false;
    }

    try
    {
        var source = File.ReadAllText(path);
        var result = KsrAnalyzer.Analyze(source, fullPath);

        Console.WriteLine(JsonSerializer.Serialize(result.Diagnostics.Select(DiagnosticDto.FromDiagnostic)));
        return result.HasErrors;
    }
    catch (Exception ex)
    {
        Console.WriteLine(JsonSerializer.Serialize(new[]
        {
            new DiagnosticDto(ex.Message, 1, 1, "error", fullPath)
        }));
        return true;
    }
}

static void RunSingleFile(
    string path,
    bool debugMode,
    KSR.AST.AsyncReturnKind asyncReturn = KSR.AST.AsyncReturnKind.Task)
{
    // Ensure KSR.StdLib assembly is loaded into the AppDomain so that Roslyn
    // (strategy 3 in ResolveReferences) can find it when compiling ksr.io / ksr.text.
    _ = typeof(KSR.Io.IO).Assembly;
    // Same for the creative-coding MVP library used by the kestrel-creative template.
    _ = typeof(KSR.Creative.CreativeApp).Assembly;

    var source = File.ReadAllText(path);
    var fullPath = Path.GetFullPath(path);
    var result = KsrAnalyzer.Analyze(source, fullPath);
    if (result.HasErrors || result.Program is null)
        throw new KsrCompileException(FormatDiagnostics(result.Diagnostics));

    var program = result.Program;

    if (debugMode)
    {
        // Print generated C# for inspection (uses the text-based CodeGenerator).
        var csharpSrc = new KSR.CodeGen.CodeGenerator(asyncReturn).Generate(program);
        Console.Error.WriteLine("══════════ Generated C# ══════════");
        Console.Error.WriteLine(csharpSrc);
        Console.Error.WriteLine("══════════════════════════════════");
    }

    // Build a Roslyn SyntaxTree directly — no C# text generation + ParseText step.
    var syntaxTree = new KSR.CodeGen.SyntaxTreeGenerator(asyncReturn).Generate(program);
    KsrCompiler.CompileAndRun(syntaxTree, debugMode);
}

static void Fail(string msg)
{
    Console.Error.WriteLine($"error: {msg}");
    Environment.Exit(1);
}

static string FormatDiagnostics(IEnumerable<KsrDiagnostic> diagnostics) =>
    "Analysis failed:\n" + string.Join(Environment.NewLine, diagnostics.Select(diagnostic =>
        $"{diagnostic.SourceFile}({diagnostic.Line},{diagnostic.Column}): {diagnostic.Severity.ToString().ToLowerInvariant()}: {diagnostic.Message}"));

static void PrintHelp()
{
    Console.WriteLine("""
        Kestrel — Kotlin-style language for .NET

        SINGLE-FILE MODE
          kestrel <file.ksr>                    Compile and run a .ksr file
          kestrel <file.ksr> --debug            Also print the generated C# source
          kestrel <file.ksr> --async-return=valuetask  Use ValueTask for all async functions

        EDITOR INTEGRATION
          kestrel check <file>          Output JSON diagnostics
          kestrel lsp                   Language Server (JSON-RPC/stdio)

        PROJECT WORKFLOW  (standard .NET)
          dotnet new kestrel-console -n MyApp
          cd MyApp
          dotnet add package Raylib-cs
          dotnet run
          dotnet build
          dotnet publish

        LANGUAGE FEATURES
          struct Point(x: Int, y: Int)           value type
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
