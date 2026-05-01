# KSR Visual Studio Integration — Design Spec

**Date:** 2026-05-01  
**Status:** Approved

---

## Problem

KSR already has a VS Code extension and an LSP server (`ksr lsp`). Visual Studio has none of this. The user wants to:

1. Create new KSR projects directly from Visual Studio's *File → New Project* dialog.
2. Add `.ksr` files to existing projects.
3. Get syntax highlighting and error squiggles (IntelliSense) while editing `.ksr` files.
4. Press F5 to run/debug KSR programs, with breakpoints settable directly in `.ksr` source files.

---

## Approach

**VSIX extension (LSP-based)** — a Visual Studio extension that reuses the existing `ksr lsp` server for language features, the existing `KSR.Build` MSBuild task for compilation, and the built-in Visual Studio coreclr debugger for debugging.

`#line` directives are added to `CodeGenerator` so the coreclr debugger can map execution back to `.ksr` source lines.

---

## Components

### 1. `#line` directives in `CodeGenerator.cs`

Every `Stmt` already carries `Line` (1-based line number) and `SourceFile` (absolute path to the `.ksr` file) from the parser. `CodeGenerator` will emit a `#line N "path"` directive before each top-level statement it generates.

**Format:**
```csharp
#line 10 "C:\projects\MyApp\Program.ksr"
// …generated C# statement for KSR line 10…
#line default
```

This is the only change to the existing `KSR.Core` codebase. The MSBuild task (`KsrCompileTask`) calls `CodeGenerator`, so the resulting `.g.cs` file will automatically contain the mapping. Roslyn embeds it into the PDB.

**Scope**: emit `#line` before every `Stmt` where `Line > 0 && SourceFile != ""`. Emit `#line default` after the last statement in each function body.

---

### 2. New `vs-extension/KSR.VisualStudio/` VSIX project

A new folder `vs-extension/KSR.VisualStudio/` alongside the existing `vscode-extension/`. It contains a single VSIX project targeting `net472` (Visual Studio SDK requirement).

**Dependencies (NuGet):**
- `Microsoft.VisualStudio.SDK` — core VS extension APIs
- `Microsoft.VisualStudio.LanguageServer.Client` — LSP client base class
- `Microsoft.VisualStudio.Threading` — `JoinableTaskFactory` (required by LSP client)

**Files:**

| File | Purpose |
|---|---|
| `KSR.VisualStudio.csproj` | VSIX project file, `net472` |
| `source.extension.vsixmanifest` | Extension metadata, targets VS 2022+ |
| `KsrContentType.cs` | Registers `"ksr"` content type for `.ksr` files |
| `KsrLanguageClient.cs` | `ILanguageClient` — spawns `ksr lsp`, connects via stdio |
| `ProjectTemplates/KsrConsoleApp/` | New Project template |
| `ItemTemplates/KsrFile/` | New Item template |

---

#### 2a. Language Client (`KsrLanguageClient.cs`)

Implements `Microsoft.VisualStudio.LanguageServer.Client.ILanguageClient`.

- `Name`: `"KSR Language Server"`
- `DocumentSelector`: `{ pattern = "**/*.ksr" }`
- `ActivateAsync()`: resolves `ksr` executable (checks `PATH`; falls back to `%USERPROFILE%\.ksr\ksr.exe`); starts `ksr lsp` as a `Process` with `RedirectStandardInput/Output = true`; returns stdio streams.
- Provides: diagnostics (error squiggles), keyword/symbol completions, hover — all from the existing LSP server with zero additional code.

If `ksr` is not found, shows a VS info bar: *"KSR executable not found. Install KSR or set the path in Tools → Options → KSR."*

**Options page** (`Tools → Options → KSR`): one setting — *KSR executable path* (default: `"ksr"`).

---

#### 2b. Project Template (`ProjectTemplates/KsrConsoleApp/`)

Appears in *File → New Project* under category **KSR** with name **"KSR Console Application"**.

Contents created in the target folder:

```
MyApp.csproj         — uses Sdk="KSR.Sdk.MSBuild", TargetFramework net8.0
Program.ksr          — fun main() { println("Hello, KSR!") }
nuget.config         — points to the KSR NuGet feed
```

The `.csproj` template:
```xml
<Project Sdk="KSR.Sdk.MSBuild/0.1.0">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

This reuses the existing `KSR.Sdk` MSBuild SDK. The `KSR.Build.targets` already sets up the `KsrCompile` item group and wires the `KsrCompileTask`.

---

#### 2c. Item Template (`ItemTemplates/KsrFile/`)

Appears in *Add → New Item* under category **KSR** with name **"KSR File"**.

Creates `$name$.ksr` with a minimal skeleton:
```
fun $safeitemname$() {

}
```

---

### 3. Adding `.ksr` to an existing project

No new code required. The user does *Add → Existing Item* or *Add → New Item → KSR File*. The existing `KSR.Build.targets` (auto-imported via NuGet) contains:

```xml
<ItemGroup>
  <KsrCompile Include="**/*.ksr" />
</ItemGroup>
```

This picks up any `.ksr` file in the project directory automatically. If the user's project does not yet reference `KSR.Build`, they add it via NuGet (same as today).

---

## Debug Flow

```
F5 in Visual Studio
 │
 ├─ MSBuild: KsrCompile target runs
 │    KsrCompileTask → CodeGenerator (emits #line directives)
 │    Output: obj/Debug/net8.0/KsrGenerated.g.cs  (with #line N "Program.ksr")
 │
 ├─ Roslyn: compiles KsrGenerated.g.cs → MyApp.dll + MyApp.pdb
 │    PDB contains source mapping: MyApp.dll line 47 → Program.ksr line 10
 │
 └─ coreclr debugger: attaches to MyApp.dll
      Breakpoint on Program.ksr:10 → maps to dll offset via PDB → pauses ✓
      Step-over moves to Program.ksr:11 ✓
      Locals / watch windows work on C# variables ✓
```

The VSIX does **not** implement a custom debug adapter. The built-in coreclr adapter handles everything once the PDB has `.ksr` source mappings.

---

## Out of Scope (v1)

- Rename / refactor across files
- Go-to-definition navigating to `.ksr` declarations (LSP `textDocument/definition` not yet implemented in `LspServer.cs`)
- Step-into closures mapping back to `.ksr` lambda source
- VS for Mac / Rider support

---

## File Locations Summary

| Path | What |
|---|---|
| `CodeGen/CodeGenerator.cs` | Add `#line` directive emission |
| `vs-extension/KSR.VisualStudio/` | New VSIX project (entire folder) |
| `vs-extension/KSR.VisualStudio/KsrLanguageClient.cs` | LSP client |
| `vs-extension/KSR.VisualStudio/KsrContentType.cs` | `.ksr` content type |
| `vs-extension/KSR.VisualStudio/ProjectTemplates/KsrConsoleApp/` | New Project template |
| `vs-extension/KSR.VisualStudio/ItemTemplates/KsrFile/` | New Item template |
