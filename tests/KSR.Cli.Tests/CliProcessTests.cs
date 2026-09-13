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

    [Fact]
    public async Task HelpUsesKestrelBrand()
    {
        var result = await CliTestHost.RunWithoutArgumentsAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Kestrel", result.Stdout);
        Assert.DoesNotContain("KSR —", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleFileDebugAndSemanticErrorOutputsAreUserFacing()
    {
        await using var host = await CliTestHost.CreateAsync("fun main() { unknownName() }");

        var error = await host.RunAsync(host.SourcePath);
        Assert.NotEqual(0, error.ExitCode);
        Assert.Contains("Analysis failed:", error.Stderr);
        Assert.Contains(host.SourcePath, error.Stderr);

        await using var valid = await CliTestHost.CreateAsync("fun main() { println(\"ok\") }");
        var debug = await valid.RunAsync(valid.SourcePath, "--debug");
        Assert.Equal(0, debug.ExitCode);
        Assert.Contains("Generated C#", debug.Stderr);
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
