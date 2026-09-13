using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace KSR.Tests.VsExtension;

public sealed class TemplateTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/developer/vstemplate/2005";
    private static readonly XNamespace VsixNs = "http://schemas.microsoft.com/developer/vsx-schema/2011";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KSR.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("KSR.sln not found; run tests from within the repo");
    }

    private static string ProjectTemplateDir =>
        Path.Combine(RepoRoot, "vs-extension", "KSR.VisualStudio", "ProjectTemplates", "KsrConsoleApp");

    private static string ItemTemplateDir =>
        Path.Combine(RepoRoot, "vs-extension", "KSR.VisualStudio", "ItemTemplates", "KsrFile");

    private static string VsCodePackageJson =>
        Path.Combine(RepoRoot, "vscode-extension", "package.json");

    // ─── Project template ───────────────────────────────────────────────────

    [Fact]
    public void ProjectTemplate_IsValidXml()
    {
        var doc = XDocument.Load(Path.Combine(ProjectTemplateDir, "KsrConsoleApp.vstemplate"));
        Assert.NotNull(doc.Root);
    }

    [Fact]
    public void ProjectTemplate_TypeIsProject()
    {
        var doc = XDocument.Load(Path.Combine(ProjectTemplateDir, "KsrConsoleApp.vstemplate"));
        Assert.Equal("Project", doc.Root!.Attribute("Type")?.Value);
    }

    [Fact]
    public void ProjectTemplate_ProjectNode_HasReplaceParametersTrue()
    {
        var doc = XDocument.Load(Path.Combine(ProjectTemplateDir, "KsrConsoleApp.vstemplate"));
        var project = doc.Descendants(Ns + "Project").Single();
        Assert.Equal("true", project.Attribute("ReplaceParameters")?.Value, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectTemplate_ProgramKsr_HasReplaceParametersTrue()
    {
        var doc = XDocument.Load(Path.Combine(ProjectTemplateDir, "KsrConsoleApp.vstemplate"));
        var item = doc.Descendants(Ns + "ProjectItem")
            .Single(e => e.Attribute("TargetFileName")?.Value == "Program.ksr");
        Assert.Equal("true", item.Attribute("ReplaceParameters")?.Value, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectTemplate_ProgramKsr_ContainsSafeProjectNameToken()
    {
        var content = File.ReadAllText(Path.Combine(ProjectTemplateDir, "Program.ksr"));
        Assert.Contains("$safeprojectname$", content);
    }

    [Fact]
    public void ProjectTemplate_AllReferencedFilesExist()
    {
        var doc = XDocument.Load(Path.Combine(ProjectTemplateDir, "KsrConsoleApp.vstemplate"));

        // Check <Icon> element if present
        var iconName = doc.Descendants(Ns + "Icon").FirstOrDefault()?.Value?.Trim();
        if (!string.IsNullOrEmpty(iconName))
            Assert.True(File.Exists(Path.Combine(ProjectTemplateDir, iconName)),
                $"Icon file '{iconName}' referenced in vstemplate does not exist.");

        // Check each ProjectItem
        foreach (var item in doc.Descendants(Ns + "ProjectItem"))
        {
            var fileName = item.Value.Trim();
            Assert.True(File.Exists(Path.Combine(ProjectTemplateDir, fileName)),
                $"Template file '{fileName}' referenced in vstemplate does not exist.");
        }

        // Check <Project File="...">
        var projectFile = doc.Descendants(Ns + "Project").FirstOrDefault()?.Attribute("File")?.Value;
        if (!string.IsNullOrEmpty(projectFile) && !projectFile.Contains('$'))
            Assert.True(File.Exists(Path.Combine(ProjectTemplateDir, projectFile)),
                $"Project file '{projectFile}' referenced in vstemplate does not exist.");
    }

    [Fact]
    public void ProjectTemplate_UsesCanonicalKestrelSdk()
    {
        var project = File.ReadAllText(Path.Combine(ProjectTemplateDir, "$projectname$.csproj"));

        Assert.Contains("Project Sdk=\"Kestrel.Sdk/0.1.0\"", project);
        Assert.DoesNotContain("Project Sdk=\"KSR.Sdk/", project);
    }

    // ─── Item template ──────────────────────────────────────────────────────

    [Fact]
    public void ItemTemplate_IsValidXml()
    {
        var doc = XDocument.Load(Path.Combine(ItemTemplateDir, "KsrFile.vstemplate"));
        Assert.NotNull(doc.Root);
    }

    [Fact]
    public void ItemTemplate_TypeIsItem()
    {
        var doc = XDocument.Load(Path.Combine(ItemTemplateDir, "KsrFile.vstemplate"));
        Assert.Equal("Item", doc.Root!.Attribute("Type")?.Value);
    }

    [Fact]
    public void ItemTemplate_ProjectItem_HasReplaceParametersTrue()
    {
        var doc = XDocument.Load(Path.Combine(ItemTemplateDir, "KsrFile.vstemplate"));
        var item = doc.Descendants(Ns + "ProjectItem").Single();
        Assert.Equal("true", item.Attribute("ReplaceParameters")?.Value, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItemTemplate_ProjectItem_TargetFileName_HasFileInputNameToken()
    {
        var doc = XDocument.Load(Path.Combine(ItemTemplateDir, "KsrFile.vstemplate"));
        var item = doc.Descendants(Ns + "ProjectItem").Single();
        Assert.Contains("$fileinputname$", item.Attribute("TargetFileName")?.Value ?? "");
    }

    [Fact]
    public void ItemTemplate_NewFileKsr_ContainsSafeItemNameToken()
    {
        var content = File.ReadAllText(Path.Combine(ItemTemplateDir, "NewFile.ksr"));
        Assert.Contains("$safeitemname$", content);
    }

    [Fact]
    public void ItemTemplate_AllReferencedFilesExist()
    {
        var doc = XDocument.Load(Path.Combine(ItemTemplateDir, "KsrFile.vstemplate"));

        var iconName = doc.Descendants(Ns + "Icon").FirstOrDefault()?.Value?.Trim();
        if (!string.IsNullOrEmpty(iconName))
            Assert.True(File.Exists(Path.Combine(ItemTemplateDir, iconName)),
                $"Icon file '{iconName}' referenced in vstemplate does not exist.");

        foreach (var item in doc.Descendants(Ns + "ProjectItem"))
        {
            var fileName = item.Value.Trim();
            Assert.True(File.Exists(Path.Combine(ItemTemplateDir, fileName)),
                $"Template file '{fileName}' referenced in vstemplate does not exist.");
        }
    }

    [Fact]
    public void VisualStudioManifest_UsesKestrelBranding()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot, "vs-extension", "KSR.VisualStudio", "source.extension.vsixmanifest"));
        var metadata = doc.Root!.Element(VsixNs + "Metadata")!;

        var identity = metadata.Element(VsixNs + "Identity")!;
        Assert.Equal("Kestrel.VisualStudio", identity.Attribute("Id")?.Value);
        Assert.Equal("Kestrel Authors", identity.Attribute("Publisher")?.Value);
        Assert.Equal("Kestrel Language Support", metadata.Element(VsixNs + "DisplayName")?.Value.Trim());
        Assert.Contains("Kestrel programming language", metadata.Element(VsixNs + "Description")?.Value ?? "");
        Assert.Contains("kestrel", metadata.Element(VsixNs + "Tags")?.Value ?? "");
        Assert.Contains("ksr", metadata.Element(VsixNs + "Tags")?.Value ?? "");
    }

    [Fact]
    public void VsCodePackage_UsesKestrelBrandingAndPreservesKsrLanguageCompatibility()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(VsCodePackageJson));
        var root = document.RootElement;

        Assert.Equal("Kestrel Language", root.GetProperty("displayName").GetString());
        Assert.Contains("Kestrel language", root.GetProperty("description").GetString());

        var language = root.GetProperty("contributes").GetProperty("languages")[0];
        Assert.Equal("ksr", language.GetProperty("id").GetString());
        Assert.Contains(".ksr", language.GetProperty("extensions").EnumerateArray().Select(value => value.GetString()));

        var executable = root.GetProperty("contributes").GetProperty("configuration")
            .GetProperty("properties").GetProperty("ksr.executablePath");
        Assert.Equal("Kestrel", root.GetProperty("contributes").GetProperty("configuration")
            .GetProperty("title").GetString());
        Assert.Equal("kestrel", executable.GetProperty("default").GetString());

        var debugger = root.GetProperty("contributes").GetProperty("debuggers")[0];
        Assert.Equal("Kestrel", debugger.GetProperty("label").GetString());
        Assert.Equal("Kestrel: Launch", debugger.GetProperty("configurationSnippets")[0].GetProperty("label").GetString());
        Assert.Equal("ksr", debugger.GetProperty("type").GetString());
    }

    [Fact]
    public void VsCodeExecutableResolution_IsCanonicalFirstWithLegacyFallback()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "vscode-extension", "src", "extension.ts"));

        Assert.Contains("if (isExplicitPath(configured) && fs.existsSync(configured))", source);
        Assert.Contains("return configured;", source);
        var canonicalIndex = source.IndexOf("findOnPath('kestrel')", StringComparison.Ordinal);
        var legacyIndex = source.IndexOf("findOnPath('ksr')", StringComparison.Ordinal);
        Assert.True(canonicalIndex >= 0);
        Assert.True(legacyIndex > canonicalIndex);
    }
}
