# Kestrel Compiler Foundation and Branding Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce structured compiler diagnostics, stronger semantic checking, shared CLI/LSP/MSBuild analysis, reliable integration coverage, aligned Visual Studio dependencies, and the Kestrel public brand while preserving internal `KSR.*` namespaces and legacy CLI/template aliases.

**Architecture:** Add a compiler-core analysis facade that returns a parsed program plus structured diagnostics, then make CLI, LSP, MSBuild, and tests consume that result. Keep the implementation namespaces and `.ksr` syntax unchanged, rename public package/template/CLI metadata to Kestrel, and provide `ksr`/`ksr-*` transition aliases in the installer and template distribution.

**Tech Stack:** C#/.NET 8, Roslyn, xUnit, MSBuild tasks and SDK props/targets, JSON-RPC over stdio for LSP, VS Code extension tooling, Visual Studio SDK/net472, Bash, PowerShell, NuGet.

**Spec:** `docs/superpowers/specs/2026-09-12-kestrel-foundation-design.md`

## Global Constraints

- Existing `.ksr` source files remain valid and the `.ksr` extension does not change.
- Internal namespaces remain `KSR.*` in this phase.
- Canonical public names are `kestrel`, `Kestrel.*`, and `kestrel-*`.
- `ksr` and `ksr-*` remain working transition aliases.
- `KSR.*` NuGet compatibility packages are not created because those package IDs have not been published.
- The Visual Studio extension remains `net472` and keeps its supported Visual Studio version range.
- Code generation never runs when error-severity diagnostics exist.
- Diagnostics use 1-based line and column values and are not reconstructed by parsing formatted strings.
- Compiler and ordinary CLI tests target .NET 8+; Visual Studio/VSIX validation may require Windows tooling.
- Every task adds or updates focused tests before changing implementation behavior.

---

## File and Component Map

The implementation follows these boundaries:

- `Diagnostics/` — severity, diagnostic records, and analysis result types.
- `Analysis/` — the shared lexer/parser/semantic facade.
- `Lexer/`, `Parser/`, `Semantic/` — producers of structured diagnostics and semantic rules.
- `Program.cs` — CLI command routing and output formatting only.
- `LspServer.cs` — JSON-RPC/LSP mapping only.
- `sdk/KSR.Build/KsrCompileTask.cs` — MSBuild input/output and logging only.
- `tests/KSR.Tests/` — compiler-core and runtime tests.
- `tests/KSR.Cli.Tests/` — process-level CLI tests.
- `tests/KSR.VsExtension.Tests/` — Visual Studio project tests.
- `sdk/*/*.csproj`, `sdk/KSR.Sdk/Sdk/*`, and `sdk/KSR.Templates/content/*` — public package/template metadata.
- `scripts/install.sh`, `scripts/install.ps1` — local distribution and legacy aliases.
- `vscode-extension/` — public extension metadata and executable configuration.

## Task 1: Add the structured diagnostic model and shared analyzer

**Files:**
- Create: `Diagnostics/DiagnosticSeverity.cs`
- Create: `Diagnostics/KsrDiagnostic.cs`
- Create: `Diagnostics/KsrAnalysisResult.cs`
- Create: `Analysis/KsrAnalyzer.cs`
- Modify: `KSR.Core.csproj` only if explicit compile includes are required.
- Test: `tests/KSR.Tests/DiagnosticsTests.cs`
- Test: `tests/KSR.Tests/AnalysisTests.cs`

**Interfaces:**
- Produces `KSR.Diagnostics.KsrDiagnostic`, `KSR.Diagnostics.DiagnosticSeverity`, `KSR.Diagnostics.KsrAnalysisResult`, and `KSR.Analysis.KsrAnalyzer.Analyze(string source, string sourceFile)`.
- `KsrAnalysisResult.Program` is nullable, `KsrAnalysisResult.Diagnostics` is an immutable/read-only list, and `HasErrors` is true when any diagnostic has error severity.
- Consumes the existing `Lexer`, `Parser`, and `SemanticAnalyzer` without changing code generation.

- [ ] **Step 1: Write failing model tests.** Verify an error diagnostic preserves message, source path, 1-based line/column, and severity; verify `HasErrors` for error and warning-only results.

