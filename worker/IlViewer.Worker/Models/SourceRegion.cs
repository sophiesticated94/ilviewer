namespace IlViewer.Worker.Models;

public sealed record SourceRegion(
    string Id,
    string Kind,
    int Depth,
    SourceRange SourceRange,
    string? ParentId,
    string DisplayName,
    bool IsSelected,
    bool IsExact,
    string Language);
