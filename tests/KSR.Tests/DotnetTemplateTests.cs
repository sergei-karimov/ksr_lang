using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace KSR.Tests;

public class DotnetTemplateTests
{
    [Theory]
    [InlineData("ksr-console", "ksr-console", "KSR Console Application")]
    [InlineData("ksr-creative", "ksr-creative", "KSR Creative Application")]
    [InlineData("ksr-creative-camera", "ksr-creative-camera", "KSR Creative Camera Application")]
    public void ProjectTemplates_HaveExpectedMetadata(string directory, string shortName, string name)
    {
        var template = LoadTemplate(directory);

        Assert.Equal(shortName, template.RootElement.GetProperty("shortName").GetString());
        Assert.Equal(name, template.RootElement.GetProperty("name").GetString());
        Assert.Equal("project", template.RootElement.GetProperty("tags").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("ksr-creative", "MyCreativeApp.csproj", "KSR.Creative")]
    [InlineData("ksr-creative-camera", "MyCameraApp.csproj", "KSR.Creative")]
    [InlineData("ksr-creative-camera", "MyCameraApp.csproj", "KSR.Vision")]
    public void CreativeTemplates_ReferenceRuntimePackages(string directory, string projectFile, string packageName)
    {
        var projectPath = Path.Combine(TemplatesRoot(), directory, projectFile);
        var projectText = File.ReadAllText(projectPath);

        Assert.Contains($"PackageReference Include=\"{packageName}\"", projectText);
    }

    [Theory]
    [InlineData("ksr-creative")]
    [InlineData("ksr-creative-camera")]
    public void CreativeTemplates_IncludeProgramAndEditorFiles(string directory)
    {
        var root = Path.Combine(TemplatesRoot(), directory);

        Assert.True(File.Exists(Path.Combine(root, "Program.ksr")));
        Assert.True(File.Exists(Path.Combine(root, "nuget.config")));
        Assert.True(File.Exists(Path.Combine(root, ".vscode", "launch.json")));
        Assert.True(File.Exists(Path.Combine(root, ".vscode", "tasks.json")));
    }

    [Theory]
    [InlineData("ksr-console")]
    [InlineData("ksr-creative")]
    [InlineData("ksr-creative-camera")]
    public void ProjectTemplates_IncludeValidNuGetConfig(string directory)
    {
        var path = Path.Combine(TemplatesRoot(), directory, "nuget.config");

        var doc = XDocument.Load(path);

        Assert.Equal("configuration", doc.Root?.Name.LocalName);
    }

    private static JsonDocument LoadTemplate(string directory)
    {
        var path = Path.Combine(TemplatesRoot(), directory, ".template.config", "template.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string TemplatesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "sdk", "KSR.Templates", "content");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate sdk/KSR.Templates/content.");
    }
}
