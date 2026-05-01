# KSR Visual Studio Integration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a Visual Studio VSIX extension that gives `.ksr` files syntax highlighting + error squiggles (via the existing LSP server), a "KSR Console Application" project template, and a "KSR File" item template.

**Architecture:** A single VSIX project (`vs-extension/KSR.VisualStudio/`) targeting `net472` (VS SDK requirement). It registers a `"ksr"` MEF content type, an `ILanguageClient` that spawns `ksr lsp`, an options page for the executable path, a VS project template, and a VS item template. Debugging works automatically because `CodeGenerator` already emits `#line` directives that Roslyn embeds into PDB files.

**Tech Stack:** C# / .NET Framework 4.7.2, `Microsoft.VisualStudio.SDK` 17.11.36, `Microsoft.VSSDK.BuildTools` 17.11.36, `Microsoft.VisualStudio.LanguageServer.Client` 17.11.36, VS `.vstemplate` XML format, MEF (`System.ComponentModel.Composition`).

> **Note — `#line` directives already done:** `CodeGenerator.EmitStmt` (line 345–349 of `CodeGen/CodeGenerator.cs`) already emits `#line N "path.ksr"` before every statement and `#line default` at end of each function body. No changes needed there.

---

## File Map

| Path | Action | Purpose |
|---|---|---|
| `vs-extension/KSR.VisualStudio/KSR.VisualStudio.csproj` | Create | VSIX project file (net472) |
| `vs-extension/KSR.VisualStudio/source.extension.vsixmanifest` | Create | Extension metadata + asset declarations |
| `vs-extension/KSR.VisualStudio/KsrPackage.cs` | Create | `AsyncPackage` — registers options page |
| `vs-extension/KSR.VisualStudio/KsrContentType.cs` | Create | MEF export: `.ksr` content type |
| `vs-extension/KSR.VisualStudio/KsrLanguageClient.cs` | Create | MEF export: LSP client (spawns `ksr lsp`) |
| `vs-extension/KSR.VisualStudio/KsrOptions.cs` | Create | `DialogPage` — Tools → Options → KSR |
| `vs-extension/KSR.VisualStudio/ProjectTemplates/KsrConsoleApp/KsrConsoleApp.vstemplate` | Create | New Project template metadata |
| `vs-extension/KSR.VisualStudio/ProjectTemplates/KsrConsoleApp/$projectname$.csproj` | Create | Template project file using KSR.Sdk |
| `vs-extension/KSR.VisualStudio/ProjectTemplates/KsrConsoleApp/Program.ksr` | Create | Skeleton `fun main()` |
| `vs-extension/KSR.VisualStudio/ProjectTemplates/KsrConsoleApp/nuget.config` | Create | NuGet feed pointing to KSR packages |
| `vs-extension/KSR.VisualStudio/ItemTemplates/KsrFile/KsrFile.vstemplate` | Create | New Item template metadata |
| `vs-extension/KSR.VisualStudio/ItemTemplates/KsrFile/NewFile.ksr` | Create | Skeleton KSR file |

---

## Task 1: Project scaffold

**Files:**
- Create: `vs-extension/KSR.VisualStudio/KSR.VisualStudio.csproj`
- Create: `vs-extension/KSR.VisualStudio/source.extension.vsixmanifest`
- Create: `vs-extension/KSR.VisualStudio/KsrPackage.cs`

- [ ] **Step 1: Create the directory**

```powershell
New-Item -ItemType Directory -Force vs-extension\KSR.VisualStudio\ProjectTemplates\KsrConsoleApp
New-Item -ItemType Directory -Force vs-extension\KSR.VisualStudio\ItemTemplates\KsrFile
```

- [ ] **Step 2: Create `KSR.VisualStudio.csproj`**

