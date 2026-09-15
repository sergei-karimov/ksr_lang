using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
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
    public void UnixInstaller_RequiresDotnet10OrLater()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts/install.sh"));

        Assert.Contains("if [[ $MAJOR -lt 10 ]]; then", script, StringComparison.Ordinal);
        Assert.Contains(".NET 10 or later is required", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnixInstaller_RejectsDotnet9BeforePackaging()
    {
        // This behavioral check is Unix-only; Windows coverage remains source-level below.
        if (OperatingSystem.IsWindows())
            return;

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "kestrel-installer-prerequisite-tests", Guid.NewGuid().ToString("N"));
        var fakeBin = Path.Combine(temporaryDirectory, "bin");
        var cliHome = Path.Combine(temporaryDirectory, "cli");
        var packagingMarker = Path.Combine(temporaryDirectory, "pack-invoked");
        Directory.CreateDirectory(fakeBin);

        try
        {
            var fakeDotnet = Path.Combine(fakeBin, "dotnet");
            await File.WriteAllTextAsync(fakeDotnet, """
                #!/usr/bin/env bash
                set -euo pipefail
                case "${1:-}" in
                  --version) echo 9.0.999 ;;
                  pack) touch "$FAKE_DOTNET_PACK_MARKER" ;;
                  *) exit 0 ;;
                esac
                """);

            var chmod = await RunProcessAsync(
                "chmod",
                ["+x", fakeDotnet],
                temporaryDirectory,
                new Dictionary<string, string?>());
            Assert.Equal(0, chmod.ExitCode);

            var environment = new Dictionary<string, string?>
            {
                ["DOTNET_CLI_HOME"] = cliHome,
                ["HOME"] = Path.Combine(temporaryDirectory, "home"),
                ["FAKE_DOTNET_PACK_MARKER"] = packagingMarker,
                ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
            };
            var installer = Path.Combine(RepoRoot(), "scripts", "install.sh");

            var result = await RunProcessAsync(
                "bash",
                [installer, "--no-vscode"],
                RepoRoot(),
                environment);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(".NET 10 or later is required", result.Output, StringComparison.Ordinal);
            Assert.False(File.Exists(packagingMarker), result.Output);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
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
    public void WindowsInstaller_RequiresDotnet10OrLater()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts/install.ps1"));

        Assert.Contains("if ($major -lt 10)", script, StringComparison.Ordinal);
        Assert.Contains(".NET 10 or later is required", script, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalPackageVerifier_CoversCanonicalPackagesAndBothTemplateNames()
    {
        var verifier = File.ReadAllText(Path.Combine(RepoRoot(), "tests", "verify-kestrel-local-packages.sh"));

        foreach (var package in ExpectedPackages)
            Assert.Contains($"{package}", verifier, StringComparison.Ordinal);

        Assert.Contains("kestrel-console", verifier, StringComparison.Ordinal);
        Assert.Contains("ksr-console", verifier, StringComparison.Ordinal);
        Assert.Contains("net10.0", verifier, StringComparison.Ordinal);
        Assert.Contains("KSR.", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("nuget.org", verifier, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--no-update-check", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalPackageVerifier_RunsCanonicalAndLegacyTemplateMatrix()
    {
        if (OperatingSystem.IsWindows())
            return;

        var verifier = Path.Combine(RepoRoot(), "tests", "verify-kestrel-local-packages.sh");
        var result = await RunProcessAsync(
            "bash",
            [verifier],
            RepoRoot(),
            new Dictionary<string, string?>
            {
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
            },
            timeout: TimeSpan.FromMinutes(3));

        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public async Task PackedTool_RuntimeConfigTargetsNet10()
    {
        var artifactDirectory = Path.Combine(Path.GetTempPath(), "kestrel-tool-runtimeconfig-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifactDirectory);

        try
        {
            var result = await RunDotnetAsync(
                "pack",
                "KSR.csproj",
                "-c",
                "Release",
                "-o",
                artifactDirectory,
                "--nologo",
                "--no-restore",
                "--disable-build-servers");

            Assert.Equal(0, result.ExitCode);

            var packagePath = Path.Combine(artifactDirectory, "Kestrel.0.1.0.nupkg");
            Assert.True(File.Exists(packagePath), result.Output);

            using var archive = ZipFile.OpenRead(packagePath);
            var runtimeConfig = archive.GetEntry("tools/net10.0/any/kestrel.runtimeconfig.json");
            Assert.NotNull(runtimeConfig);

            using var document = JsonDocument.Parse(runtimeConfig!.Open());
            var runtimeOptions = document.RootElement.GetProperty("runtimeOptions");
            Assert.Equal("net10.0", runtimeOptions.GetProperty("tfm").GetString());

            var frameworkVersion = runtimeOptions
                .GetProperty("framework")
                .GetProperty("version")
                .GetString();

            Assert.StartsWith("10.", frameworkVersion, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(artifactDirectory, recursive: true);
        }
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
            Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
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

    [Theory]
    [InlineData("kestrel-console", "KestrelAotSmokeApp")]
    [InlineData("kestrel-creative", "KestrelAotCreativeApp")]
    public async Task TemplateAliases_SupportAotPublishSwitch(string template, string projectName)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "kestrel-template-aot-tests", Guid.NewGuid().ToString("N"));
        var cliHome = Path.Combine(temporaryDirectory, "cli");
        var defaultDirectory = Path.Combine(temporaryDirectory, projectName + "Default");
        var aotDirectory = Path.Combine(temporaryDirectory, projectName + "Aot");
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

            var createDefault = await RunProcessAsync(
                "dotnet", ["new", template, "-n", projectName, "-o", defaultDirectory], RepoRoot(), environment);
            Assert.Equal(0, createDefault.ExitCode);

            var createAot = await RunProcessAsync(
                "dotnet", ["new", template, "-n", projectName, "-o", aotDirectory, "--Aot"], RepoRoot(), environment);
            Assert.Equal(0, createAot.ExitCode);

            var defaultProjectFile = Assert.Single(Directory.GetFiles(defaultDirectory, "*.csproj"));
            var defaultProject = File.ReadAllText(defaultProjectFile);
            Assert.DoesNotContain("PublishAot", defaultProject, StringComparison.Ordinal);

            var aotProjectFile = Assert.Single(Directory.GetFiles(aotDirectory, "*.csproj"));
            var aotProject = XDocument.Load(aotProjectFile);
            Assert.Equal("true", aotProject.Descendants("PublishAot").Single().Value);
            Assert.Equal("net10.0", aotProject.Descendants("TargetFramework").Single().Value);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task UnixInstaller_AliasLifecycleIsIdempotentAndDelegatesToKestrel()
    {
        // This behavioral check is Unix-only; Windows coverage remains source-level above
        // (WindowsInstaller_InstallsCanonicalArtifactsAndManagesKsrAliases). scripts/install.sh's
        // path handling assumes a POSIX shell and doesn't behave correctly under Git Bash on Windows.
        if (OperatingSystem.IsWindows())
            return;

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
        "Kestrel.Templates.0.1.0.nupkg"
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
