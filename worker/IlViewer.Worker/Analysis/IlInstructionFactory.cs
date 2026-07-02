using IlViewer.Worker.Models;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace IlViewer.Worker.Analysis;

public sealed class IlInstructionFactory
{
    private readonly IInstructionCatalog _instructionCatalog;
    private readonly InstructionNavigationTargetFactory _navigationTargetFactory;

    public IlInstructionFactory(IInstructionCatalog instructionCatalog, InstructionNavigationTargetFactory? navigationTargetFactory = null)
    {
        _instructionCatalog = instructionCatalog;
        _navigationTargetFactory = navigationTargetFactory ?? new InstructionNavigationTargetFactory();
    }

    public IlInstruction Create(
        MethodCandidate candidate,
        Instruction instruction,
        IReadOnlyCollection<InstructionRange> activeRanges)
    {
        var sequencePoint = FindSequencePointForInstruction(candidate.VisibleSequencePoints, instruction.Offset);
        var sourceRange = sequencePoint is not null ? ToSourceRange(sequencePoint) : null;
        var operandDisplay = FormatOperand(instruction.Operand);
        var resolvedSignature = ResolveSignature(instruction.Operand);

        return new IlInstruction(
            BuildInstructionId(candidate, instruction),
            instruction.Offset,
            $"IL_{instruction.Offset:x4}",
            instruction.ToString(),
            activeRanges.Any(range => range.Contains(instruction.Offset)),
            sourceRange,
            instruction.OpCode.Name,
            instruction.Operand?.ToString(),
            instruction.OpCode.OperandType.ToString(),
            operandDisplay,
            resolvedSignature,
            instruction.OpCode.StackBehaviourPop.ToString(),
            instruction.OpCode.StackBehaviourPush.ToString(),
            instruction.OpCode.FlowControl.ToString(),
            _instructionCatalog.Describe(instruction.OpCode),
            _instructionCatalog.BuildTooltip(instruction, operandDisplay, resolvedSignature),
            _navigationTargetFactory.Build(candidate, instruction));
    }

    public static string BuildMethodId(MethodCandidate candidate)
    {
        return $"{candidate.AssemblyName}:{candidate.Method.MetadataToken.RID:x8}";
    }

    private static string BuildInstructionId(MethodCandidate candidate, Instruction instruction)
    {
        return $"{BuildMethodId(candidate)}:{instruction.Offset:x4}";
    }

    private static SequencePoint? FindSequencePointForInstruction(IReadOnlyList<SequencePoint> sequencePoints, int offset)
    {
        SequencePoint? current = null;

        foreach (var sequencePoint in sequencePoints.OrderBy(sequencePoint => sequencePoint.Offset))
        {
            if (sequencePoint.Offset > offset)
            {
                break;
            }

            current = sequencePoint;
        }

        return current;
    }

    private static SourceRange ToSourceRange(SequencePoint sequencePoint)
    {
        return new SourceRange(
            sequencePoint.StartLine,
            sequencePoint.EndLine >= sequencePoint.StartLine ? sequencePoint.EndLine : sequencePoint.StartLine,
            Math.Max(sequencePoint.StartColumn, 1),
            Math.Max(sequencePoint.EndColumn, 1));
    }

    private static string? FormatOperand(object? operand)
    {
        return operand switch
        {
            null => null,
            Instruction instruction => instruction.ToString(),
            Instruction[] instructions => string.Join(", ", instructions.Select(instruction => instruction.ToString())),
            VariableDefinition variable => $"{variable.VariableType.FullName} V_{variable.Index}",
            ParameterDefinition parameter => $"{parameter.ParameterType.FullName} {parameter.Name}",
            MethodReference method => method.FullName,
            FieldReference field => field.FullName,
            TypeReference type => type.FullName,
            string value => $"\"{value}\"",
            _ => operand.ToString()
        };
    }

    private static string? ResolveSignature(object? operand)
    {
        return operand switch
        {
            MethodReference method => method.FullName,
            FieldReference field => field.FullName,
            TypeReference type => type.FullName,
            _ => null
        };
    }
}
