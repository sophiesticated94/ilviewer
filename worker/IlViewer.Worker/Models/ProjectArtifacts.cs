namespace IlViewer.Worker.Models;

public sealed record ProjectArtifacts(
    string ProjectPath,
    string AssemblyPath,
    string? PdbPath,
    string TargetFramework,
    string Configuration,
    DateTime AssemblyLastWriteTimeUtc,
    DateTime? PdbLastWriteTimeUtc)
{
    public IReadOnlyList<AssemblyArtifact> ApplicationAssemblies { get; init; } = [];
    public IReadOnlyList<AssemblyArtifact> ReferenceAssemblies { get; init; } = [];
    public IReadOnlyList<string> AssemblySearchDirectories { get; init; } = [];
}

public sealed record AssemblyArtifact(
    string ProjectPath,
    string AssemblyPath,
    string? PdbPath,
    string TargetFramework,
    string Configuration,
    DateTime AssemblyLastWriteTimeUtc,
    DateTime? PdbLastWriteTimeUtc,
    bool IsRoot)
{
    public string AssemblyName { get; init; } = Path.GetFileNameWithoutExtension(AssemblyPath);
    public string AssemblyKind { get; init; } = IsRoot ? AssemblyKinds.Project : AssemblyKinds.ProjectReference;
    public string? PackageName { get; init; }
    public string? PackageVersion { get; init; }
}

public static class AssemblyKinds
{
    public const string Project = "project";
    public const string ProjectReference = "projectReference";
    public const string Nuget = "nuget";
    public const string Framework = "framework";
    public const string Runtime = "runtime";
    public const string ExternalUnknown = "externalUnknown";
}
