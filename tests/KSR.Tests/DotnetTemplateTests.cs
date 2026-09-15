using System.Text.Json;
using System.Xml.Linq;
using KSR.Analysis;
using KSR.AST;
using KSR.Build;
using KSR.Diagnostics;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;

namespace KSR.Tests;

public class DotnetTemplateTests
{
    [Fact]
    public void BuildTaskLogsSemanticErrorsAndDoesNotWriteGeneratedOutput()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.WriteFile("Program.ksr", "fun main() { unknownName() }");
        var output = Path.Combine(directory.Path, "generated", "Program.g.cs");
        var engine = new RecordingBuildEngine();
        var task = new KsrCompileTask
        {
            BuildEngine = engine,
            KsrCompile = [new TaskItem(input)],
            OutputFile = output
        };

        var succeeded = task.Execute();

        Assert.False(succeeded);
        var error = Assert.Single(engine.Errors);
        Assert.Equal(input, error.File);
        Assert.True(error.LineNumber > 0);
        Assert.True(error.ColumnNumber > 0);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void BuildTaskAllowsCrossFileFunctionReferences()
    {
        using var directory = new TemporaryDirectory();
        var helper = directory.WriteFile("Helpers.ksr", "fun helper() { }");
        var program = directory.WriteFile("Program.ksr", "fun main() { helper() }");
        var output = Path.Combine(directory.Path, "generated", "Program.g.cs");
        var task = new KsrCompileTask
        {
            BuildEngine = new RecordingBuildEngine(),
            KsrCompile = [new TaskItem(helper), new TaskItem(program)],
            OutputFile = output
        };

        var succeeded = task.Execute();

        Assert.True(succeeded);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void BuildTaskReportsSecondFileErrorsFromTheirSourceFile()
    {
        using var directory = new TemporaryDirectory();
        var helper = directory.WriteFile("Helpers.ksr", "fun helper() { }");
        var program = directory.WriteFile("Program.ksr", "fun main() { unknownName() }");
        var engine = new RecordingBuildEngine();
        var task = new KsrCompileTask
        {
            BuildEngine = engine,
            KsrCompile = [new TaskItem(helper), new TaskItem(program)],
            OutputFile = Path.Combine(directory.Path, "generated", "Program.g.cs")
        };

        var succeeded = task.Execute();

        Assert.False(succeeded);
        var error = Assert.Single(engine.Errors);
        Assert.Equal(program, error.File);
    }

    [Fact]
    public void BuildTaskDeletesPreviousOutputAfterSourceBecomesInvalid()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.WriteFile("Program.ksr", "fun main() { }");
        var output = Path.Combine(directory.Path, "generated", "Program.g.cs");
        var task = new KsrCompileTask
        {
            BuildEngine = new RecordingBuildEngine(),
            KsrCompile = [new TaskItem(input)],
            OutputFile = output
        };

        Assert.True(task.Execute());
        Assert.True(File.Exists(output));

        File.WriteAllText(input, "fun main() { unknownName() }");

        Assert.False(task.Execute());
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void BuildTaskLogsWarningsWithLocationAndStillWritesOutput()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.WriteFile("Program.ksr", "fun main() { }");
        var output = Path.Combine(directory.Path, "generated", "Program.g.cs");
        var engine = new RecordingBuildEngine();
        var task = new WarningKsrCompileTask(input)
        {
            BuildEngine = engine,
            KsrCompile = [new TaskItem(input)],
            OutputFile = output
        };

        var succeeded = task.Execute();

        Assert.True(succeeded);
        var warning = Assert.Single(engine.Warnings);
        Assert.Equal(input, warning.File);
        Assert.Equal(3, warning.LineNumber);
        Assert.Equal(7, warning.ColumnNumber);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void BuildTaskWritesGeneratedOutputForValidSource()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.WriteFile("Program.ksr", "fun main() { }");
        var output = Path.Combine(directory.Path, "generated", "Program.g.cs");
        var task = new KsrCompileTask
        {
            BuildEngine = new RecordingBuildEngine(),
            KsrCompile = [new TaskItem(input)],
            OutputFile = output
        };

        var succeeded = task.Execute();

        Assert.True(succeeded);
        Assert.True(File.Exists(output));
        Assert.Contains("auto-generated", File.ReadAllText(output));
    }

    [Fact]
    public void BuildTaskLogsUseKestrelBrand()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.WriteFile("Program.ksr", "fun main() { }");
        var engine = new RecordingBuildEngine();
        var task = new KsrCompileTask
        {
            BuildEngine = engine,
            KsrCompile = [new TaskItem(input)],
            OutputFile = Path.Combine(directory.Path, "generated", "Program.g.cs")
        };

