using System.Globalization;
using IlViewer.Worker.Models;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace IlViewer.Worker.Analysis;

public sealed class ApplicationGraphService : IApplicationGraphService
{
    private const int DefaultPageSize = 250;

    private readonly CecilModuleLoader _moduleLoader;

    public ApplicationGraphService(CecilModuleLoader moduleLoader)
    {
        _moduleLoader = moduleLoader;
    }

    public GraphExpandResult GetRoot(ProjectArtifacts artifacts, GraphRequest request)
    {
        var root = artifacts.ReferenceAssemblies.FirstOrDefault(artifact => artifact.IsRoot)
            ?? artifacts.ApplicationAssemblies.FirstOrDefault(artifact => artifact.IsRoot)
            ?? new AssemblyArtifact(
                artifacts.ProjectPath,
                artifacts.AssemblyPath,
                artifacts.PdbPath,
                artifacts.TargetFramework,
                artifacts.Configuration,
                artifacts.AssemblyLastWriteTimeUtc,
                artifacts.PdbLastWriteTimeUtc,
                true)
            {
                AssemblyKind = AssemblyKinds.Project
            };

        return new GraphExpandResult
        {
            Success = true,
            RootAssembly = root.AssemblyName,
            Nodes = [CreateAssemblyNode(root)]
        };
    }

