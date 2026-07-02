using IlViewer.Worker.Models;
using Mono.Cecil;
using System.IO;
using System.Diagnostics;
using Mono.Cecil.Cil;
using System.Reflection.Metadata;
using TypeDefinition = Mono.Cecil.TypeDefinition;
using MethodDefinition = Mono.Cecil.MethodDefinition;
using SequencePoint = Mono.Cecil.Cil.SequencePoint;

namespace IlViewer.Worker.Analysis;

public sealed class AssemblyIlAnalyzer : IAssemblyIlAnalyzer
{
    private const int HiddenSequencePointLine = 0xFEEFEE;

    private readonly ISourceRegionAnalyzer _sourceRegionAnalyzer;
    private readonly IInstructionCatalog _instructionCatalog;
    private readonly IlScopeBuilder _scopeBuilder;
    private readonly InstructionHighlightBuilder _highlightBuilder;
    private readonly CecilModuleLoader _moduleLoader;
    private readonly AnalysisCacheManager _cacheManager = new();

    public AssemblyIlAnalyzer(
        ISourceRegionAnalyzer sourceRegionAnalyzer,
        IInstructionCatalog instructionCatalog,
        IlScopeBuilder scopeBuilder,
        InstructionHighlightBuilder highlightBuilder,
        CecilModuleLoader? moduleLoader = null)
    {
        _sourceRegionAnalyzer = sourceRegionAnalyzer;
        _instructionCatalog = instructionCatalog;
        _scopeBuilder = scopeBuilder;
        _highlightBuilder = highlightBuilder;
        _moduleLoader = moduleLoader ?? new CecilModuleLoader();
    }

