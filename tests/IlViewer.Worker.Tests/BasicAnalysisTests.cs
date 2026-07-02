using IlViewer.Worker.Models;

namespace IlViewer.Worker.Tests;

public sealed class BasicAnalysisTests : WorkerTestBase
{
    [Fact]
    public async Task Analyze_CSharpLine_ReturnsFragmentAndMethodContext()
    {
        using var project = await TemporaryDotnetProject.CreateAsync(
            "CSharpSample",
            "csproj",
            ProjectFiles.CSharpProject(),
            "Calculator.cs",
            """
            namespace Sample;

            public class Calculator
            {
                public int Add(int value)
                {
                    var doubled = value * 2;

                    return doubled + 1;
                }
            }
            """);

        var result = Analyze(project, "return doubled + 1;");

        Assert.True(result.Success, result.Error);
        Assert.False(result.IsApproximate);
        Assert.NotNull(result.Fragment);
        Assert.NotNull(result.Context);
        Assert.NotEmpty(result.Fragment!.Instructions);
        Assert.True(result.Context!.Instructions.Count > result.Fragment.Instructions.Count);
        Assert.All(result.Fragment.Instructions, instruction => Assert.True(instruction.IsActive));
        Assert.Contains(result.Context.Instructions, instruction => instruction.IsActive);
        Assert.Contains(result.Scopes, scope => scope.Kind == "fragment");
        Assert.Contains(result.Scopes, scope => scope.Kind == "function");
        Assert.Contains(result.Scopes, scope => scope.Kind == "class");
        Assert.Contains(result.Scopes, scope => scope.Kind == "project");
        Assert.Contains(result.Scopes, scope => scope.Kind == "application");
        Assert.NotEmpty(result.InstructionExplanations);
    }

    [Fact]
    public async Task Analyze_CSharpLineWithoutSequencePoint_UsesNearestGeneratedIl()
    {
        using var project = await TemporaryDotnetProject.CreateAsync(
            "CSharpApproximateSample",
            "csproj",
            ProjectFiles.CSharpProject(),
            "Calculator.cs",
            """
            namespace Sample;

            public class Calculator
            {
                public int Add(int value)
                {
                    var doubled = value * 2;

                    return doubled + 1;
                }
            }
            """);

        var line = project.FindBlankLineAfter("var doubled = value * 2;");
        var request = new AnalysisRequest(project.ProjectPath, project.SourcePath, line, line, "Debug", "net8.0");
        var artifacts = ArtifactResolver.Resolve(request);
        var result = Analyzer.Analyze(request, artifacts);

        Assert.True(result.Success, result.Error);
        Assert.True(result.IsApproximate);
        Assert.NotEmpty(result.Fragment!.Instructions);
    }

    [Fact]
    public async Task Analyze_CSharpAsyncIteratorAndLambda_ReturnsGeneratedIl()
    {
        using var project = await TemporaryDotnetProject.CreateAsync(
            "CSharpAdvancedSample",
            "csproj",
            ProjectFiles.CSharpProject(),
            "Advanced.cs",
            """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;

            namespace Sample;

            public class Advanced
            {
                public async Task<int> ComputeAsync(int value)
                {
                    await Task.Yield();
                    return Enumerable.Range(0, value).Select(item => item + 1).Sum();
                }

                public IEnumerable<int> Count(int max)
                {
                    for (var index = 0; index < max; index++)
                    {
                        yield return index;
                    }
                }
            }
            """);

        var asyncResult = Analyze(project, "return Enumerable.Range");
        var iteratorResult = Analyze(project, "yield return index;");

        Assert.True(asyncResult.Success, asyncResult.Error);
        Assert.NotEmpty(asyncResult.Fragment!.Instructions);
        Assert.True(iteratorResult.Success, iteratorResult.Error);
        Assert.NotEmpty(iteratorResult.Fragment!.Instructions);
    }

    [Fact]
    public async Task Analyze_FSharpLine_ReturnsGeneratedIl()
    {
        using var project = await TemporaryDotnetProject.CreateAsync(
            "FSharpSample",
            "fsproj",
            ProjectFiles.FSharpProject(),
            "Library.fs",
            """
            namespace Sample

            module Math =
                let add value =
                    let doubled = value * 2
                    doubled + 1
            """);

        var result = Analyze(project, "doubled + 1");

        Assert.True(result.Success, result.Error);
        Assert.NotEmpty(result.Fragment!.Instructions);
        Assert.Contains(result.Context!.Instructions, instruction => instruction.IsActive);
    }

    [Fact]
    public async Task Analyze_VisualBasicLine_ReturnsGeneratedIl()
    {
        using var project = await TemporaryDotnetProject.CreateAsync(
            "VisualBasicSample",
            "vbproj",
            ProjectFiles.VisualBasicProject(),
            "Calculator.vb",
            """
            Namespace Sample
                Public Class Calculator
                    Public Function Add(value As Integer) As Integer
                        Dim doubled = value * 2
                        Return doubled + 1
                    End Function
                End Class
            End Namespace
            """);

        var result = Analyze(project, "Return doubled + 1");

        Assert.True(result.Success, result.Error);
        Assert.NotEmpty(result.Fragment!.Instructions);
        Assert.Contains(result.Context!.Instructions, instruction => instruction.IsActive);
    }
}
