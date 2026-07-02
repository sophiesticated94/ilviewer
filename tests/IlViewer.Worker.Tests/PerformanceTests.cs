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

        var request1 = new AnalysisRequest(
            project.ProjectPath,
            project.SourcePath,
            20, // return new Widget
            20,
            "Debug",
            "net8.0");

        // Warm-up to JIT compile Mono.Cecil and System.Text.Json
        {
            var warmupArtifacts = ArtifactResolver.Resolve(request1);
            _ = Analyzer.Analyze(request1, warmupArtifacts);
            _ = Analyzer.Analyze(request1, warmupArtifacts);
        }

        // 1. Cold run (triggered by changing write time of assembly to invalidate warm-up cache)
        var artifacts1 = ArtifactResolver.Resolve(request1);
        var assemblyPath = artifacts1.AssemblyPath;
        File.SetLastWriteTimeUtc(assemblyPath, DateTime.UtcNow.AddMinutes(1));

        var artifactsCold = ArtifactResolver.Resolve(request1);
        var stopwatch = Stopwatch.StartNew();
        var result1 = Analyzer.Analyze(request1, artifactsCold);
        stopwatch.Stop();
        var coldTime = stopwatch.ElapsedMilliseconds;

        Assert.True(result1.Success, result1.Error);
        Assert.False(result1.IsFromCache, "Cold run should not be from cache.");
        Assert.NotNull(result1.Fragment);

        // 2. Hot run (exact same location) - Run 5 iterations to take the minimum time (bypassing JIT/AV filter locks)
        var artifacts2 = ArtifactResolver.Resolve(request1);
        long hotTimeSameLocation = long.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            stopwatch.Restart();
            var result2 = Analyzer.Analyze(request1, artifacts2);
            stopwatch.Stop();
            hotTimeSameLocation = Math.Min(hotTimeSameLocation, stopwatch.ElapsedMilliseconds);
            Assert.True(result2.IsFromCache, "Hot run same location should be a cache hit.");
        }

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

        var artifacts3 = ArtifactResolver.Resolve(request3);
        long hotTimeDifferentLocation = long.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            stopwatch.Restart();
            var result3 = Analyzer.Analyze(request3, artifacts3);
            stopwatch.Stop();
            hotTimeDifferentLocation = Math.Min(hotTimeDifferentLocation, stopwatch.ElapsedMilliseconds);
            Assert.True(result3.IsFromCache, "Hot run different location in same method should be a cache hit.");
            Assert.Contains(result3.SourceRegions, r => r.Kind == "lambda");
        }

        Assert.True(hotTimeDifferentLocation < coldTime, $"Hot run different location ({hotTimeDifferentLocation}ms) should be faster than cold run ({coldTime}ms)");

        // 4. Cache Invalidation (change write time of assembly)
        File.SetLastWriteTimeUtc(assemblyPath, DateTime.UtcNow.AddMinutes(2));

        var artifacts4 = ArtifactResolver.Resolve(request1);
        stopwatch.Restart();
        var result4 = Analyzer.Analyze(request1, artifacts4);
        stopwatch.Stop();
        var invalidationTime = stopwatch.ElapsedMilliseconds;

        Assert.True(result4.Success, result4.Error);
        Assert.False(result4.IsFromCache, "Invalidation run should be a cache miss.");
        Assert.True(invalidationTime > hotTimeSameLocation, $"Invalidation run ({invalidationTime}ms) should take longer than hot run ({hotTimeSameLocation}ms)");
    }

    [Fact]
    public async Task Analyze_ComplexClassPerformance_LineNavigation_IsInstant()
    {
        using var project = await TemporaryDotnetProject.CreateAsync(
            "ComplexPerformanceSample",
            "csproj",
            ProjectFiles.CSharpProject(),
            "ComplexWorkerCli.cs",
            """
            using System;
            using System.IO;
            using System.Threading.Tasks;

            namespace Sample;

            public class ComplexWorkerCli
            {
                public async Task<int> RunCommandAsync(string[] args, TextWriter stdout, TextWriter stderr)
                {
                    if (args.Length == 0)
                    {
                        await stderr.WriteLineAsync("No arguments provided.");
                        return 1;
                    }

                    try
                    {
                        var command = args[0].ToLowerInvariant();
                        switch (command)
                        {
                            case "analyze":
                                var line = int.Parse(args[1]);
                                var code = args[2];
                                Func<string, bool> validator = s => s.Length > 0;
                                if (validator(code))
                                {
                                    await stdout.WriteLineAsync($"Analyzing line {line} code: {code}");
                                    return 0;
                                }
                                return -1;
                            case "compile":
                                await stdout.WriteLineAsync("Compiling...");
                                return 0;
                            default:
                                return 2;
                        }
                    }
                    catch (Exception ex)
                    {
                        await stderr.WriteLineAsync(ex.Message);
                        return 99;
                    }
                }
            }
            """);

        var lineSwitch = project.FindLine("switch (command)");
        var lineAnalyzeCase = project.FindLine("case \"analyze\":");
        var lineValidator = project.FindLine("Func<string, bool> validator");

        var request1 = new AnalysisRequest(
            project.ProjectPath,
            project.SourcePath,
            lineSwitch,
            lineSwitch,
            "Debug",
            "net8.0");

        // Warm-up
        {
            var warmupArtifacts = ArtifactResolver.Resolve(request1);
            _ = Analyzer.Analyze(request1, warmupArtifacts);
            _ = Analyzer.Analyze(request1, warmupArtifacts);
        }

        // 1. Cold run (invalidate cache first)
        var artifacts1 = ArtifactResolver.Resolve(request1);
        var assemblyPath = artifacts1.AssemblyPath;
        File.SetLastWriteTimeUtc(assemblyPath, DateTime.UtcNow.AddMinutes(1));

        var artifactsCold = ArtifactResolver.Resolve(request1);
        var stopwatch = Stopwatch.StartNew();
        var result1 = Analyzer.Analyze(request1, artifactsCold);
        stopwatch.Stop();
        var coldTime = stopwatch.ElapsedMilliseconds;

        Assert.True(result1.Success, result1.Error);
        Assert.False(result1.IsFromCache, "Complex cold run should not be from cache.");

        // 2. Hot run (line navigation) to lineAnalyzeCase - Run 5 iterations to take the minimum time
        var request2 = new AnalysisRequest(
            project.ProjectPath,
            project.SourcePath,
            lineAnalyzeCase,
            lineAnalyzeCase,
            "Debug",
            "net8.0");

        var artifacts2 = ArtifactResolver.Resolve(request2);
        long hotTimeCase = long.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            stopwatch.Restart();
            var result2 = Analyzer.Analyze(request2, artifacts2);
            stopwatch.Stop();
            hotTimeCase = Math.Min(hotTimeCase, stopwatch.ElapsedMilliseconds);
            Assert.True(result2.IsFromCache, "Line change 1 should be a cache hit.");
        }

        Assert.True(hotTimeCase < coldTime, $"Line change 1 ({hotTimeCase}ms) should be faster than cold run ({coldTime}ms)");

        // 3. Hot run (line navigation) to lineValidator
        var request3 = new AnalysisRequest(
            project.ProjectPath,
            project.SourcePath,
            lineValidator,
            lineValidator,
            "Debug",
            "net8.0");

        var artifacts3 = ArtifactResolver.Resolve(request3);
        long hotTimeValidator = long.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            stopwatch.Restart();
            var result3 = Analyzer.Analyze(request3, artifacts3);
            stopwatch.Stop();
            hotTimeValidator = Math.Min(hotTimeValidator, stopwatch.ElapsedMilliseconds);
            Assert.True(result3.IsFromCache, "Line change 2 should be a cache hit.");
        }

        Assert.True(hotTimeValidator < coldTime, $"Line change 2 ({hotTimeValidator}ms) should be faster than cold run ({coldTime}ms)");

        // Log times to stdout
        Console.WriteLine($"[PerfTest] Cold run: {coldTime}ms");
        Console.WriteLine($"[PerfTest] Hot run (Line 1 - switch case): {hotTimeCase}ms");
        Console.WriteLine($"[PerfTest] Hot run (Line 2 - validator/lambda): {hotTimeValidator}ms");
    }
}
