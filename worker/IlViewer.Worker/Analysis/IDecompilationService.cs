using IlViewer.Worker.Models;

namespace IlViewer.Worker.Analysis;

public interface IDecompilationService
{
    DecompileResult Decompile(ProjectArtifacts artifacts, DecompileRequest request);
}
