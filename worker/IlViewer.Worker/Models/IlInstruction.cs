namespace IlViewer.Worker.Models;

public sealed record IlInstruction(
    string Id,
    int Offset,
    string OffsetLabel,
    string Text,
    bool IsActive,
    SourceRange? SourceRange,
    string Opcode = "",
    string? Operand = null,
    string? OperandKind = null,
    string? OperandDisplay = null,
    string? ResolvedSignature = null,
    string? StackBehaviourPop = null,
    string? StackBehaviourPush = null,
    string? FlowControl = null,
    string? Description = null,
    string? Tooltip = null);
