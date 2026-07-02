using IlViewer.Worker.Models;

namespace IlViewer.Worker.Analysis;

public interface IApplicationGraphService
{
    GraphExpandResult GetRoot(ProjectArtifacts artifacts, GraphRequest request);
    GraphExpandResult Expand(ProjectArtifacts artifacts, GraphRequest request);
}
