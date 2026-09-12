using Xunit;

namespace KSR.Cli.Tests;

public class CliProcessTests
{
    [Fact]
    public async Task CheckReturnsNoDiagnosticsForValidSource()
    {
        await using var host = await CliTestHost.CreateAsync("fun main() { }");

        var result = await host.RunAsync("check", host.SourcePath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("[]", result.Stdout.Trim());
    }

    [Fact]
    public async Task CheckReturnsJsonDiagnosticsForSyntaxErrors()
    {
        await using var host = await CliTestHost.CreateAsync("fun main( { }");

        var result = await host.RunAsync("check", host.SourcePath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckReturnsJsonDiagnosticsForSemanticErrors()
    {
        await using var host = await CliTestHost.CreateAsync(
            "fun main() { unknownName() }");

        var result = await host.RunAsync("check", host.SourcePath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("unknown", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }
}