    public GraphExpandResult Expand(ProjectArtifacts artifacts, GraphRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NodeId))
        {
            return GetRoot(artifacts, request);
        }

        var key = GraphNodeIds.Parse(request.NodeId);
        var artifact = ResolveArtifact(artifacts, key);
        if (artifact is null)
        {
            return GraphExpandResult.Failure("Nie udało się odnaleźć assembly dla wybranego węzła grafu.");
        }

        using var module = _moduleLoader.LoadModule(artifacts, artifact.AssemblyPath);
        var pageSize = request.PageSize > 0 ? request.PageSize : DefaultPageSize;

        return key.Kind switch
        {
            GraphNodeKinds.Assembly => ExpandAssembly(artifacts, artifact, module, request.NodeId, pageSize, request.ContinuationToken),
            GraphNodeKinds.Type => ExpandType(artifact, module, key, request.NodeId, pageSize, request.ContinuationToken),
            GraphNodeKinds.Method => ExpandMethod(artifacts, artifact, module, key, request.NodeId, pageSize, request.ContinuationToken),
            _ => GraphExpandResult.Failure($"Nieobsługiwany typ węzła grafu: {key.Kind}.")
        };
    }

    private static GraphExpandResult ExpandAssembly(
        ProjectArtifacts artifacts,
        AssemblyArtifact artifact,
        ModuleDefinition module,
        string parentId,
        int pageSize,
        string? continuationToken)
    {
        var entries = new List<GraphEntry>();

        foreach (var type in module.Types.Where(type => type.Name != "<Module>").OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var node = CreateTypeNode(artifact, type);
            entries.Add(new GraphEntry(node, new GraphEdge(parentId, node.Id, GraphEdgeKinds.Contains, "typ")));
        }

        foreach (var reference in module.AssemblyReferences.OrderBy(reference => reference.Name, StringComparer.OrdinalIgnoreCase))
        {
            var referencedArtifact = ResolveArtifactByName(artifacts, reference.Name);
            if (referencedArtifact is null)
            {
                var node = CreateExternalAssemblyNode(reference.Name);
                entries.Add(new GraphEntry(node, new GraphEdge(parentId, node.Id, GraphEdgeKinds.Type, "assembly ref")));
            }
            else
            {
                var node = CreateAssemblyNode(referencedArtifact);
                entries.Add(new GraphEntry(node, new GraphEdge(parentId, node.Id, GraphEdgeKinds.Type, "assembly ref")));
            }
        }

        return BuildPage(artifact.AssemblyName, entries, pageSize, continuationToken);
    }

    private static GraphExpandResult ExpandType(
        AssemblyArtifact artifact,
        ModuleDefinition module,
        GraphNodeKey key,
        string parentId,
        int pageSize,
        string? continuationToken)
    {
        var type = FindType(module, key.TypeName);
        if (type is null)
        {
            return GraphExpandResult.Failure("Nie udało się odnaleźć typu w assembly.");
        }

        var entries = new List<GraphEntry>();
        foreach (var nestedType in type.NestedTypes.OrderBy(nestedType => nestedType.FullName, StringComparer.Ordinal))
        {
            var node = CreateTypeNode(artifact, nestedType);
            entries.Add(new GraphEntry(node, new GraphEdge(parentId, node.Id, GraphEdgeKinds.Contains, "typ")));
        }

        foreach (var method in type.Methods.OrderBy(method => method.MetadataToken.RID))
        {
            var node = CreateMethodNode(artifact, method);
            entries.Add(new GraphEntry(node, new GraphEdge(parentId, node.Id, GraphEdgeKinds.Contains, "metoda")));
        }

        return BuildPage(artifact.AssemblyName, entries, pageSize, continuationToken);
    }

    private static GraphExpandResult ExpandMethod(
        ProjectArtifacts artifacts,
        AssemblyArtifact artifact,
        ModuleDefinition module,
        GraphNodeKey key,
        string parentId,
        int pageSize,
        string? continuationToken)
    {
        var method = FindMethod(module, key);
        if (method is null)
        {
            return GraphExpandResult.Failure("Nie udało się odnaleźć metody w assembly.");
        }

        if (!method.HasBody)
        {
            return new GraphExpandResult
            {
                Success = true,
                RootAssembly = artifact.AssemblyName,
                Diagnostics = ["Metoda nie ma ciała IL w tym assembly."]
            };
        }

        var entries = new List<GraphEntry>();
        foreach (var instruction in method.Body.Instructions)
        {
            switch (instruction.Operand)
            {
                case MethodReference methodReference:
                    entries.Add(CreateMethodReferenceEntry(artifacts, parentId, instruction, methodReference));
                    break;
                case FieldReference fieldReference:
                    entries.Add(CreateFieldReferenceEntry(artifacts, parentId, instruction, fieldReference));
                    break;
                case TypeReference typeReference:
                    entries.Add(CreateTypeReferenceEntry(artifacts, parentId, instruction, typeReference));
                    break;
                case Instruction branchTarget:
                    var branchNode = CreateMethodNode(artifact, method);
                    entries.Add(new GraphEntry(branchNode, new GraphEdge(parentId, branchNode.Id, GraphEdgeKinds.Branch, $"{instruction.OpCode.Name} IL_{branchTarget.Offset:x4}")));
                    break;
            }
        }

        return BuildPage(artifact.AssemblyName, Deduplicate(entries), pageSize, continuationToken);
    }

    private static GraphEntry CreateMethodReferenceEntry(ProjectArtifacts artifacts, string parentId, Instruction instruction, MethodReference methodReference)
    {
        var resolved = TryResolve(methodReference);
        var artifact = ResolveArtifactByPathOrName(artifacts, resolved?.Module.FileName, resolved?.Module.Assembly.Name.Name ?? ResolveAssemblyName(methodReference.DeclaringType.Scope));
        var node = resolved is not null && artifact is not null
            ? CreateMethodNode(artifact, resolved)
            : CreateExternalMethodNode(methodReference, artifact);
        return new GraphEntry(node, new GraphEdge(parentId, node.Id, GraphEdgeKinds.Call, instruction.OpCode.Name));
    }

    private static GraphEntry CreateFieldReferenceEntry(ProjectArtifacts artifacts, string parentId, Instruction instruction, FieldReference fieldReference)
    {
        var resolved = TryResolve(fieldReference);
        var artifact = ResolveArtifactByPathOrName(artifacts, resolved?.Module.FileName, resolved?.Module.Assembly.Name.Name ?? ResolveAssemblyName(fieldReference.DeclaringType.Scope));
        var node = resolved is not null && artifact is not null
            ? CreateTypeNode(artifact, resolved.DeclaringType)
            : CreateExternalTypeNode(fieldReference.DeclaringType.FullName, artifact, fieldReference.FullName);
        return new GraphEntry(node, new GraphEdge(parentId, node.Id, GraphEdgeKinds.Field, instruction.OpCode.Name));
    }

    private static GraphEntry CreateTypeReferenceEntry(ProjectArtifacts artifacts, string parentId, Instruction instruction, TypeReference typeReference)
    {
        var resolved = TryResolve(typeReference);
        var artifact = ResolveArtifactByPathOrName(artifacts, resolved?.Module.FileName, resolved?.Module.Assembly.Name.Name ?? ResolveAssemblyName(typeReference.Scope));
        var node = resolved is not null && artifact is not null
            ? CreateTypeNode(artifact, resolved)
            : CreateExternalTypeNode(typeReference.FullName, artifact, typeReference.FullName);
        return new GraphEntry(node, new GraphEdge(parentId, node.Id, GraphEdgeKinds.Type, instruction.OpCode.Name));
    }

    private static GraphExpandResult BuildPage(string rootAssembly, IReadOnlyList<GraphEntry> entries, int pageSize, string? continuationToken)
    {
        var offset = ParseContinuationToken(continuationToken);
        var page = entries.Skip(offset).Take(pageSize).ToList();
        var nextOffset = offset + page.Count;
        return new GraphExpandResult
        {
            Success = true,
            RootAssembly = rootAssembly,
            Nodes = page.Select(entry => entry.Node).ToList(),
            Edges = page.Select(entry => entry.Edge).ToList(),
            ContinuationToken = nextOffset < entries.Count ? nextOffset.ToString(CultureInfo.InvariantCulture) : null
        };
    }

    private static IReadOnlyList<GraphEntry> Deduplicate(IEnumerable<GraphEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GraphEntry>();
        foreach (var entry in entries)
        {
            var key = $"{entry.Edge.From}|{entry.Edge.To}|{entry.Edge.Kind}|{entry.Edge.Label}";
            if (seen.Add(key))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private static int ParseContinuationToken(string? continuationToken)
    {
        return int.TryParse(continuationToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset) && offset > 0
            ? offset
            : 0;
    }

    private static AssemblyArtifact? ResolveArtifact(ProjectArtifacts artifacts, GraphNodeKey key)
    {
        return ResolveArtifactByPathOrName(artifacts, key.AssemblyPath, key.AssemblyName);
    }

    private static AssemblyArtifact? ResolveArtifactByPathOrName(ProjectArtifacts artifacts, string? assemblyPath, string? assemblyName)
    {
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            var byPath = artifacts.ReferenceAssemblies.FirstOrDefault(artifact => string.Equals(artifact.AssemblyPath, assemblyPath, StringComparison.OrdinalIgnoreCase));
            if (byPath is not null)
            {
                return byPath;
            }

            if (File.Exists(assemblyPath))
            {
                return CreateAdHocArtifact(artifacts, assemblyPath);
            }
        }

        return !string.IsNullOrWhiteSpace(assemblyName)
            ? ResolveArtifactByName(artifacts, assemblyName)
            : null;
    }

    private static AssemblyArtifact? ResolveArtifactByName(ProjectArtifacts artifacts, string assemblyName)
    {
        return artifacts.ReferenceAssemblies
            .Where(artifact => string.Equals(artifact.AssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(artifact => AssemblyKindOrder(artifact.AssemblyKind))
            .ThenByDescending(artifact => artifact.AssemblyLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static AssemblyArtifact CreateAdHocArtifact(ProjectArtifacts artifacts, string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        var pdbPath = Path.ChangeExtension(fullPath, ".pdb");
        return new AssemblyArtifact(
            artifacts.ProjectPath,
            fullPath,
            File.Exists(pdbPath) ? pdbPath : null,
            artifacts.TargetFramework,
            artifacts.Configuration,
            File.GetLastWriteTimeUtc(fullPath),
            File.Exists(pdbPath) ? File.GetLastWriteTimeUtc(pdbPath) : null,
            false)
        {
            AssemblyKind = InferAssemblyKind(fullPath),
            AssemblyName = Path.GetFileNameWithoutExtension(fullPath)
        };
    }

    private static GraphNode CreateAssemblyNode(AssemblyArtifact artifact)
    {
        return new GraphNode(
            Id: GraphNodeIds.Create(GraphNodeKinds.Assembly, artifact.AssemblyPath, artifact.AssemblyName),
            Kind: GraphNodeKinds.Assembly,
            Label: artifact.AssemblyName,
            AssemblyName: artifact.AssemblyName,
            AssemblyKind: artifact.AssemblyKind,
            AssemblyPath: artifact.AssemblyPath,
            HasChildren: true,
            DecompileAvailable: true,
            IsExternal: IsExternalAssembly(artifact));
    }

    private static GraphNode CreateExternalAssemblyNode(string assemblyName)
    {
        return new GraphNode(
            Id: GraphNodeIds.Create(GraphNodeKinds.Assembly, null, assemblyName),
            Kind: GraphNodeKinds.Assembly,
            Label: assemblyName,
            AssemblyName: assemblyName,
            AssemblyKind: AssemblyKinds.ExternalUnknown,
            HasChildren: false,
            IsExternal: true);
    }

    private static GraphNode CreateTypeNode(AssemblyArtifact artifact, TypeDefinition type)
    {
        return new GraphNode(
            Id: GraphNodeIds.Create(GraphNodeKinds.Type, artifact.AssemblyPath, artifact.AssemblyName, type.FullName, FormatMetadataToken(type.MetadataToken), type.FullName),
            Kind: GraphNodeKinds.Type,
            Label: type.FullName,
            AssemblyName: artifact.AssemblyName,
            AssemblyKind: artifact.AssemblyKind,
            AssemblyPath: artifact.AssemblyPath,
            TypeName: type.FullName,
            MetadataToken: FormatMetadataToken(type.MetadataToken),
            Signature: type.FullName,
            HasChildren: type.HasMethods || type.HasNestedTypes,
            DecompileAvailable: true,
            IsExternal: IsExternalAssembly(artifact));
    }

    private static GraphNode CreateExternalTypeNode(string typeName, AssemblyArtifact? artifact, string signature)
    {
        var assemblyName = artifact?.AssemblyName ?? "external";
        return new GraphNode(
            Id: GraphNodeIds.Create(GraphNodeKinds.Type, artifact?.AssemblyPath, assemblyName, typeName, signature: signature),
            Kind: GraphNodeKinds.Type,
            Label: signature,
            AssemblyName: assemblyName,
            AssemblyKind: artifact?.AssemblyKind ?? AssemblyKinds.ExternalUnknown,
            AssemblyPath: artifact?.AssemblyPath,
            TypeName: typeName,
            Signature: signature,
            HasChildren: artifact?.AssemblyPath is not null,
            DecompileAvailable: artifact?.AssemblyPath is not null,
            IsExternal: true);
    }

    private static GraphNode CreateMethodNode(AssemblyArtifact artifact, MethodDefinition method)
    {
        return new GraphNode(
            Id: GraphNodeIds.Create(GraphNodeKinds.Method, artifact.AssemblyPath, artifact.AssemblyName, method.DeclaringType.FullName, FormatMetadataToken(method.MetadataToken), method.FullName),
            Kind: GraphNodeKinds.Method,
            Label: method.FullName,
            AssemblyName: artifact.AssemblyName,
            AssemblyKind: artifact.AssemblyKind,
            AssemblyPath: artifact.AssemblyPath,
            TypeName: method.DeclaringType.FullName,
            MethodName: method.Name,
            MetadataToken: FormatMetadataToken(method.MetadataToken),
            Signature: method.FullName,
            SourceRange: BuildSourceRange(method),
            HasChildren: method.HasBody,
            DecompileAvailable: true,
            IsExternal: IsExternalAssembly(artifact));
    }

    private static GraphNode CreateExternalMethodNode(MethodReference method, AssemblyArtifact? artifact)
    {
        var assemblyName = artifact?.AssemblyName ?? ResolveAssemblyName(method.DeclaringType.Scope);
        return new GraphNode(
            Id: GraphNodeIds.Create(GraphNodeKinds.Method, artifact?.AssemblyPath, assemblyName, method.DeclaringType.FullName, signature: method.FullName),
            Kind: GraphNodeKinds.Method,
            Label: method.FullName,
            AssemblyName: assemblyName,
            AssemblyKind: artifact?.AssemblyKind ?? AssemblyKinds.ExternalUnknown,
            AssemblyPath: artifact?.AssemblyPath,
            TypeName: method.DeclaringType.FullName,
            MethodName: method.Name,
            Signature: method.FullName,
            HasChildren: artifact?.AssemblyPath is not null,
            DecompileAvailable: artifact?.AssemblyPath is not null,
            IsExternal: true);
    }

    private static TypeDefinition? FindType(ModuleDefinition module, string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        return module.Types.SelectMany(GetTypes).FirstOrDefault(type => string.Equals(type.FullName, typeName, StringComparison.Ordinal));
    }

    private static MethodDefinition? FindMethod(ModuleDefinition module, GraphNodeKey key)
    {
        if (!string.IsNullOrWhiteSpace(key.MetadataToken) && TryParseMetadataToken(key.MetadataToken, out var metadataToken))
        {
            try
            {
                if (module.LookupToken(metadataToken) is MethodDefinition method)
                {
                    return method;
                }
            }
            catch
            {
                // Fall back to signature matching below.
            }
        }

        return module.Types
            .SelectMany(GetTypes)
            .Where(type => string.IsNullOrWhiteSpace(key.TypeName) || string.Equals(type.FullName, key.TypeName, StringComparison.Ordinal))
            .SelectMany(type => type.Methods)
            .FirstOrDefault(method => string.Equals(method.FullName, key.Signature, StringComparison.Ordinal)
                || string.Equals(method.Name, key.Signature, StringComparison.Ordinal)
                || string.Equals(method.Name, key.MetadataToken, StringComparison.Ordinal));
    }

    private static IEnumerable<TypeDefinition> GetTypes(TypeDefinition type)
    {
        yield return type;
        foreach (var nestedType in type.NestedTypes.SelectMany(GetTypes))
        {
            yield return nestedType;
        }
    }

    private static SourceRange? BuildSourceRange(MethodDefinition method)
    {
        var sequencePoints = method.DebugInformation.SequencePoints
            .Where(sequencePoint => !sequencePoint.IsHidden && sequencePoint.StartLine > 0)
            .ToList();
        if (sequencePoints.Count == 0)
        {
            return null;
        }

        var startLine = sequencePoints.Min(sequencePoint => sequencePoint.StartLine);
        var endLine = sequencePoints.Max(sequencePoint => sequencePoint.EndLine >= sequencePoint.StartLine ? sequencePoint.EndLine : sequencePoint.StartLine);
        var startColumn = sequencePoints.Where(sequencePoint => sequencePoint.StartLine == startLine).Min(sequencePoint => Math.Max(sequencePoint.StartColumn, 1));
        var endColumn = sequencePoints
            .Where(sequencePoint => (sequencePoint.EndLine >= sequencePoint.StartLine ? sequencePoint.EndLine : sequencePoint.StartLine) == endLine)
            .Max(sequencePoint => Math.Max(sequencePoint.EndColumn, 1));
        return new SourceRange(startLine, endLine, startColumn, endColumn);
    }

    private static MethodDefinition? TryResolve(MethodReference method)
    {
        try
        {
            return method.Resolve();
        }
        catch
        {
            return null;
        }
    }

    private static FieldDefinition? TryResolve(FieldReference field)
    {
        try
        {
            return field.Resolve();
        }
        catch
        {
            return null;
        }
    }

    private static TypeDefinition? TryResolve(TypeReference type)
    {
        try
        {
            return type.Resolve();
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveAssemblyName(IMetadataScope scope)
    {
        return scope switch
        {
            AssemblyNameReference assemblyName => assemblyName.Name,
            ModuleDefinition module => module.Assembly.Name.Name,
            ModuleReference moduleReference => moduleReference.Name,
            _ => scope.Name
        };
    }

    private static string FormatMetadataToken(MetadataToken token)
    {
        return $"0x{token.ToInt32():x8}";
    }

    private static bool TryParseMetadataToken(string value, out MetadataToken metadataToken)
    {
        var normalized = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var token))
        {
            metadataToken = new MetadataToken(unchecked((uint)token));
            return true;
        }

        metadataToken = default;
        return false;
    }

    private static int AssemblyKindOrder(string assemblyKind)
    {
        return assemblyKind switch
        {
            AssemblyKinds.Project => 0,
            AssemblyKinds.ProjectReference => 1,
            AssemblyKinds.Nuget => 2,
            AssemblyKinds.Framework => 3,
            AssemblyKinds.Runtime => 4,
            _ => 5
        };
    }

    private static string InferAssemblyKind(string assemblyPath)
    {
        var normalized = assemblyPath.Replace('\\', '/');
        if (normalized.Contains("/.nuget/packages/", StringComparison.OrdinalIgnoreCase))
        {
            return AssemblyKinds.Nuget;
        }

        if (normalized.Contains("/shared/Microsoft.NETCore.App/", StringComparison.OrdinalIgnoreCase))
        {
            return AssemblyKinds.Runtime;
        }

        if (normalized.Contains("/shared/", StringComparison.OrdinalIgnoreCase))
        {
            return AssemblyKinds.Framework;
        }

        return AssemblyKinds.ExternalUnknown;
    }

    private static bool IsExternalAssembly(AssemblyArtifact artifact)
    {
        return !string.Equals(artifact.AssemblyKind, AssemblyKinds.Project, StringComparison.Ordinal)
            && !string.Equals(artifact.AssemblyKind, AssemblyKinds.ProjectReference, StringComparison.Ordinal);
    }

    private sealed record GraphEntry(GraphNode Node, GraphEdge Edge);
}
