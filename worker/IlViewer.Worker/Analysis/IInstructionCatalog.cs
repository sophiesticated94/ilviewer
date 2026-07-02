using IlViewer.Worker.Models;
using Mono.Cecil.Cil;

namespace IlViewer.Worker.Analysis;

public interface IInstructionCatalog
{
    InstructionExplanation Explain(OpCode opcode);
    string BuildTooltip(Instruction instruction, string? operandDisplay, string? resolvedSignature);
    string Describe(OpCode opcode);
}
