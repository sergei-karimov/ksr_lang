using System.Diagnostics;

namespace KSR.Cli.Tests;

public sealed class CliTestHost : IAsyncDisposable
{
    private readonly string _temporaryDirectory;

    private CliTestHost(string temporaryDirectory, string sourcePath)
    {
        _temporaryDirectory = temporaryDirectory;
        SourcePath = sourcePath;
    }

    public string SourcePath { get; }

    public static async Task<CliTestHost> CreateAsync(string source)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "ksr-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        var sourcePath = Path.Combine(temporaryDirectory, "Program.ksr");
        await File.WriteAllTextAsync(sourcePath, source);

        return new CliTestHost(temporaryDirectory, sourcePath);
    }

    public async Task<CliProcessResult> RunAsync(string command, params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment["DOTNET_ROLL_FORWARD"] = "Major";

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine(RepositoryRoot(), "KSR.csproj"));
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(command);
        foreach (var argument in args)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the KSR CLI.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        return new CliProcessResult(process.ExitCode, await stdout, await stderr);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, recursive: true);

        return ValueTask.CompletedTask;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KSR.csproj")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing KSR.csproj.");
    }
}

public sealed record CliProcessResult(int ExitCode, string Stdout, string Stderr);
