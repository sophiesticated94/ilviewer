using System.Text.Json;
using IlViewer.Worker.Cli;

namespace IlViewer.Worker.Tests;

public sealed class CliWorkerTests : WorkerTestBase
{
    [Fact]
    public async Task CliAnalyze_WritesSuccessfulJsonResult()
    {
        using var project = await TemporaryDotnetProject.CreateAsync(
            "CliSample",
            "csproj",
            ProjectFiles.CSharpProject(),
            "Calculator.cs",
            """
            namespace Sample;

            public class Calculator
            {
                public int Add(int value)
                {
                    return value + 1;
                }
            }
            """);
        var line = project.FindLine("return value + 1;");
        var cli = new WorkerCli(ArtifactResolver, Analyzer);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await cli.RunAsync(
            [
                "analyze",
                "--project",
                project.ProjectPath,
                "--document",
                project.SourcePath,
                "--line",
                line.ToString(),
                "--configuration",
                "Debug",
                "--target-framework",
                "net8.0"
            ],
            stdout,
            stderr);

        using var json = JsonDocument.Parse(stdout.ToString());
        Assert.Equal(0, exitCode);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.True(json.RootElement.GetProperty("fragment").GetProperty("instructions").GetArrayLength() > 0);
    }

    [Fact]
    public async Task CliGraphRootAndDecompile_WriteSuccessfulJsonResults()
    {
        using var project = await TemporaryDotnetProject.CreateWithProjectReferenceAsync();
        var cli = new WorkerCli(ArtifactResolver, Analyzer);

        using var graphStdout = new StringWriter();
        var graphExitCode = await cli.RunAsync(
            [
                "graph-root",
                "--project",
                project.ProjectPath,
                "--configuration",
                "Debug",
                "--target-framework",
                "net8.0"
            ],
            graphStdout,
            TextWriter.Null);

        using var graphJson = JsonDocument.Parse(graphStdout.ToString());
        Assert.Equal(0, graphExitCode);
        Assert.True(graphJson.RootElement.GetProperty("success").GetBoolean());
        Assert.True(graphJson.RootElement.GetProperty("nodes").GetArrayLength() > 0);

        using var decompileStdout = new StringWriter();
        var decompileExitCode = await cli.RunAsync(
            [
                "decompile",
                "--project",
                project.ProjectPath,
                "--configuration",
                "Debug",
                "--target-framework",
                "net8.0",
                "--assembly-name",
                "RootApplication",
                "--type-name",
                "RootApplication.Entry",
                "--language",
                "csharp"
            ],
            decompileStdout,
            TextWriter.Null);

        using var decompileJson = JsonDocument.Parse(decompileStdout.ToString());
        Assert.Equal(0, decompileExitCode);
        Assert.True(decompileJson.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("class Entry", decompileJson.RootElement.GetProperty("content").GetString(), StringComparison.Ordinal);
    }
}
