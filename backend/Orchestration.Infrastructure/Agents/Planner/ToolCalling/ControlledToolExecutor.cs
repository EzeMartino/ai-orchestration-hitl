using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Infrastructure.Agents.Planner.ToolCalling;

public sealed class ControlledToolExecutor : IControlledToolExecutor
{
    private const string EngineName = "Controlled Tool Executor";
    private const string DataToolName = "data.analyze_transactions";
    private const string LegalToolName = "legal.search_cnv_regulation";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataAgent _dataAgent;
    private readonly ICnvRegulationMcpClient _mcpClient;
    private readonly ILogger<ControlledToolExecutor> _logger;

    public ControlledToolExecutor(
        IDataAgent dataAgent,
        ICnvRegulationMcpClient mcpClient,
        ILogger<ControlledToolExecutor> logger)
    {
        _dataAgent = dataAgent;
        _mcpClient = mcpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
        IReadOnlyList<ApprovedToolCall> calls,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(calls);

        var results = new List<ToolExecutionResult>();

        foreach (var call in calls)
        {
            var result = await ExecuteAsync(call, cancellationToken);

            results.Add(result);
        }

        return results;
    }

    private async Task<ToolExecutionResult> ExecuteAsync(
        ApprovedToolCall call,
        CancellationToken cancellationToken)
    {
        try
        {
            return call.ToolName switch
            {
                DataToolName => await ExecuteDataAnalysisAsync(call, cancellationToken),
                LegalToolName => await ExecuteLegalSearchAsync(call, cancellationToken),
                _ => Failed(call.ToolName, "Tool is not executable.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Controlled tool execution failed for {ToolName}.",
                call.ToolName
            );

            return Failed(call.ToolName, "Tool execution failed.");
        }
    }

    private async Task<ToolExecutionResult> ExecuteDataAnalysisAsync(
        ApprovedToolCall call,
        CancellationToken cancellationToken)
    {
        if (!TryGetGuid(call.Arguments, "sessionId", out var sessionId, out var error) ||
            !TryGetString(call.Arguments, "reportName", out var reportName, out error) ||
            !TryGetDecimal(call.Arguments, "totalAmount", out var totalAmount, out error) ||
            !TryGetInt(call.Arguments, "transactionCount", out var transactionCount, out error) ||
            !TryGetDateTimeOffset(call.Arguments, "submittedAt", out var submittedAt, out error))
        {
            return Failed(call.ToolName, error);
        }

        var report = new FinancialReportContext(
            SessionId: sessionId,
            ReportName: reportName,
            TotalAmount: totalAmount,
            TransactionCount: transactionCount,
            SubmittedAt: submittedAt
        );

        var result = await _dataAgent.AnalyzeAsync(report, cancellationToken);

        return Succeeded(
            call.ToolName,
            result.Summary,
            result.Engine,
            result
        );
    }

    private async Task<ToolExecutionResult> ExecuteLegalSearchAsync(
        ApprovedToolCall call,
        CancellationToken cancellationToken)
    {
        if (!TryGetString(call.Arguments, "query", out var query, out var error))
        {
            return Failed(call.ToolName, error);
        }

        var limit = 5;

        if (TryFindValue(call.Arguments, "limit", out var rawLimit) &&
            !TryParseInt("limit", rawLimit, out limit, out error))
        {
            return Failed(call.ToolName, error);
        }

        bool? requiresReview = null;

        if (TryFindValue(call.Arguments, "requiresReview", out _))
        {
            if (!TryGetBool(call.Arguments, "requiresReview", out var parsedRequiresReview, out error))
            {
                return Failed(call.ToolName, error);
            }

            requiresReview = parsedRequiresReview;
        }

        var request = new CnvRegulationSearchRequest(
            Query: query,
            Area: GetOptionalString(call.Arguments, "area"),
            Limit: limit,
            Source: GetOptionalString(call.Arguments, "source"),
            DocumentType: GetOptionalString(call.Arguments, "documentType"),
            ResolutionNumber: GetOptionalString(call.Arguments, "resolutionNumber"),
            Status: GetOptionalString(call.Arguments, "status"),
            RequiresReview: requiresReview
        );

        var response = await _mcpClient.SearchAsync(request, cancellationToken);

        return Succeeded(
            call.ToolName,
            $"CNV search returned {response.Results.Count} results.",
            "MCP CNV Regulation Server",
            response
        );
    }

    private static ToolExecutionResult Succeeded(
        string toolName,
        string summary,
        string engine,
        object output)
    {
        return new ToolExecutionResult(
            ToolName: toolName,
            Status: ToolExecutionStatus.Executed,
            Succeeded: true,
            Summary: summary,
            Engine: engine,
            OutputJson: JsonSerializer.Serialize(output, JsonOptions),
            Error: null
        );
    }

    private static ToolExecutionResult Failed(
        string toolName,
        string error)
    {
        return new ToolExecutionResult(
            ToolName: toolName,
            Status: ToolExecutionStatus.Failed,
            Succeeded: false,
            Summary: error,
            Engine: EngineName,
            OutputJson: "{}",
            Error: error
        );
    }

    private static bool TryGetString(
        IReadOnlyDictionary<string, string> arguments,
        string key,
        out string value,
        out string error)
    {
        if (!TryFindValue(arguments, key, out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            value = "";
            error = $"Missing required argument: {key}.";

            return false;
        }

        value = rawValue;
        error = "";

        return true;
    }

    private static bool TryGetGuid(
        IReadOnlyDictionary<string, string> arguments,
        string key,
        out Guid value,
        out string error)
    {
        if (!TryGetString(arguments, key, out var rawValue, out error))
        {
            value = Guid.Empty;

            return false;
        }

        if (!Guid.TryParse(rawValue, out value))
        {
            error = $"Invalid Guid argument: {key}.";

            return false;
        }

        return true;
    }

    private static bool TryGetInt(
        IReadOnlyDictionary<string, string> arguments,
        string key,
        out int value,
        out string error)
    {
        if (!TryGetString(arguments, key, out var rawValue, out error))
        {
            value = default;

            return false;
        }

        return TryParseInt(key, rawValue, out value, out error);
    }

    private static bool TryGetDecimal(
        IReadOnlyDictionary<string, string> arguments,
        string key,
        out decimal value,
        out string error)
    {
        if (!TryGetString(arguments, key, out var rawValue, out error))
        {
            value = default;

            return false;
        }

        if (!decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            error = $"Invalid decimal argument: {key}.";

            return false;
        }

        return true;
    }

    private static bool TryGetBool(
        IReadOnlyDictionary<string, string> arguments,
        string key,
        out bool value,
        out string error)
    {
        if (!TryGetString(arguments, key, out var rawValue, out error))
        {
            value = default;

            return false;
        }

        return TryParseBool(key, rawValue, out value, out error);
    }

    private static bool TryGetDateTimeOffset(
        IReadOnlyDictionary<string, string> arguments,
        string key,
        out DateTimeOffset value,
        out string error)
    {
        if (!TryGetString(arguments, key, out var rawValue, out error))
        {
            value = default;

            return false;
        }

        if (!DateTimeOffset.TryParse(
                rawValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out value))
        {
            error = $"Invalid DateTimeOffset argument: {key}.";

            return false;
        }

        return true;
    }

    private static bool TryParseInt(
        string key,
        string rawValue,
        out int value,
        out string error)
    {
        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = $"Invalid int argument: {key}.";

            return false;
        }

        error = "";

        return true;
    }

    private static bool TryParseBool(
        string key,
        string rawValue,
        out bool value,
        out string error)
    {
        if (!bool.TryParse(rawValue, out value))
        {
            error = $"Invalid bool argument: {key}.";

            return false;
        }

        error = "";

        return true;
    }

    private static string? GetOptionalString(
        IReadOnlyDictionary<string, string> arguments,
        string key)
    {
        return TryFindValue(arguments, key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static bool TryFindValue(
        IReadOnlyDictionary<string, string> arguments,
        string key,
        out string value)
    {
        foreach (var argument in arguments)
        {
            if (string.Equals(argument.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = argument.Value;

                return true;
            }
        }

        value = "";

        return false;
    }
}
