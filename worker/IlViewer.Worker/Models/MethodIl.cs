namespace IlViewer.Worker.Models;

public sealed record MethodIl(
    string TypeName,
    string MethodName,
    string FullName,
    SourceRange SourceRange,
    IReadOnlyList<IlInstruction> Instructions,
    IReadOnlyList<int> ActiveInstructionOffsets);
