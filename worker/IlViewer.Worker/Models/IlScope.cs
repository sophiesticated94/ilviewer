namespace IlViewer.Worker.Models;

public sealed record IlScope(
    string Id,
    string Kind,
    string DisplayName,
    string? AssemblyName,
    string? TypeName,
    string? MethodName,
    string? FullName,
    SourceRange? SourceRange,
    IReadOnlyList<IlMethodBlock> Methods,
    IReadOnlyList<IlInstruction> Instructions,
    IReadOnlyList<string> ActiveInstructionIds,
    IReadOnlyList<string> ActiveHighlightIds);
