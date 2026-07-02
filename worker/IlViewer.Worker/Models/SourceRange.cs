namespace IlViewer.Worker.Models;

public sealed record SourceRange(int StartLine, int EndLine, int StartColumn, int EndColumn);