Create `vs-extension/KSR.VisualStudio/KSR.VisualStudio.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <RootNamespace>KSR.VisualStudio</RootNamespace>
    <AssemblyName>KSR.VisualStudio</AssemblyName>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <GeneratePkgDefFile>true</GeneratePkgDefFile>
    <IncludeAssemblyInVSIXContainer>true</IncludeAssemblyInVSIXContainer>
    <CopyBuildOutputToOutputDirectory>true</CopyBuildOutputToOutputDirectory>
    <UseCodebase>true</UseCodebase>
    <!-- Suppress the net472 warning about TFM when building from dotnet CLI -->
    <NoWarn>$(NoWarn);NU1702</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.VSSDK.BuildTools" Version="17.11.36">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.VisualStudio.SDK" Version="17.11.36" ExcludeAssets="runtime" />
    <PackageReference Include="Microsoft.VisualStudio.LanguageServer.Client" Version="17.11.36" ExcludeAssets="runtime" />
  </ItemGroup>

  <ItemGroup>
    <None Include="source.extension.vsixmanifest">
      <SubType>Designer</SubType>
    </None>
  </ItemGroup>

  <!-- Template files must be packaged as Content inside the VSIX -->
  <ItemGroup>
    <Content Include="ProjectTemplates\KsrConsoleApp\**\*" />
    <Content Include="ItemTemplates\KsrFile\**\*" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create `source.extension.vsixmanifest`**

Create `vs-extension/KSR.VisualStudio/source.extension.vsixmanifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<PackageManifest Version="2.0.0"
    xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"
    xmlns:d="http://schemas.microsoft.com/developer/vsx-schema-design/2011">
  <Metadata>
    <Identity
      Id="KSR.VisualStudio"
      Version="0.1.0"
      Language="en-US"
      Publisher="KSR Authors" />
    <DisplayName>KSR Language Support</DisplayName>
    <Description xml:space="preserve">
      Syntax highlighting, IntelliSense (via Language Server Protocol),
      and project templates for the KSR programming language.
    </Description>
    <Tags>ksr;language;lsp</Tags>
  </Metadata>

  <Installation>
    <!-- Targets Visual Studio 2022 and later -->
    <InstallationTarget Id="Microsoft.VisualStudio.Community" Version="[17.0,)" />
    <InstallationTarget Id="Microsoft.VisualStudio.Professional" Version="[17.0,)" />
    <InstallationTarget Id="Microsoft.VisualStudio.Enterprise" Version="[17.0,)" />
  </Installation>

  <Dependencies>
    <Dependency
      Id="Microsoft.Framework.NDP"
      DisplayName=".NET Framework 4.7.2"
      Version="[4.7.2,)" />
  </Dependencies>

  <Prerequisites>
    <Prerequisite
      Id="Microsoft.VisualStudio.Component.CoreEditor"
      Version="[17.0,)"
      DisplayName="Visual Studio core editor" />
  </Prerequisites>

  <Assets>
    <!-- Package with pkgdef (for Options page registration) -->
    <Asset Type="Microsoft.VisualStudio.VsPackage"
           d:Source="Project"
           d:ProjectName="%CurrentProject%"
           Path="|KSR.VisualStudio|Microsoft.VisualStudio.VsPackage" />

    <!-- MEF components: ILanguageClient + ContentType -->
    <Asset Type="Microsoft.VisualStudio.MefComponent"
           d:Source="Project"
           d:ProjectName="%CurrentProject%"
           Path="|KSR.VisualStudio|" />

    <!-- Project template (shown in File → New Project) -->
    <Asset Type="Microsoft.VisualStudio.ProjectTemplate"
           Path="ProjectTemplates" />

    <!-- Item template (shown in Add → New Item) -->
    <Asset Type="Microsoft.VisualStudio.ItemTemplate"
           Path="ItemTemplates" />
  </Assets>
</PackageManifest>
```

- [ ] **Step 4: Create `KsrPackage.cs`**

Create `vs-extension/KSR.VisualStudio/KsrPackage.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace KSR.VisualStudio;

/// <summary>
/// VS Package entry point.
/// Responsibilities: options-page registration only.
/// Language features (LSP) are handled by KsrLanguageClient via MEF.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideOptionPage(
    typeof(KsrOptionsPage),
    categoryName:    "KSR",
    pageName:        "General",
    categoryResourceID: 0,
    pageNameResourceID: 0,
    supportsAutomation: true)]
