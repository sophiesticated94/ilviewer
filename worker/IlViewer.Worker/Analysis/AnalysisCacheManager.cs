using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IlViewer.Worker.Models;

namespace IlViewer.Worker.Analysis;

public sealed class CacheMetadata
{
    public int CacheSchemaVersion { get; set; }
    public string WorkerVersion { get; set; } = "1.0.0";
    public string WorkerBuildHash { get; set; } = "dev";
    public string AssemblyPath { get; set; } = "";
    public string? AssemblyMvid { get; set; }
    public string AssemblyLastWriteTimeUtc { get; set; } = "";
    public long AssemblyLength { get; set; }
    public string? PdbPath { get; set; }
    public string? PdbLastWriteTimeUtc { get; set; }
    public long PdbLength { get; set; }
    public string TargetFramework { get; set; } = "";
    public string Configuration { get; set; } = "";
}

public sealed class CachedSequencePoint
{
    public int Offset { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public int StartColumn { get; set; }
    public int EndColumn { get; set; }
    public bool IsHidden { get; set; }
}

public sealed class CachedMethodCandidate
{
    public int Token { get; set; }
    public string FullName { get; set; } = "";
    public string TypeFullName { get; set; } = "";
    public string MethodName { get; set; } = "";
    public SourceRange? SourceRange { get; set; }
    public List<CachedSequencePoint> SequencePoints { get; set; } = [];
}

public sealed class AssemblyIndexCache
{
    public CacheMetadata Metadata { get; set; } = new();
    public Dictionary<string, List<CachedMethodCandidate>> Documents { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CachedMethodData
{
    public int Token { get; set; }
    public MethodIl Context { get; set; } = null!;
    public List<SourceRegion> AllSourceRegions { get; set; } = [];
    public List<InstructionHighlight> AllInstructionHighlights { get; set; } = [];
    public List<InstructionExplanation> Explanations { get; set; } = [];
    public List<IlScope> Scopes { get; set; } = [];
}

public sealed class AnalysisCacheManager
{
    private const int SchemaVersion = 1;
    private static readonly string SchemaDirName = $"schema-{SchemaVersion:D4}";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public string? GetCacheDirectory(string projectPath)
    {
        var projectDir = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
        {
            return null;
        }

        var objDir = Path.Combine(projectDir, "obj");
        return Path.Combine(objDir, "IlAnalysisCache", SchemaDirName);
    }

    public void OpportunisticCleanup(string projectPath)
    {
        try
        {
            var projectDir = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrWhiteSpace(projectDir)) return;

            var cacheBaseDir = Path.Combine(projectDir, "obj", "IlAnalysisCache");
            if (!Directory.Exists(cacheBaseDir)) return;

            // Delete old schema directories
            foreach (var dir in Directory.GetDirectories(cacheBaseDir))
            {
                var name = Path.GetFileName(dir);
                if (name != SchemaDirName)
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
        catch
        {
            // Ignore cleanup failures to not block main analysis
        }
    }

    public string GetCacheKey(string assemblyPath)
    {
        var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(assemblyPath.ToLowerInvariant()));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public AssemblyIndexCache? LoadIndex(string projectPath, ProjectArtifacts artifacts, string mvid)
    {
        var cacheDir = GetCacheDirectory(projectPath);
        if (cacheDir is null || !Directory.Exists(cacheDir))
        {
            return null;
        }

        var key = GetCacheKey(artifacts.AssemblyPath);
        var indexPath = Path.Combine(cacheDir, $"{key}.index.json");

        if (!File.Exists(indexPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(indexPath);
            var index = JsonSerializer.Deserialize<AssemblyIndexCache>(json, JsonOptions);

            if (index is null || !ValidateMetadata(index.Metadata, artifacts, mvid))
            {
                DeleteCacheFile(indexPath);
                return null;
            }

            return index;
        }
        catch
        {
            DeleteCacheFile(indexPath);
            return null;
        }
    }

    public void SaveIndex(string projectPath, ProjectArtifacts artifacts, string mvid, AssemblyIndexCache index)
    {
        var cacheDir = GetCacheDirectory(projectPath);
        if (cacheDir is null) return;

        try
        {
            Directory.CreateDirectory(cacheDir);
            var key = GetCacheKey(artifacts.AssemblyPath);
            var indexPath = Path.Combine(cacheDir, $"{key}.index.json");

            index.Metadata = CreateMetadata(artifacts, mvid);
            var json = JsonSerializer.Serialize(index, JsonOptions);
            WriteFileAtomic(indexPath, json);
        }
        catch
        {
            // Ignore cache write errors to not block compilation
        }
    }

    public CachedMethodData? LoadMethodData(string projectPath, ProjectArtifacts artifacts, int token)
    {
        var cacheDir = GetCacheDirectory(projectPath);
        if (cacheDir is null || !Directory.Exists(cacheDir))
        {
            return null;
        }

        var key = GetCacheKey(artifacts.AssemblyPath);
        var methodPath = Path.Combine(cacheDir, $"{key}.method-0x{token:x8}.json");

        if (!File.Exists(methodPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(methodPath);
            return JsonSerializer.Deserialize<CachedMethodData>(json, JsonOptions);
        }
        catch
        {
            DeleteCacheFile(methodPath);
            return null;
        }
    }

    public void SaveMethodData(string projectPath, ProjectArtifacts artifacts, int token, CachedMethodData data)
    {
        var cacheDir = GetCacheDirectory(projectPath);
        if (cacheDir is null) return;

        try
        {
            Directory.CreateDirectory(cacheDir);
            var key = GetCacheKey(artifacts.AssemblyPath);
            var methodPath = Path.Combine(cacheDir, $"{key}.method-0x{token:x8}.json");

            var json = JsonSerializer.Serialize(data, JsonOptions);
            WriteFileAtomic(methodPath, json);
        }
        catch
        {
            // Ignore cache write errors
        }
    }

    private static bool ValidateMetadata(CacheMetadata metadata, ProjectArtifacts artifacts, string mvid)
    {
        if (metadata.CacheSchemaVersion != SchemaVersion) return false;
        if (metadata.AssemblyPath != artifacts.AssemblyPath) return false;
        if (metadata.AssemblyMvid != mvid) return false;
        if (metadata.AssemblyLength != GetFileLength(artifacts.AssemblyPath)) return false;
        if (metadata.AssemblyLastWriteTimeUtc != GetFileLastWriteTimeUtcString(artifacts.AssemblyPath)) return false;

        if (artifacts.PdbPath is not null)
        {
            if (metadata.PdbPath != artifacts.PdbPath) return false;
            if (metadata.PdbLength != GetFileLength(artifacts.PdbPath)) return false;
            if (metadata.PdbLastWriteTimeUtc != GetFileLastWriteTimeUtcString(artifacts.PdbPath)) return false;
        }

        if (metadata.TargetFramework != artifacts.TargetFramework) return false;
        if (metadata.Configuration != artifacts.Configuration) return false;

        return true;
    }

    private static CacheMetadata CreateMetadata(ProjectArtifacts artifacts, string mvid)
    {
        return new CacheMetadata
        {
            CacheSchemaVersion = SchemaVersion,
            AssemblyPath = artifacts.AssemblyPath,
            AssemblyMvid = mvid,
            AssemblyLength = GetFileLength(artifacts.AssemblyPath),
            AssemblyLastWriteTimeUtc = GetFileLastWriteTimeUtcString(artifacts.AssemblyPath),
            PdbPath = artifacts.PdbPath,
            PdbLength = artifacts.PdbPath is not null ? GetFileLength(artifacts.PdbPath) : 0,
            PdbLastWriteTimeUtc = artifacts.PdbPath is not null ? GetFileLastWriteTimeUtcString(artifacts.PdbPath) : null,
            TargetFramework = artifacts.TargetFramework,
            Configuration = artifacts.Configuration
        };
    }

    private static long GetFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return -1;
        }
    }

    private static string GetFileLastWriteTimeUtcString(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path).ToString("O");
        }
        catch
        {
            return "";
        }
    }

    private static void DeleteCacheFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore
        }
    }

    private static void WriteFileAtomic(string path, string content)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content, Encoding.UTF8);
        try
        {
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            throw;
        }
    }
}
