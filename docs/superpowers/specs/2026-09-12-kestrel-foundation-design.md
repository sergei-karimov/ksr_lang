# Kestrel Compiler Foundation and Branding Migration Design

**Date:** 2026-09-12
**Status:** Proposed for review

## Goal

Improve the compiler foundation across diagnostics, semantic analysis, CLI/LSP consistency, integration tests, and Visual Studio dependency management while introducing the Kestrel brand without breaking the existing internal `KSR.*` namespaces.

## Scope

This design covers five engineering improvements already identified in the repository's improvement plan plus the external rename from KSR to Kestrel:

1. Structured diagnostics.
2. Stronger semantic/type checking.
3. CLI integration tests.
4. A shared analysis pipeline for CLI, LSP, MSBuild, and tests.
5. Visual Studio SDK dependency alignment.
6. KSR-to-Kestrel external branding migration.

The first migration phase does not rename internal namespaces, AST types, or compiler implementation namespaces. Existing source-level `KSR.*` names remain valid inside the repository.

## Design Decisions

### External naming

Kestrel becomes the canonical public name:

- CLI command: `kestrel`.
- NuGet package IDs: `Kestrel.Build`, `Kestrel.Sdk`, `Kestrel.StdLib`, `Kestrel.Creative`, `Kestrel.Vision`, and `Kestrel.Templates`.
- Project templates: `kestrel-console`, `kestrel-library`, `kestrel-creative`, and `kestrel-creative-camera`.
- Documentation, installers, examples, generated project metadata, and user-facing diagnostics use Kestrel.

The old `KSR.*` packages have not been published, so no compatibility NuGet packages are required. Package and project metadata can be renamed directly while implementation namespaces remain `KSR.*` for now.

The existing `ksr` CLI command and `ksr-*` template names remain transition aliases. The alias behavior must be implemented at the distribution/installer layer because a .NET global tool exposes its declared command name directly. Both names must execute the same implementation and must be covered by tests.

Language syntax and source file extension remain unchanged: existing `.ksr` files continue to be valid Kestrel source files.

### Structured diagnostics

The compiler core introduces a structured diagnostic model with at least:

```csharp
public sealed record KsrDiagnostic(
    string Message,
    string? SourceFile,
    int Line,
    int Column,
    DiagnosticSeverity Severity);
```

The exact namespace may remain under `KSR.*` during this phase. Diagnostics are created by the lexer, parser, and semantic analyzer rather than reconstructed from formatted strings. Formatting is performed only at output boundaries:

- CLI stderr: human-readable compiler messages.
- `kestrel check`: stable JSON diagnostics.
- LSP: `PublishDiagnostics` with mapped severity and ranges.
- MSBuild: `LogError`/`LogWarning` with source location.

Compatibility formatting may preserve the current textual style where practical, but internal consumers must use structured fields.

### Shared analysis facade

The compiler core exposes a single analysis entry point conceptually equivalent to:

```csharp
KsrAnalysisResult Analyze(string source, string sourceFile);
```

The result contains the parsed program when parsing succeeds and all diagnostics collected up to the applicable phase. The facade owns the common lexer, parser, and semantic-analysis sequence. Code generation is not part of the diagnostic-only facade; compilation invokes it only when the result has no error diagnostics.

CLI, LSP, MSBuild, and tests use this facade. This ensures that an error reported by `kestrel check` is the same error reported by the language server and build task.

### Semantic checks

The semantic analyzer is extended to validate:

- argument types, in addition to argument counts;
- named argument names, duplicates, and positional/named ordering;
- return paths for non-`Unit` functions;
- rejection of `await` outside async functions;
- exhaustiveness of `when` over sealed types;
- unknown member access without silently falling back to `Any`.

Each failure produces a structured diagnostic with source location. Existing valid programs and examples must remain valid unless they depended on behavior explicitly identified as an unsound fallback.

### Testing strategy

Tests are layered:

