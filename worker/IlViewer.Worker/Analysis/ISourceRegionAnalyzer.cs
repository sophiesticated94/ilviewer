using IlViewer.Worker.Models;

namespace IlViewer.Worker.Analysis;

public interface ISourceRegionAnalyzer
{
    IReadOnlyList<SourceRegion> Analyze(AnalysisRequest request);
}