public sealed class KsrPackage : AsyncPackage
{
    public const string PackageGuidString = "4A1F9B3C-E7D2-4C8A-B5F0-9E3D6A2C1B7F";

    protected override async Task InitializeAsync(
        CancellationToken cancellationToken,
        IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
    }
}
```

- [ ] **Step 5: Build to verify project scaffolding compiles**

```powershell
cd vs-extension\KSR.VisualStudio
dotnet build KSR.VisualStudio.csproj
```

Expected: errors about missing types (`KsrOptionsPage`) — these are resolved in the next tasks. If there are MSBuild or SDK errors, fix those first.

- [ ] **Step 6: Commit**

```powershell
git add vs-extension\
git commit -m "feat(vs): add VSIX project scaffold"
```

---

## Task 2: Register `.ksr` content type

**Files:**
- Create: `vs-extension/KSR.VisualStudio/KsrContentType.cs`

- [ ] **Step 1: Create `KsrContentType.cs`**

Create `vs-extension/KSR.VisualStudio/KsrContentType.cs`:

```csharp
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Utilities;

namespace KSR.VisualStudio;

/// <summary>
/// Registers the "ksr" MEF content type and maps the .ksr file extension to it.
/// VS uses the content type to route language-service requests to KsrLanguageClient.
/// </summary>
internal static class KsrContentTypeDefinitions
{
    [Export]
    [Name("ksr")]
    [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
    internal static ContentTypeDefinition? KsrContentType;

    [Export]
    [ContentType("ksr")]
    [FileExtension(".ksr")]
    internal static FileExtensionToContentTypeDefinition? KsrFileExtension;
}
```

- [ ] **Step 2: Build**

```powershell
cd vs-extension\KSR.VisualStudio
dotnet build KSR.VisualStudio.csproj
```

Expected: same error about `KsrOptionsPage` (not yet created). No NEW errors.

- [ ] **Step 3: Commit**

```powershell
git add vs-extension\KSR.VisualStudio\KsrContentType.cs
git commit -m "feat(vs): register ksr content type"
```

---

## Task 3: Add options page

**Files:**
- Create: `vs-extension/KSR.VisualStudio/KsrOptions.cs`

- [ ] **Step 1: Create `KsrOptions.cs`**

Create `vs-extension/KSR.VisualStudio/KsrOptions.cs`:

```csharp
using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace KSR.VisualStudio;

/// <summary>
/// Exposes KSR settings under Tools → Options → KSR → General.
/// </summary>
public sealed class KsrOptionsPage : DialogPage
{
    [Category("General")]
    [DisplayName("KSR Executable Path")]
    [Description(
        "Path to the ksr executable used to start the Language Server. " +
        "Defaults to 'ksr' (must be on PATH). " +
        "Example: C:\\Users\\you\\.ksr\\ksr.exe")]
    public string ExecutablePath { get; set; } = "ksr";
}
```

- [ ] **Step 2: Build**

```powershell
cd vs-extension\KSR.VisualStudio
dotnet build KSR.VisualStudio.csproj
```

Expected: build succeeds (missing-type error about `KsrOptionsPage` is now resolved). Only remaining errors should be about `KsrLanguageClient` not yet existing.

- [ ] **Step 3: Commit**

```powershell
git add vs-extension\KSR.VisualStudio\KsrOptions.cs
git commit -m "feat(vs): add KSR options page"
```

---

## Task 4: Implement the language client

**Files:**
- Create: `vs-extension/KSR.VisualStudio/KsrLanguageClient.cs`

- [ ] **Step 1: Create `KsrLanguageClient.cs`**

Create `vs-extension/KSR.VisualStudio/KsrLanguageClient.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;

namespace KSR.VisualStudio;

/// <summary>
/// Starts `ksr lsp` as a child process and exposes it to Visual Studio
/// as an LSP language client.  VS routes all .ksr documents through this
/// client, which provides diagnostics, completions, and hover.
/// </summary>
[ContentType("ksr")]
[Export(typeof(ILanguageClient))]
public sealed class KsrLanguageClient : ILanguageClient
{
    // ── ILanguageClient ───────────────────────────────────────────────────────

