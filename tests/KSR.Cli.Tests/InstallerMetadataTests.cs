using System.Diagnostics;
using Xunit;

namespace KSR.Cli.Tests;

public class InstallerMetadataTests
{
    [Fact]
    public void UnixInstaller_InstallsCanonicalArtifactsAndManagesKsrAlias()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts/install.sh"));

        Assert.Contains("kestrel-local", script, StringComparison.Ordinal);
        Assert.Contains("dotnet tool install -g Kestrel", script, StringComparison.Ordinal);
        Assert.Contains("Kestrel.Templates", script, StringComparison.Ordinal);
        Assert.Contains("Kestrel.Templates.0.1.0.nupkg", script, StringComparison.Ordinal);
        Assert.Contains("${DOTNET_CLI_HOME:-$HOME}/.dotnet/tools", script, StringComparison.Ordinal);
        Assert.Contains("exec \"$(dirname \"$0\")/kestrel\" \"$@\"", script, StringComparison.Ordinal);
        Assert.Contains("rm -f \"$TOOLS_PATH/ksr\"", script, StringComparison.Ordinal);
        Assert.Contains("pushd \"$TOOLS_PATH\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("tool install -g Kestrel --add-source", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsInstaller_InstallsCanonicalArtifactsAndManagesKsrAliases()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts/install.ps1"));

        Assert.Contains("$feedName = 'kestrel-local'", script, StringComparison.Ordinal);
        Assert.Contains("'tool', 'install', '-g', 'Kestrel'", script, StringComparison.Ordinal);
        Assert.Contains("Kestrel.Templates.0.1.0.nupkg", script, StringComparison.Ordinal);
        Assert.Contains("ksr.cmd", script, StringComparison.Ordinal);
        Assert.Contains("ksr.ps1", script, StringComparison.Ordinal);
        Assert.Contains("kestrel.exe", script, StringComparison.Ordinal);
        Assert.Contains("Removing ksr compatibility aliases", script, StringComparison.Ordinal);
        Assert.Contains("Push-Location $toolsPath", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'--add-source'", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublicProjects_PackOnlyCanonicalKestrelArtifacts()
    {
        var artifactDirectory = Path.Combine(Path.GetTempPath(), "kestrel-package-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifactDirectory);

        try
        {
            foreach (var project in PublicProjects)
            {
                var result = await RunDotnetAsync("pack", project, "-c", "Release", "-o", artifactDirectory, "--nologo");
                Assert.True(result.ExitCode == 0, result.Output);
            }

            var packages = Directory.GetFiles(artifactDirectory, "*.nupkg")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(ExpectedPackages.OrderBy(name => name, StringComparer.Ordinal), packages);
            Assert.DoesNotContain(packages, package => package!.StartsWith("KSR.", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(artifactDirectory, recursive: true);
        }
    }

    private static readonly string[] PublicProjects =
    [
        "KSR.Core.csproj",
        "sdk/KSR.Build/KSR.Build.csproj",
        "sdk/KSR.Sdk/KSR.Sdk.csproj",
        "sdk/KSR.StdLib/KSR.StdLib.csproj",
        "sdk/KSR.Vision/KSR.Vision.csproj",
        "sdk/KSR.Creative/KSR.Creative.csproj",
        "sdk/KSR.Templates/KSR.Templates.csproj",
        "KSR.csproj"
    ];

    private static readonly string[] ExpectedPackages =
    [
        "Kestrel.0.1.0.nupkg",
        "Kestrel.Build.0.1.0.nupkg",
        "Kestrel.Core.0.1.0.nupkg",
        "Kestrel.Creative.0.1.0.nupkg",
        "Kestrel.Sdk.0.1.0.nupkg",
        "Kestrel.StdLib.0.1.0.nupkg",
        "Kestrel.Templates.0.1.0.nupkg",
        "Kestrel.Vision.0.1.0.nupkg"
    ];

    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment["DOTNET_ROLL_FORWARD"] = "Major";
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet pack.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KSR.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing KSR.sln.");
    }
}
