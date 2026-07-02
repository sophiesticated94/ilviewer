namespace IlViewer.Worker.Models;

public sealed record AnalysisResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }
    public string? ProjectPath { get; init; }
    public string? AssemblyPath { get; init; }
    public string? PdbPath { get; init; }
    public string? TargetFramework { get; init; }
    public string? Configuration { get; init; }
    public DateTime? AssemblyLastWriteTimeUtc { get; init; }
    public DateTime? PdbLastWriteTimeUtc { get; init; }
    public string? DocumentPath { get; init; }
    public int? Line { get; init; }
    public int? EndLine { get; init; }
    public bool IsApproximate { get; init; }
    public MethodIl? Fragment { get; init; }
    public MethodIl? Context { get; init; }
    public IReadOnlyList<IlScope> Scopes { get; init; } = [];
    public IReadOnlyList<SourceRegion> SourceRegions { get; init; } = [];
    public IReadOnlyList<InstructionHighlight> InstructionHighlights { get; init; } = [];
    public IReadOnlyList<InstructionExplanation> InstructionExplanations { get; init; } = [];
    public string? SelectedRegionId { get; init; }

    public static AnalysisResult Failure(string error)
    {
        return new AnalysisResult
        {
            Success = false,
            Error = error
        };
    }

    public static AnalysisResult SuccessResult(
        ProjectArtifacts artifacts,
        string documentPath,
        int line,
        int endLine,
        MethodIl fragment,
        MethodIl context,
        IReadOnlyList<IlScope> scopes,
        IReadOnlyList<SourceRegion> sourceRegions,
        IReadOnlyList<InstructionHighlight> instructionHighlights,
        IReadOnlyList<InstructionExplanation> instructionExplanations,
        string? selectedRegionId,
        bool isApproximate,
        string? message)
    {
        return new AnalysisResult
        {
            Success = true,
            ProjectPath = artifacts.ProjectPath,
            AssemblyPath = artifacts.AssemblyPath,
            PdbPath = artifacts.PdbPath,
            TargetFramework = artifacts.TargetFramework,
            Configuration = artifacts.Configuration,
            AssemblyLastWriteTimeUtc = artifacts.AssemblyLastWriteTimeUtc,
            PdbLastWriteTimeUtc = artifacts.PdbLastWriteTimeUtc,
            DocumentPath = documentPath,
            Line = line,
            EndLine = endLine,
            Fragment = fragment,
            Context = context,
            Scopes = scopes,
            SourceRegions = sourceRegions,
            InstructionHighlights = instructionHighlights,
            InstructionExplanations = instructionExplanations,
            SelectedRegionId = selectedRegionId,
            IsApproximate = isApproximate,
            Message = message
        };
    }

    public AnalysisResult WithArtifacts(ProjectArtifacts artifacts)
    {
        return this with
        {
            ProjectPath = artifacts.ProjectPath,
            AssemblyPath = artifacts.AssemblyPath,
            PdbPath = artifacts.PdbPath,
            TargetFramework = artifacts.TargetFramework,
            Configuration = artifacts.Configuration,
            AssemblyLastWriteTimeUtc = artifacts.AssemblyLastWriteTimeUtc,
            PdbLastWriteTimeUtc = artifacts.PdbLastWriteTimeUtc
        };
    }
}