```csharp
[Fact]
public void AnalysisResultReportsWhetherItHasErrors()
{
    var result = new KsrAnalysisResult(
        Program: null,
        Diagnostics: new[]
        {
            new KsrDiagnostic("warning", "a.ksr", 2, 3, DiagnosticSeverity.Warning)
        });

    Assert.False(result.HasErrors);
}
```

- [ ] **Step 2: Run the focused tests and verify they fail because the types do not exist.**

Run: `dotnet test tests/KSR.Tests/KSR.Tests.csproj --no-restore --filter FullyQualifiedName~DiagnosticsTests`

Expected: compilation failure for the missing diagnostics types.

- [ ] **Step 3: Implement the immutable diagnostic types and analyzer result.** Use a public enum with `Info`, `Warning`, and `Error`; make `KsrDiagnostic` a sealed record; copy the incoming diagnostic sequence into a read-only collection so callers cannot mutate analysis state.

- [ ] **Step 4: Add `KsrAnalyzer.Analyze`.** Run lexing, parsing, and semantic analysis in order; preserve the parsed `ProgramNode` when available; convert lexer/parser failures at this boundary into `KsrDiagnostic` instances using the exception's existing line and column fields; skip semantic analysis when parsing did not produce a program.

- [ ] **Step 5: Add analyzer tests for valid, lexical, syntax, and semantic input.** Assert that a valid `fun main()` source has a program and no errors, malformed syntax has a source location, and semantic failures are returned without throwing.

- [ ] **Step 6: Run the focused tests and the existing parser/semantic tests.**

Run: `dotnet test tests/KSR.Tests/KSR.Tests.csproj --no-restore --filter "FullyQualifiedName~DiagnosticsTests|FullyQualifiedName~AnalysisTests|FullyQualifiedName~ParserTests|FullyQualifiedName~SemanticTests"`

Expected: all selected tests pass once the repository has restored assets.

- [ ] **Step 7: Commit the core model and facade.**

```bash
git add Diagnostics Analysis tests/KSR.Tests KSR.Core.csproj
git commit -m "feat: add shared structured analysis diagnostics"
```

## Task 2: Migrate lexer, parser, and semantic analyzer to structured diagnostics

**Files:**
- Modify: `Lexer/Lexer.cs`
- Modify: `Lexer/KsrLexException.cs`
- Modify: `Parser/Parser.cs`
- Modify: `Parser/KsrParseException.cs`
- Modify: `Semantic/SemanticAnalyzer.cs`
- Modify: `Analysis/KsrAnalyzer.cs`
- Test: `tests/KSR.Tests/DiagnosticsTests.cs`
- Test: `tests/KSR.Tests/SemanticTests.cs`
- Test: `tests/KSR.Tests/ParserTests.cs`

**Interfaces:**
- Lexer, parser, and semantic analyzer expose structured diagnostics to `KsrAnalyzer`; exception types remain available for the throwing single-file compiler path.
- `SemanticAnalyzer.Errors` is replaced or supplemented by a read-only `Diagnostics` collection; existing tests may use a compatibility projection only until all consumers migrate.
- Diagnostic-producing code carries source locations directly from tokens, parser state, or AST nodes.

- [ ] **Step 1: Add failing tests proving diagnostics do not depend on formatted error strings.** Assert that a parser error's message, file, line, and column are available as fields and that a semantic error can be rendered without reparsing a string.

- [ ] **Step 2: Run the focused tests to capture the current failure.**

Run: `dotnet test tests/KSR.Tests/KSR.Tests.csproj --no-restore --filter "FullyQualifiedName~DiagnosticsTests|FullyQualifiedName~ParserTests|FullyQualifiedName~SemanticTests"`

Expected: new field/property assertions fail against the current string-based error storage.

- [ ] **Step 3: Change lexer/parser/semantic producers to construct `KsrDiagnostic` values.** Preserve current exception behavior for fail-fast APIs, but ensure the shared analyzer receives structured values directly. Do not parse strings such as `"(line,column): error"`.