    public string Name => "KSR Language Server";

    /// <summary>
    /// VS uses these section names to forward workspace configuration to the
    /// LSP server via workspace/configuration requests.
    /// </summary>
    public IEnumerable<string>? ConfigurationSections => new[] { "ksr" };

    public object? InitializationOptions => null;

    public IEnumerable<string>? FilesToWatch => null;

    public bool ShowNotificationOnInitializeFailed => true;

    public event AsyncEventHandler<EventArgs>? StartAsync;
    public event AsyncEventHandler<EventArgs>? StopAsync;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by VS when the extension is loaded.
    /// We fire StartAsync immediately so VS knows it can call ActivateAsync.
    /// </summary>
    public async Task OnLoadedAsync()
    {
        await StartAsync.InvokeAsync(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called by VS to launch the LSP server.
    /// Resolves the ksr executable, starts `ksr lsp`, and returns stdio streams.
    /// </summary>
    public Task<Connection?> ActivateAsync(CancellationToken token)
    {
        var exe = ResolveExecutable();
        if (exe is null)
        {
            // Show info bar on the VS main thread
            _ = Task.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                VsShellUtilities.ShowMessageBox(
                    ServiceProvider.GlobalProvider,
                    "KSR executable not found. Install KSR or set the path under " +
                    "Tools → Options → KSR → General → KSR Executable Path.",
                    "KSR Language Server",
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_WARNING,
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }, token);
            return Task.FromResult<Connection?>(null);
        }

        var psi = new ProcessStartInfo
        {
            FileName               = exe,
            Arguments              = "lsp",
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = false,
            CreateNoWindow         = true,
        };

        var process = new Process { StartInfo = psi };
        process.Start();

        return Task.FromResult<Connection?>(new Connection(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream));
    }

    public Task OnServerInitializedAsync() => Task.CompletedTask;

    public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(
        ILanguageClientInitializationInfo initializationFailureContext)
        => Task.FromResult<InitializationFailureContext?>(null);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the configured executable path from the options page, then searches:
    ///   1. The path as-is (if absolute)
    ///   2. %USERPROFILE%\.ksr\ksr.exe
    ///   3. Falls back to just "ksr" (relies on PATH — returns it even if not confirmed)
    /// </summary>
    private static string? ResolveExecutable()
    {
        // Read from options page if available
        var configured = "ksr";
        try
        {
            if (ServiceProvider.GlobalProvider.GetService(typeof(KsrPackage))
                    is KsrPackage pkg)
            {
                var page = (KsrOptionsPage)pkg.GetDialogPage(typeof(KsrOptionsPage));
                configured = page.ExecutablePath;
            }
        }
        catch { /* options not yet initialized — use default */ }

        if (Path.IsPathRooted(configured))
            return File.Exists(configured) ? configured : null;

        // Well-known install locations
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(userProfile, ".ksr", "ksr.exe"),
            @"C:\Program Files\ksr\ksr.exe",
        };

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        // Return the configured name and let the OS resolve it via PATH.
        // If `ksr` is not on PATH the process start will throw,
        // and VS will surface the error naturally.
        return configured;
    }
}
```

- [ ] **Step 2: Build**

```powershell
cd vs-extension\KSR.VisualStudio
dotnet build KSR.VisualStudio.csproj
```

Expected: build succeeds with zero errors.

- [ ] **Step 3: Commit**

```powershell
git add vs-extension\KSR.VisualStudio\KsrLanguageClient.cs
git commit -m "feat(vs): implement LSP language client"
```

---

## Task 5: Project template (File → New Project)

**Files:**
- Create: `vs-extension/KSR.VisualStudio/ProjectTemplates/KsrConsoleApp/KsrConsoleApp.vstemplate`
- Create: `vs-extension/KSR.VisualStudio/ProjectTemplates/KsrConsoleApp/$projectname$.csproj`
- Create: `vs-extension/KSR.VisualStudio/ProjectTemplates/KsrConsoleApp/Program.ksr`
- Create: `vs-extension/KSR.VisualStudio/ProjectTemplates/KsrConsoleApp/nuget.config`

- [ ] **Step 1: Create `KsrConsoleApp.vstemplate`**

Create `vs-extension/KSR.VisualStudio/ProjectTemplates/KsrConsoleApp/KsrConsoleApp.vstemplate`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<VSTemplate Version="3.0.0"
    Type="Project"
    xmlns="http://schemas.microsoft.com/developer/vstemplate/2005">
  <TemplateData>
    <Name>KSR Console Application</Name>
    <Description>A command-line application written in the KSR language.</Description>
    <ProjectType>CSharp</ProjectType>
    <ProjectSubType></ProjectSubType>
    <SortOrder>1000</SortOrder>
    <CreateNewFolder>true</CreateNewFolder>
    <DefaultName>KsrApp</DefaultName>
    <ProvideDefaultName>true</ProvideDefaultName>
    <LocationField>Enabled</LocationField>
    <EnableLocationBrowseButton>true</EnableLocationBrowseButton>
    <!-- Show under the "KSR" category in New Project dialog -->
    <TemplateGroupID>KSR</TemplateGroupID>
    <NumberOfParentCategoriesToRollUp>1</NumberOfParentCategoriesToRollUp>
    <Icon>__TemplateIcon.ico</Icon>
  </TemplateData>
  <TemplateContent>
    <Project File="$projectname$.csproj" ReplaceParameters="true">
      <ProjectItem ReplaceParameters="true" TargetFileName="Program.ksr">Program.ksr</ProjectItem>
      <ProjectItem ReplaceParameters="false" TargetFileName="nuget.config">nuget.config</ProjectItem>
    </Project>
  </TemplateContent>
</VSTemplate>
```

- [ ] **Step 2: Create `$projectname$.csproj`**

Create `vs-extension/KSR.VisualStudio/ProjectTemplates/KsrConsoleApp/$projectname$.csproj`:

```xml
<Project Sdk="KSR.Sdk/0.1.0">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

</Project>
```

- [ ] **Step 3: Create `Program.ksr`**

Create `vs-extension/KSR.VisualStudio/ProjectTemplates/KsrConsoleApp/Program.ksr`:

```
fun main() {
    println("Hello from $safeprojectname$!")
}
```

- [ ] **Step 4: Create `nuget.config`**

Create `vs-extension/KSR.VisualStudio/ProjectTemplates/KsrConsoleApp/nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

- [ ] **Step 5: Build**

```powershell
cd vs-extension\KSR.VisualStudio
dotnet build KSR.VisualStudio.csproj
```

Expected: succeeds. The template files appear inside the VSIX output.

- [ ] **Step 6: Commit**

```powershell
git add vs-extension\KSR.VisualStudio\ProjectTemplates\
git commit -m "feat(vs): add KSR Console Application project template"
```

---

## Task 6: Item template (Add → New Item)

**Files:**
- Create: `vs-extension/KSR.VisualStudio/ItemTemplates/KsrFile/KsrFile.vstemplate`
- Create: `vs-extension/KSR.VisualStudio/ItemTemplates/KsrFile/NewFile.ksr`

- [ ] **Step 1: Create `KsrFile.vstemplate`**

Create `vs-extension/KSR.VisualStudio/ItemTemplates/KsrFile/KsrFile.vstemplate`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<VSTemplate Version="3.0.0"
    Type="Item"
    xmlns="http://schemas.microsoft.com/developer/vstemplate/2005">
  <TemplateData>
    <Name>KSR File</Name>
    <Description>A new KSR source file.</Description>
    <ProjectType>CSharp</ProjectType>
    <SortOrder>10</SortOrder>
    <DefaultName>NewFile.ksr</DefaultName>
    <TemplateGroupID>KSR</TemplateGroupID>
  </TemplateData>
  <TemplateContent>
    <ProjectItem ReplaceParameters="true" TargetFileName="$fileinputname$.ksr">
      NewFile.ksr
    </ProjectItem>
  </TemplateContent>
</VSTemplate>
```

- [ ] **Step 2: Create `NewFile.ksr`**

Create `vs-extension/KSR.VisualStudio/ItemTemplates/KsrFile/NewFile.ksr`:

```
fun $safeitemname$() {

}
```

- [ ] **Step 3: Build the VSIX**

```powershell
cd vs-extension\KSR.VisualStudio
dotnet build KSR.VisualStudio.csproj -c Release
```

Expected: `bin\Release\net472\KSR.VisualStudio.vsix` created.

- [ ] **Step 4: Commit**

```powershell
git add vs-extension\KSR.VisualStudio\ItemTemplates\
git commit -m "feat(vs): add KSR File item template"
```

---

## Task 7: Smoke test (manual)

No automated test harness is possible for VS extension integration. Verify each feature manually.

**Pre-condition:** `ksr` is installed and on PATH. VSIX built in Release mode.

- [ ] **Step 1: Install the VSIX**

In Visual Studio, go to:
`Extensions → Manage Extensions → Install from file → select bin\Release\net472\KSR.VisualStudio.vsix`

Or double-click the `.vsix` file in Explorer.

Restart Visual Studio when prompted.

- [ ] **Step 2: Verify options page**

`Tools → Options → KSR → General`

Expected: dialog shows *KSR Executable Path* field with default value `"ksr"`.

- [ ] **Step 3: Verify project template**

`File → New → Project`, search "KSR".

Expected: "KSR Console Application" template appears. Create a project.

Expected: project opens with `Program.ksr` containing `fun main() { println("Hello from …!") }`.

- [ ] **Step 4: Verify content type and IntelliSense**

Open `Program.ksr`. Type `val x = ` and press `Ctrl+Space`.

Expected: completion list with KSR keywords and built-in types appears.

Edit to introduce an error (e.g., `val x = unknownVar`).

Expected: red squiggle appears within a few seconds.

- [ ] **Step 5: Verify F5 debugging**

Press F5 (or `Debug → Start Debugging`).

Expected: console window opens and prints `Hello from <ProjectName>!`.

Set a breakpoint on the `println` line in `Program.ksr`.

Press F5 again.

Expected: execution pauses at the breakpoint inside `Program.ksr` (not in a generated file).

- [ ] **Step 6: Verify adding .ksr to existing project**

Open an existing `.csproj` that references `KSR.Build` NuGet package.

Right-click project → `Add → New Item → KSR`.

Expected: "KSR File" template appears; creates `NewFile.ksr`.

- [ ] **Step 7: Final commit**

```powershell
git add .
git commit -m "feat(vs): KSR Visual Studio extension complete — smoke tests passing"
```

---

## Appendix: Known gotchas

1. **`net472` build on non-Windows:** VSIX projects must be built on Windows because they reference Windows-only VS SDK assemblies. CI should run this project only on a Windows agent.

2. **`$projectname$.csproj` filename:** The dollar-sign name is a VS template token. The VSSDK BuildTools know to skip tokenizing `.csproj` files that start with `$`; ensure the file is committed with that exact name.

3. **`ILanguageClientInitializationInfo` vs `InitializationFailureContext`:** The return type of `OnServerInitializeFailedAsync` changed between VS SDK versions. If the build fails with a type mismatch, check the installed `Microsoft.VisualStudio.LanguageServer.Client` version and adjust the return type accordingly (`InitializationFailureContext?` for 17.x, `InitializationOptions?` for 16.x).

4. **MEF composition errors:** If the language client doesn't activate, open `%AppData%\Microsoft\VisualStudio\17.0_<id>\ComponentModelCache` and delete `Microsoft.VisualStudio.Default.err`. VS rebuilds the MEF cache on next start.

5. **LSP server not found:** If `ksr lsp` doesn't start, verify in `Tools → Options → KSR` that the path is correct, then check `%TEMP%\VSFeedbackIntelliSense*.log` for startup errors.
