using System.Xml.Linq;
using System.Text.Json;
using IlViewer.Worker.Models;

namespace IlViewer.Worker.Artifacts;

public sealed class ProjectArtifactResolver : IProjectArtifactResolver
{
    public ProjectArtifacts Resolve(AnalysisRequest request)
    {
        var root = ResolveProject(request.ProjectPath, request.Configuration, request.TargetFramework, isRoot: true, required: true)
            ?? throw new FileNotFoundException("Project assembly was not found.", request.ProjectPath);
        var applicationAssemblies = ResolveApplicationAssemblies(request, root).ToList();
        var referenceAssemblies = ResolveReferenceAssemblies(root, applicationAssemblies).ToList();
        var searchDirectories = referenceAssemblies
            .Select(artifact => Path.GetDirectoryName(artifact.AssemblyPath))
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProjectArtifacts(
            root.ProjectPath,
            root.AssemblyPath,
            root.PdbPath,
            root.TargetFramework,
            root.Configuration,
            root.AssemblyLastWriteTimeUtc,
            root.PdbLastWriteTimeUtc)
        {
            ApplicationAssemblies = applicationAssemblies,
            ReferenceAssemblies = referenceAssemblies,
            AssemblySearchDirectories = searchDirectories
        };
    }

    private static IEnumerable<AssemblyArtifact> ResolveApplicationAssemblies(AnalysisRequest request, AssemblyArtifact root)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<AssemblyArtifact>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(Path.GetFullPath(current.ProjectPath)))
            {
                continue;
            }

            yield return current;

            foreach (var referenceProjectPath in ReadProjectReferences(current.ProjectPath))
            {
                var reference = ResolveProject(referenceProjectPath, request.Configuration, request.TargetFramework, isRoot: false, required: false);
                if (reference is not null)
                {
                    queue.Enqueue(reference);
                }
            }
        }
    }

    private static AssemblyArtifact? ResolveProject(
        string projectPath,
        string configuration,
        string? requestedTargetFramework,
        bool isRoot,
        bool required)
    {
        if (!File.Exists(projectPath))
        {
            if (required)
            {
                throw new FileNotFoundException("Project file was not found.", projectPath);
            }

            return null;
        }

        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException("Project path does not have a parent directory.");
        var project = XDocument.Load(projectPath);
        var assemblyName = ReadProperty(project, "AssemblyName");
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            assemblyName = Path.GetFileNameWithoutExtension(projectPath);
        }

        var targetFramework = ResolveTargetFramework(project, requestedTargetFramework);
        var candidates = FindAssemblyCandidates(projectDirectory, assemblyName, configuration, targetFramework).ToList();
        var assemblyPath = candidates
            .Where(File.Exists)
            .OrderByDescending(candidate => File.GetLastWriteTimeUtc(candidate))
            .FirstOrDefault();

        if (assemblyPath is null)
        {
            if (required)
            {
                throw new FileNotFoundException($"Could not find '{assemblyName}.dll'. Build the project first or use the Przebuduj command.");
            }

            return null;
        }

        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        if (!File.Exists(pdbPath))
        {
            if (required)
            {
                throw new FileNotFoundException($"Could not find portable PDB next to '{assemblyPath}'. Build with debug symbols enabled.");
            }

            return null;
        }

        return new AssemblyArtifact(
            Path.GetFullPath(projectPath),
            Path.GetFullPath(assemblyPath),
            Path.GetFullPath(pdbPath),
            targetFramework,
            configuration,
            File.GetLastWriteTimeUtc(assemblyPath),
            File.GetLastWriteTimeUtc(pdbPath),
            isRoot)
        {
            AssemblyKind = isRoot ? AssemblyKinds.Project : AssemblyKinds.ProjectReference
        };
    }

    private static IEnumerable<AssemblyArtifact> ResolveReferenceAssemblies(AssemblyArtifact root, IReadOnlyList<AssemblyArtifact> applicationAssemblies)
    {
        var byPath = new Dictionary<string, AssemblyArtifact>(StringComparer.OrdinalIgnoreCase);

        foreach (var artifact in applicationAssemblies)
        {
            AddArtifact(byPath, artifact);
        }

        foreach (var artifact in ResolveOutputDirectoryAssemblies(root))
        {
            AddArtifact(byPath, artifact);
        }

        foreach (var artifact in ResolveDepsJsonAssemblies(root))
        {
            AddArtifact(byPath, artifact);
        }

        foreach (var artifact in ResolveSharedRuntimeAssemblies(root))
        {
            AddArtifact(byPath, artifact);
        }

        return byPath.Values
            .OrderBy(artifact => AssemblyKindOrder(artifact.AssemblyKind))
            .ThenBy(artifact => artifact.AssemblyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(artifact => artifact.AssemblyPath, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<AssemblyArtifact> ResolveOutputDirectoryAssemblies(AssemblyArtifact root)
    {
        var outputDirectory = Path.GetDirectoryName(root.AssemblyPath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            yield break;
        }

        foreach (var assemblyPath in Directory.EnumerateFiles(outputDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            yield return CreateExternalArtifact(root, assemblyPath, AssemblyKinds.ExternalUnknown);
        }
    }

    private static IEnumerable<AssemblyArtifact> ResolveDepsJsonAssemblies(AssemblyArtifact root)
    {
        var depsJsonPath = Path.ChangeExtension(root.AssemblyPath, ".deps.json");
        if (!File.Exists(depsJsonPath))
        {
            yield break;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(depsJsonPath));
        if (!document.RootElement.TryGetProperty("targets", out var targets)
            || !document.RootElement.TryGetProperty("libraries", out var libraries))
        {
            yield break;
        }

        var target = targets.EnumerateObject().FirstOrDefault().Value;
        if (target.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var libraryTarget in target.EnumerateObject())
        {
            if (!libraries.TryGetProperty(libraryTarget.Name, out var library)
                || !library.TryGetProperty("type", out var typeElement)
                || !string.Equals(typeElement.GetString(), "package", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var packagePath = library.TryGetProperty("path", out var pathElement)
                ? pathElement.GetString()
                : libraryTarget.Name.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                continue;
            }

            foreach (var asset in ReadRuntimeAssets(libraryTarget.Value))
            {
                var assemblyPath = Path.Combine(GetNuGetPackagesRoot(), ToPlatformPath(packagePath), ToPlatformPath(asset));
                if (!File.Exists(assemblyPath))
                {
                    continue;
                }

                var (packageName, packageVersion) = SplitPackageIdentity(libraryTarget.Name);
                var kind = IsRuntimePackage(packageName) ? AssemblyKinds.Runtime : AssemblyKinds.Nuget;
                yield return CreateExternalArtifact(root, assemblyPath, kind, packageName, packageVersion);
            }
        }
    }

    private static IEnumerable<string> ReadRuntimeAssets(JsonElement libraryTarget)
    {
        if (libraryTarget.TryGetProperty("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in runtime.EnumerateObject())
            {
                if (property.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    yield return property.Name;
                }
            }
        }

        if (libraryTarget.TryGetProperty("compile", out var compile) && compile.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in compile.EnumerateObject())
            {
                if (property.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && !property.Name.EndsWith("/_._", StringComparison.Ordinal))
                {
                    yield return property.Name;
                }
            }
        }
    }

    private static IEnumerable<AssemblyArtifact> ResolveSharedRuntimeAssemblies(AssemblyArtifact root)
    {
        foreach (var sharedDirectory in GetDotnetSharedDirectories())
        {
            var kind = sharedDirectory.Contains("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase)
                ? AssemblyKinds.Runtime
                : AssemblyKinds.Framework;
            foreach (var assemblyPath in Directory.EnumerateFiles(sharedDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                yield return CreateExternalArtifact(root, assemblyPath, kind);
            }
        }
    }

    private static IEnumerable<string> GetDotnetSharedDirectories()
    {
        var dotnetRoots = new[]
            {
                Environment.GetEnvironmentVariable("DOTNET_ROOT"),
                Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)"),
                Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")
            }
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Cast<string>()
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in dotnetRoots)
        {
            var sharedRoot = Path.Combine(root, "shared");
            if (!Directory.Exists(sharedRoot))
            {
                continue;
            }

            foreach (var familyDirectory in Directory.EnumerateDirectories(sharedRoot))
            {
                foreach (var versionDirectory in Directory.EnumerateDirectories(familyDirectory))
                {
                    yield return versionDirectory;
                }
            }
        }
    }

    private static AssemblyArtifact CreateExternalArtifact(
        AssemblyArtifact root,
        string assemblyPath,
        string assemblyKind,
        string? packageName = null,
        string? packageVersion = null)
    {
        var fullAssemblyPath = Path.GetFullPath(assemblyPath);
        var pdbPath = Path.ChangeExtension(fullAssemblyPath, ".pdb");
        return new AssemblyArtifact(
            root.ProjectPath,
            fullAssemblyPath,
            File.Exists(pdbPath) ? Path.GetFullPath(pdbPath) : null,
            root.TargetFramework,
            root.Configuration,
            File.GetLastWriteTimeUtc(fullAssemblyPath),
            File.Exists(pdbPath) ? File.GetLastWriteTimeUtc(pdbPath) : null,
            false)
        {
            AssemblyKind = assemblyKind,
            AssemblyName = Path.GetFileNameWithoutExtension(fullAssemblyPath),
            PackageName = packageName,
            PackageVersion = packageVersion
        };
    }

    private static void AddArtifact(IDictionary<string, AssemblyArtifact> byPath, AssemblyArtifact artifact)
    {
        byPath[Path.GetFullPath(artifact.AssemblyPath)] = artifact;
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

    private static string GetNuGetPackagesRoot()
    {
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    }

    private static string ToPlatformPath(string value)
    {
        return value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static (string PackageName, string? PackageVersion) SplitPackageIdentity(string libraryName)
    {
        var parts = libraryName.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (libraryName, null);
    }

    private static bool IsRuntimePackage(string packageName)
    {
        return packageName.StartsWith("runtimepack.", StringComparison.OrdinalIgnoreCase)
            || packageName.StartsWith("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveTargetFramework(XDocument project, string? requestedTargetFramework)
    {
        var targetFrameworks = ReadTargetFrameworks(project).ToList();
        if (!string.IsNullOrWhiteSpace(requestedTargetFramework)
            && (targetFrameworks.Count == 0 || targetFrameworks.Contains(requestedTargetFramework, StringComparer.OrdinalIgnoreCase)))
        {
            return requestedTargetFramework;
        }

        if (targetFrameworks.Count > 0)
        {
            return targetFrameworks[0];
        }

        return string.Empty;
    }

    private static IEnumerable<string> ReadTargetFrameworks(XDocument project)
    {
        var targetFramework = ReadProperty(project, "TargetFramework");
        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            yield return targetFramework;
        }

        var targetFrameworks = ReadProperty(project, "TargetFrameworks");
        if (string.IsNullOrWhiteSpace(targetFrameworks))
        {
            yield break;
        }

        foreach (var value in targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return value;
        }
    }

    private static IEnumerable<string> FindAssemblyCandidates(string projectDirectory, string assemblyName, string configuration, string targetFramework)
    {
        var fileName = assemblyName + ".dll";
        var binDirectory = Path.Combine(projectDirectory, "bin");

        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            yield return Path.Combine(binDirectory, configuration, targetFramework, fileName);
        }

        yield return Path.Combine(binDirectory, configuration, fileName);

        if (!Directory.Exists(binDirectory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(binDirectory, fileName, SearchOption.AllDirectories))
        {
            if (PathContainsSegment(path, configuration))
            {
                yield return path;
            }
        }
    }

    private static bool PathContainsSegment(string path, string segment)
    {
        return path
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, segment, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadProperty(XDocument document, string propertyName)
    {
        return document
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim();
    }

    private static IEnumerable<string> ReadProjectReferences(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException("Project path does not have a parent directory.");
        var project = XDocument.Load(projectPath);

        return project
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include!)));
    }
}
