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
    [Theory]
    [InlineData("ksr-console", "ksr-console", "KSR Console Application")]
    [InlineData("ksr-creative", "ksr-creative", "KSR Creative Application")]
    [InlineData("ksr-creative-camera", "ksr-creative-camera", "KSR Creative Camera Application")]
    public void ProjectTemplates_HaveExpectedMetadata(string directory, string shortName, string name)
    {
        var template = LoadTemplate(directory);

        Assert.Equal(shortName, template.RootElement.GetProperty("shortName").GetString());
        Assert.Equal(name, template.RootElement.GetProperty("name").GetString());
        Assert.Equal("project", template.RootElement.GetProperty("tags").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("ksr-creative", "MyCreativeApp.csproj", "KSR.Creative")]
    [InlineData("ksr-creative-camera", "MyCameraApp.csproj", "KSR.Creative")]
    [InlineData("ksr-creative-camera", "MyCameraApp.csproj", "KSR.Vision")]
    public void CreativeTemplates_ReferenceRuntimePackages(string directory, string projectFile, string packageName)
    {
        var projectPath = Path.Combine(TemplatesRoot(), directory, projectFile);
        var projectText = File.ReadAllText(projectPath);

        Assert.Contains($"PackageReference Include=\"{packageName}\"", projectText);
    }

    [Theory]
    [InlineData("ksr-creative")]
    [InlineData("ksr-creative-camera")]
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
    [InlineData("ksr-creative-camera")]
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

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public List<BuildWarningEventArgs> Warnings { get; } = [];
        public int ColumnNumberOfTaskNode => 1;
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 1;
        public string ProjectFileOfTaskNode => "KSR.Tests";

        public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => false;
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);
        public void LogMessageEvent(BuildMessageEventArgs e) { }
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
