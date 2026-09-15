using System.Text.Json;
using Xunit;

namespace KSR.Tests;

public sealed class VsCodeExtensionTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KSR.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("KSR.sln not found; run tests from within the repo");
    }

    private static string VsCodePackageJson =>
        Path.Combine(RepoRoot, "vscode-extension", "package.json");

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
        var program = debugger.GetProperty("configurationSnippets")[0]
            .GetProperty("body").GetProperty("program").GetString();
        Assert.Contains("bin/Debug/net10.0/", program);
        Assert.DoesNotContain("bin/Debug/net8.0/", program);
    }

    [Fact]
    public void VsCodeExecutableResolution_IsCanonicalFirstWithLegacyFallback()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "vscode-extension", "src", "executableResolver.ts"));

        Assert.Contains("findOnPath('kestrel'", source);
        Assert.Contains("`${command}.cmd`", source);
        Assert.Contains("`${command}.ps1`", source);
        Assert.Contains("return configured;", source);
        var canonicalIndex = source.IndexOf("findOnPath('kestrel'", StringComparison.Ordinal);
        var legacyIndex = source.IndexOf("findOnPath('ksr'", StringComparison.Ordinal);
        Assert.True(canonicalIndex >= 0);
        Assert.True(legacyIndex > canonicalIndex);
    }

    [Fact]
    public void VsCodeDefaultLaunchConfig_UsesNet10OutputPath()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "vscode-extension", "src", "extension.ts"));

        Assert.Contains("bin/Debug/net10.0/", source);
        Assert.DoesNotContain("bin/Debug/net8.0/", source);
    }

    [Theory]
    [InlineData("ksr-console", "${workspaceFolder}/bin/Debug/net10.0/${workspaceFolderBasename}.dll")]
    [InlineData("ksr-creative", "${workspaceFolder}/bin/Debug/net10.0/MyCreativeApp.dll")]
    public void TemplateLaunchConfigs_UseNet10OutputPath(string directory, string expectedProgram)
    {
        var path = Path.Combine(RepoRoot, "sdk", "KSR.Templates", "content", directory, ".vscode", "launch.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var program = document.RootElement.GetProperty("configurations")[0].GetProperty("program").GetString();

        Assert.Equal(expectedProgram, program);
        Assert.DoesNotContain("net8.0", program, StringComparison.Ordinal);
    }
}
