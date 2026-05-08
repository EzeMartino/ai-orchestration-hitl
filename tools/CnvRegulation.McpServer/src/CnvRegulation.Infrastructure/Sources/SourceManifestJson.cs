using System.Text.Json;

namespace CnvRegulation.Infrastructure.Sources;

internal static class SourceManifestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
