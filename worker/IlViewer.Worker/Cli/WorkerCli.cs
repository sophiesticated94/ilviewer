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

    public WorkerCli(IProjectArtifactResolver artifactResolver, IAssemblyIlAnalyzer assemblyIlAnalyzer)
    {
        _artifactResolver = artifactResolver;
        _assemblyIlAnalyzer = assemblyIlAnalyzer;
    }

    public async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            await stderr.WriteLineAsync("Usage: IlViewer.Worker analyze --project <path> --document <path> --line <number> [--end-line <number>] [--start-column <number>] [--end-column <number>] [--configuration Debug] [--target-framework net8.0]");
            return args.Length == 0 ? 1 : 0;
        }

        if (!string.Equals(args[0], "analyze", StringComparison.OrdinalIgnoreCase))
        {
            await WriteFailureAsync(stdout, $"Unknown command '{args[0]}'.");
            return 1;
        }

        try
        {
            var request = ParseAnalyzeRequest(args.Skip(1).ToArray());
            var artifacts = _artifactResolver.Resolve(request);
            var result = _assemblyIlAnalyzer.Analyze(request, artifacts);
            await stdout.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
            return result.Success ? 0 : 2;
        }
        catch (Exception exception)
        {
            await WriteFailureAsync(stdout, exception.Message);
            return 2;
        }
    }

    private static AnalysisRequest ParseAnalyzeRequest(IReadOnlyList<string> args)
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
}