- unit tests for diagnostic construction and semantic rules;
- CLI integration tests that invoke the actual command path and inspect exit code/stdout/stderr;
- LSP tests for diagnostics mapping;
- build-task tests for generated output and MSBuild logging;
- template tests for canonical Kestrel names and legacy aliases.

The project must document platform-specific test requirements. In particular, Visual Studio/VSIX tests may require Windows tooling, while the compiler and ordinary CLI tests should run on .NET 8+. The existing multi-target test configuration is preserved unless a targeted change is required to make the test matrix reliable.

### Visual Studio dependencies

Visual Studio SDK package versions are aligned across the VS extension projects and test projects. The change must remove avoidable `NU1603` dependency warnings without changing the extension's supported Visual Studio version range or its `net472` target. A central package-version file may be introduced only if it reduces duplication without obscuring the extension's platform-specific constraints.

## Architecture and Data Flow

```text
source text
    |
    v
KsrAnalyzer.Analyze(source, path)
    |
    +--> lexer diagnostics
    +--> parser diagnostics + ProgramNode?
    +--> semantic diagnostics
    |
    v
KsrAnalysisResult
    |             |             |
    v             v             v
CLI JSON/text   LSP           MSBuild
    |
    v
code generation only when no error diagnostics exist
```

The analyzer must retain source locations from tokens/AST nodes so consumers do not need to parse strings such as `"(line,column): error"`. Output adapters are thin and should not contain language-analysis logic.

## Migration and Compatibility

The migration is intentionally asymmetric:

- Public branding changes from KSR to Kestrel.
- Internal namespaces remain `KSR.*`.
- Existing `.ksr` source files remain supported.
- `ksr` remains an executable alias during the transition.
- `ksr-*` templates remain aliases for the corresponding `kestrel-*` templates.
- `KSR.*` NuGet package IDs are not preserved because they have not been published.

The transition period must include an explicit deprecation note in CLI help and documentation. The alias removal date is not part of this design and must be decided separately before a breaking release.

## Error Handling Requirements

- Lexing, parsing, and semantic errors must be represented as diagnostics whenever the caller supports diagnostic aggregation.
- Fatal internal/compiler failures remain exceptions at the implementation boundary and are converted to stable CLI/LSP/MSBuild errors.
- Code generation must not run after error-severity diagnostics.
- Diagnostics must include a valid 1-based line and column, falling back to `1,1` only when no source location exists.
- LSP and CLI must not emit malformed output when diagnostics contain Unicode or multiline messages.

## Non-Goals

- Renaming internal `KSR.*` namespaces in this phase.
- Changing the Kestrel language syntax or `.ksr` extension.
- Adding a new package registry or publishing workflow.
- Reworking the code generator architecture beyond what is needed to consume the shared analysis result.
- Removing the `ksr` or `ksr-*` aliases.
- Supporting old `KSR.*` NuGet package IDs that were never published.

## Acceptance Criteria

The design is considered implemented when:

1. The compiler core exposes structured diagnostics and all four consumers use them.
2. The listed semantic checks have focused passing and failing tests.
3. Real CLI integration tests cover syntax errors, semantic errors, successful checks, and the no-code-generation-on-error rule.
4. LSP and MSBuild diagnostics agree with CLI analysis for the same source.
5. Visual Studio dependency warnings targeted by this work are resolved or documented with a concrete platform limitation.
6. New Kestrel package/template/CLI names work, while `ksr` and `ksr-*` continue to work as aliases.
7. Existing internal `KSR.*` namespaces and `.ksr` examples continue to compile.
8. Documentation clearly describes Kestrel as canonical and KSR as transitional aliases.

## Suggested Implementation Order

1. Introduce the diagnostic model and shared analysis facade.
2. Migrate parser and semantic analyzer consumers to structured diagnostics.
3. Add and enforce the semantic checks, with tests first for each rule.
4. Add CLI/LSP/MSBuild integration coverage.
5. Align Visual Studio dependencies and verify the supported build paths.
6. Perform the external Kestrel branding migration, add aliases, update templates/installers/docs, and run the full validation matrix.