- [ ] **Step 4: Update `KsrAnalyzer` to aggregate diagnostics consistently.** Lexing and parsing errors should stop later phases when no valid AST exists; recoverable parser diagnostics may be aggregated with semantic diagnostics only when a valid partial program is available.

- [ ] **Step 5: Keep compatibility projections for existing internal tests and callers.** If `Errors` remains temporarily, derive it from structured diagnostics in one place and mark it as transitional in the implementation comments.

- [ ] **Step 6: Run all compiler-core tests.**

Run: `dotnet test tests/KSR.Tests/KSR.Tests.csproj --no-restore --filter "FullyQualifiedName~LexerTests|FullyQualifiedName~ParserTests|FullyQualifiedName~SemanticTests|FullyQualifiedName~DiagnosticsTests|FullyQualifiedName~AnalysisTests"`

- [ ] **Step 7: Commit the producer migration.**

```bash
git add Lexer Parser Semantic Analysis tests/KSR.Tests
git commit -m "refactor: produce structured compiler diagnostics"
```

## Task 3: Strengthen semantic checking with focused tests

**Files:**
- Modify: `Semantic/SemanticAnalyzer.cs`
- Modify: `Semantic/SymbolTable.cs` if type/member lookup needs a focused API.
- Modify: `AST/AstNodes.cs` only when source locations are missing from the relevant nodes.
- Modify: `CodeGen/CodeGenerator.cs` only to remove code-generation fallbacks made unreachable by semantic checks.
- Test: `tests/KSR.Tests/SemanticTests.cs`
- Test: `tests/KSR.Tests/WhenTests.cs`
- Test: `tests/KSR.Tests/AsyncTests.cs`

**Interfaces:**
- Semantic analysis consumes the existing AST and symbol table and emits `KsrDiagnostic` values with source locations.
- Type checking must compare declared/expected types using the existing type representation rather than string comparisons in individual rules.
- The analyzer must expose enough symbol/member information to distinguish an unknown member from an unresolved dynamic/`Any` value.

- [ ] **Step 1: Add one failing test for each required rule.** Cover wrong argument types, unknown/duplicate named arguments, positional arguments after named arguments, missing return paths, `await` outside async functions, non-exhaustive sealed `when`, and unknown member access.

