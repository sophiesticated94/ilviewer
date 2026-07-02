using System.Text;
using System.Text.Json;

namespace IlViewer.Worker.Analysis;

public sealed record GraphNodeKey(
    string Kind,
    string? AssemblyPath,
    string? AssemblyName,
    string? TypeName,
    string? MetadataToken,
    string? Signature);

public static class GraphNodeIds
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Create(
        string kind,
        string? assemblyPath,
        string? assemblyName,
        string? typeName = null,
        string? metadataToken = null,
        string? signature = null)
    {
        var key = new GraphNodeKey(kind, assemblyPath, assemblyName, typeName, metadataToken, signature);
        var json = JsonSerializer.Serialize(key, JsonOptions);
        return "g:" + ToBase64Url(Encoding.UTF8.GetBytes(json));
    }

    public static GraphNodeKey Parse(string nodeId)
    {
        var encoded = nodeId.StartsWith("g:", StringComparison.Ordinal) ? nodeId[2..] : nodeId;
        var json = Encoding.UTF8.GetString(FromBase64Url(encoded));
        return JsonSerializer.Deserialize<GraphNodeKey>(json, JsonOptions)
            ?? throw new ArgumentException("Invalid graph node id.", nameof(nodeId));
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        var padding = (4 - base64.Length % 4) % 4;
        return Convert.FromBase64String(base64 + new string('=', padding));
    }
}
