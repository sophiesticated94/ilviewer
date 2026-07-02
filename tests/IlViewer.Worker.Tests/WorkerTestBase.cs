using IlViewer.Worker.Analysis;
using IlViewer.Worker.Artifacts;
using IlViewer.Worker.Models;

namespace IlViewer.Worker.Tests;

public abstract class WorkerTestBase
{
    protected readonly ProjectArtifactResolver ArtifactResolver = new();
    protected readonly AssemblyIlAnalyzer Analyzer = CreateAnalyzer();

    protected AnalysisResult Analyze(TemporaryDotnetProject project, string lineText)
    {
        var line = project.FindLine(lineText);
        var request = new AnalysisRequest(
            project.ProjectPath,
            project.SourcePath,
            line,
            line,
            "Debug",
            "net8.0");
        var artifacts = ArtifactResolver.Resolve(request);

        return Analyzer.Analyze(request, artifacts);
    }

    protected AnalysisResult Analyze(TemporaryDotnetProject project, SourceSelection selection)
    {
        var request = new AnalysisRequest(
            project.ProjectPath,
            project.SourcePath,
            selection.StartLine,
            selection.EndLine,
            "Debug",
            "net8.0",
            selection.StartColumn,
            selection.EndColumn);
        var artifacts = ArtifactResolver.Resolve(request);

        return Analyzer.Analyze(request, artifacts);
    }

    private static AssemblyIlAnalyzer CreateAnalyzer()
    {
        var catalog = new InstructionCatalog();
        var instructionFactory = new IlInstructionFactory(catalog);
        return new AssemblyIlAnalyzer(
            new SourceRegionAnalyzer(),
            catalog,
            new IlScopeBuilder(instructionFactory),
            new InstructionHighlightBuilder());
    }
}