```csharp
[Fact]
public void CallWithWrongArgumentTypeProducesAnErrorDiagnostic()
{
    var result = Analyze("fun add(x: Int): Int { return x }\nfun main() { add(\"nope\") }");

    Assert.Contains(result.Diagnostics, d =>
        d.Severity == DiagnosticSeverity.Error &&
        d.Message.Contains("argument", StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 2: Run the new semantic tests and verify each currently fails or exposes the unsound behavior.**

Run: `dotnet test tests/KSR.Tests/KSR.Tests.csproj --no-restore --filter "FullyQualifiedName~SemanticTests|FullyQualifiedName~WhenTests|FullyQualifiedName~AsyncTests"`

- [ ] **Step 3: Implement argument type and named-argument validation.** Resolve the callee signature, compare each supplied expression type to the parameter type, reject unknown/duplicate names, and reject positional arguments after the first named argument with a diagnostic at the offending argument.

- [ ] **Step 4: Implement return-path analysis.** For every non-`Unit` function, recursively inspect blocks and control-flow expressions; accept a final unconditional return, reject paths that can fall through, and report the function declaration location.

- [ ] **Step 5: Track async context and reject invalid `await`.** Pass an `isAsyncFunction` context through expression analysis and issue a diagnostic at each await expression outside an async function.

- [ ] **Step 6: Validate sealed `when` exhaustiveness.** Enumerate variants from the sealed declaration, account for covered variants and `else`, and issue an error in value context when a subject form can fall through.

- [ ] **Step 7: Make member lookup explicit.** Return an unknown-member diagnostic when a type has no matching field/method; only preserve `Any` behavior for values that are explicitly typed or intentionally represented as dynamic.

- [ ] **Step 8: Run all language and example tests and confirm valid examples remain valid.**

Run: `dotnet test tests/KSR.Tests/KSR.Tests.csproj --no-restore`

- [ ] **Step 9: Commit the semantic checker improvements.**

```bash
git add Semantic AST CodeGen tests/KSR.Tests
git commit -m "feat: strengthen Kestrel semantic analysis"
```

## Task 4: Migrate CLI and LSP to the shared analysis pipeline

**Files:**
- Modify: `Program.cs`
- Modify: `LspServer.cs`
- Modify: `CodeGen/Compiler.cs` only where compile entry points need an analyzed `ProgramNode`.
- Modify: `tests/KSR.Tests/TestHelper.cs` if common source-analysis helpers belong there.
- Test: `tests/KSR.Tests/DiagnosticsTests.cs`
- Test: `tests/KSR.Tests/ParserTests.cs`
- Test: `tests/KSR.Tests/SemanticTests.cs`

**Interfaces:**
- `Program.CheckFile` calls `KsrAnalyzer.Analyze` and serializes `KsrDiagnostic` fields directly.
- `Program.RunSingleFile` calls the shared analyzer, stops on `HasErrors`, and passes the returned program to code generation.
- LSP diagnostic publishing consumes `KsrDiagnostic` values and maps severity/range without parsing text.

- [ ] **Step 1: Add failing tests around `CheckFile` output and LSP mapping.** Verify JSON contains `message`, `line`, `col`, and severity; verify Unicode and multiline messages serialize as valid JSON; verify LSP ranges are zero-based while core diagnostics remain one-based.

- [ ] **Step 2: Run the focused tests and capture failures from the current string parsing path.**

Run: `dotnet test tests/KSR.Tests/KSR.Tests.csproj --no-restore --filter FullyQualifiedName~DiagnosticsTests`

- [ ] **Step 3: Replace CLI diagnostic parsing with direct serialization.** Keep the current public JSON field names where possible, add severity/source file fields consistently, and return a non-zero exit code for error diagnostics.

- [ ] **Step 4: Update single-file execution.** Analyze once, throw a compile exception containing formatted diagnostics only at the CLI boundary, and never invoke `SyntaxTreeGenerator` or Roslyn when the analysis result has errors.

- [ ] **Step 5: Update LSP diagnostics.** Use the analyzer for open/change documents, publish an empty diagnostic list after a clean edit, and map the 1-based core location to LSP's zero-based character/line range.

- [ ] **Step 6: Run compiler and LSP tests.**

Run: `dotnet test tests/KSR.Tests/KSR.Tests.csproj --no-restore`

- [ ] **Step 7: Commit the consumer migration.**

```bash
git add Program.cs LspServer.cs CodeGen/Compiler.cs tests/KSR.Tests
git commit -m "refactor: share analysis between CLI and LSP"
```

## Task 5: Add process-level CLI and build-task integration coverage

**Files:**
- Create: `tests/KSR.Cli.Tests/KSR.Cli.Tests.csproj`
- Create: `tests/KSR.Cli.Tests/CliProcessTests.cs`
- Create: `tests/KSR.Cli.Tests/CliTestHost.cs`
- Modify: `KSR.sln`
- Modify: `sdk/KSR.Build/KsrCompileTask.cs`
- Test: `tests/KSR.Tests/DotnetTemplateTests.cs` for build-generated output where applicable.

**Interfaces:**
- `CliTestHost.RunAsync(string command, params string[] args)` starts the built Kestrel CLI with a temporary `.ksr` file and returns exit code/stdout/stderr.
- `KsrCompileTask` consumes the same `KsrAnalyzer` result and logs diagnostics through MSBuild with file/line/column metadata.

- [ ] **Step 1: Add the CLI test project and failing process tests.** Cover valid `check`, syntax-invalid `check`, semantic-invalid `check`, and a source with semantic errors that must not produce generated output.

```csharp
[Fact]
public async Task CheckReturnsJsonDiagnosticsForSemanticErrors()
{
    await using var host = await CliTestHost.CreateAsync(
        "fun main() { unknownName() }");

    var result = await host.RunAsync("check", host.SourcePath);

    Assert.NotEqual(0, result.ExitCode);
    Assert.Contains("unknown", result.Stdout, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run the new project before implementation.**

Run: `dotnet test tests/KSR.Cli.Tests/KSR.Cli.Tests.csproj --no-restore`

Expected: project/test compilation fails until the host and CLI integration are added.

- [ ] **Step 3: Implement `CliTestHost` with isolated temporary directories.** Resolve the CLI project path from the repository root, invoke `dotnet run --project KSR.csproj --no-launch-profile --`, pass through command arguments, capture all streams, and delete only the host's own temporary directory during disposal.

- [ ] **Step 4: Add the project to `KSR.sln` and make the process tests deterministic.** Do not depend on a globally installed `ksr` binary or user NuGet caches.

- [ ] **Step 5: Update `KsrCompileTask` to use `KsrAnalyzer`.** Aggregate diagnostics per input file, call `Log.LogError`/`Log.LogWarning` with `File`, `Line`, and `Column`, and write generated C# only when no error diagnostics exist.

- [ ] **Step 6: Add MSBuild task tests for error logging and no-output behavior.** Use a fake/build engine logger and a temporary input/output directory; assert the output file is absent after an error and contains generated code after valid input.

- [ ] **Step 7: Run process-level, build-task, and template tests.**

Run: `dotnet test tests/KSR.Cli.Tests/KSR.Cli.Tests.csproj --no-restore`

Run: `dotnet test tests/KSR.Tests/KSR.Tests.csproj --no-restore --filter "FullyQualifiedName~DotnetTemplateTests|FullyQualifiedName~DiagnosticsTests"`

- [ ] **Step 8: Commit the integration coverage.**

```bash
git add tests/KSR.Cli.Tests KSR.sln sdk/KSR.Build/KsrCompileTask.cs tests/KSR.Tests
git commit -m "test: cover CLI and MSBuild diagnostics"
```

## Task 6: Align Visual Studio SDK dependencies

**Files:**
- Modify: `vs-extension/KSR.VisualStudio/KSR.VisualStudio.csproj`
- Modify: `tests/KSR.VsExtension.Tests/KSR.VsExtension.Tests.csproj`
- Modify: `KSR.sln` only if project configuration requires it.
- Test: `tests/KSR.VsExtension.Tests/KsrExecutableResolverTests.cs`
- Test: `tests/KSR.VsExtension.Tests/KsrPathSettingsTests.cs`

**Interfaces:**
- Keep the extension target framework `net472` and Visual Studio installation targets unchanged.
- Keep all Microsoft Visual Studio SDK packages on one compatible version line and pin transitive packages only when required to remove a concrete warning.

- [ ] **Step 1: Capture the current dependency graph and warnings.**

Run on a machine with the required Visual Studio build tools: `msbuild vs-extension/KSR.VisualStudio/KSR.VisualStudio.csproj /t:Restore /v:minimal`

Record the exact `NU1603` chain and the package versions producing it.

- [ ] **Step 2: Add or update a dependency regression check.** Assert through restore/build output or a checked package-version property that the Microsoft Visual Studio package family remains aligned.

- [ ] **Step 3: Update package versions and direct pins.** Change only the package references needed to resolve the recorded downgrade/approximate-match warnings; retain `ExcludeAssets`, `PrivateAssets`, and `System.Text.Json` runtime behavior.

- [ ] **Step 4: Build the extension and run its tests.**

Run: `msbuild vs-extension/KSR.VisualStudio/KSR.VisualStudio.csproj /t:Build /p:Configuration=Release /v:minimal`

Run: `dotnet test tests/KSR.VsExtension.Tests/KSR.VsExtension.Tests.csproj --no-restore`

Expected: the project builds with no targeted `NU1603` warnings and resolver/path/template tests pass.

- [ ] **Step 5: Commit the dependency alignment.**

```bash
git add vs-extension tests/KSR.VsExtension.Tests
git commit -m "build: align Visual Studio SDK dependencies"
```

## Task 7: Rename public packages, templates, CLI metadata, and documentation

**Files:**
- Modify: `KSR.csproj`
- Modify: `KSR.Core.csproj`
- Modify: `sdk/KSR.Build/KSR.Build.csproj`
- Modify: `sdk/KSR.Sdk/KSR.Sdk.csproj`
- Modify: `sdk/KSR.Sdk/Sdk/Sdk.props`
- Modify: `sdk/KSR.StdLib/KSR.StdLib.csproj`
- Modify: `sdk/KSR.Vision/KSR.Vision.csproj`
- Modify: `sdk/KSR.Creative/KSR.Creative.csproj`
- Modify: `sdk/KSR.Templates/KSR.Templates.csproj`
- Modify: `sdk/KSR.Build/build/KSR.Build.props`
- Modify: `sdk/KSR.Build/build/KSR.Build.targets`
- Modify: `sdk/KSR.Templates/content/*/*.csproj`
- Modify: `sdk/KSR.Templates/content/*/nuget.config`
- Modify: `vs-extension/KSR.VisualStudio/source.extension.vsixmanifest`
- Modify: `vscode-extension/package.json`
- Modify: `README.md`
- Modify: `docs/code-review-improvement-plan.md` if terminology is now stale.
- Test: `tests/KSR.Tests/DotnetTemplateTests.cs`
- Test: `tests/KSR.Tests/VsExtension/TemplateTests.cs`

**Interfaces:**
- Package IDs and generated project references use `Kestrel.*`.
- Implementation namespaces and assembly references that are intentionally internal remain `KSR.*`.
- The CLI tool package declares `kestrel` as its canonical command; the legacy `ksr` command is installed by the distribution layer.

- [ ] **Step 1: Add failing metadata tests.** Assert that package metadata, template package references, template short names, and VS Code configuration contain canonical Kestrel names; add explicit assertions for legacy `ksr-*` aliases.

- [ ] **Step 2: Run metadata tests to capture current KSR names.**

Run: `dotnet test tests/KSR.Tests/KSR.Tests.csproj --no-restore --filter "FullyQualifiedName~DotnetTemplateTests|FullyQualifiedName~TemplateTests"`

- [ ] **Step 3: Rename package IDs and package-to-package references.** Update the `PackageId`, descriptions, tags, SDK props/targets imports, local feed names, and template project references. Do not change `RootNamespace` or source namespaces.

- [ ] **Step 4: Rename template identities and preserve aliases.** Make Kestrel template names canonical, keep the old `ksr-*` names as additional template aliases supported by the installed template metadata, and verify both names create equivalent projects.

- [ ] **Step 5: Rename CLI metadata.** Change `AssemblyName`, `PackageId`, description, and `ToolCommandName` to Kestrel/canonical `kestrel`; keep command routing and internal namespace names unchanged.

- [ ] **Step 6: Update VS Code branding and generated debug configuration.** Change extension display name, package description, executable setting default, and debug labels to Kestrel while retaining `.ksr` language ID and compatibility behavior for existing workspaces.

- [ ] **Step 7: Update README, installer-facing help, package descriptions, and examples.** Present Kestrel as canonical, document `ksr` and `ksr-*` as temporary aliases, and retain `.ksr` in all source examples.

- [ ] **Step 8: Run metadata and compiler tests.**

Run: `dotnet test tests/KSR.Tests/KSR.Tests.csproj --no-restore`

- [ ] **Step 9: Commit the public branding migration.**

```bash
git add KSR.csproj KSR.Core.csproj sdk vs-extension vscode-extension README.md docs tests/KSR.Tests
git commit -m "feat: rename public toolchain to Kestrel"
```

## Task 8: Implement installer aliases and end-to-end packaging validation

**Files:**
- Modify: `scripts/install.sh`
- Modify: `scripts/install.ps1`
- Modify: `README.md`
- Modify: `tests/KSR.Cli.Tests/CliProcessTests.cs`
- Create: `tests/KSR.Cli.Tests/InstallerMetadataTests.cs`
- Modify: `vscode-extension/package.json` only if final executable setting needs alias fallback.

**Interfaces:**
- Installers build and install Kestrel package IDs and the `kestrel` global tool.
- Installers provide a working `ksr` alias that invokes the same installed implementation, using a shell wrapper/symlink on Unix-like systems and a PowerShell/cmd-compatible launcher on Windows.
- Installers install canonical templates and verify legacy `ksr-*` aliases are available.

- [ ] **Step 1: Add failing installer metadata tests.** Check that scripts reference Kestrel artifacts, install the `kestrel` tool, and contain explicit alias setup/removal for `ksr`.

- [ ] **Step 2: Run the metadata tests.**

Run: `dotnet test tests/KSR.Cli.Tests/KSR.Cli.Tests.csproj --no-restore --filter FullyQualifiedName~InstallerMetadataTests`

- [ ] **Step 3: Update `install.sh`.** Build Kestrel packages, register a `kestrel-local` feed, install/uninstall `Kestrel`, install canonical templates, and create an executable `ksr` wrapper in the same user tool directory that delegates to `kestrel`.

- [ ] **Step 4: Update `install.ps1`.** Mirror the package/tool/template changes and create/remove a Windows-compatible `ksr` launcher beside the global `kestrel` command, preserving strict error handling and the existing optional VS Code installation.

- [ ] **Step 5: Validate both command names and template names from a clean local feed.**

Run: `kestrel --help`

Run: `ksr --help`

Run: `dotnet new kestrel-console -n KestrelSmokeApp`

Run: `dotnet new ksr-console -n KsrAliasSmokeApp`

Expected: both commands use the same compiler/toolchain and both generated projects build.

- [ ] **Step 6: Add package artifact assertions.** Pack all public projects and assert that artifacts are named `Kestrel.*.nupkg`, that no compatibility `KSR.*` package is created, and that generated projects reference the canonical IDs.

- [ ] **Step 7: Commit installer and packaging validation.**

```bash
git add scripts tests/KSR.Cli.Tests README.md vscode-extension
git commit -m "feat: add Kestrel installation aliases"
```

## Task 9: Full verification, documentation review, and release checklist

**Files:**
- Modify: `README.md` for final command examples and migration notes.
- Modify: `docs/code-review-improvement-plan.md` to mark completed items and record remaining limitations.
- Modify: `docs/superpowers/specs/2026-09-12-kestrel-foundation-design.md` only if implementation discoveries require an approved design correction.

- [ ] **Step 1: Restore dependencies in a normal development environment.**

Run: `dotnet restore KSR.sln`

Run: `npm ci --prefix vscode-extension`

- [ ] **Step 2: Run all compiler and CLI tests.**

Run: `dotnet test tests/KSR.Tests/KSR.Tests.csproj`

Run: `dotnet test tests/KSR.Cli.Tests/KSR.Cli.Tests.csproj`

- [ ] **Step 3: Build and test the Visual Studio extension on Windows.**

Run: `msbuild KSR.sln /t:Build /p:Configuration=Release /v:minimal`

Run: `dotnet test tests/KSR.VsExtension.Tests/KSR.VsExtension.Tests.csproj --configuration Release`

- [ ] **Step 4: Build the VS Code extension.**

Run: `npm run bundle --prefix vscode-extension`

Run: `npm run package --prefix vscode-extension`

- [ ] **Step 5: Execute representative Kestrel examples.** Run the console, async, interfaces, sealed/when, standard-library, creative, and camera examples on their supported platforms; record camera/VSIX platform restrictions in the README rather than hiding failures.

- [ ] **Step 6: Check the final repository state.** Confirm no generated `bin/`, `obj/`, artifacts, temporary projects, or package caches are accidentally tracked; confirm all changed source files use internal `KSR.*` namespaces intentionally and all public metadata uses Kestrel.

- [ ] **Step 7: Commit the final documentation and verification notes.**

```bash
git add README.md docs/code-review-improvement-plan.md
git commit -m "docs: finalize Kestrel migration and verification notes"
```

## Self-Review Checklist

- Structured diagnostics: Tasks 1, 2, 4, and 5.
- Stronger semantic checks: Task 3.
- CLI integration tests: Task 5.
- Shared CLI/LSP/MSBuild analysis: Tasks 1, 4, and 5.
- Visual Studio dependency alignment: Task 6.
- Kestrel package/CLI/template branding: Tasks 7 and 8.
- Legacy CLI/template aliases: Tasks 7 and 8.
- `.ksr` and internal `KSR.*` compatibility: Tasks 3, 7, and 9.
- Documentation and platform limitations: Tasks 7 and 9.
