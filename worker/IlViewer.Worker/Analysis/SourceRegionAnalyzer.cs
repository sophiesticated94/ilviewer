using IlViewer.Worker.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using VbSyntaxKind = Microsoft.CodeAnalysis.VisualBasic.SyntaxKind;

namespace IlViewer.Worker.Analysis;

public sealed class SourceRegionAnalyzer : ISourceRegionAnalyzer
{
    public IReadOnlyList<SourceRegion> Analyze(AnalysisRequest request)
    {
        if (!File.Exists(request.DocumentPath))
        {
            return [];
        }

        var source = File.ReadAllText(request.DocumentPath);
        var text = SourceText.From(source);
        var selectedSpan = BuildSelectionSpan(text, request);
        var language = ResolveLanguage(request.DocumentPath);

        var regions = FindInterestingRegions(source, language)
            .Where(region => region.Span.IntersectsWith(selectedSpan) || region.Span.Contains(selectedSpan.Start) || selectedSpan.Contains(region.Span.Start))
            .DistinctBy(region => $"{region.Kind}:{region.Span.Start}:{region.Span.Length}")
            .ToList();

        var selected = new RegionCandidate("selection", selectedSpan, "Zaznaczenie");
        regions.Insert(0, selected);

        var regionIds = new Dictionary<RegionCandidate, string>();
        for (var index = 0; index < regions.Count; index++)
        {
            regionIds[regions[index]] = $"region-{index}";
        }

        return regions
            .Select(region =>
            {
                var parent = FindParent(region, regions);
                var depth = region.Kind == "selection" ? 0 : CalculateDepth(region, regions);
                return new SourceRegion(
                    regionIds[region],
                    region.Kind,
                    depth,
                    ToSourceRange(text, region.Span),
                    parent is null ? null : regionIds[parent],
                    region.DisplayName,
                    region.Kind == "selection",
                    region.Span.Equals(selectedSpan),
                    language);
            })
            .OrderBy(region => region.Depth)
            .ThenBy(region => region.SourceRange.StartLine)
            .ThenBy(region => region.SourceRange.StartColumn)
            .ThenByDescending(region => region.SourceRange.EndLine - region.SourceRange.StartLine)
            .ToList();
    }

    public IReadOnlyList<SourceRegion> Analyze(AnalysisRequest request, SourceRange methodSourceRange)
    {
        if (!File.Exists(request.DocumentPath))
        {
            return [];
        }

        var source = File.ReadAllText(request.DocumentPath);
        var text = SourceText.From(source);
        var selectedSpan = BuildSelectionSpan(text, request);
        var methodSpan = BuildRangeSpan(text, methodSourceRange);
        var language = ResolveLanguage(request.DocumentPath);

        var regions = FindInterestingRegions(source, language)
            .Where(region => methodSpan.Contains(region.Span))
            .DistinctBy(region => $"{region.Kind}:{region.Span.Start}:{region.Span.Length}")
            .ToList();

        var selected = new RegionCandidate("selection", selectedSpan, "Zaznaczenie");
        regions.Insert(0, selected);

        var regionIds = new Dictionary<RegionCandidate, string>();
        for (var index = 0; index < regions.Count; index++)
        {
            regionIds[regions[index]] = $"region-{index}";
        }

        return regions
            .Select(region =>
            {
                var parent = FindParent(region, regions);
                var depth = region.Kind == "selection" ? 0 : CalculateDepth(region, regions);
                return new SourceRegion(
                    regionIds[region],
                    region.Kind,
                    depth,
                    ToSourceRange(text, region.Span),
                    parent is null ? null : regionIds[parent],
                    region.DisplayName,
                    region.Kind == "selection",
                    region.Span.Equals(selectedSpan),
                    language);
            })
            .OrderBy(region => region.Depth)
            .ThenBy(region => region.SourceRange.StartLine)
            .ThenBy(region => region.SourceRange.StartColumn)
            .ThenByDescending(region => region.SourceRange.EndLine - region.SourceRange.StartLine)
            .ToList();
    }

    public IReadOnlySet<string> GetDeclaredTypeNames(string source, string language)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (language == "csharp")
        {
            var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
            var root = tree.GetRoot();
            foreach (var node in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax>())
            {
                names.Add(node.Identifier.Text);
            }
        }
        else if (language == "vb")
        {
            var tree = Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree.ParseText(source);
            var root = tree.GetRoot();
            foreach (var node in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.VisualBasic.Syntax.TypeBlockSyntax>())
            {
                names.Add(node.BlockStatement.Identifier.Text);
            }
        }

