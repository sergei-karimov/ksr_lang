using KSR.Analysis;
using KSR.Diagnostics;
using Xunit;

namespace KSR.Tests;

public class ExampleCompatibilityTests
{
    // Baseline at Task 3 commit 2b2441a. Empty entries classify expected-valid
    // examples; nonempty entries pin the existing semantic limitations exactly.
    private static readonly Dictionary<string, string[]> ExpectedDiagnostics = new()
    {
        ["async_demo.ksr"] = [],
        ["camera_demo.ksr"] = [],
        ["generic_interfaces.ksr"] = [],
        ["hello.ksr"] = [],
        ["lifecycle_demo.ksr"] = [],
        ["stdlib_demo.ksr"] = [],
        ["collections_demo.ksr"] = [
            "30:5 Type mismatch: cannot assign 'Map<Any, Any>' to 'Map<String, Int>'",
            "41:5 Type mismatch: cannot assign 'List<Int>' to 'MutableList<Int>'"],
        ["game_of_life.ksr"] = [
            "141:5 Condition must be Bool, but found 'Any'",
            "144:9 Condition must be Bool, but found 'Any'",
            "147:9 Condition must be Bool, but found 'Any'",
            "151:9 Condition must be Bool, but found 'Any'",
            "157:9 Condition must be Bool, but found 'Any'"],
        ["generic_funs.ksr"] = ["24:5 Type mismatch: cannot assign 'T' to 'U'"],
        ["raylib_demo.ksr"] = ["13:5 Condition must be Bool, but found 'Any'"],
        ["sealed_demo.ksr"] = [],
        ["text_processing.ksr"] = [],
    };

    public static IEnumerable<object[]> Examples => ExpectedDiagnostics.Keys.Select(file => new object[] { file });

    [Theory]
    [MemberData(nameof(Examples))]
    public void CheckedInExampleMatchesItsSemanticBaseline(string example)
    {
        var path = Path.Combine(ExamplesDirectory(), example);
        var result = KsrAnalyzer.Analyze(File.ReadAllText(path), path);
        Assert.NotNull(result.Program);
        Assert.All(result.Diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(path, diagnostic.SourceFile);
        });
        Assert.Equal(ExpectedDiagnostics[example], result.Diagnostics
            .Select(diagnostic => $"{diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}"));
    }

    [Fact]
    public void EveryCheckedInExampleHasAnExplicitCompatibilityClassification()
    {
        Assert.Equal(ExpectedDiagnostics.Keys.Order(), Directory.GetFiles(ExamplesDirectory(), "*.ksr")
            .Select(Path.GetFileName).Order());
    }

    private static string ExamplesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "KSR.sln")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName
            ?? throw new DirectoryNotFoundException("Cannot locate repository examples."), "examples");
    }
}
