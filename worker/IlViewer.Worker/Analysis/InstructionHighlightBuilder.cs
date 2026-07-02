using IlViewer.Worker.Models;

namespace IlViewer.Worker.Analysis;

public sealed class InstructionHighlightBuilder
{
    public IReadOnlyList<InstructionHighlight> Build(
        IReadOnlyList<SourceRegion> sourceRegions,
        IlMethodBlock activeMethodBlock,
        IReadOnlyList<IlInstruction> activeInstructions,
        bool isApproximate)
    {
        var highlights = new List<InstructionHighlight>();

        foreach (var region in sourceRegions)
        {
            var instructionIds = activeMethodBlock.Instructions
                .Where(instruction => instruction.SourceRange is not null && Overlaps(instruction.SourceRange, region.SourceRange))
                .Select(instruction => instruction.Id)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var approximate = isApproximate;

            if (instructionIds.Count == 0 && activeInstructions.Count > 0)
            {
                instructionIds = activeInstructions.Select(instruction => instruction.Id).ToList();
                approximate = true;
            }

            if (instructionIds.Count > 0)
            {
                highlights.Add(new InstructionHighlight(
                    $"highlight-{region.Id}",
                    region.Id,
                    region.Depth,
                    instructionIds,
                    approximate));
            }
        }

        return highlights;
    }

    private static bool Overlaps(SourceRange? left, SourceRange right)
    {
        if (left is null)
        {
            return false;
        }

        if (left.EndLine < right.StartLine || right.EndLine < left.StartLine)
        {
            return false;
        }

        if (left.EndLine == right.StartLine && left.EndColumn < right.StartColumn)
        {
            return false;
        }

        if (right.EndLine == left.StartLine && right.EndColumn < left.StartColumn)
        {
            return false;
        }

        return true;
    }
}
