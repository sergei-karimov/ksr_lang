# Kestrel Improvement Plan

> Kestrel is the canonical public brand. Internal `KSR.*` namespaces, the `.ksr`
> source extension, and the transitional `ksr`/`ksr-*` aliases remain unchanged.

This plan captures the next engineering improvements after the semantic diagnostics fixes.

## 1. Introduce Structured Diagnostics — completed

Replace string-parsed diagnostics with a core diagnostic model, for example:

```csharp
public record KsrDiagnostic(string Message, string SourceFile, int Line, int Col);
```

Parser and semantic analysis now return structured diagnostics. Formatting happens
only at output boundaries such as CLI stderr, `kestrel check` JSON (with `ksr`
compatibility alias), and LSP diagnostics.

## 2. Strengthen Semantic Analysis — completed

Extend the semantic analyzer into a more complete type checker:

- [x] Check argument types, not only argument count.
- [x] Validate named arguments: unknown names, duplicates, and positional/named ordering.
- [x] Add return path analysis so non-Unit functions return on all paths.
- [x] Reject `await` outside async functions.
- [x] Check `when` exhaustiveness for sealed types.
- [x] Report unknown member access instead of falling back to `Any`.

## 3. Add CLI Integration Tests — completed

Cover the real CLI paths where regressions can hide:

- [x] `kestrel check bad.ksr` returns semantic errors as JSON.
- [x] `kestrel check syntax-bad.ksr` returns parser errors as JSON.
- [x] `kestrel file.ksr` stops before code generation when semantic errors exist.

The legacy `ksr` command remains an alias for each path.

## 4. Share CLI and LSP Analysis Pipeline — completed

`KsrAnalyzer.Check(source, path)` is the shared analysis facade used by the CLI,
LSP, tests, and MSBuild path so diagnostics behavior stays consistent.

## 5. Align Visual Studio SDK Dependencies — completed

Visual Studio SDK dependency versions are aligned, removing the prior `NU1603`
version-resolution warnings. Package vulnerability warnings are tracked separately
below and are not equivalent to version-resolution failures.

## 6. Kestrel .NET 10 migration and verification status — completed with platform limits

- [x] Public packages, CLI, templates, installer messaging, and VS Code metadata
  use the Kestrel brand; internal `KSR.*` namespaces and `.ksr` remain intentional
  compatibility surfaces.
- [x] Ordinary compiler, CLI, SDK, package, template, and test projects target
  `net10.0`; the Visual Studio extension remains the separate `net472` surface.
- [x] All eight canonical `Kestrel.*` packages pack successfully and no
  `KSR.*.nupkg` is produced by the ordinary package verifier.
- [x] Compiler and CLI suites pass on the available `net10.0` target. The
  canonical tool package installs and the console, library, creative, and
  camera templates build through both canonical and legacy names from an
  isolated temporary local feed. The verifier seeds that feed from the host's
  already-restored third-party Raylib/OpenCV packages and never uses
  `nuget.org`.
- [ ] Run the Visual Studio VSIX build/test/install validation on Windows with
  Visual Studio MSBuild. On macOS/Linux, the `net472` test target requires Mono.
- [x] Verify representative single-file examples end-to-end: `hello`, `async`,
  generic-interface, and sealed examples now compile and run through the CLI.
- [x] Bound the CLI integration suite's package-metadata process harness; the
  canonical-artifact test completes without hanging.

### Active dependency warnings

- `NU1900` can occur when NuGet cannot fetch vulnerability metadata from
  `api.nuget.org`; restore/build may otherwise succeed.
- `NU1903` currently reports a high-severity advisory for
  `Microsoft.Build.Utilities.Core` 17.11.4. This requires an intentional
  dependency update and was not changed as part of the foundation migration.
