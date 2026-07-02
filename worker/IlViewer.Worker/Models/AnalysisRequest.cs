namespace IlViewer.Worker.Models;

public sealed record AnalysisRequest(
    string ProjectPath,
    string DocumentPath,
    int Line,
    int EndLine,
    string Configuration,
    string? TargetFramework,
    int StartColumn = 1,
    int EndColumn = 1);
