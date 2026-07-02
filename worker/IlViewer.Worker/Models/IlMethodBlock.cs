namespace IlViewer.Worker.Models;

public sealed record IlMethodBlock(
    string Id,
    string AssemblyName,
    string TypeName,
    string MethodName,
    string FullName,
    SourceRange? SourceRange,
    IReadOnlyList<IlInstruction> Instructions,
    bool ContainsActiveInstruction);
