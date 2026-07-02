using System.Text.Json;
using IlViewer.Worker.Analysis;
using IlViewer.Worker.Artifacts;
using IlViewer.Worker.Models;

namespace IlViewer.Worker.Cli;

public sealed class WorkerCli
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IProjectArtifactResolver _artifactResolver;
    private readonly IAssemblyIlAnalyzer _assemblyIlAnalyzer;
    private readonly IApplicationGraphService _applicationGraphService;
    private readonly IDecompilationService _decompilationService;

    public WorkerCli(
        IProjectArtifactResolver artifactResolver,
        IAssemblyIlAnalyzer assemblyIlAnalyzer,
        IApplicationGraphService? applicationGraphService = null,
        IDecompilationService? decompilationService = null)
    {
        _artifactResolver = artifactResolver;
        _assemblyIlAnalyzer = assemblyIlAnalyzer;
        var moduleLoader = new CecilModuleLoader();
        _applicationGraphService = applicationGraphService ?? new ApplicationGraphService(moduleLoader);
        _decompilationService = decompilationService ?? new DecompilationService(moduleLoader);
    }

    public async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            await stderr.WriteLineAsync("Usage: IlViewer.Worker <analyze|graph-root|graph-expand|decompile> --project <path> [options]");
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "analyze":
                {
                    var request = ParseAnalyzeRequest(args.Skip(1).ToArray());
                    var artifacts = _artifactResolver.Resolve(request);
                    var result = _assemblyIlAnalyzer.Analyze(request, artifacts);
                    await stdout.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
                    return result.Success ? 0 : 2;
                }
                case "graph-root":
                {
                    var request = ParseGraphRequest(args.Skip(1).ToArray(), requireNodeId: false);
                    var artifacts = _artifactResolver.Resolve(ToAnalysisRequest(request));
                    var result = _applicationGraphService.GetRoot(artifacts, request);
                    await stdout.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
                    return result.Success ? 0 : 2;
                }
                case "graph-expand":
                {
                    var request = ParseGraphRequest(args.Skip(1).ToArray(), requireNodeId: true);
                    var artifacts = _artifactResolver.Resolve(ToAnalysisRequest(request));
                    var result = _applicationGraphService.Expand(artifacts, request);
                    await stdout.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
                    return result.Success ? 0 : 2;
                }
                case "decompile":
                {
                    var request = ParseDecompileRequest(args.Skip(1).ToArray());
                    var artifacts = _artifactResolver.Resolve(ToAnalysisRequest(request));
                    var result = _decompilationService.Decompile(artifacts, request);
                    await stdout.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
                    return result.Success ? 0 : 2;
                }
                default:
                    await WriteCommandFailureAsync(stdout, args[0], $"Unknown command '{args[0]}'.");
                    return 1;
            }
        }
        catch (Exception exception)
        {
            await WriteCommandFailureAsync(stdout, args[0], exception.Message);
            return 2;
        }
    }

    private static AnalysisRequest ParseAnalyzeRequest(IReadOnlyList<string> args)
    {
        var options = ReadOptions(args);

        var projectPath = Require(options, "project");
        var documentPath = Require(options, "document");
        var line = ParsePositiveInt(Require(options, "line"), "line");
        var endLine = options.TryGetValue("end-line", out var endLineValue)
            ? ParsePositiveInt(endLineValue, "end-line")
            : line;
        var startColumn = options.TryGetValue("start-column", out var startColumnValue)
            ? ParsePositiveInt(startColumnValue, "start-column")
            : 1;
        var endColumn = options.TryGetValue("end-column", out var endColumnValue)
            ? ParsePositiveInt(endColumnValue, "end-column")
            : startColumn;

        if (endLine < line)
        {
            (line, endLine) = (endLine, line);
            (startColumn, endColumn) = (endColumn, startColumn);
        }

        options.TryGetValue("configuration", out var configuration);
        options.TryGetValue("target-framework", out var targetFramework);

        return new AnalysisRequest(
            Path.GetFullPath(projectPath),
            Path.GetFullPath(documentPath),
            line,
            endLine,
            string.IsNullOrWhiteSpace(configuration) ? "Debug" : configuration,
            string.IsNullOrWhiteSpace(targetFramework) ? null : targetFramework,
            startColumn,
            endColumn);
    }

    private static GraphRequest ParseGraphRequest(IReadOnlyList<string> args, bool requireNodeId)
    {
        var options = ReadOptions(args);
        var projectPath = Path.GetFullPath(Require(options, "project"));
        options.TryGetValue("configuration", out var configuration);
        options.TryGetValue("target-framework", out var targetFramework);
        options.TryGetValue("node-id", out var nodeId);
        options.TryGetValue("continuation-token", out var continuationToken);
        var pageSize = options.TryGetValue("page-size", out var pageSizeValue)
            ? ParsePositiveInt(pageSizeValue, "page-size")
            : 250;

        if (requireNodeId && string.IsNullOrWhiteSpace(nodeId))
        {
            throw new ArgumentException("Missing required option '--node-id'.");
        }

        return new GraphRequest(
            projectPath,
            string.IsNullOrWhiteSpace(configuration) ? "Debug" : configuration,
            string.IsNullOrWhiteSpace(targetFramework) ? null : targetFramework,
            string.IsNullOrWhiteSpace(nodeId) ? null : nodeId,
            pageSize,
            string.IsNullOrWhiteSpace(continuationToken) ? null : continuationToken);
    }

    private static DecompileRequest ParseDecompileRequest(IReadOnlyList<string> args)
    {
        var options = ReadOptions(args);
        var projectPath = Path.GetFullPath(Require(options, "project"));
        options.TryGetValue("configuration", out var configuration);
        options.TryGetValue("target-framework", out var targetFramework);
        options.TryGetValue("assembly-path", out var assemblyPath);
        options.TryGetValue("assembly-name", out var assemblyName);
        options.TryGetValue("type-name", out var typeName);
        options.TryGetValue("method-name", out var methodName);
        options.TryGetValue("metadata-token", out var metadataToken);
        options.TryGetValue("language", out var language);

        return new DecompileRequest(
            projectPath,
            string.IsNullOrWhiteSpace(configuration) ? "Debug" : configuration,
            string.IsNullOrWhiteSpace(targetFramework) ? null : targetFramework,
            string.IsNullOrWhiteSpace(assemblyPath) ? null : Path.GetFullPath(assemblyPath),
            string.IsNullOrWhiteSpace(assemblyName) ? null : assemblyName,
            string.IsNullOrWhiteSpace(typeName) ? null : typeName,
            string.IsNullOrWhiteSpace(methodName) ? null : methodName,
            string.IsNullOrWhiteSpace(metadataToken) ? null : metadataToken,
            string.IsNullOrWhiteSpace(language) ? null : language);
    }

    private static Dictionary<string, string> ReadOptions(IReadOnlyList<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Count; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{key}'.");
            }

            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Missing value for '{key}'.");
            }

            options[key[2..]] = args[++i];
        }

        return options;
    }

    private static AnalysisRequest ToAnalysisRequest(GraphRequest request)
    {
        return new AnalysisRequest(
            request.ProjectPath,
            request.ProjectPath,
            1,
            1,
            request.Configuration,
            request.TargetFramework,
            1,
            1);
    }

    private static AnalysisRequest ToAnalysisRequest(DecompileRequest request)
    {
        return new AnalysisRequest(
            request.ProjectPath,
            request.ProjectPath,
            1,
            1,
            request.Configuration,
            request.TargetFramework,
            1,
            1);
    }

    private static string Require(IReadOnlyDictionary<string, string> options, string key)
    {
        if (options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new ArgumentException($"Missing required option '--{key}'.");
    }

    private static int ParsePositiveInt(string value, string name)
    {
        if (int.TryParse(value, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        throw new ArgumentException($"Option '--{name}' must be a positive integer.");
    }

    private static Task WriteFailureAsync(TextWriter stdout, string message)
    {
        var result = AnalysisResult.Failure(message);
        return stdout.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
    }

    private static Task WriteCommandFailureAsync(TextWriter stdout, string command, string message)
    {
        object result = command.ToLowerInvariant() switch
        {
            "graph-root" or "graph-expand" => GraphExpandResult.Failure(message),
            "decompile" => DecompileResult.Failure(message),
            _ => AnalysisResult.Failure(message)
        };
        return stdout.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
    }
}
