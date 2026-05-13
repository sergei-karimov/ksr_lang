# KSR Improvement Plan

This plan captures the next engineering improvements after the semantic diagnostics fixes.

## 1. Introduce Structured Diagnostics

Replace string-parsed diagnostics with a core diagnostic model, for example:

```csharp
public record KsrDiagnostic(string Message, string SourceFile, int Line, int Col);
```

Parser and semantic analysis should return structured diagnostics. String formatting should happen only at output boundaries such as CLI stderr, `ksr check` JSON, and LSP diagnostics.

## 2. Strengthen Semantic Analysis

Extend the semantic analyzer into a more complete type checker:

- Check argument types, not only argument count.
- Validate named arguments: unknown names, duplicates, and positional/named ordering.
- Add return path analysis so non-Unit functions return on all paths.
- Reject `await` outside async functions.
- Check `when` exhaustiveness for sealed types.
- Report unknown member access instead of falling back to `Any`.

## 3. Add CLI Integration Tests

Cover the real CLI paths where regressions can hide:

- `ksr check bad.ksr` returns semantic errors as JSON.
- `ksr check syntax-bad.ksr` returns parser errors as JSON.
- `ksr file.ksr` stops before code generation when semantic errors exist.

## 4. Share CLI and LSP Analysis Pipeline

Extract a shared facade, such as `KsrAnalyzer.Check(source, path)`, that returns the parsed program and diagnostics.

Use that facade from CLI, LSP, tests, and future editor integrations so diagnostics behavior stays consistent.

## 5. Align Visual Studio SDK Dependencies

Clean up the current `NU1603` warnings by aligning package versions in the Visual Studio extension projects.

Consider centralizing versions in `Directory.Packages.props` if dependency management keeps growing.

