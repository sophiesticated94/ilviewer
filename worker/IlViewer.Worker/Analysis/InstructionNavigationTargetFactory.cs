using IlViewer.Worker.Models;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace IlViewer.Worker.Analysis;

public sealed class InstructionNavigationTargetFactory
{
    public IReadOnlyList<IlNavigationTarget> Build(MethodCandidate candidate, Instruction instruction)
    {
        var targets = new List<IlNavigationTarget>();
        AddBranchTargets(targets, candidate, instruction.Operand);
        AddSymbolTarget(targets, instruction.Operand);
        return targets;
    }

    private static void AddBranchTargets(ICollection<IlNavigationTarget> targets, MethodCandidate candidate, object? operand)
    {
        switch (operand)
        {
            case Instruction targetInstruction:
                targets.Add(CreateBranchTarget(candidate, targetInstruction));
                break;
            case Instruction[] targetInstructions:
                foreach (var targetInstruction in targetInstructions)
                {
                    targets.Add(CreateBranchTarget(candidate, targetInstruction));
                }

                break;
        }
    }

    private static IlNavigationTarget CreateBranchTarget(MethodCandidate candidate, Instruction targetInstruction)
    {
        var targetInstructionId = $"{IlInstructionFactory.BuildMethodId(candidate)}:{targetInstruction.Offset:x4}";
        return new IlNavigationTarget(
            Id: $"il:{targetInstructionId}",
            Kind: NavigationTargetKinds.Il,
            Label: targetInstruction.ToString(),
            AssemblyName: candidate.AssemblyName,
            AssemblyPath: candidate.Artifact.AssemblyPath,
            AssemblyKind: candidate.Artifact.AssemblyKind,
            TypeName: candidate.Method.DeclaringType.FullName,
            MethodName: candidate.Method.Name,
            Signature: candidate.Method.FullName,
            MetadataToken: FormatMetadataToken(candidate.Method.MetadataToken),
            IlOffset: targetInstruction.Offset,
            TargetInstructionId: targetInstructionId);
    }

    private static void AddSymbolTarget(ICollection<IlNavigationTarget> targets, object? operand)
    {
        switch (operand)
        {
            case MethodReference method:
                targets.Add(CreateMethodTarget(method));
                break;
            case FieldReference field:
                targets.Add(CreateFieldTarget(field));
                break;
            case TypeReference type:
                targets.Add(CreateTypeTarget(type));
                break;
        }
    }

    private static IlNavigationTarget CreateMethodTarget(MethodReference method)
    {
        var resolved = TryResolve(method);
        var assemblyPath = resolved?.Module.FileName;
        var assemblyName = resolved?.Module.Assembly.Name.Name ?? ResolveAssemblyName(method.DeclaringType.Scope);
        var source = resolved is not null ? ResolveSource(resolved) : null;
        var nodeId = GraphNodeIds.Create(
            GraphNodeKinds.Method,
            assemblyPath,
            assemblyName,
            method.DeclaringType.FullName,
            resolved is not null ? FormatMetadataToken(resolved.MetadataToken) : null,
            method.FullName);

        return new IlNavigationTarget(
            Id: nodeId,
            Kind: source is not null ? NavigationTargetKinds.Source : NavigationTargetKinds.Decompiled,
            Label: method.FullName,
            AssemblyName: assemblyName,
            AssemblyPath: assemblyPath,
            AssemblyKind: ResolveAssemblyKind(assemblyPath),
            TypeName: method.DeclaringType.FullName,
            MethodName: method.Name,
            Signature: method.FullName,
            MetadataToken: resolved is not null ? FormatMetadataToken(resolved.MetadataToken) : null,
            SourcePath: source?.Path,
            SourceRange: source?.Range,
            Language: source?.Language,
            IsExternal: source is null,
            DecompileAvailable: true);
    }

