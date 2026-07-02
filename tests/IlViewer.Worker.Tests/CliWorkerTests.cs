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
}
