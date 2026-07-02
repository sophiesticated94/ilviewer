import { AnalysisResult, HoverOverlay } from "./types";

export function toHoverOverlay(result: AnalysisResult): HoverOverlay {
  const selectedRegionId = result.selectedRegionId;
  const selectedHighlight = result.instructionHighlights.find(highlight => highlight.regionId === selectedRegionId)
    ?? [...result.instructionHighlights]
      .sort((left, right) => right.depth - left.depth)[0];
  const instructionIds = selectedHighlight?.instructionIds
    ?? result.fragment?.instructions.filter(instruction => instruction.isActive).map(instruction => instruction.id)
    ?? [];
  const region = result.sourceRegions.find(sourceRegion => sourceRegion.id === (selectedHighlight?.regionId ?? selectedRegionId));

  return {
    instructionIds,
    regionId: selectedHighlight?.regionId ?? selectedRegionId,
    sourceRange: region?.sourceRange,
    statusText: instructionIds.length > 0
      ? `Hover: ${instructionIds.length} instrukcji IL`
      : "Hover: brak instrukcji IL w bieżącym kontekście",
    isApproximate: result.isApproximate || selectedHighlight?.isApproximate === true
  };
}
