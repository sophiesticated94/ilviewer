using IlViewer.Worker.Models;

namespace IlViewer.Worker.Analysis;

public interface IAssemblyIlAnalyzer
{
    AnalysisResult Analyze(AnalysisRequest request, ProjectArtifacts artifacts);
}
