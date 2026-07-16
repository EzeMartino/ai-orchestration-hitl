using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Planner.ToolCalling;

public sealed class ControlledToolExecutor : IControlledToolExecutor
{
    private const string EngineName = "Controlled Tool Executor";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDataAgent _dataAgent;
    private readonly ILegalAgent _legalAgent;
    private readonly ILogger<ControlledToolExecutor> _logger;

    public ControlledToolExecutor(
        IDataAgent dataAgent,
        ILegalAgent legalAgent,
        ILogger<ControlledToolExecutor> logger)
    {
        _dataAgent = dataAgent;
        _legalAgent = legalAgent;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
        IReadOnlyList<ApprovedToolCall> calls,
        CancellationToken cancellationToken,
        PlannerToolExecutionContext? runtimeContext = null)
    {
        ArgumentNullException.ThrowIfNull(calls);

        var results = new List<ToolExecutionResult>();

        foreach (var call in calls)
        {
            var result = await ExecuteAsync(
                call,
                cancellationToken,
                runtimeContext);

            results.Add(result);
        }

        return results;
    }

    private async Task<ToolExecutionResult> ExecuteAsync(
        ApprovedToolCall call,
        CancellationToken cancellationToken,
        PlannerToolExecutionContext? runtimeContext)
    {
        try
        {
            return PlannerToolCatalog.Find(call.ToolName)?.Handler switch
            {
                PlannerToolHandler.AnalyzeTransactions =>
                    await ExecuteDataAnalysisAsync(call, cancellationToken),
                PlannerToolHandler.SearchCnvRegulation =>
                    await ExecuteLegalSearchAsync(
                        call,
                        cancellationToken,
                        runtimeContext),
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
        CancellationToken cancellationToken,
        PlannerToolExecutionContext? runtimeContext)
    {
        if (runtimeContext is null)
        {
            return Failed(
                call.ToolName,
                "Trusted planner runtime context is required.");
        }

        var legalReport = runtimeContext.Report with
        {
            FinancialAnalysis = runtimeContext.DataResult.FinancialAnalysis
        };
        var legalReviewContext = new LegalReviewContext(
            FinancialAnalysisResolutionMode.ProvidedOnly,
            runtimeContext.DataEvidence
        );
        var result = await _legalAgent.ReviewAsync(
            legalReport,
            legalReviewContext,
            cancellationToken
        );

        return Succeeded(
            call.ToolName,
            result.Summary,
            result.Engine,
            result
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
