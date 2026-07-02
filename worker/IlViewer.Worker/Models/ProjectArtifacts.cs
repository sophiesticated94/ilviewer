namespace IlViewer.Worker.Models;

public sealed record ProjectArtifacts(
    string ProjectPath,
    string AssemblyPath,
    string PdbPath,
    string TargetFramework,
    string Configuration,
    DateTime AssemblyLastWriteTimeUtc,
    DateTime PdbLastWriteTimeUtc)
{
    public IReadOnlyList<AssemblyArtifact> ApplicationAssemblies { get; init; } = [];
}

public sealed record AssemblyArtifact(
    string ProjectPath,
    string AssemblyPath,
    string PdbPath,
    string TargetFramework,
    string Configuration,
    DateTime AssemblyLastWriteTimeUtc,
    DateTime PdbLastWriteTimeUtc,
    bool IsRoot);
