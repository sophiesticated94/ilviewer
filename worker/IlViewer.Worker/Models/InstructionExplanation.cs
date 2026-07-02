namespace IlViewer.Worker.Models;

public sealed record InstructionExplanation(
    string Opcode,
    string Title,
    string Description,
    string OperandKind,
    string StackBehaviourPop,
    string StackBehaviourPush,
    string FlowControl);
