using IlViewer.Worker.Models;
using Mono.Cecil.Cil;

namespace IlViewer.Worker.Analysis;

public sealed class InstructionCatalog : IInstructionCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["nop"] = "Brak operacji; często używane jako punkt mapowania debuggera.",
        ["ldarg"] = "Ładuje argument metody na stos.",
        ["ldarg.0"] = "Ładuje pierwszy argument metody na stos.",
        ["ldarg.1"] = "Ładuje drugi argument metody na stos.",
        ["ldloc"] = "Ładuje zmienną lokalną na stos.",
        ["ldloc.0"] = "Ładuje pierwszą zmienną lokalną na stos.",
        ["stloc"] = "Zdejmuje wartość ze stosu i zapisuje ją do zmiennej lokalnej.",
        ["ldc.i4"] = "Ładuje stałą liczbę całkowitą int32 na stos.",
        ["ldstr"] = "Ładuje referencję do stałego stringa na stos.",
        ["newobj"] = "Tworzy nowy obiekt przez wywołanie konstruktora.",
        ["call"] = "Wywołuje statycznie wskazaną metodę.",
        ["callvirt"] = "Wywołuje metodę wirtualnie i zwykle sprawdza null dla instancji.",
        ["ret"] = "Kończy metodę i opcjonalnie zwraca wartość ze stosu.",
        ["br"] = "Bezwarunkowo przechodzi do innej instrukcji.",
        ["brtrue"] = "Przechodzi, gdy wartość na stosie jest prawdziwa lub niezerowa.",
        ["brfalse"] = "Przechodzi, gdy wartość na stosie jest fałszywa, zerowa lub null.",
        ["ldfld"] = "Ładuje wartość pola instancji na stos.",
        ["stfld"] = "Zapisuje wartość do pola instancji.",
        ["ldsfld"] = "Ładuje wartość pola statycznego na stos.",
        ["stsfld"] = "Zapisuje wartość do pola statycznego.",
        ["box"] = "Opakowuje typ wartościowy w obiekt.",
        ["castclass"] = "Rzutuje referencję na wskazany typ.",
        ["newarr"] = "Tworzy nową tablicę elementów wskazanego typu.",
        ["ldtoken"] = "Ładuje token metadanych typu, metody lub pola.",
        ["pop"] = "Usuwa wartość ze szczytu stosu.",
        ["dup"] = "Duplikuje wartość ze szczytu stosu."
    };

    public InstructionExplanation Explain(OpCode opcode)
    {
        return new InstructionExplanation(
            opcode.Name,
            opcode.Name,
            Describe(opcode),
            opcode.OperandType.ToString(),
            opcode.StackBehaviourPop.ToString(),
            opcode.StackBehaviourPush.ToString(),
            opcode.FlowControl.ToString());
    }

    public string BuildTooltip(Instruction instruction, string? operandDisplay, string? resolvedSignature)
    {
        var opcode = instruction.OpCode;
        var lines = new List<string>
        {
            $"Opcode: {opcode.Name}",
            $"Opis: {Describe(opcode)}",
            $"Operand: {opcode.OperandType}" + (string.IsNullOrWhiteSpace(operandDisplay) ? string.Empty : $" = {operandDisplay}"),
            $"Stos pop: {opcode.StackBehaviourPop}",
            $"Stos push: {opcode.StackBehaviourPush}",
            $"Przepływ: {opcode.FlowControl}"
        };

        if (!string.IsNullOrWhiteSpace(resolvedSignature))
        {
            lines.Add($"Sygnatura: {resolvedSignature}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public string Describe(OpCode opcode)
    {
        if (Descriptions.TryGetValue(opcode.Name, out var description))
        {
            return description;
        }

        var baseName = opcode.Name.Split('.')[0];
        return Descriptions.TryGetValue(baseName, out var baseDescription)
            ? baseDescription
            : "Instrukcja IL zdefiniowana przez CLI; szczegóły zależą od opcode i operandu.";
    }
}
