using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
using IlViewer.Worker.Models;
using Mono.Cecil;

namespace IlViewer.Worker.Analysis;

public sealed class DecompilationService : IDecompilationService
{
    private readonly CecilModuleLoader _moduleLoader;

    public DecompilationService(CecilModuleLoader moduleLoader)
    {
        _moduleLoader = moduleLoader;
    }

    public DecompileResult Decompile(ProjectArtifacts artifacts, DecompileRequest request)
    {
        var artifact = ResolveArtifact(artifacts, request);
        if (artifact is null)
        {
            return DecompileResult.Failure("Nie udało się odnaleźć assembly do dekompilacji.");
        }

        try
        {
            var typeName = ResolveTypeName(artifacts, artifact, request);
            var csharp = DecompileCSharp(artifact.AssemblyPath, typeName);
            var title = BuildTitle(artifact, request, typeName);
            var requestedLanguage = NormalizeLanguage(request.Language);

            if (requestedLanguage == "vb")
            {
                return new DecompileResult
                {
                    Success = true,
                    Language = "plaintext",
                    Title = title + " (VB pseudokod + C#)",
                    Content = BuildVbFallback(artifacts, artifact, request, csharp),
                    SourceAvailable = false,
                    Diagnostics = ["VB bez źródła jest pseudokodem pomocniczym; C# pochodzi z ILSpy."]
                };
            }

            return new DecompileResult
            {
                Success = true,
                Language = "csharp",
                Title = title,
                Content = csharp,
                SourceAvailable = false
            };
        }
        catch (Exception exception)
        {
            return DecompileResult.Failure(exception.Message);
        }
    }

    private static string DecompileCSharp(string assemblyPath, string? typeName)
    {
        var decompiler = new CSharpDecompiler(assemblyPath, new DecompilerSettings());
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            try
            {
                return decompiler.DecompileTypeAsString(new FullTypeName(typeName));
            }
            catch
            {
                // Some compiler-generated names are not accepted by FullTypeName. Fall back to whole module.
            }
        }

        return decompiler.DecompileWholeModuleAsString();
    }

    private string BuildVbFallback(ProjectArtifacts artifacts, AssemblyArtifact artifact, DecompileRequest request, string csharp)
    {
        var pseudoCode = BuildVbPseudoCode(artifacts, artifact, request);
        return $"""
' VB pseudokod wygenerowany z IL. To jest przybliżenie, nie gwarancja kompilowalnego VB.
{pseudoCode}

' ===== C# z ILSpy =====
{csharp}
""";
    }

    private string BuildVbPseudoCode(ProjectArtifacts artifacts, AssemblyArtifact artifact, DecompileRequest request)
    {
        using var module = _moduleLoader.LoadModule(artifacts, artifact.AssemblyPath);
        var type = FindType(module, request.TypeName);
        if (type is null)
        {
            return "' Nie udało się zawęzić dekompilacji do typu; poniżej dostępny jest C# z ILSpy.";
        }

        var methods = type.Methods
            .Where(method => string.IsNullOrWhiteSpace(request.MethodName)
                || string.Equals(method.Name, request.MethodName, StringComparison.Ordinal)
                || string.Equals(method.FullName, request.MethodName, StringComparison.Ordinal))
            .DefaultIfEmpty(type.Methods.FirstOrDefault())
            .Where(method => method is not null)
            .Cast<MethodDefinition>();

        var lines = new List<string>();
        lines.Add($"Class {SanitizeVbIdentifier(type.Name)}");
        foreach (var method in methods)
        {
            lines.Add($"  Sub {SanitizeVbIdentifier(method.Name)}()");
            if (!method.HasBody)
            {
                lines.Add("    ' Brak ciała IL.");
            }
            else
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    lines.Add($"    ' {instruction}");
                }
            }

            lines.Add("  End Sub");
        }

        lines.Add("End Class");
        return string.Join(Environment.NewLine, lines);
    }

    private static AssemblyArtifact? ResolveArtifact(ProjectArtifacts artifacts, DecompileRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.AssemblyPath) && File.Exists(request.AssemblyPath))
        {
            return artifacts.ReferenceAssemblies.FirstOrDefault(artifact => string.Equals(artifact.AssemblyPath, request.AssemblyPath, StringComparison.OrdinalIgnoreCase))
                ?? CreateAdHocArtifact(artifacts, request.AssemblyPath);
        }

        if (!string.IsNullOrWhiteSpace(request.AssemblyName))
        {
            return artifacts.ReferenceAssemblies
                .Where(artifact => string.Equals(artifact.AssemblyName, request.AssemblyName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(artifact => AssemblyKindOrder(artifact.AssemblyKind))
                .ThenByDescending(artifact => artifact.AssemblyLastWriteTimeUtc)
                .FirstOrDefault();
        }

        return artifacts.ReferenceAssemblies.FirstOrDefault(artifact => artifact.IsRoot);
    }

    private string? ResolveTypeName(ProjectArtifacts artifacts, AssemblyArtifact artifact, DecompileRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.TypeName))
        {
            return request.TypeName;
        }

        if (string.IsNullOrWhiteSpace(request.MetadataToken))
        {
            return null;
        }

        using var module = _moduleLoader.LoadModule(artifacts, artifact.AssemblyPath);
        if (!TryParseMetadataToken(request.MetadataToken, out var token))
        {
            return null;
        }

        return module.LookupToken(token) switch
        {
            TypeDefinition type => type.FullName,
            MethodDefinition method => method.DeclaringType.FullName,
            FieldDefinition field => field.DeclaringType.FullName,
            _ => null
        };
    }

    private static TypeDefinition? FindType(ModuleDefinition module, string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        return module.Types.SelectMany(GetTypes).FirstOrDefault(type => string.Equals(type.FullName, typeName, StringComparison.Ordinal));
    }

    private static IEnumerable<TypeDefinition> GetTypes(TypeDefinition type)
    {
        yield return type;
        foreach (var nestedType in type.NestedTypes.SelectMany(GetTypes))
        {
            yield return nestedType;
        }
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
            AssemblyKind = AssemblyKinds.ExternalUnknown,
            AssemblyName = Path.GetFileNameWithoutExtension(fullPath)
        };
    }

    private static string BuildTitle(AssemblyArtifact artifact, DecompileRequest request, string? typeName)
    {
        var name = request.MethodName ?? typeName ?? artifact.AssemblyName;
        return $"{artifact.AssemblyName}: {name}";
    }

    private static string NormalizeLanguage(string? language)
    {
        if (string.Equals(language, "vb", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "visualbasic", StringComparison.OrdinalIgnoreCase))
        {
            return "vb";
        }

        return "csharp";
    }

    private static string SanitizeVbIdentifier(string value)
    {
        var identifier = new string(value.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
        return string.IsNullOrWhiteSpace(identifier) ? "GeneratedMember" : identifier;
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

    private static bool TryParseMetadataToken(string value, out MetadataToken metadataToken)
    {
        var normalized = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (int.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, null, out var token))
        {
            metadataToken = new MetadataToken(unchecked((uint)token));
            return true;
        }

        metadataToken = default;
        return false;
    }
}
