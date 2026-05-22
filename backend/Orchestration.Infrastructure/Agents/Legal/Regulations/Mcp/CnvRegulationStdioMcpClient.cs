using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Text.Json;

namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

public sealed class CnvRegulationStdioMcpClient(
    IOptions<CnvRegulationMcpOptions> options,
    ILogger<CnvRegulationStdioMcpClient> logger) : ICnvRegulationMcpClient, IAsyncDisposable
{
    private readonly CnvRegulationMcpOptions _options = options.Value;
    private readonly ILogger<CnvRegulationStdioMcpClient> _logger = logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly SemaphoreSlim _lock = new(1, 1);

    private StdioClientTransport? _transport;
    private McpClient? _client;
    private bool _isDisposed;

    private int _queryCount;
    private int _coldStartCount;
    private int _resetCount;
    private string? _lastError;

    public bool IsConnected => _client != null;
    public int ColdStartCount => _coldStartCount;
    public int ResetCount => _resetCount;
    public string? LastError => _lastError;


    public async Task<CnvRegulationSearchResponse> SearchAsync(
        CnvRegulationSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(CnvRegulationStdioMcpClient));
        }

        _queryCount++;
        var startTimestamp = Stopwatch.GetTimestamp();

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedAsync(cancellationToken);

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

            CallToolResult result;
            var toolCallStart = Stopwatch.GetTimestamp();

            // We wrap tool call logic in a cancellation token that respects our ToolCallTimeoutSeconds
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ToolCallTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                if (_client == null)
                {
                    throw new InvalidOperationException("MCP client is not connected.");
                }

                result = await _client.CallToolAsync(
                    "search_cnv_regulation",
                    arguments,
                    cancellationToken: linkedCts.Token
                );
            }
            catch (Exception ex)
            {
                var toolCallDuration = Stopwatch.GetElapsedTime(toolCallStart);
                _logger.LogError(ex, "MCP tool call failed after {ToolCallMs} ms.", toolCallDuration.TotalMilliseconds);

                // Check if it is a transport or protocol error to trigger a connection reset
                if (IsTransportOrProtocolError(ex) || (ex is OperationCanceledException && timeoutCts.IsCancellationRequested))
                {
                    var resetReason = (ex is OperationCanceledException && timeoutCts.IsCancellationRequested)
                        ? "Tool call timed out"
                        : "Transport or protocol error";
                    
                    await ResetConnectionAsync(resetReason, ex);
                }

                throw;
            }

            var toolCallElapsed = Stopwatch.GetElapsedTime(toolCallStart);
            var totalElapsed = Stopwatch.GetElapsedTime(startTimestamp);

            // Log structured telemetry fields
            _logger.LogInformation(
                "MCP tool call succeeded. query_count={QueryCount}, mcp_tool_call_ms={McpToolCallMs:F2}, total_duration_ms={TotalDurationMs:F2}",
                _queryCount,
                toolCallElapsed.TotalMilliseconds,
                totalElapsed.TotalMilliseconds
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
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client != null)
        {
            return;
        }

        if (_options.Args.Length == 0)
        {
            throw new InvalidOperationException(
                "MCP CNV regulation server args are missing."
            );
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        _logger.LogInformation("Starting MCP client connection (Cold Start)...");

        // We wrap connection logic in a cancellation token that respects our ConnectionTimeoutSeconds
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            _transport = new StdioClientTransport(
                new StdioClientTransportOptions
                {
                    Name = "cnv-regulation",
                    Command = _options.Command,
                    Arguments = _options.Args
                }
            );

            _client = await McpClient.CreateAsync(
                _transport,
                cancellationToken: linkedCts.Token
            );
            
            _coldStartCount++;

            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            
            // Structured log for cold start duration
            _logger.LogInformation(
                "MCP client connection established successfully. mcp_cold_start_ms={ColdStartMs:F2}",
                elapsed.TotalMilliseconds
            );
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            _lastError = ex.Message;
            _logger.LogError(
                ex,
                "Failed to connect to MCP CNV regulation server. duration_ms={DurationMs:F2}",
                elapsed.TotalMilliseconds
            );
            await CleanupConnectionAsync();
            throw;
        }
    }

    private async Task ResetConnectionAsync(string reason, Exception? ex = null)
    {
        _resetCount++;
        
        // Structured log for reset reason
        _logger.LogWarning(
            ex,
            "Resetting MCP client connection. reset_reason={ResetReason}",
            reason
        );

        await CleanupConnectionAsync();
    }

    private async Task CleanupConnectionAsync()
    {
        if (_client != null)
        {
            try
            {
                await _client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing MCP client during cleanup.");
            }
            _client = null;
        }

        if (_transport != null)
        {
            _transport = null;
        }
    }

    private static bool IsTransportOrProtocolError(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return false;
        }

        if (ex is System.IO.IOException or System.Net.Sockets.SocketException or System.ObjectDisposedException)
        {
            return true;
        }

        var typeName = ex.GetType().FullName ?? "";
        if (typeName.Contains("Transport") || typeName.Contains("Protocol") || typeName.Contains("JsonRpc"))
        {
            return true;
        }

        var msg = ex.Message.ToLowerInvariant();
        if (msg.Contains("broken pipe") || msg.Contains("stream closed") || msg.Contains("connection reset") || msg.Contains("channel closed"))
        {
            return true;
        }

        return false;
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

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        await _lock.WaitAsync();
        try
        {
            await CleanupConnectionAsync();
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }
}
