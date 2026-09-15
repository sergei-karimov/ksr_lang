# Kestrel .NET 10 Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate Kestrel's ordinary compiler, CLI, SDK, package, template, installer, and test surfaces from `net8.0` to `net10.0` without changing internal `KSR.*` namespaces, `.ksr`, or compatibility aliases.

**Architecture:** Use a single `net10.0` target for all ordinary runtime projects and generated projects. Keep the Visual Studio extension and its tests on `net472`; update MSBuild package assets, installers, templates, docs, and launch metadata to consume the new runtime target. Preserve the existing Kestrel public names and `ksr`/`ksr-*` aliases.

**Tech Stack:** C#/.NET 10, Roslyn, xUnit, MSBuild SDK/tasks, JSON-RPC over stdio, VS Code extension tooling, Visual Studio SDK/net472, Bash, PowerShell, NuGet.

**Spec:** `docs/superpowers/specs/2026-09-14-dotnet10-migration-design.md`

## Global Constraints

- Ordinary runtime projects become single-target `net10.0`.
- Visual Studio extension projects remain `net472`.
- Generated projects and templates target `net10.0`.
- MSBuild task assets use `build/net10.0`.
- Public package IDs remain `Kestrel.*`; commands remain `kestrel`/`kestrel-*`.
- `ksr` and `ksr-*` remain working aliases.
- Internal `KSR.*` namespaces, source filenames, and `.ksr` remain unchanged.
- Every behavior change gets a focused regression test before implementation.
- Do not use `DOTNET_ROLL_FORWARD` as a supported migration workaround.

---

## Task 1: Establish the .NET 10 target matrix

**Files:**
- Modify: `KSR.csproj`
- Modify: `KSR.Core.csproj`
- Modify: `sdk/KSR.Build/KSR.Build.csproj`
- Modify: `sdk/KSR.Sdk/KSR.Sdk.csproj`
- Modify: `sdk/KSR.StdLib/KSR.StdLib.csproj`
- Modify: `sdk/KSR.Vision/KSR.Vision.csproj`
- Modify: `sdk/KSR.Creative/KSR.Creative.csproj`
- Modify: `sdk/KSR.Templates/KSR.Templates.csproj`
- Modify: `tests/KSR.Tests/KSR.Tests.csproj`
- Modify: `tests/KSR.Cli.Tests/KSR.Cli.Tests.csproj`
- Test: existing build/test projects

**Interfaces:**
- Produces a consistent ordinary-project target of `net10.0` for later package, template, and installer tasks.
- Does not modify `vs-extension/KSR.VisualStudio/KSR.VisualStudio.csproj` or `tests/KSR.VsExtension.Tests/KSR.VsExtension.Tests.csproj` target `net472`.

- [ ] **Step 1: Add a target-matrix regression check**

  Extend the existing project/template metadata tests so ordinary project files
  assert `net10.0`, while Visual Studio project files assert `net472`.

- [ ] **Step 2: Run the check and verify it fails**

  Run:

  ```bash
  dotnet test tests/KSR.Tests/KSR.Tests.csproj -f net10.0 --filter FullyQualifiedName~DotnetTemplateTests
  ```

  Expected: failure identifying the current `net8.0` targets.

- [ ] **Step 3: Change ordinary project targets**

  Replace ordinary `<TargetFramework>net8.0</TargetFramework>` with
  `net10.0` and replace `TargetFrameworks>net8.0;net10.0` with a single
  `net10.0` target. Preserve all `net472` Visual Studio targets.

- [ ] **Step 4: Run focused and build verification**

  ```bash
  dotnet test tests/KSR.Tests/KSR.Tests.csproj -f net10.0 --filter FullyQualifiedName~DotnetTemplateTests
  dotnet build KSR.sln --no-restore -m:1
  ```

  Expected: target assertions pass and the solution builds with zero errors.

- [ ] **Step 5: Commit**

  ```bash
  git add KSR.csproj KSR.Core.csproj sdk tests
  git commit -m "build: target ordinary projects at net10"
  ```

## Task 2: Move MSBuild and SDK package assets to net10

**Files:**
- Modify: `sdk/KSR.Build/KSR.Build.csproj`
- Modify: `sdk/KSR.Build/build/Kestrel.Build.props`
- Modify: `sdk/KSR.Build/build/Kestrel.Build.targets`
- Modify: `sdk/KSR.Sdk/Sdk/Sdk.props`
- Test: `tests/KSR.Tests/DotnetTemplateTests.cs`
- Test: `tests/KSR.Cli.Tests/InstallerMetadataTests.cs`

