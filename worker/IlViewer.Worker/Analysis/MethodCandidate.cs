using IlViewer.Worker.Models;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace IlViewer.Worker.Analysis;

public sealed record LoadedModule(AssemblyArtifact Artifact, ModuleDefinition Module);

public sealed record MethodCandidate(
    AssemblyArtifact Artifact,
    string AssemblyName,
    MethodDefinition Method,
    IReadOnlyList<SequencePoint> VisibleSequencePoints,
    IReadOnlyList<SequencePoint> DocumentSequencePoints,
    SourceRange? SourceRange);

public readonly record struct InstructionRange(int StartOffset, int EndOffset)
{
    public bool Contains(int offset)
    {
        return offset >= StartOffset && offset < EndOffset;
    }
}
