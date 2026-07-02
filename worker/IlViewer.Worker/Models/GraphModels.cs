namespace IlViewer.Worker.Models;

public sealed record GraphRequest(
    string ProjectPath,
    string Configuration,
    string? TargetFramework,
    string? NodeId,
    int PageSize,
    string? ContinuationToken);

public sealed record GraphNode(
    string Id,
    string Kind,
    string Label,
    string AssemblyName,
    string AssemblyKind,
    string? AssemblyPath = null,
    string? TypeName = null,
    string? MethodName = null,
    string? MetadataToken = null,
    string? Signature = null,
    SourceRange? SourceRange = null,
    bool HasChildren = false,
    bool DecompileAvailable = false,
    bool IsExternal = false);

public sealed record GraphEdge(
    string From,
    string To,
    string Kind,
    string Label);

public sealed record GraphExpandResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? RootAssembly { get; init; }
    public IReadOnlyList<GraphNode> Nodes { get; init; } = [];
    public IReadOnlyList<GraphEdge> Edges { get; init; } = [];
    public string? ContinuationToken { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    public static GraphExpandResult Failure(string error)
    {
        return new GraphExpandResult
        {
            Success = false,
            Error = error
        };
    }
}

public sealed record DecompileRequest(
    string ProjectPath,
    string Configuration,
    string? TargetFramework,
    string? AssemblyPath,
    string? AssemblyName,
    string? TypeName,
    string? MethodName,
    string? MetadataToken,
    string? Language);

public sealed record DecompileResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string Language { get; init; } = "csharp";
    public string Title { get; init; } = "Dekompilacja";
    public string Content { get; init; } = string.Empty;
    public bool SourceAvailable { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    public static DecompileResult Failure(string error)
    {
        return new DecompileResult
        {
            Success = false,
            Error = error
        };
    }
}

public static class GraphNodeKinds
{
    public const string Assembly = "assembly";
    public const string Type = "type";
    public const string Method = "method";
    public const string External = "external";
}

public static class GraphEdgeKinds
{
    public const string Contains = "contains";
    public const string Call = "call";
    public const string Field = "field";
    public const string Type = "type";
    public const string Branch = "branch";
    public const string Override = "override";
    public const string InterfaceImplementation = "interfaceImpl";
}
