# Kestrel .NET 10 Migration Design

**Date:** 2026-09-14
**Status:** Proposed for review

## Goal

Move the Kestrel toolchain from `net8.0` to `net10.0` so the compiler, global
tool, Language Server, generated projects, and ordinary test suites run on the
.NET 10 runtime without requiring .NET 8.

## Scope

The migration covers the compiler, CLI, compiler libraries, SDK, MSBuild task,
standard libraries, creative/vision libraries, templates, generated projects,
ordinary tests, installers, documentation, and VS Code launch metadata.

The Visual Studio extension and its tests remain targeted to `net472`; that is
an intentional Visual Studio SDK boundary and is not part of the runtime
migration.

## Compatibility and naming

- Ordinary runtime projects become single-target `net10.0`; no temporary
  `net8.0;net10.0` multi-target matrix is introduced.
- Generated projects and templates target `net10.0`.
- MSBuild task assets move from `build/net8.0` to `build/net10.0`.
- Public package IDs remain `Kestrel.*`; the public commands remain `kestrel`
  and `kestrel-*`.
- `ksr` and `ksr-*` remain compatibility aliases.
- Internal `KSR.*` namespaces, source filenames, and the `.ksr` extension remain
  unchanged.
- The Visual Studio extension keeps its existing `net472` target and package
  compatibility constraints.

## Installer and developer experience

Installers must require and validate .NET 10 SDK/runtime availability, update
their error/help text, and continue to build/install the canonical Kestrel
packages and aliases. README, project-file examples, VS Code launch paths, and
platform notes must describe `net10.0` accurately.

The installed global tool must launch the Language Server directly under .NET
10. No `DOTNET_ROLL_FORWARD` workaround is required for the supported setup.

## Build and package behavior

All ordinary package projects and the CLI must restore, build, pack, and run on
.NET 10. Kestrel SDK imports and MSBuild task assembly paths must agree on the
new `net10.0` asset directory. Template-generated projects must build with
`dotnet build` and run with `dotnet run` using only the supported .NET 10
runtime.

## Verification requirements

The implementation must add or update focused tests before behavior changes and
must verify:

1. All ordinary solution projects build on .NET 10 with zero errors.
2. Compiler and CLI test suites pass on `net10.0`.
3. The global tool starts and serves LSP over stdio on .NET 10.
4. All eight canonical Kestrel packages pack successfully.
5. Canonical and legacy templates generate projects with `net10.0` and
   canonical package references.
6. The verified `.ksr` examples compile and execute.
7. Install/uninstall and alias smoke tests pass on the supported host.
8. Visual Studio/VSIX build remains valid; Windows runtime execution and Mono
   test execution are recorded as environment-specific checks when unavailable.

## Non-goals

- Renaming internal namespaces from `KSR.*`.
- Changing the `.ksr` language syntax or source extension.
- Removing compatibility aliases.
- Migrating the Visual Studio extension from `net472`.
- Introducing unrelated language features or package API changes.

## Acceptance criteria

The migration is complete when a clean machine with the .NET 10 SDK/runtime
can install Kestrel from source, create and run a `kestrel-console` project,
start `kestrel lsp`, execute a verified `.ksr` file, and build the solution
without requiring .NET 8. Existing internal namespace and alias compatibility
tests remain green.