        return names;
    }

    private static TextSpan BuildRangeSpan(SourceText text, SourceRange range)
    {
        var startLine = Math.Clamp(range.StartLine - 1, 0, Math.Max(text.Lines.Count - 1, 0));
        var endLine = Math.Clamp(range.EndLine - 1, 0, Math.Max(text.Lines.Count - 1, 0));
        var startColumn = Math.Max(range.StartColumn - 1, 0);
        var endColumn = Math.Max(range.EndColumn - 1, 0);

        var startPosition = GetPosition(text, startLine, startColumn);
        var endPosition = GetPosition(text, endLine, endColumn);

        return TextSpan.FromBounds(startPosition, Math.Max(startPosition, endPosition));
    }

    private static IEnumerable<RegionCandidate> FindInterestingRegions(string source, string language)
    {
        return language switch
        {
            "csharp" => FindCSharpRegions(source),
            "vb" => FindVisualBasicRegions(source),
            _ => []
        };
    }

    private static IEnumerable<RegionCandidate> FindCSharpRegions(string source)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        foreach (var node in root.DescendantNodes())
        {
            var kind = (CSharpSyntaxKind)node.RawKind;
            var regionKind = kind switch
            {
                CSharpSyntaxKind.SimpleLambdaExpression => "lambda",
                CSharpSyntaxKind.ParenthesizedLambdaExpression => "lambda",
                CSharpSyntaxKind.AnonymousMethodExpression => "lambda",
                CSharpSyntaxKind.ObjectCreationExpression => "objectCreation",
                CSharpSyntaxKind.ImplicitObjectCreationExpression => "objectCreation",
                CSharpSyntaxKind.AnonymousObjectCreationExpression => "objectCreation",
                CSharpSyntaxKind.ObjectInitializerExpression => "objectInitializer",
                CSharpSyntaxKind.CollectionInitializerExpression => "collectionInitializer",
                CSharpSyntaxKind.AnonymousObjectMemberDeclarator => "memberInitializer",
                CSharpSyntaxKind.SimpleAssignmentExpression when IsInsideCSharpInitializer(node) => "memberInitializer",
                _ => null
            };

            if (regionKind is not null)
            {
                yield return new RegionCandidate(regionKind, node.Span, BuildDisplayName(regionKind, node.ToString()));
            }
        }
    }

    private static IEnumerable<RegionCandidate> FindVisualBasicRegions(string source)
    {
        var tree = Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        foreach (var node in root.DescendantNodes())
        {
            var kind = (VbSyntaxKind)node.RawKind;
            var kindName = kind.ToString();
            var regionKind = kindName switch
            {
                "SingleLineFunctionLambdaExpression" => "lambda",
                "SingleLineSubLambdaExpression" => "lambda",
                "MultiLineFunctionLambdaExpression" => "lambda",
                "MultiLineSubLambdaExpression" => "lambda",
                "ObjectCreationExpression" => "objectCreation",
                "ObjectMemberInitializer" => "objectInitializer",
                "CollectionInitializer" => "collectionInitializer",
                "NamedFieldInitializer" => "memberInitializer",
                "InferredFieldInitializer" => "memberInitializer",
                _ => kindName.Contains("Lambda", StringComparison.Ordinal) ? "lambda" : null
            };

            if (regionKind is not null)
            {
                yield return new RegionCandidate(regionKind, node.Span, BuildDisplayName(regionKind, node.ToString()));
            }
        }
    }

    private static bool IsInsideCSharpInitializer(SyntaxNode node)
    {
        return node.Ancestors().Any(ancestor =>
        {
            var kind = (CSharpSyntaxKind)ancestor.RawKind;
            return kind is CSharpSyntaxKind.ObjectInitializerExpression
                or CSharpSyntaxKind.CollectionInitializerExpression
                or CSharpSyntaxKind.ComplexElementInitializerExpression
                or CSharpSyntaxKind.AnonymousObjectCreationExpression;
        });
    }

    private static TextSpan BuildSelectionSpan(SourceText text, AnalysisRequest request)
    {
        var startLine = Math.Clamp(request.Line - 1, 0, Math.Max(text.Lines.Count - 1, 0));
        var endLine = Math.Clamp(request.EndLine - 1, 0, Math.Max(text.Lines.Count - 1, 0));
        var startColumn = Math.Max(request.StartColumn - 1, 0);
        var endColumn = Math.Max(request.EndColumn - 1, 0);

        var startPosition = GetPosition(text, startLine, startColumn);
        var endPosition = GetPosition(text, endLine, endColumn);

        if (endPosition <= startPosition)
        {
            var line = text.Lines[startLine];
            startPosition = line.Start;
            endPosition = line.End;
        }

        return TextSpan.FromBounds(startPosition, endPosition);
    }

    private static int GetPosition(SourceText text, int lineIndex, int columnIndex)
    {
        var line = text.Lines[lineIndex];
        return Math.Clamp(line.Start + columnIndex, line.Start, line.End);
    }

    private static RegionCandidate? FindParent(RegionCandidate region, IReadOnlyList<RegionCandidate> regions)
    {
        if (region.Kind == "selection")
        {
            return null;
        }

        return regions
            .Where(candidate => !ReferenceEquals(candidate, region)
                && candidate.Span.Contains(region.Span)
                && candidate.Span.Length > region.Span.Length)
            .OrderBy(candidate => candidate.Span.Length)
            .FirstOrDefault();
    }

    private static int CalculateDepth(RegionCandidate region, IReadOnlyList<RegionCandidate> regions)
    {
        var depth = 1;
        var current = region;

        while (FindParent(current, regions) is { } parent && parent.Kind != "selection")
        {
            depth++;
            current = parent;
        }

        return depth;
    }

    private static SourceRange ToSourceRange(SourceText text, TextSpan span)
    {
        var start = text.Lines.GetLinePosition(span.Start);
        var end = text.Lines.GetLinePosition(span.End);

        return new SourceRange(
            start.Line + 1,
            end.Line + 1,
            start.Character + 1,
            end.Character + 1);
    }

    private static string ResolveLanguage(string documentPath)
    {
        return Path.GetExtension(documentPath).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".vb" => "vb",
            _ => "unknown"
        };
    }

    private static string BuildDisplayName(string kind, string text)
    {
        var singleLine = string.Join(" ", text.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()));
        if (singleLine.Length > 96)
        {
            singleLine = singleLine[..93] + "...";
        }

        return kind switch
        {
            "lambda" => $"Lambda: {singleLine}",
            "objectCreation" => $"new: {singleLine}",
            "objectInitializer" => $"Initializer: {singleLine}",
            "collectionInitializer" => $"Kolekcja: {singleLine}",
            "memberInitializer" => $"Pole: {singleLine}",
            _ => singleLine
        };
    }

    private sealed record RegionCandidate(string Kind, TextSpan Span, string DisplayName);
}
