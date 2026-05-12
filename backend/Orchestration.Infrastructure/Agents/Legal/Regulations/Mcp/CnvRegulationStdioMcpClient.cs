using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

public sealed class CnvRegulationStdioMcpClient : ICnvRegulationMcpClient
{
    private readonly CnvRegulationMcpOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public CnvRegulationStdioMcpClient(
        IOptions<CnvRegulationMcpOptions> options)
    {
        _options = options.Value;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<CnvRegulationSearchResponse> SearchAsync(
        CnvRegulationSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (_options.Args.Length == 0)
        {
            throw new InvalidOperationException(
                "MCP CNV regulation server args are missing."
            );
        }

        var clientTransport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = "cnv-regulation",
                Command = _options.Command,
                Arguments = _options.Args
            }
        );

        await using var client = await McpClient.CreateAsync(
            clientTransport,
            cancellationToken: cancellationToken
        );

        var arguments = new Dictionary<string, object?>
        {
            ["query"] = request.Query,
            ["limit"] = request.Limit
        };

        AddOptional(arguments, "area", request.Area);
        AddOptional(arguments, "source", request.Source);
        AddOptional(arguments, "documentType", request.DocumentType);
        AddOptional(arguments, "resolutionNumber", request.ResolutionNumber);
        AddOptional(arguments, "status", request.Status);

        if (request.RequiresReview.HasValue)
        {
            arguments["requiresReview"] = request.RequiresReview.Value;
        }

        var result = await client.CallToolAsync(
            "search_cnv_regulation",
            arguments,
            cancellationToken: cancellationToken
        );

        if (result.IsError == true)
        {
            throw new InvalidOperationException(
                $"MCP tool returned an error: {ExtractText(result)}"
            );
        }

        var response = DeserializeResponse(result);

        return response
            ?? new CnvRegulationSearchResponse(
                request.Query,
                [],
                ["MCP tool returned an empty or invalid response."]
            );
    }

    private static void AddOptional(
        Dictionary<string, object?> arguments,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            arguments[key] = value;
        }
    }

    private CnvRegulationSearchResponse? DeserializeResponse(CallToolResult result)
    {
        if (result.StructuredContent is JsonElement structuredContent)
        {
            return structuredContent.Deserialize<CnvRegulationSearchResponse>(
                _jsonOptions
            );
        }

        var text = ExtractText(result);

        if (!LooksLikeJson(text))
        {
            throw new InvalidOperationException(
                $"MCP tool returned non-JSON text content: {text}"
            );
        }

        return JsonSerializer.Deserialize<CnvRegulationSearchResponse>(
            text,
            _jsonOptions
        );
    }

    private static string ExtractText(CallToolResult result)
    {
        var textContent = result.Content
            .OfType<TextContentBlock>()
            .FirstOrDefault();

        var text = textContent?.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                "MCP tool did not return text content."
            );
        }

        return text;
    }

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.TrimStart();

        return trimmed.StartsWith('{')
            || trimmed.StartsWith('[');
    }
}
