namespace IlViewer.Worker.Tests;

public sealed class SourceRegionAnalysisTests : WorkerTestBase
{
    [Fact]
    public async Task Analyze_CSharpSelection_ReturnsNestedRegionsAndHighlights()
    {
        using var project = await TemporaryDotnetProject.CreateAsync(
            "CSharpRegionsSample",
            "csproj",
            ProjectFiles.CSharpProject(),
            "Regions.cs",
            """
            using System;

            namespace Sample;

            public class Widget
            {
                public bool A { get; set; }
                public string B { get; set; } = "";
                public Func<int, int>? Map { get; set; }
            }

            public class Factory
            {
                public Widget Create()
                {
                    return new Widget { A = true, B = "abcd", Map = value => value + 1 };
                }
            }
            """);

        var range = project.FindRange("new Widget { A = true, B = \"abcd\", Map = value => value + 1 }");
        var result = Analyze(project, range);

        Assert.True(result.Success, result.Error);
        Assert.Contains(result.SourceRegions, region => region.Kind == "objectCreation");
        Assert.Contains(result.SourceRegions, region => region.Kind == "memberInitializer" && region.DisplayName.Contains("A = true", StringComparison.Ordinal));
        Assert.Contains(result.SourceRegions, region => region.Kind == "memberInitializer" && region.DisplayName.Contains("B = \"abcd\"", StringComparison.Ordinal));
        Assert.Contains(result.SourceRegions, region => region.Kind == "lambda");
        Assert.Contains(result.InstructionHighlights, highlight => highlight.Depth > 0 && highlight.InstructionIds.Count > 0);
        Assert.Contains(result.InstructionExplanations, explanation => explanation.Opcode == "newobj");
    }

    [Fact]
    public async Task Analyze_CSharpHoverLikeSelection_ReturnsInstructionIdsPresentInFunctionScope()
    {
        using var project = await TemporaryDotnetProject.CreateAsync(
            "CSharpHoverOverlaySample",
            "csproj",
            ProjectFiles.CSharpProject(),
            "Regions.cs",
            """
            namespace Sample;

            public class Widget
            {
                public bool A { get; set; }
            }

            public class Factory
            {
                public Widget Create()
                {
                    return new Widget { A = true };
                }
            }
            """);

        var result = Analyze(project, project.FindRange("A = true"));
        var functionScope = Assert.Single(result.Scopes.Where(scope => scope.Kind == "function"));
        var functionInstructionIds = functionScope.Instructions.Select(instruction => instruction.Id).ToHashSet(StringComparer.Ordinal);
        var selectedRegionId = Assert.IsType<string>(result.SelectedRegionId);
        var selectedHighlight = Assert.Single(result.InstructionHighlights.Where(highlight => highlight.RegionId == selectedRegionId));

        Assert.NotEmpty(selectedHighlight.InstructionIds);
        Assert.All(selectedHighlight.InstructionIds, instructionId => Assert.Contains(instructionId, functionInstructionIds));
    }

    [Fact]
    public async Task Analyze_VisualBasicSelection_ReturnsObjectInitializerAndLambdaRegions()
    {
        using var project = await TemporaryDotnetProject.CreateAsync(
            "VisualBasicRegionsSample",
            "vbproj",
            ProjectFiles.VisualBasicProject(),
            "Regions.vb",
            """
            Imports System

            Namespace Sample
                Public Class Widget
                    Public Property A As Boolean
                    Public Property B As String = ""
                    Public Property Map As Func(Of Integer, Integer)
                End Class

                Public Class Factory
                    Public Function Create() As Widget
                        Dim localMap As Func(Of Integer, Integer) = Function(value) value + 1
                        Return New Widget With {.A = True, .B = "abcd", .Map = localMap}
                    End Function
                End Class
            End Namespace
            """);

        var range = project.FindRange("New Widget With {.A = True, .B = \"abcd\", .Map = localMap}");
        var result = Analyze(project, range);

        Assert.True(result.Success, result.Error);
        Assert.Contains(result.SourceRegions, region => region.Kind == "objectCreation");
        Assert.Contains(result.SourceRegions, region => region.Kind == "memberInitializer");
        Assert.Contains(result.InstructionHighlights, highlight => highlight.InstructionIds.Count > 0);
    }
}
