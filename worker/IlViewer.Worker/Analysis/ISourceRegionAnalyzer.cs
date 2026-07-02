using IlViewer.Worker.Models;

namespace IlViewer.Worker.Analysis;

public interface ISourceRegionAnalyzer
{
    IReadOnlyList<SourceRegion> Analyze(AnalysisRequest request);
    IReadOnlyList<SourceRegion> Analyze(AnalysisRequest request, SourceRange methodSourceRange);
    IReadOnlySet<string> GetDeclaredTypeNames(string source, string language);
}
