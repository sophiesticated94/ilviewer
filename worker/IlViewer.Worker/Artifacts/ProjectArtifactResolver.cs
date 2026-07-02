using System.Xml.Linq;
using IlViewer.Worker.Models;

namespace IlViewer.Worker.Artifacts;

public sealed class ProjectArtifactResolver : IProjectArtifactResolver
{
    public ProjectArtifacts Resolve(AnalysisRequest request)
    {
        var root = ResolveProject(request.ProjectPath, request.Configuration, request.TargetFramework, isRoot: true, required: true)
            ?? throw new FileNotFoundException("Project assembly was not found.", request.ProjectPath);
        var applicationAssemblies = ResolveApplicationAssemblies(request, root).ToList();

        return new ProjectArtifacts(
            root.ProjectPath,
            root.AssemblyPath,
            root.PdbPath,
            root.TargetFramework,
            root.Configuration,
            root.AssemblyLastWriteTimeUtc,
            root.PdbLastWriteTimeUtc)
        {
            ApplicationAssemblies = applicationAssemblies
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
            isRoot);
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
