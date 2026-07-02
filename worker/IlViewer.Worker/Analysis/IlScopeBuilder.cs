using IlViewer.Worker.Models;
using Mono.Cecil;

namespace IlViewer.Worker.Analysis;

public sealed class IlScopeBuilder
{
    private readonly IlInstructionFactory _instructionFactory;

    public IlScopeBuilder(IlInstructionFactory instructionFactory)
    {
        _instructionFactory = instructionFactory;
    }

    public IReadOnlyList<IlScope> BuildScopes(
        IReadOnlyList<MethodCandidate> candidates,
        MethodCandidate activeCandidate,
        IlMethodBlock activeMethodBlock,
        IReadOnlyList<IlInstruction> activeInstructions,
        IReadOnlyList<InstructionHighlight> highlights)
    {
        var activeInstructionIds = activeInstructions.Select(instruction => instruction.Id).ToArray();
        var activeHighlightIds = highlights.Select(highlight => highlight.Id).ToArray();
        var fragmentBlock = activeMethodBlock with
        {
            Instructions = activeInstructions,
            ContainsActiveInstruction = true
        };

        var functionScope = BuildScope("function", "Funkcja", activeCandidate, [activeMethodBlock], activeInstructionIds, activeHighlightIds);
        var classScope = BuildScope(
            "class",
            "Klasa",
            activeCandidate,
            candidates
                .Where(candidate => ReferenceEquals(candidate.Method.DeclaringType, activeCandidate.Method.DeclaringType))
                .Select(candidate => ReferenceEquals(candidate, activeCandidate) ? activeMethodBlock : BuildMethodBlock(candidate, []))
                .ToList(),
            activeInstructionIds,
            activeHighlightIds);

        var rootType = GetRootDeclaringType(activeCandidate.Method.DeclaringType);
        var typeScope = BuildScope(
            "typeWithNested",
            "Typ + zagnieżdżone",
            activeCandidate,
            candidates
                .Where(candidate => ReferenceEquals(GetRootDeclaringType(candidate.Method.DeclaringType), rootType))
                .Select(candidate => ReferenceEquals(candidate, activeCandidate) ? activeMethodBlock : BuildMethodBlock(candidate, []))
                .ToList(),
            activeInstructionIds,
            activeHighlightIds);

        var projectScope = BuildScope(
            "project",
            "Projekt",
            activeCandidate,
            candidates
                .Where(candidate => candidate.Artifact.IsRoot)
                .Select(candidate => ReferenceEquals(candidate, activeCandidate) ? activeMethodBlock : BuildMethodBlock(candidate, []))
                .ToList(),
            activeInstructionIds,
            activeHighlightIds);

        var applicationScope = BuildScope(
            "application",
            "Aplikacja",
            activeCandidate,
            candidates
                .Select(candidate => ReferenceEquals(candidate, activeCandidate) ? activeMethodBlock : BuildMethodBlock(candidate, []))
                .ToList(),
            activeInstructionIds,
            activeHighlightIds);

        var fragmentScope = BuildScope("fragment", "Fragment", activeCandidate, [fragmentBlock], activeInstructionIds, activeHighlightIds);

        return [fragmentScope, functionScope, classScope, typeScope, projectScope, applicationScope];
    }

    public IlMethodBlock BuildMethodBlock(MethodCandidate candidate, IReadOnlyCollection<InstructionRange> activeRanges)
    {
        var instructions = candidate.Method.Body.Instructions
            .Select(instruction => _instructionFactory.Create(candidate, instruction, activeRanges))
            .ToList();

        return new IlMethodBlock(
            IlInstructionFactory.BuildMethodId(candidate),
            candidate.AssemblyName,
            candidate.Method.DeclaringType.FullName,
            candidate.Method.Name,
            candidate.Method.FullName,
            candidate.SourceRange,
            instructions,
            instructions.Any(instruction => instruction.IsActive));
    }

    private static IlScope BuildScope(
        string kind,
        string displayName,
        MethodCandidate activeCandidate,
        IReadOnlyList<IlMethodBlock> methods,
        IReadOnlyList<string> activeInstructionIds,
        IReadOnlyList<string> activeHighlightIds)
    {
        var instructions = methods.SelectMany(method => method.Instructions).ToList();
        return new IlScope(
            kind,
            kind,
            displayName,
            kind is "project" or "application" ? null : activeCandidate.AssemblyName,
            kind is "project" or "application" ? null : activeCandidate.Method.DeclaringType.FullName,
            kind is "fragment" or "function" ? activeCandidate.Method.Name : null,
            kind is "fragment" or "function" ? activeCandidate.Method.FullName : displayName,
            Union(methods.Select(method => method.SourceRange)),
            methods,
            instructions,
            activeInstructionIds,
            activeHighlightIds);
    }

    private static TypeDefinition GetRootDeclaringType(TypeDefinition type)
    {
        var current = type;
        while (current.DeclaringType is not null)
        {
            current = current.DeclaringType;
        }

        return current;
    }

    private static SourceRange? Union(IEnumerable<SourceRange?> ranges)
    {
        var nonNull = ranges.Where(range => range is not null).Cast<SourceRange>().ToList();
        if (nonNull.Count == 0)
        {
            return null;
        }

        var startLine = nonNull.Min(range => range.StartLine);
        var endLine = nonNull.Max(range => range.EndLine);
        var startColumn = nonNull.Where(range => range.StartLine == startLine).Min(range => range.StartColumn);
        var endColumn = nonNull.Where(range => range.EndLine == endLine).Max(range => range.EndColumn);
        return new SourceRange(startLine, endLine, startColumn, endColumn);
    }
}
