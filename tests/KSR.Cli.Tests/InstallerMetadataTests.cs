using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
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
        Assert.Contains("--configfile \"$TOOL_CONFIG\"", script, StringComparison.Ordinal);
        Assert.Contains("kestrel-artifacts", script, StringComparison.Ordinal);
        Assert.Contains("<clear />", script, StringComparison.Ordinal);
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
        Assert.Contains("'--configfile', $toolConfig", script, StringComparison.Ordinal);
        Assert.Contains("'kestrel-artifacts'", script, StringComparison.Ordinal);
        Assert.Contains("<clear />", script, StringComparison.Ordinal);
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
                var result = await RunDotnetAsync("pack", project, "-c", "Release", "-o", artifactDirectory, "--nologo", "--no-restore", "--disable-build-servers");
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

    [Fact]
    public async Task BuildPackage_ContainsNet10BuildAssetsAndImports()
    {
        var artifactDirectory = Path.Combine(Path.GetTempPath(), "kestrel-build-package-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifactDirectory);

        try
        {
            var result = await RunDotnetAsync(
                "pack",
                "sdk/KSR.Build/KSR.Build.csproj",
                "-c",
                "Release",
                "-o",
                artifactDirectory,
                "--nologo",
                "--no-restore",
                "--disable-build-servers");

            Assert.Equal(0, result.ExitCode);

            var packagePath = Path.Combine(artifactDirectory, "Kestrel.Build.0.1.0.nupkg");
            Assert.True(File.Exists(packagePath), result.Output);

            using var archive = ZipFile.OpenRead(packagePath);
            var entries = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Contains("build/net10.0/KSR.Build.dll", entries);
            Assert.Contains("build/net10.0/KSR.Core.dll", entries);
            Assert.Contains("build/Kestrel.Build.props", entries);
            Assert.Contains("build/Kestrel.Build.targets", entries);
            Assert.DoesNotContain(entries, entry => entry.StartsWith("build/net8.0/", StringComparison.OrdinalIgnoreCase));

            var targets = archive.GetEntry("build/Kestrel.Build.targets");
            Assert.NotNull(targets);
            using var reader = new StreamReader(targets!.Open());
            var targetsText = await reader.ReadToEndAsync();
            Assert.Contains("$(KsrBuildTargetFramework)\\KSR.Build.dll", targetsText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(artifactDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("kestrel-console", "KestrelSmokeApp")]
    [InlineData("ksr-console", "KsrAliasSmokeApp")]
    [InlineData("kestrel-lib", "KestrelLibrary")]
    [InlineData("ksr-lib", "KsrAliasLibrary")]
    [InlineData("kestrel-creative", "KestrelCreativeApp")]
    [InlineData("ksr-creative", "KsrAliasCreativeApp")]
    [InlineData("kestrel-creative-camera", "KestrelCameraApp")]
    [InlineData("ksr-creative-camera", "KsrAliasCameraApp")]
    public async Task TemplateAliases_GenerateProjectsWithCanonicalPackageReferences(string template, string projectName)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "kestrel-template-tests", Guid.NewGuid().ToString("N"));
        var cliHome = Path.Combine(temporaryDirectory, "cli");
        var projectDirectory = Path.Combine(temporaryDirectory, projectName);
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var environment = new Dictionary<string, string?>
            {
                ["DOTNET_CLI_HOME"] = cliHome,
                ["HOME"] = Path.Combine(temporaryDirectory, "home")
            };
            var templatePackage = Path.Combine(RepoRoot(), "artifacts", "Kestrel.Templates.0.1.0.nupkg");
            Assert.True(File.Exists(templatePackage), "Kestrel templates package must be packed before template validation.");

            var install = await RunProcessAsync("dotnet", ["new", "install", templatePackage], RepoRoot(), environment);
            Assert.Equal(0, install.ExitCode);

            var create = await RunProcessAsync("dotnet", ["new", template, "-n", projectName, "-o", projectDirectory], RepoRoot(), environment);
            Assert.Equal(0, create.ExitCode);

            var projectFile = Assert.Single(Directory.GetFiles(projectDirectory, "*.csproj"));
            var project = XDocument.Load(projectFile);
            Assert.Equal("Kestrel.Sdk/0.1.0", project.Root?.Attribute("Sdk")?.Value);
            Assert.DoesNotContain(project.Descendants().Attributes("Include").Select(attribute => attribute.Value),
                packageId => packageId.StartsWith("KSR.", StringComparison.Ordinal));
            Assert.All(project.Descendants("PackageReference"), reference =>
                Assert.StartsWith("Kestrel.", reference.Attribute("Include")?.Value));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task UnixInstaller_AliasLifecycleIsIdempotentAndDelegatesToKestrel()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "kestrel-installer-tests", Guid.NewGuid().ToString("N"));
        var fakeBin = Path.Combine(temporaryDirectory, "bin");
        var cliHome = Path.Combine(temporaryDirectory, "cli");
        Directory.CreateDirectory(fakeBin);

        try
        {
            var fakeDotnet = Path.Combine(fakeBin, "dotnet");
            await File.WriteAllTextAsync(fakeDotnet, """
                #!/usr/bin/env bash
                set -euo pipefail
                case "$1" in
                  --version) echo 10.0.400 ;;
                  pack|nuget|new) exit 0 ;;
                  tool)
                    if [[ "$2" == "install" ]]; then
                      printf '%s\n' '#!/usr/bin/env bash' 'printf "kestrel:%s\n" "$*"' > kestrel
                      chmod +x kestrel
                    fi
                    ;;
                esac
                """);
            var chmod = await RunProcessAsync("chmod", ["+x", fakeDotnet], temporaryDirectory, new Dictionary<string, string?>());
            Assert.Equal(0, chmod.ExitCode);

            var environment = new Dictionary<string, string?>
            {
                ["DOTNET_CLI_HOME"] = cliHome,
                ["HOME"] = Path.Combine(temporaryDirectory, "home"),
                ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
            };
            var installer = Path.Combine(RepoRoot(), "scripts", "install.sh");

            var firstInstall = await RunProcessAsync("bash", [installer, "--no-vscode"], RepoRoot(), environment);
            Assert.Equal(0, firstInstall.ExitCode);
            var secondInstall = await RunProcessAsync("bash", [installer, "--no-vscode"], RepoRoot(), environment);
            Assert.Equal(0, secondInstall.ExitCode);

            var toolsPath = Path.Combine(cliHome, ".dotnet", "tools");
            var alias = await RunProcessAsync(Path.Combine(toolsPath, "ksr"), ["alias-check"], temporaryDirectory, environment);
            Assert.Equal(0, alias.ExitCode);
            Assert.Equal("kestrel:alias-check", alias.Output.Trim());

            var firstUninstall = await RunProcessAsync("bash", [installer, "--uninstall", "--no-vscode"], RepoRoot(), environment);
            Assert.Equal(0, firstUninstall.ExitCode);
            var secondUninstall = await RunProcessAsync("bash", [installer, "--uninstall", "--no-vscode"], RepoRoot(), environment);
            Assert.Equal(0, secondUninstall.ExitCode);
            Assert.False(File.Exists(Path.Combine(toolsPath, "ksr")));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessHarness_TimesOutAndPreservesChildOutput()
    {
        var result = await RunProcessAsync(
            "bash",
            ["-c", "echo before-timeout; sleep 0.1"],
            RepoRoot(),
            new Dictionary<string, string?>(),
            timeout: TimeSpan.FromMilliseconds(20));

        Assert.Contains("before-timeout", result.Output);
        Assert.Contains("timed out", result.Output, StringComparison.OrdinalIgnoreCase);
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

        return await RunProcessAsync(startInfo, TimeSpan.FromMinutes(2));
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var (key, value) in environment)
            startInfo.Environment[key] = value;
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return await RunProcessAsync(startInfo, timeout ?? TimeSpan.FromMinutes(2));
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {startInfo.FileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var waitTask = process.WaitForExitAsync();
        var completed = await Task.WhenAny(waitTask, Task.Delay(timeout));
        var timedOut = completed != waitTask;
        if (timedOut)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync();
        }

        var output = await stdout + await stderr;
        if (timedOut)
        {
            var command = string.Join(' ', new[] { startInfo.FileName }.Concat(startInfo.ArgumentList));
            output += $"{Environment.NewLine}Process timed out after {timeout}: {command}";
        }

        return (timedOut ? -1 : process.ExitCode, output);
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