**Interfaces:**
- Consumes Task 1's `net10.0` project targets.
- Produces package assets under `build/net10.0` and SDK defaults of `net10.0`.

- [ ] **Step 1: Add failing package-layout assertions**

  Assert that a packed `Kestrel.Build` package contains
  `build/net10.0/KSR.Build.dll`, `build/net10.0/KSR.Core.dll`, and matching
  props/targets imports, and does not require `build/net8.0`.

- [ ] **Step 2: Run the focused package test and verify failure**

  ```bash
  dotnet test tests/KSR.Cli.Tests/KSR.Cli.Tests.csproj -f net10.0 --filter FullyQualifiedName~InstallerMetadataTests
  ```

  Expected: failure on the current `build/net8.0` package layout.

- [ ] **Step 3: Update package asset paths and SDK default**

  Change the MSBuild task output/package paths and `Sdk.props` default to
  `net10.0`. Keep assembly names, internal namespaces, and public package IDs
  unchanged.

- [ ] **Step 4: Pack and test the package layout**

  ```bash
  dotnet pack sdk/KSR.Build/KSR.Build.csproj -c Release -o /tmp/kestrel-net10-pack --no-restore
  dotnet test tests/KSR.Cli.Tests/KSR.Cli.Tests.csproj -f net10.0 --filter FullyQualifiedName~InstallerMetadataTests
  ```

  Expected: canonical package and asset assertions pass.

- [ ] **Step 5: Commit**

  ```bash
  git add sdk/KSR.Build sdk/KSR.Sdk tests
  git commit -m "build: package MSBuild assets for net10"
  ```

## Task 3: Migrate templates, generated projects, and editor metadata

**Files:**
- Modify: `sdk/KSR.Templates/content/ksr-console/MyApp.csproj`
- Modify: `sdk/KSR.Templates/content/ksr-creative/MyCreativeApp.csproj`
- Modify: `sdk/KSR.Templates/content/ksr-creative-camera/MyCameraApp.csproj`
- Modify: `sdk/KSR.Templates/content/ksr-library/MyLibrary.csproj`
- Modify: `vs-extension/KSR.VisualStudio/ProjectTemplates/KsrConsoleApp/$projectname$.csproj`
- Modify: `vscode-extension/package.json`
- Modify: `README.md`
- Test: `tests/KSR.Tests/DotnetTemplateTests.cs`
- Test: `tests/KSR.Tests/VsExtension/TemplateTests.cs`

**Interfaces:**
- Consumes the `net10.0` SDK and package layout from Tasks 1–2.
- Produces generated projects and debugger paths that target `net10.0`.

- [ ] **Step 1: Add failing generated-project assertions**

  Extend canonical and alias template tests to assert generated project files
  target `net10.0`, and update VS Code launch metadata tests to expect the
  `bin/Debug/net10.0` output path.

- [ ] **Step 2: Run focused tests and verify failure**

  ```bash
  dotnet test tests/KSR.Tests/KSR.Tests.csproj -f net10.0 --filter FullyQualifiedName~Template
  ```

  Expected: failures on generated `net8.0` projects and launch paths.

- [ ] **Step 3: Update template and documentation targets**

  Change generated csproj target frameworks, VS Code launch paths, README
  prerequisites/examples, and any public `net8.0` references that describe
  supported Kestrel projects. Keep historical design notes clearly historical
  if they are not executable configuration.

- [ ] **Step 4: Generate and build canonical and alias projects**

  ```bash
  dotnet new kestrel-console -n Net10Smoke
  dotnet build Net10Smoke/Net10Smoke.csproj
  dotnet new ksr-console -n KsrNet10Smoke
  dotnet build KsrNet10Smoke/KsrNet10Smoke.csproj
  ```

  Expected: both projects target and build on `net10.0`.

- [ ] **Step 5: Commit**

  ```bash
  git add sdk/KSR.Templates vs-extension vscode-extension README.md tests
  git commit -m "feat: generate net10 Kestrel projects"
  ```

## Task 4: Update installers, tool prerequisites, and LSP startup

