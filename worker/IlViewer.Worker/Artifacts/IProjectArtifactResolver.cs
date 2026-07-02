using IlViewer.Worker.Models;

namespace IlViewer.Worker.Artifacts;

public interface IProjectArtifactResolver
{
    ProjectArtifacts Resolve(AnalysisRequest request);
}
