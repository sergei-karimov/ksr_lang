using System.Reflection;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KSR.CodeGen;

/// <summary>
/// Uses Roslyn to compile a C# source string in memory, then invokes
/// the <c>KsrProgram.Main()</c> method via reflection.
/// </summary>
public static class KsrCompiler {
    public static void CompileAndRun(
        string csharpSource,
        bool debugMode = false,
        IEnumerable<string>? extraReferencePaths = null) {
        if (debugMode) {
            Console.Error.WriteLine("══════════ Generated C# ══════════");
            Console.Error.WriteLine(csharpSource);
            Console.Error.WriteLine("══════════════════════════════════");
        }

        // ── 1. Parse ─────────────────────────────────────────────────────────
        var syntaxTree = CSharpSyntaxTree.ParseText(csharpSource);

        // ── 2. References ────────────────────────────────────────────────────
        var references = ResolveReferences(debugMode);

        // Add package DLLs (from NuGet resolution)
        if (extraReferencePaths is not null) {
            foreach (var path in extraReferencePaths)
                if (File.Exists(path))
                    references.Add(MetadataReference.CreateFromFile(path));
        }

        // ── 3. Compile ───────────────────────────────────────────────────────
        var compilation = CSharpCompilation.Create(
            assemblyName: "KsrOutput",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success) {
            var errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"  {d}")
                .ToList();
            throw new KsrCompileException(
                "C# compilation failed:\n" + string.Join("\n", errors));
        }

        // ── 4. Load & execute via reflection ─────────────────────────────────
        ms.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());

        var type = assembly.GetType("KsrProgram")
            ?? throw new KsrCompileException(
                "Compiled assembly does not contain 'KsrProgram'. " +
                "This is an internal code-generator bug.");

        var main = type.GetMethod(
                       "Main",
                       BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new KsrCompileException(
                "Could not find a 'Main' method in 'KsrProgram'. " +
                "Did you define a 'fun main()' in your KSR program?");

        // Build the argument array: if Main has no params pass nothing,
        // otherwise pass an empty string array (matches string[]? or string[]).
        var invokeArgs = main.GetParameters().Length == 0
            ? null
            : new object[] { Array.Empty<string>() };

        main.Invoke(null, invokeArgs);
    }

    // ── reference resolution ──────────────────────────────────────────────────

    /// <summary>
    /// Locates .NET framework assemblies for Roslyn.
    /// Strategy order (first non-empty set wins):
    ///   1. typeof(object) directory  — self-contained folder publish (always works)
    ///   2. TRUSTED_PLATFORM_ASSEMBLIES — dotnet run / non-single-file builds
    ///   3. AppDomain loaded assemblies — picks up Roslyn dlls with real paths
    ///   4. .NET install directory discovery — single-file publish fallback
    /// </summary>
    private static List<MetadataReference> ResolveReferences(bool debugMode) {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void ScanDir(string? dir) {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
                paths.Add(dll);
        }

        // 1 — BCL directory via typeof(object)
        //     Reliable for self-contained folder publish: all assemblies are real files
        //     next to the exe.  Empty in single-file mode (by .NET 6+ design).
        var coreLocation = typeof(object).Assembly.Location;
        if (!string.IsNullOrEmpty(coreLocation))
            ScanDir(Path.GetDirectoryName(coreLocation));

        // 2 — TRUSTED_PLATFORM_ASSEMBLIES
        //     Set by the host for dotnet run and non-single-file publishes.
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "";
        foreach (var p in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            if (File.Exists(p)) paths.Add(p);

        // 3 — AppDomain loaded assemblies with real file locations
        //     Roslyn's own assemblies have disk paths in framework-dependent mode.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            if (!asm.IsDynamic && !string.IsNullOrWhiteSpace(asm.Location)
                               && File.Exists(asm.Location))
                paths.Add(asm.Location);

        // 4 — Discover the .NET runtime directory explicitly
        //     Last resort for single-file publishes where strategies 1-3 yield nothing.
        if (paths.Count == 0)
            ScanDir(FindDotNetRuntimeDir());

        if (debugMode)
            Console.Error.WriteLine($"[refs] {paths.Count} assemblies resolved");

        if (paths.Count == 0)
            throw new KsrCompileException(
                "No framework assemblies found for Roslyn.\n" +
                "Ensure .NET 8+ runtime is installed.\n" +
                "Set the DOTNET_ROOT environment variable if .NET is in a non-standard location.");

        return paths
            .Where(IsManagedAssembly)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }

    /// <summary>
    /// Finds the directory containing .NET BCL assemblies (System.Runtime.dll etc.)
    /// by probing DOTNET_ROOT and well-known installation paths.
    /// </summary>
    private static string? FindDotNetRuntimeDir() {
        // Probe list: env vars, then well-known Windows + Linux paths
        var roots = new List<string?>();
        roots.Add(Environment.GetEnvironmentVariable("DOTNET_ROOT"));
        roots.Add(Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"));
        roots.Add(Environment.GetEnvironmentVariable("DOTNET_INSTALL_DIR"));

        if (OperatingSystem.IsWindows()) {
            roots.Add(@"C:\Program Files\dotnet");
            roots.Add(@"C:\Program Files (x86)\dotnet");
            roots.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet"));
        } else {
            roots.Add("/usr/share/dotnet");
            roots.Add("/usr/local/share/dotnet");
            roots.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dotnet"));
        }

        foreach (var root in roots) {
            if (string.IsNullOrEmpty(root)) continue;
            var sharedDir = Path.Combine(root, "shared", "Microsoft.NETCore.App");
            if (!Directory.Exists(sharedDir)) continue;

            // Pick the highest installed version
            var best = Directory.GetDirectories(sharedDir)
                .Select(d => (dir: d, ver: TryParseVersion(Path.GetFileName(d))))
                .Where(x => x.ver is not null)
                .OrderByDescending(x => x.ver)
                .Select(x => x.dir)
                .FirstOrDefault();

            if (best is not null) return best;
        }

        return null;
    }

    /// <summary>
    /// Returns true only if the file is a managed .NET assembly (has CLR metadata).
    /// Filters out native DLLs like coreclr.dll, clrjit.dll, hostfxr.dll, etc.
    /// </summary>
    private static bool IsManagedAssembly(string path) {
        try {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            return peReader.HasMetadata;
        } catch {
            return false;
        }
    }

    private static Version? TryParseVersion(string? s) {
        if (s is null) return null;
        // Strip pre-release suffix (e.g. "8.0.0-preview") before parsing
        var clean = s.Split('-')[0];
        return Version.TryParse(clean, out var v) ? v : null;
    }
}

public class KsrCompileException : Exception {
    public KsrCompileException(string message) : base(message) { }
}
