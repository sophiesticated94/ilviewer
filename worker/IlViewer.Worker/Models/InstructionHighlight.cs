namespace IlViewer.Worker.Models;

public sealed record InstructionHighlight(
    string Id,
    string RegionId,
    int Depth,
    IReadOnlyList<string> InstructionIds,
    bool IsApproximate);