    public AnalysisResult Analyze(AnalysisRequest request, ProjectArtifacts artifacts)
    {
        System.Threading.Tasks.Task.Run(() => _cacheManager.OpportunisticCleanup(request.ProjectPath));

        string mvid = GetMvid(artifacts.AssemblyPath);

        var cachedIndex = _cacheManager.LoadIndex(request.ProjectPath, artifacts, mvid);
        if (cachedIndex is not null)
        {
            if (cachedIndex.Documents.TryGetValue(request.DocumentPath, out var cachedCandidates))
            {
                var cachedMethod = cachedCandidates
                    .Where(c => c.SequencePoints.Any(sp => !sp.IsHidden && sp.StartLine > 0 && Overlaps(sp, request.Line, request.EndLine)))
                    .OrderBy(c => c.SourceRange?.StartLine ?? int.MaxValue)
                    .FirstOrDefault();

                if (cachedMethod is null && cachedCandidates.Count > 0)
                {
                    cachedMethod = cachedCandidates
                        .Where(c => c.SourceRange is not null && ContainsLine(c.SourceRange, request.Line))
                        .OrderBy(c => Math.Abs((c.SourceRange?.StartLine ?? request.Line) - request.Line))
                        .FirstOrDefault();
                }

                if (cachedMethod is not null)
                {
                    var cachedData = _cacheManager.LoadMethodData(request.ProjectPath, artifacts, cachedMethod.Token);
                    if (cachedData is not null)
                    {
                        return ReconstructResult(request, artifacts, cachedMethod, cachedData);
                    }
                }
            }
        }

        var sourceRegions = _sourceRegionAnalyzer.Analyze(request);
        var modules = new List<LoadedModule>();

        try
        {
            var language = Path.GetExtension(request.DocumentPath).ToLowerInvariant() switch
            {
                ".cs" => "csharp",
                ".vb" => "vb",
                _ => "unknown"
            };
            var sourceText = File.ReadAllText(request.DocumentPath);
            var declaredTypeNames = _sourceRegionAnalyzer.GetDeclaredTypeNames(sourceText, language);
            if (declaredTypeNames.Count > 0 && !declaredTypeNames.Contains("Program"))
            {
                declaredTypeNames = new HashSet<string>(declaredTypeNames, StringComparer.OrdinalIgnoreCase) { "Program" };
            }

            foreach (var artifact in ResolveAssemblies(artifacts))
            {
                modules.Add(new LoadedModule(
                    artifact,
                    _moduleLoader.LoadModule(artifacts, artifact.AssemblyPath)));
            }

            var candidates = modules
                .SelectMany(module => module.Module.Types
                    .SelectMany(GetTypes)
                    .SelectMany(type => type.Methods)
                    .Where(method => method.HasBody)
                    .Select(method => BuildMethodCandidate(module.Artifact, method, request.DocumentPath, module.Artifact.IsRoot)))
                .OrderBy(candidate => candidate.Artifact.IsRoot ? 0 : 1)
                .ThenBy(candidate => candidate.AssemblyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Method.DeclaringType.FullName, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Method.MetadataToken.RID)
                .ToList();

            var activeCandidate = FindActiveCandidate(candidates, request);
            if (activeCandidate is null)
            {
                return AnalysisResult.Failure("No IL sequence points were found for this source location.")
                    .WithArtifacts(artifacts);
            }

            // Fetch regions for the entire method
            sourceRegions = _sourceRegionAnalyzer.Analyze(request, activeCandidate.SourceRange ?? new SourceRange(request.Line, request.EndLine, request.StartColumn, request.EndColumn));

            var activeSequencePoints = activeCandidate.DocumentSequencePoints
                .Where(sequencePoint => Overlaps(sequencePoint, request.Line, request.EndLine))
                .ToList();
            var isApproximate = activeSequencePoints.Count == 0;
            if (isApproximate && activeCandidate.DocumentSequencePoints.Count > 0)
            {
                activeSequencePoints.Add(FindNearestSequencePoint(activeCandidate.DocumentSequencePoints, request.Line));
            }

            var activeRanges = activeSequencePoints
                .Select(sequencePoint => BuildInstructionRange(activeCandidate.DocumentSequencePoints, sequencePoint))
                .ToList();

            var activeMethodBlock = _scopeBuilder.BuildMethodBlock(activeCandidate, activeRanges);
            var activeInstructions = activeMethodBlock.Instructions
                .Where(instruction => instruction.IsActive)
                .ToList();
            if (activeInstructions.Count == 0)
            {
                activeInstructions = activeMethodBlock.Instructions
                    .Where(instruction => instruction.SourceRange is not null && Overlaps(instruction.SourceRange, request.Line, request.EndLine))
                    .ToList();
            }

            var highlights = _highlightBuilder.Build(sourceRegions, activeMethodBlock, activeInstructions, isApproximate);
            var scopes = _scopeBuilder.BuildScopes(candidates, activeCandidate, activeMethodBlock, activeInstructions, highlights);
            var explanations = scopes
                .SelectMany(scope => scope.Instructions)
                .Select(instruction => instruction.Opcode)
                .Where(opcode => !string.IsNullOrWhiteSpace(opcode))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(opcode => _instructionCatalog.Explain(FindOpCode(scopes, opcode)))
                .OrderBy(explanation => explanation.Opcode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var fragment = new MethodIl(
                activeCandidate.Method.DeclaringType.FullName,
                activeCandidate.Method.Name,
                activeCandidate.Method.FullName,
                activeCandidate.SourceRange ?? new SourceRange(request.Line, request.EndLine, request.StartColumn, request.EndColumn),
                activeInstructions,
                activeInstructions.Select(instruction => instruction.Offset).ToArray());

            var context = new MethodIl(
                activeCandidate.Method.DeclaringType.FullName,
                activeCandidate.Method.Name,
                activeCandidate.Method.FullName,
                activeCandidate.SourceRange ?? new SourceRange(request.Line, request.EndLine, request.StartColumn, request.EndColumn),
                activeMethodBlock.Instructions,
                activeInstructions.Select(instruction => instruction.Offset).ToArray());

            var message = isApproximate
                ? "This line has no direct sequence point. Showing the nearest generated IL in the surrounding method."
                : null;

            var finalResult = AnalysisResult.SuccessResult(
                artifacts,
                request.DocumentPath,
                request.Line,
                request.EndLine,
                fragment,
                context,
                scopes,
                sourceRegions,
                highlights,
                explanations,
                sourceRegions.FirstOrDefault(region => region.IsSelected)?.Id,
                isApproximate,
                message);

            try
            {
                var newIndexCache = new AssemblyIndexCache();
                newIndexCache.Documents = candidates
                    .GroupBy(c => c.Method.DebugInformation.SequencePoints.FirstOrDefault(sp => !sp.IsHidden)?.Document.Url)
                    .Where(g => !string.IsNullOrEmpty(g.Key))
                    .ToDictionary(
                        g => NormalizePath(g.Key),
                        g => g.Select(c => new CachedMethodCandidate
                        {
                            Token = c.Method.MetadataToken.ToInt32(),
                            FullName = c.Method.FullName,
                            TypeFullName = c.Method.DeclaringType.FullName,
                            MethodName = c.Method.Name,
                            SourceRange = c.SourceRange,
                            SequencePoints = c.DocumentSequencePoints.Select(sp => new CachedSequencePoint
                            {
                                Offset = sp.Offset,
                                StartLine = sp.StartLine,
                                EndLine = sp.EndLine,
                                StartColumn = sp.StartColumn,
                                EndColumn = sp.EndColumn,
                                IsHidden = sp.IsHidden
                            }).ToList()
                        }).ToList(),
                        StringComparer.OrdinalIgnoreCase);

                _cacheManager.SaveIndex(request.ProjectPath, artifacts, mvid, newIndexCache);

                var methodData = new CachedMethodData
                {
                    Token = activeCandidate.Method.MetadataToken.ToInt32(),
                    Context = context,
                    AllSourceRegions = sourceRegions.ToList(),
                    AllInstructionHighlights = highlights.ToList(),
                    Explanations = explanations,
                    Scopes = scopes.ToList()
                };
                _cacheManager.SaveMethodData(request.ProjectPath, artifacts, methodData.Token, methodData);
            }
            catch
            {
                // Ignore cache errors
            }

            return finalResult;
        }
        finally
        {
            foreach (var module in modules)
            {
                module.Module.Dispose();
            }
        }
    }

    private static MethodCandidate? FindActiveCandidate(IReadOnlyList<MethodCandidate> candidates, AnalysisRequest request)
    {
        var directMatch = candidates
            .Where(candidate => candidate.Artifact.IsRoot)
            .Where(candidate => candidate.DocumentSequencePoints.Any(sequencePoint => Overlaps(sequencePoint, request.Line, request.EndLine)))
            .OrderBy(candidate => candidate.SourceRange?.StartLine ?? int.MaxValue)
            .ThenBy(candidate => candidate.Method.FullName, StringComparer.Ordinal)
            .FirstOrDefault();

        return directMatch ?? candidates
            .Where(candidate => candidate.Artifact.IsRoot)
            .Where(candidate => candidate.SourceRange is not null && ContainsLine(candidate.SourceRange, request.Line))
            .OrderBy(candidate => Math.Abs((candidate.SourceRange?.StartLine ?? request.Line) - request.Line))
            .ThenBy(candidate => candidate.Method.FullName, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static MethodCandidate BuildMethodCandidate(AssemblyArtifact artifact, MethodDefinition method, string documentPath, bool isTargetAssembly)
    {
        if (!isTargetAssembly || !method.DebugInformation.HasSequencePoints)
        {
            return new MethodCandidate(
                artifact,
                Path.GetFileNameWithoutExtension(artifact.AssemblyPath),
                method,
                [],
                [],
                null);
        }

        var visibleSequencePoints = method.DebugInformation.SequencePoints
            .Where(IsVisible)
            .OrderBy(sequencePoint => sequencePoint.Offset)
            .ToList();
        var documentSequencePoints = visibleSequencePoints
            .Where(sequencePoint => MatchesDocument(sequencePoint.Document.Url, documentPath))
            .ToList();
        var sourceRange = BuildSourceRange(documentSequencePoints.Count > 0 ? documentSequencePoints : visibleSequencePoints);

        return new MethodCandidate(
            artifact,
            Path.GetFileNameWithoutExtension(artifact.AssemblyPath),
            method,
            visibleSequencePoints,
            documentSequencePoints,
            sourceRange);
    }

    private static SourceRange? BuildSourceRange(IReadOnlyList<SequencePoint> sequencePoints)
    {
        if (sequencePoints.Count == 0)
        {
            return null;
        }

        var startLine = sequencePoints.Min(sequencePoint => sequencePoint.StartLine);
        var endLine = sequencePoints.Max(GetEffectiveEndLine);
        var startColumn = sequencePoints
            .Where(sequencePoint => sequencePoint.StartLine == startLine)
            .Min(sequencePoint => Math.Max(sequencePoint.StartColumn, 1));
        var endColumn = sequencePoints
            .Where(sequencePoint => GetEffectiveEndLine(sequencePoint) == endLine)
            .Max(sequencePoint => Math.Max(sequencePoint.EndColumn, 1));

        return new SourceRange(startLine, endLine, startColumn, endColumn);
    }

    private static IEnumerable<AssemblyArtifact> ResolveAssemblies(ProjectArtifacts artifacts)
    {
        if (artifacts.ApplicationAssemblies.Count > 0)
        {
            return artifacts.ApplicationAssemblies;
        }

        return
        [
            new AssemblyArtifact(
                artifacts.ProjectPath,
                artifacts.AssemblyPath,
                artifacts.PdbPath,
                artifacts.TargetFramework,
                artifacts.Configuration,
                artifacts.AssemblyLastWriteTimeUtc,
                artifacts.PdbLastWriteTimeUtc,
                true)
        ];
    }

    private static IEnumerable<TypeDefinition> GetTypes(TypeDefinition type)
    {
        yield return type;

        foreach (var nestedType in type.NestedTypes.SelectMany(GetTypes))
        {
            yield return nestedType;
        }
    }

    private static InstructionRange BuildInstructionRange(IReadOnlyList<SequencePoint> sequencePoints, SequencePoint activeSequencePoint)
    {
        var startOffset = activeSequencePoint.Offset;
        var endOffset = sequencePoints
            .Where(sequencePoint => sequencePoint.Offset > startOffset)
            .OrderBy(sequencePoint => sequencePoint.Offset)
            .Select(sequencePoint => sequencePoint.Offset)
            .FirstOrDefault();

        return new InstructionRange(startOffset, endOffset == 0 ? int.MaxValue : endOffset);
    }

    private static SequencePoint FindNearestSequencePoint(IReadOnlyList<SequencePoint> sequencePoints, int line)
    {
        return sequencePoints
            .OrderBy(sequencePoint =>
            {
                if (line < sequencePoint.StartLine)
                {
                    return sequencePoint.StartLine - line;
                }

                var endLine = GetEffectiveEndLine(sequencePoint);
                return line > endLine ? line - endLine : 0;
            })
            .ThenBy(sequencePoint => sequencePoint.Offset)
            .First();
    }

    private static bool MatchesDocument(string documentUrl, string documentPath)
    {
        var normalizedUrl = NormalizePath(documentUrl);
        var normalizedPath = NormalizePath(documentPath);

        if (string.Equals(normalizedUrl, normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedUrl.EndsWith(Path.DirectorySeparatorChar + Path.GetFileName(normalizedPath), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(normalizedUrl), Path.GetFileName(normalizedPath), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string pathOrUri)
    {
        var path = pathOrUri;

        if (Uri.TryCreate(pathOrUri, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            path = uri.LocalPath;
        }

        path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            // SourceLink paths may not be local paths; keep the normalized string.
        }

        return path.TrimEnd(Path.DirectorySeparatorChar);
    }

    private static bool IsVisible(SequencePoint sequencePoint)
    {
        return !sequencePoint.IsHidden
            && sequencePoint.StartLine != HiddenSequencePointLine
            && sequencePoint.StartLine > 0;
    }

    private static bool Overlaps(SequencePoint sequencePoint, int startLine, int endLine)
    {
        return sequencePoint.StartLine <= endLine && GetEffectiveEndLine(sequencePoint) >= startLine;
    }

    private static bool Overlaps(SourceRange? left, SourceRange right)
    {
        if (left is null)
        {
            return false;
        }

        if (left.EndLine < right.StartLine || right.EndLine < left.StartLine)
        {
            return false;
        }

        if (left.EndLine == right.StartLine && left.EndColumn < right.StartColumn)
        {
            return false;
        }

        if (right.EndLine == left.StartLine && right.EndColumn < left.StartColumn)
        {
            return false;
        }

        return true;
    }

    private static bool Overlaps(SourceRange? range, int startLine, int endLine)
    {
        return range is not null && range.StartLine <= endLine && range.EndLine >= startLine;
    }

    private static bool ContainsLine(SourceRange range, int line)
    {
        return range.StartLine <= line && range.EndLine >= line;
    }

    private static int GetEffectiveEndLine(SequencePoint sequencePoint)
    {
        return sequencePoint.EndLine >= sequencePoint.StartLine
            ? sequencePoint.EndLine
            : sequencePoint.StartLine;
    }

    private static SourceRange ToSourceRange(SequencePoint sequencePoint)
    {
        return new SourceRange(
            sequencePoint.StartLine,
            GetEffectiveEndLine(sequencePoint),
            Math.Max(sequencePoint.StartColumn, 1),
            Math.Max(sequencePoint.EndColumn, 1));
    }

    private static OpCode FindOpCode(IEnumerable<IlScope> scopes, string opcode)
    {
        foreach (var instruction in scopes.SelectMany(scope => scope.Instructions))
        {
            if (string.Equals(instruction.Opcode, opcode, StringComparison.OrdinalIgnoreCase))
            {
                return FindOpCodeByName(opcode);
            }
        }

        return OpCodes.Nop;
    }

    private static OpCode FindOpCodeByName(string name)
    {
        var fields = typeof(OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        foreach (var field in fields)
        {
            if (field.GetValue(null) is OpCode opcode && string.Equals(opcode.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return opcode;
            }
        }

        return OpCodes.Nop;
    }

    private static AnalysisResult ReconstructResult(
        AnalysisRequest request,
        ProjectArtifacts artifacts,
        CachedMethodCandidate cachedMethod,
        CachedMethodData cachedData)
    {
        var activeSequencePoints = cachedMethod.SequencePoints
            .Where(sp => !sp.IsHidden && sp.StartLine > 0 && Overlaps(sp, request.Line, request.EndLine))
            .ToList();
        var isApproximate = activeSequencePoints.Count == 0;
        if (isApproximate && cachedMethod.SequencePoints.Count > 0)
        {
            activeSequencePoints.Add(FindNearestCachedSequencePoint(cachedMethod.SequencePoints, request.Line));
        }

        var activeRanges = activeSequencePoints
            .Select(sp => BuildCachedInstructionRange(cachedMethod.SequencePoints, sp))
            .ToList();

        var contextInstructions = cachedData.Context.Instructions
            .Select(inst => inst with { IsActive = activeRanges.Any(range => inst.Offset >= range.StartOffset && inst.Offset < range.EndOffset) })
            .ToList();

        var activeInstructions = contextInstructions.Where(i => i.IsActive).ToList();
        if (activeInstructions.Count == 0)
        {
            contextInstructions = cachedData.Context.Instructions
                .Select(inst => inst with { IsActive = inst.SourceRange is not null && Overlaps(inst.SourceRange, request.Line, request.EndLine) })
                .ToList();
            activeInstructions = contextInstructions.Where(i => i.IsActive).ToList();
        }

        var context = cachedData.Context with { Instructions = contextInstructions, ActiveInstructionOffsets = activeInstructions.Select(i => i.Offset).ToArray() };

        var fragment = new MethodIl(
            context.TypeName,
            context.MethodName,
            context.FullName,
            context.SourceRange,
            activeInstructions,
            activeInstructions.Select(i => i.Offset).ToArray());

        var sourceRegions = cachedData.AllSourceRegions
            .Select(region => region with
            {
                IsSelected = region.Kind == "selection" ? (region.SourceRange.StartLine == request.Line && region.SourceRange.StartColumn == request.StartColumn) : IsRegionSelected(region, request)
            })
            .ToList();

        var selectedRegionId = sourceRegions.FirstOrDefault(r => r.IsSelected)?.Id;

        var highlights = cachedData.AllInstructionHighlights
            .Select(h =>
            {
                var isRegionApprox = isApproximate;
                var regionInsts = contextInstructions
                    .Where(i => i.SourceRange is not null && Overlaps(i.SourceRange, sourceRegions.First(r => r.Id == h.RegionId).SourceRange))
                    .Select(i => i.Id)
                    .ToList();
                if (regionInsts.Count == 0 && activeInstructions.Count > 0)
                {
                    regionInsts = activeInstructions.Select(i => i.Id).ToList();
                    isRegionApprox = true;
                }
                return h with { InstructionIds = regionInsts, IsApproximate = isRegionApprox };
            })
            .Where(h => h.InstructionIds.Count > 0)
            .ToList();

        var activeInstructionIds = activeInstructions.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var activeHighlightIds = highlights.Select(h => h.Id).ToHashSet(StringComparer.Ordinal);

        var scopes = cachedData.Scopes.Select(scope =>
        {
            var scopeInstructions = scope.Instructions
                .Select(inst => inst with { IsActive = activeInstructionIds.Contains(inst.Id) })
                .ToList();

            if (scope.Kind == "fragment")
            {
                scopeInstructions = scopeInstructions.Where(i => i.IsActive).ToList();
            }

            var scopeMethods = scope.Methods.Select(mb =>
            {
                var mbInstructions = mb.Instructions
                    .Select(inst => inst with { IsActive = activeInstructionIds.Contains(inst.Id) })
                    .ToList();
                return mb with
                {
                    Instructions = mbInstructions,
                    ContainsActiveInstruction = mbInstructions.Any(i => i.IsActive)
                };
            }).ToList();

            return scope with
            {
                Instructions = scopeInstructions,
                Methods = scopeMethods,
                ActiveInstructionIds = scopeInstructions.Where(i => i.IsActive).Select(i => i.Id).ToList(),
                ActiveHighlightIds = highlights.Where(h => activeHighlightIds.Contains(h.Id)).Select(h => h.Id).ToList()
            };
        }).ToList();

        var message = isApproximate
            ? "This line has no direct sequence point. Showing the nearest generated IL in the surrounding method."
            : null;

        return AnalysisResult.SuccessResult(
            artifacts,
            request.DocumentPath,
            request.Line,
            request.EndLine,
            fragment,
            context,
            scopes,
            sourceRegions,
            highlights,
            cachedData.Explanations,
            selectedRegionId,
            isApproximate,
            message) with { IsFromCache = true };
    }

    private static bool Overlaps(CachedSequencePoint sp, int startLine, int endLine)
    {
        return sp.StartLine <= endLine && sp.EndLine >= startLine;
    }

    private static CachedSequencePoint FindNearestCachedSequencePoint(IReadOnlyList<CachedSequencePoint> sequencePoints, int line)
    {
        return sequencePoints
            .OrderBy(sp =>
            {
                if (line < sp.StartLine) return sp.StartLine - line;
                return line > sp.EndLine ? line - sp.EndLine : 0;
            })
            .ThenBy(sp => sp.Offset)
            .First();
    }

    private static InstructionRange BuildCachedInstructionRange(IReadOnlyList<CachedSequencePoint> sequencePoints, CachedSequencePoint activeSequencePoint)
    {
        var startOffset = activeSequencePoint.Offset;
        var endOffset = sequencePoints
            .Where(sp => sp.Offset > startOffset)
            .OrderBy(sp => sp.Offset)
            .Select(sp => sp.Offset)
            .FirstOrDefault();

        return new InstructionRange(startOffset, endOffset == 0 ? int.MaxValue : endOffset);
    }

    private static bool IsRegionSelected(SourceRegion region, AnalysisRequest request)
    {
        return request.Line >= region.SourceRange.StartLine && request.Line <= region.SourceRange.EndLine;
    }

    private static Mono.Cecil.TypeDefinition GetOutermostType(Mono.Cecil.TypeDefinition type)
    {
        var current = type;
        while (current.DeclaringType is not null)
        {
            current = current.DeclaringType;
        }
        return current;
    }

    private static string GetMvid(string assemblyPath)
    {
        using (var fs = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var peReader = new System.Reflection.PortableExecutable.PEReader(fs))
        {
            var metadataReader = peReader.GetMetadataReader();
            var mvidGuid = metadataReader.GetGuid(metadataReader.GetModuleDefinition().Mvid);
            return mvidGuid.ToString();
        }
    }
}
