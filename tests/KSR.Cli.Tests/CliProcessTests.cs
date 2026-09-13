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

    [Theory]
    [InlineData("hello.ksr")]
    [InlineData("async_demo.ksr")]
    [InlineData("generic_interfaces.ksr")]
    [InlineData("sealed_demo.ksr")]
    public async Task SupportedExamplesCompileAndRun(string example)
    {
        var result = await CliTestHost.RunFileAsync(Path.Combine("examples", example));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("error:", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }
}
