namespace IlViewer.Worker.Models;

public sealed record IlNavigationTarget(
    string Id,
    string Kind,
    string Label,
    string? AssemblyName = null,
    string? AssemblyPath = null,
    string? AssemblyKind = null,
    string? TypeName = null,
    string? MethodName = null,
    string? Signature = null,
    string? MetadataToken = null,
    int? IlOffset = null,
    string? TargetInstructionId = null,
    string? SourcePath = null,
    SourceRange? SourceRange = null,
    string? Language = null,
    bool IsExternal = false,
    bool DecompileAvailable = false);

public static class NavigationTargetKinds
{
    public const string Source = "source";
    public const string Il = "il";
    public const string Decompiled = "decompiled";
    public const string GraphNode = "graphNode";
}