        Assert.True(task.Execute());
        Assert.Contains(engine.Messages, message => message.Message?.Contains("Kestrel:", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(engine.Messages, message => message.Message?.Contains("KSR:", StringComparison.Ordinal) == true);
    }
    [Theory]
    [InlineData("ksr-console", "kestrel-console", "ksr-console", "Kestrel Console Application", "Kestrel.Console")]
    [InlineData("ksr-library", "kestrel-lib", "ksr-lib", "Kestrel Class Library", "Kestrel.Library")]
    [InlineData("ksr-creative", "kestrel-creative", "ksr-creative", "Kestrel Creative Application", "Kestrel.CreativeApp")]
    public void ProjectTemplates_HaveExpectedMetadata(string directory, string shortName, string alias, string name, string identity)
    {
        var template = LoadTemplate(directory);

        Assert.Equal(new[] { shortName, alias }, template.RootElement.GetProperty("shortName").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(identity, template.RootElement.GetProperty("identity").GetString());
        Assert.Equal("Kestrel", template.RootElement.GetProperty("tags").GetProperty("language").GetString());
        Assert.Equal(name, template.RootElement.GetProperty("name").GetString());
        Assert.Equal("project", template.RootElement.GetProperty("tags").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("ksr-console", "MyApp.csproj")]
    [InlineData("ksr-library", "MyLibrary.csproj")]
    [InlineData("ksr-creative", "MyCreativeApp.csproj")]
    public void ProjectTemplateContent_DefaultsToNet10ForCanonicalAndAliasNames(string directory, string projectFile)
    {
        var projectPath = Path.Combine(TemplatesRoot(), directory, projectFile);
        var project = XDocument.Load(projectPath);
        var template = LoadTemplate(directory).RootElement;

        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        var framework = template.GetProperty("symbols").GetProperty("Framework");
        Assert.Equal("net10.0", framework.GetProperty("defaultValue").GetString());
        Assert.Equal("net10.0", framework.GetProperty("choices")[0].GetProperty("choice").GetString());
    }

    [Theory]
    [InlineData("ksr-console", "MyApp.csproj")]
    [InlineData("ksr-creative", "MyCreativeApp.csproj")]
    public void ProjectTemplates_ExposeAotSymbolDefaultingToDisabled(string directory, string projectFile)
    {
        var template = LoadTemplate(directory).RootElement;
        var aot = template.GetProperty("symbols").GetProperty("Aot");

        Assert.Equal("bool", aot.GetProperty("datatype").GetString());
        Assert.Equal("false", aot.GetProperty("defaultValue").GetString());

        var projectPath = Path.Combine(TemplatesRoot(), directory, projectFile);
        var projectSource = File.ReadAllText(projectPath);
        Assert.Contains("<!--#if (Aot) -->", projectSource, StringComparison.Ordinal);
        Assert.Contains("<PublishAot>true</PublishAot>", projectSource, StringComparison.Ordinal);
        Assert.Contains("<!--#endif -->", projectSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectTemplates_LibraryHasNoAotSymbol()
    {
        var template = LoadTemplate("ksr-library").RootElement;

        Assert.False(template.GetProperty("symbols").TryGetProperty("Aot", out _));
    }

    [Fact]
    public void PublicPackageMetadata_UsesKestrelNamesAndKeepsInternalAssemblies()
    {
        var expectedPackages = new Dictionary<string, string>
        {
            ["KSR.csproj"] = "Kestrel",
            ["KSR.Core.csproj"] = "Kestrel.Core",
            ["sdk/KSR.Build/KSR.Build.csproj"] = "Kestrel.Build",
            ["sdk/KSR.Sdk/KSR.Sdk.csproj"] = "Kestrel.Sdk",
            ["sdk/KSR.StdLib/KSR.StdLib.csproj"] = "Kestrel.StdLib",
            ["sdk/KSR.Creative/KSR.Creative.csproj"] = "Kestrel.Creative",
            ["sdk/KSR.Templates/KSR.Templates.csproj"] = "Kestrel.Templates"
        };

        foreach (var (relativePath, packageId) in expectedPackages)
        {
            var project = XDocument.Load(Path.Combine(RepoRoot(), relativePath));
            Assert.Equal(packageId, project.Descendants("PackageId").Single().Value);
        }

        var cli = File.ReadAllText(Path.Combine(RepoRoot(), "KSR.csproj"));
        Assert.Contains("<AssemblyName>kestrel</AssemblyName>", cli);
        Assert.Contains("<ToolCommandName>kestrel</ToolCommandName>", cli);
        Assert.Contains("ksr", cli);

        var core = File.ReadAllText(Path.Combine(RepoRoot(), "KSR.Core.csproj"));
        Assert.Contains("<AssemblyName>KSR.Core</AssemblyName>", core);
        Assert.Contains("<RootNamespace>KSR</RootNamespace>", core);
    }

    [Fact]
    public void ProjectTargetMatrix_UsesNet10ForAllProjects()
    {
        var ordinaryProjects = new[]
        {
            "KSR.csproj",
            "KSR.Core.csproj",
            "sdk/KSR.Build/KSR.Build.csproj",
            "sdk/KSR.Sdk/KSR.Sdk.csproj",
            "sdk/KSR.StdLib/KSR.StdLib.csproj",
            "sdk/KSR.Creative/KSR.Creative.csproj",
            "sdk/KSR.Templates/KSR.Templates.csproj",
            "tests/KSR.Tests/KSR.Tests.csproj",
            "tests/KSR.Cli.Tests/KSR.Cli.Tests.csproj"
        };

        foreach (var relativePath in ordinaryProjects)
            AssertProjectTargetFramework(relativePath, "net10.0");
    }

    [Fact]
    public void KestrelSdk_DefaultTargetFrameworkIsNet10()
    {
        var sdkProps = File.ReadAllText(Path.Combine(RepoRoot(), "sdk/KSR.Sdk/Sdk/Sdk.props"));

        Assert.Contains("<TargetFramework Condition=\"'$(TargetFramework)' == ''\">net10.0</TargetFramework>", sdkProps);
    }

    [Fact]
    public void SdkAndBuildMetadata_UseCanonicalPackageIdsWithoutRenamingTasks()
    {
        var sdkProps = File.ReadAllText(Path.Combine(RepoRoot(), "sdk/KSR.Sdk/Sdk/Sdk.props"));
        Assert.Contains("PackageReference Include=\"Kestrel.Build\"", sdkProps);
        Assert.Contains("PackageReference Include=\"Kestrel.StdLib\"", sdkProps);

        var buildProject = File.ReadAllText(Path.Combine(RepoRoot(), "sdk/KSR.Build/KSR.Build.csproj"));
        Assert.Contains("Kestrel.Build.props", buildProject);
        Assert.Contains("Kestrel.Build.targets", buildProject);
        Assert.Contains("<AssemblyName>KSR.Build</AssemblyName>", buildProject);

        var buildTargets = File.ReadAllText(Path.Combine(RepoRoot(), "sdk/KSR.Build/build/Kestrel.Build.targets"));
        Assert.Contains("TaskName=\"KSR.Build.KsrCompileTask\"", buildTargets);
    }

    [Fact]
    public void RootNuGetConfig_MapsRootAndDottedKestrelPackagesToLocalArtifacts()
    {
        var config = XDocument.Load(Path.Combine(RepoRoot(), "nuget.config"));
        var localSource = config.Descendants("packageSource")
            .Single(element => (string?)element.Attribute("key") == "local-artifacts");
        var patterns = localSource.Elements("package")
            .Select(element => (string?)element.Attribute("pattern"))
            .ToArray();

        Assert.Contains("Kestrel", patterns);
        Assert.Contains("Kestrel.*", patterns);
    }

    [Theory]
    [InlineData("ksr-creative", "MyCreativeApp.csproj", "Kestrel.Creative")]
    public void CreativeTemplates_ReferenceRuntimePackages(string directory, string projectFile, string packageName)
    {
        var projectPath = Path.Combine(TemplatesRoot(), directory, projectFile);
        var projectText = File.ReadAllText(projectPath);

        Assert.Contains($"PackageReference Include=\"{packageName}\"", projectText);
    }

    [Theory]
    [InlineData("ksr-creative")]
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

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "KSR.sln")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate KSR.sln.");
    }

    private static void AssertProjectTargetFramework(string relativePath, string expectedTargetFramework)
    {
        var project = XDocument.Load(Path.Combine(RepoRoot(), relativePath));
        var targetFramework = project.Descendants("TargetFramework").SingleOrDefault()?.Value
            ?? project.Descendants("TargetFrameworks").SingleOrDefault()?.Value;

        Assert.Equal(expectedTargetFramework, targetFramework);
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public List<BuildWarningEventArgs> Warnings { get; } = [];
        public List<BuildMessageEventArgs> Messages { get; } = [];
        public int ColumnNumberOfTaskNode => 1;
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 1;
        public string ProjectFileOfTaskNode => "KSR.Tests";

        public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => false;
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);
        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e);
        public void LogCustomEvent(CustomBuildEventArgs e) { }
    }

    private sealed class WarningKsrCompileTask(string sourceFile) : KsrCompileTask
    {
        protected override KsrAnalysisResult AnalyzeSources(IReadOnlyList<KsrSource> sources) =>
            new(new ProgramNode([]), [
                new KsrDiagnostic("warning", sourceFile, 3, 7, DiagnosticSeverity.Warning)
            ]);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ksr-build-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string WriteFile(string name, string contents)
        {
            var file = System.IO.Path.Combine(Path, name);
            File.WriteAllText(file, contents);
            return file;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
