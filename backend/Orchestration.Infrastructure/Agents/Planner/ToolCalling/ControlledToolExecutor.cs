using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Infrastructure.Agents.Planner.ToolCalling;

public sealed class ControlledToolExecutor : IControlledToolExecutor
{
    private const string EngineName = "Controlled Tool Executor";
    private const string FinancialAnalysisEngineName = "Semantic Kernel + CSnakes + Python/Pandas";
    private const string DataToolName = "data.analyze_transactions";
    private const string LegalToolName = "legal.search_cnv_regulation";
    private const string ComputeFinancialRatiosToolName = "data.compute_financial_ratios";
    private const string ComparePeriodsToolName = "data.compare_periods";
    private const string DetectFinancialRiskSignalsToolName = "data.detect_financial_risk_signals";
    private const string SummarizeQuantitativeEvidenceToolName = "data.summarize_quantitative_evidence";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDataAgent _dataAgent;
    private readonly IPythonFinancialAnalysisService _financialAnalysisService;
    private readonly ICnvRegulationMcpClient _mcpClient;
    private readonly ILogger<ControlledToolExecutor> _logger;

    public ControlledToolExecutor(
        IDataAgent dataAgent,
        IPythonFinancialAnalysisService financialAnalysisService,
        ICnvRegulationMcpClient mcpClient,
        ILogger<ControlledToolExecutor> logger)
    {
        _dataAgent = dataAgent;
        _financialAnalysisService = financialAnalysisService;
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
                ComputeFinancialRatiosToolName => await ExecuteComputeFinancialRatiosAsync(call, cancellationToken),
                ComparePeriodsToolName => await ExecuteComparePeriodsAsync(call, cancellationToken),
                DetectFinancialRiskSignalsToolName => await ExecuteDetectFinancialRiskSignalsAsync(call, cancellationToken),
                SummarizeQuantitativeEvidenceToolName => await ExecuteSummarizeQuantitativeEvidenceAsync(call, cancellationToken),
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

    private async Task<ToolExecutionResult> ExecuteComputeFinancialRatiosAsync(
        ApprovedToolCall call,
        CancellationToken cancellationToken)
    {
        if (!TryGetRequest(
                call,
                out ComputeFinancialRatiosRequest request,
                out var error))
        {
            return Failed(call.ToolName, error);
        }

        var response = await _financialAnalysisService.ComputeFinancialRatiosAsync(
            request,
            cancellationToken
        );

        return Succeeded(
            call.ToolName,
            $"Computed {response.Ratios.Count} financial ratio(s).",
            FinancialAnalysisEngineName,
            response
        );
    }

    private async Task<ToolExecutionResult> ExecuteComparePeriodsAsync(
        ApprovedToolCall call,
        CancellationToken cancellationToken)
    {
        if (!TryGetRequest(
                call,
                out ComparePeriodsRequest request,
                out var error))
        {
            return Failed(call.ToolName, error);
        }

        var response = await _financialAnalysisService.ComparePeriodsAsync(
            request,
            cancellationToken
        );

        return Succeeded(
            call.ToolName,
            $"Computed {response.Comparisons.Count} period comparison(s).",
            FinancialAnalysisEngineName,
            response
        );
    }

    private async Task<ToolExecutionResult> ExecuteDetectFinancialRiskSignalsAsync(
        ApprovedToolCall call,
        CancellationToken cancellationToken)
    {
        if (!TryGetRequest(
                call,
                out DetectFinancialRiskSignalsRequest request,
                out var error))
        {
            return Failed(call.ToolName, error);
        }

        var response = await _financialAnalysisService.DetectFinancialRiskSignalsAsync(
            request,
            cancellationToken
        );

        return Succeeded(
            call.ToolName,
            $"Detected {response.Signals.Count} financial risk signal(s).",
            FinancialAnalysisEngineName,
            response
        );
    }

    private async Task<ToolExecutionResult> ExecuteSummarizeQuantitativeEvidenceAsync(
        ApprovedToolCall call,
        CancellationToken cancellationToken)
    {
        if (!TryGetRequest(
                call,
                out SummarizeQuantitativeEvidenceRequest request,
                out var error))
        {
            return Failed(call.ToolName, error);
        }

        var response = await _financialAnalysisService.SummarizeQuantitativeEvidenceAsync(
            request,
            cancellationToken
        );

        return Succeeded(
            call.ToolName,
            response.Narrative,
            FinancialAnalysisEngineName,
            response
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
            error = $"Falta el argumento obligatorio: {key}.";

            return false;
        }

        value = rawValue;
        error = "";

        return true;
    }

    private static bool TryGetRequest<TRequest>(
        ApprovedToolCall call,
        out TRequest request,
        out string error)
        where TRequest : class
    {
        if (!TryGetString(call.Arguments, "requestJson", out var requestJson, out error))
        {
            request = default!;

            return false;
        }

        try
        {
            var parsedRequest = JsonSerializer.Deserialize<TRequest>(
                requestJson,
                JsonOptions
            );

            if (parsedRequest is null)
            {
                request = default!;
                error = "Argumento JSON no válido: requestJson.";

                return false;
            }

            request = parsedRequest;
            error = "";

            return true;
        }
        catch (JsonException)
        {
            request = default!;
            error = "Argumento JSON no válido: requestJson.";

            return false;
        }
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
            error = $"Argumento Guid no válido: {key}.";

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
            error = $"Argumento decimal no válido: {key}.";

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
            error = $"Argumento DateTimeOffset no válido: {key}.";

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
            error = $"Argumento int no válido: {key}.";

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
            error = $"Argumento bool no válido: {key}.";

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