    private static IlNavigationTarget CreateFieldTarget(FieldReference field)
    {
        var resolved = TryResolve(field);
        var assemblyPath = resolved?.Module.FileName;
        var assemblyName = resolved?.Module.Assembly.Name.Name ?? ResolveAssemblyName(field.DeclaringType.Scope);
        var nodeId = GraphNodeIds.Create(
            GraphNodeKinds.Type,
            assemblyPath,
            assemblyName,
            field.DeclaringType.FullName,
            resolved is not null ? FormatMetadataToken(resolved.MetadataToken) : null,
            field.FullName);

        return new IlNavigationTarget(
            Id: nodeId,
            Kind: NavigationTargetKinds.Decompiled,
            Label: field.FullName,
            AssemblyName: assemblyName,
            AssemblyPath: assemblyPath,
            AssemblyKind: ResolveAssemblyKind(assemblyPath),
            TypeName: field.DeclaringType.FullName,
            Signature: field.FullName,
            MetadataToken: resolved is not null ? FormatMetadataToken(resolved.MetadataToken) : null,
            IsExternal: true,
            DecompileAvailable: true);
    }

    private static IlNavigationTarget CreateTypeTarget(TypeReference type)
    {
        var resolved = TryResolve(type);
        var assemblyPath = resolved?.Module.FileName;
        var assemblyName = resolved?.Module.Assembly.Name.Name ?? ResolveAssemblyName(type.Scope);
        var nodeId = GraphNodeIds.Create(
            GraphNodeKinds.Type,
            assemblyPath,
            assemblyName,
            type.FullName,
            resolved is not null ? FormatMetadataToken(resolved.MetadataToken) : null,
            type.FullName);

        return new IlNavigationTarget(
            Id: nodeId,
            Kind: NavigationTargetKinds.Decompiled,
            Label: type.FullName,
            AssemblyName: assemblyName,
            AssemblyPath: assemblyPath,
            AssemblyKind: ResolveAssemblyKind(assemblyPath),
            TypeName: type.FullName,
            Signature: type.FullName,
            MetadataToken: resolved is not null ? FormatMetadataToken(resolved.MetadataToken) : null,
            IsExternal: true,
            DecompileAvailable: true);
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

    private static SourceLocation? ResolveSource(MethodDefinition method)
    {
        var sequencePoints = method.DebugInformation.SequencePoints
            .Where(sequencePoint => !sequencePoint.IsHidden && sequencePoint.StartLine > 0)
            .OrderBy(sequencePoint => sequencePoint.Offset)
            .ToList();
        if (sequencePoints.Count == 0)
        {
            return null;
        }

        var documentUrl = sequencePoints.First().Document.Url;
        var sourcePath = ToLocalPath(documentUrl);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return null;
        }

        var startLine = sequencePoints.Min(sequencePoint => sequencePoint.StartLine);
        var endLine = sequencePoints.Max(sequencePoint => sequencePoint.EndLine >= sequencePoint.StartLine ? sequencePoint.EndLine : sequencePoint.StartLine);
        var startColumn = sequencePoints.Where(sequencePoint => sequencePoint.StartLine == startLine).Min(sequencePoint => Math.Max(sequencePoint.StartColumn, 1));
        var endColumn = sequencePoints
            .Where(sequencePoint => (sequencePoint.EndLine >= sequencePoint.StartLine ? sequencePoint.EndLine : sequencePoint.StartLine) == endLine)
            .Max(sequencePoint => Math.Max(sequencePoint.EndColumn, 1));
        return new SourceLocation(sourcePath, new SourceRange(startLine, endLine, startColumn, endColumn), ResolveLanguage(sourcePath));
    }

    private static string? ToLocalPath(string documentUrl)
    {
        if (Uri.TryCreate(documentUrl, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        try
        {
            return Path.GetFullPath(documentUrl);
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

    private static string? ResolveAssemblyKind(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            return null;
        }

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

    private static string ResolveLanguage(string sourcePath)
    {
        return Path.GetExtension(sourcePath).ToLowerInvariant() switch
        {
            ".vb" => "vb",
            ".fs" => "fsharp",
            _ => "csharp"
        };
    }

    private static string FormatMetadataToken(MetadataToken token)
    {
        return $"0x{token.ToInt32():x8}";
    }

    private sealed record SourceLocation(string Path, SourceRange Range, string Language);
}
