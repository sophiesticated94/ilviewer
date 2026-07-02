using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IlViewer.Worker.Analysis;
using IlViewer.Worker.Models;
using Xunit;

namespace IlViewer.Worker.Tests;

public sealed class PerformanceTests : WorkerTestBase
{
    [Fact]
    public async Task Analyze_PerformanceAndCacheValidation_SpeedsUpSubsequentRequests()
    {
        using var project = await TemporaryDotnetProject.CreateAsync(
            "PerformanceSample",
            "csproj",
            ProjectFiles.CSharpProject(),
            "WidgetFactory.cs",
            """
            using System;

            namespace Sample;

            public class Widget
            {
                public bool Enabled { get; set; }
                public string Name { get; set; } = "";
                public Func<int, int> Worker { get; set; } = x => x;
            }

            public class WidgetFactory
            {
                public Widget CreateWidget()
                {
                    var nameVal = "Dynamic";
                    return new Widget
                    {
                        Enabled = true,
                        Name = nameVal,
                        Worker = input => 
                        {
                            var intermediate = input * 2;
                            return intermediate + 1;
                        }
                    };
                }
            }
            """);

        // 1. Cold run
        var request1 = new AnalysisRequest(
            project.ProjectPath,
            project.SourcePath,
            20, // return new Widget
            20,
            "Debug",
            "net8.0");

        var stopwatch = Stopwatch.StartNew();
        var artifacts1 = ArtifactResolver.Resolve(request1);
        var result1 = Analyzer.Analyze(request1, artifacts1);
        stopwatch.Stop();
        var coldTime = stopwatch.ElapsedMilliseconds;

        Assert.True(result1.Success, result1.Error);
        Assert.NotNull(result1.Fragment);

        // 2. Hot run (exact same location)
        stopwatch.Restart();
        var artifacts2 = ArtifactResolver.Resolve(request1);
        var result2 = Analyzer.Analyze(request1, artifacts2);
        stopwatch.Stop();
        var hotTimeSameLocation = stopwatch.ElapsedMilliseconds;

        Assert.True(result2.Success, result2.Error);
        // Verify it was a cache hit and is much faster
        Assert.True(hotTimeSameLocation < coldTime, $"Hot run ({hotTimeSameLocation}ms) should be faster than cold run ({coldTime}ms)");

        // 3. Hot run (different location in the same method - e.g. inside the lambda)
        var request3 = new AnalysisRequest(
            project.ProjectPath,
            project.SourcePath,
            24, // var intermediate = input * 2;
            24,
            "Debug",
            "net8.0");

        stopwatch.Restart();
        var artifacts3 = ArtifactResolver.Resolve(request3);
        var result3 = Analyzer.Analyze(request3, artifacts3);
        stopwatch.Stop();
        var hotTimeDifferentLocation = stopwatch.ElapsedMilliseconds;

        Assert.True(result3.Success, result3.Error);
        Assert.Contains(result3.SourceRegions, r => r.Kind == "lambda");
        Assert.True(hotTimeDifferentLocation < coldTime, $"Hot run different location ({hotTimeDifferentLocation}ms) should be faster than cold run ({coldTime}ms)");

        // 4. Cache Invalidation (change write time of assembly)
        var artifacts = ArtifactResolver.Resolve(request1);
        var assemblyPath = artifacts.AssemblyPath;
        File.SetLastWriteTimeUtc(assemblyPath, DateTime.UtcNow.AddMinutes(1));

        stopwatch.Restart();
        var artifacts4 = ArtifactResolver.Resolve(request1);
        var result4 = Analyzer.Analyze(request1, artifacts4);
        stopwatch.Stop();
        var invalidationTime = stopwatch.ElapsedMilliseconds;

        Assert.True(result4.Success, result4.Error);
        // Invalidation should trigger a cold run, which takes longer than hot runs
        Assert.True(invalidationTime > hotTimeSameLocation, $"Invalidation run ({invalidationTime}ms) should take longer than hot run ({hotTimeSameLocation}ms)");
    }
}