**Files:**
- Modify: `scripts/install.sh`
- Modify: `scripts/install.ps1`
- Modify: `Program.cs`
- Modify: `vscode-extension/src/extension.ts`
- Test: `tests/KSR.Cli.Tests/CliProcessTests.cs`
- Test: `tests/KSR.Cli.Tests/InstallerMetadataTests.cs`
- Test: `tests/KSR.Cli.Tests/LspTransportTests.cs`

**Interfaces:**
- Consumes the net10 CLI/package/template artifacts from Tasks 1–3.
- Produces installers that reject unsupported pre-.NET10 environments and a
  global tool/LSP process that starts directly under .NET 10.

- [ ] **Step 1: Add failing prerequisite and LSP runtime assertions**

  Add tests that installer messages require .NET 10, the packed tool runtime
  config targets `net10.0`, and the LSP transport starts without a roll-forward
  environment override.

- [ ] **Step 2: Run focused tests and verify failure**

  ```bash
  dotnet test tests/KSR.Cli.Tests/KSR.Cli.Tests.csproj -f net10.0 --filter FullyQualifiedName~CliProcessTests|FullyQualifiedName~InstallerMetadataTests|FullyQualifiedName~LspTransportTests
  ```

  Expected: failures on .NET 8 prerequisite text/runtime metadata.

- [ ] **Step 3: Update installer checks and user-facing text**

  Require major version 10 or newer in Bash and PowerShell installers, update
  failure/help messages, and preserve canonical package source isolation and
  `ksr` alias lifecycle behavior.

- [ ] **Step 4: Verify tool and LSP startup**

  ```bash
  dotnet pack KSR.csproj -c Release -o /tmp/kestrel-net10-pack --no-restore
  dotnet test tests/KSR.Cli.Tests/KSR.Cli.Tests.csproj -f net10.0 --filter FullyQualifiedName~LspTransportTests
  ```

  Expected: LSP initializes and responds over stdio without `DOTNET_ROLL_FORWARD`.

- [ ] **Step 5: Commit**

  ```bash
  git add scripts Program.cs vscode-extension tests
  git commit -m "feat: require dotnet 10 for Kestrel tooling"
  ```

## Task 5: Complete package, installer, example, and compatibility verification

**Files:**
- Modify: `docs/code-review-improvement-plan.md`
- Modify: `README.md`
- Test: existing `tests/KSR.Tests/ExampleCompatibilityTests.cs`
- Test: existing `tests/KSR.Cli.Tests/InstallerMetadataTests.cs`
- Test: existing `tests/KSR.VsExtension.Tests/*`
- Create: `tests/verify-kestrel-net10-packages.sh` if the existing verifier needs a target-specific replacement

**Interfaces:**
- Consumes all migrated artifacts from Tasks 1–4.
- Produces the final evidence and documented platform limitations.

- [ ] **Step 1: Add or update final compatibility assertions**

  Assert that all eight canonical packages are packable, no `KSR.*` packages
  are produced, aliases still install, and verified examples retain internal
  namespace/source compatibility under `net10.0`.

- [ ] **Step 2: Run the final verification commands**

  ```bash
  dotnet build KSR.sln --no-restore -m:1
  dotnet test tests/KSR.Tests/KSR.Tests.csproj -f net10.0 -m:1
  dotnet test tests/KSR.Cli.Tests/KSR.Cli.Tests.csproj -f net10.0 -m:1
  bash tests/verify-kestrel-local-packages.sh
  ```

  Expected: zero test failures and eight canonical Kestrel packages.

- [ ] **Step 3: Run installer and example smoke tests**

  Run the supported-host install/uninstall lifecycle, canonical and alias
  template generation, and the verified `.ksr` examples. Record Windows/Mono
  limitations rather than claiming unavailable runtime checks.

- [ ] **Step 4: Update verification documentation**

  Mark completed .NET 10 checks in `docs/code-review-improvement-plan.md` and
  document only genuine remaining environment restrictions.

- [ ] **Step 5: Commit**

  ```bash
  git add README.md docs tests
  git commit -m "test: verify Kestrel net10 migration"
  ```

## Final review and handoff

- [ ] Run `git diff --check` and inspect the complete branch diff.
- [ ] Run the full net10 test/build/package matrix once more.
- [ ] Dispatch a broad whole-branch reviewer against the spec and this plan.
- [ ] Resolve review findings with one bounded fix round and scoped re-review.
- [ ] Keep the Visual Studio/Mono limitations explicit.
- [ ] Present merge/push/keep-branch options without merging or publishing automatically.
