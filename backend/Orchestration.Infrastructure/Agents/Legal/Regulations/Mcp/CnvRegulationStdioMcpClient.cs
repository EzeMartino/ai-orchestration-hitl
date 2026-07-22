using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Text.Json;

namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

public sealed class CnvRegulationStdioMcpClient : ICnvRegulationMcpClient, IAsyncDisposable
{
    private readonly CnvRegulationMcpOptions _options;
    private readonly ILogger<CnvRegulationStdioMcpClient> _logger;
    private readonly Func<string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<CallToolResult>>? _toolCallOverride;
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

    public CnvRegulationStdioMcpClient(
        IOptions<CnvRegulationMcpOptions> options,
        ILogger<CnvRegulationStdioMcpClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    internal CnvRegulationStdioMcpClient(
        IOptions<CnvRegulationMcpOptions> options,
        ILogger<CnvRegulationStdioMcpClient> logger,
        Func<string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<CallToolResult>> toolCallOverride)
        : this(options, logger)
    {
        _toolCallOverride = toolCallOverride;
    }

    public Task<CnvRegulationSearchResponse> SearchAsync(
        CnvRegulationSearchRequest request,
        CancellationToken cancellationToken)
    {
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

        return CallToolAsync<CnvRegulationSearchResponse>(
            "search_cnv_regulation",
            arguments,
            cancellationToken,
            () => new CnvRegulationSearchResponse(
                request.Query,
                [],
                ["La herramienta MCP devolvió una respuesta vacía o no válida."]));
    }

    public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
        CnvRegulationDocumentRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, object?> arguments = new Dictionary<string, object?>
        {
            ["documentId"] = request.DocumentId
        };

        return CallToolAsync<CnvRegulationDocumentResponse>(
            "get_cnv_document",
            arguments,
            cancellationToken);
    }

    public Task<CnvRegulationArticleResponse> GetArticleAsync(
        CnvRegulationArticleRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["article"] = request.Article
        };

        AddOptional(arguments, "title", request.Title);
        AddOptional(arguments, "chapter", request.Chapter);
        AddOptional(arguments, "section", request.Section);

        return CallToolAsync<CnvRegulationArticleResponse>(
            "get_cnv_article",
            arguments,
            cancellationToken);
    }

    private async Task<TResponse> CallToolAsync<TResponse>(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken,
        Func<TResponse>? nullResponseFactory = null)
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
            if (_toolCallOverride == null)
            {
                await EnsureConnectedAsync(cancellationToken);
            }

            var toolCallStart = Stopwatch.GetTimestamp();
            using var timeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(_options.ToolCallTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            CallToolResult result;
            try
            {
                if (_toolCallOverride != null)
                {
                    result = await _toolCallOverride(toolName, arguments, linkedCts.Token);
                }
                else
                {
                    if (_client == null)
                    {
                        throw new InvalidOperationException("MCP client is not connected.");
                    }

                    result = await _client.CallToolAsync(
                        toolName,
                        arguments,
                        cancellationToken: linkedCts.Token);
                }
            }
            catch (OperationCanceledException ex) when (
                timeoutCts.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                LogToolCallFailure(ex, toolCallStart);
                await ResetConnectionAsync("Tool call timed out", ex);

                var timeoutException = new TimeoutException(
                    $"La herramienta MCP '{toolName}' excedió el tiempo de espera configurado.",
                    ex);
                _lastError = timeoutException.Message;
                throw timeoutException;
            }
            catch (Exception ex)
            {
                LogToolCallFailure(ex, toolCallStart);

                if (IsTransportOrProtocolError(ex))
                {
                    await ResetConnectionAsync("Transport or protocol error", ex);
                }

                throw;
            }

            var toolCallElapsed = Stopwatch.GetElapsedTime(toolCallStart);
            var totalElapsed = Stopwatch.GetElapsedTime(startTimestamp);
            _logger.LogInformation(
                "MCP tool call succeeded. query_count={QueryCount}, mcp_tool_call_ms={McpToolCallMs:F2}, total_duration_ms={TotalDurationMs:F2}",
                _queryCount,
                toolCallElapsed.TotalMilliseconds,
                totalElapsed.TotalMilliseconds);

            if (result.IsError == true)
            {
                var errorMessage = GetToolErrorMessage(result);
                _lastError = errorMessage;
                throw new InvalidOperationException(errorMessage);
            }

            try
            {
                var response = DeserializeResponse<TResponse>(result);
                if (response == null)
                {
                    if (nullResponseFactory == null)
                    {
                        throw new InvalidOperationException(
                            "La herramienta MCP devolvió una respuesta vacía o no válida.");
                    }

                    response = nullResponseFactory();
                }

                _lastError = null;
                return response;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _lastError = ex.Message;
                throw;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private void LogToolCallFailure(Exception ex, long toolCallStart)
    {
        var toolCallDuration = Stopwatch.GetElapsedTime(toolCallStart);
        _lastError = ex.Message;
        _logger.LogError(
            ex,
            "MCP tool call failed after {ToolCallMs} ms.",
            toolCallDuration.TotalMilliseconds);
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
                "Faltan argumentos para el servidor MCP de regulación CNV."
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

            _lastError = null;
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
        _lastError = ex?.Message ?? reason;

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

    private TResponse? DeserializeResponse<TResponse>(CallToolResult result)
    {
        if (result.StructuredContent is JsonElement structuredContent)
        {
            return structuredContent.Deserialize<TResponse>(
                _jsonOptions
            );
        }

        var text = ExtractText(result);

        if (!LooksLikeJson(text))
        {
            throw new InvalidOperationException(
                $"La herramienta MCP devolvió contenido de texto que no es JSON: {text}"
            );
        }

        return JsonSerializer.Deserialize<TResponse>(
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
                "La herramienta MCP no devolvió contenido de texto."
            );
        }

        return text;
    }

    private static string GetToolErrorMessage(CallToolResult result)
    {
        try
        {
            return $"La herramienta MCP devolvió un error: {ExtractText(result)}";
        }
        catch (InvalidOperationException ex)
        {
            return $"La herramienta MCP devolvió un error, pero no se pudo leer su contenido de texto: {ex.Message}";
        }
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
