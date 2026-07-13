using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;

public sealed class CSnakesFinancialAnalysisService : IPythonFinancialAnalysisService
{
    private const string Engine = "Python/CSnakes Financial Analysis";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IFinancialAnalysisPythonInvoker _pythonInvoker;
    private readonly ILogger<CSnakesFinancialAnalysisService> _logger;

    internal CSnakesFinancialAnalysisService(
        IFinancialAnalysisPythonInvoker pythonInvoker,
        ILogger<CSnakesFinancialAnalysisService> logger)
    {
        _pythonInvoker = pythonInvoker;
        _logger = logger;
    }

    public Task<ComputeFinancialRatiosResponse> ComputeFinancialRatiosAsync(
        ComputeFinancialRatiosRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);

        return Task.FromResult(Execute(
            FinancialAnalysisOperations.Ratios,
            request.SessionId,
            cancellationToken,
            () => _pythonInvoker.ComputeFinancialRatios(requestJson),
            MapRatios,
            SafeRatiosResponse,
            static (response, execution) => response with { Execution = execution }));
    }

    public Task<ComparePeriodsResponse> ComparePeriodsAsync(
        ComparePeriodsRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);

        return Task.FromResult(Execute(
            FinancialAnalysisOperations.Comparisons,
            request.SessionId,
            cancellationToken,
            () => _pythonInvoker.ComparePeriods(requestJson),
            MapComparisons,
            SafeComparePeriodsResponse,
            static (response, execution) => response with { Execution = execution }));
    }

    public Task<DetectFinancialRiskSignalsResponse> DetectFinancialRiskSignalsAsync(
        DetectFinancialRiskSignalsRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);

        return Task.FromResult(Execute(
            FinancialAnalysisOperations.Signals,
            request.SessionId,
            cancellationToken,
            () => _pythonInvoker.DetectFinancialRiskSignals(requestJson),
            MapRiskSignals,
            SafeRiskSignalsResponse,
            static (response, execution) => response with { Execution = execution }));
    }

    public Task<SummarizeQuantitativeEvidenceResponse> SummarizeQuantitativeEvidenceAsync(
        SummarizeQuantitativeEvidenceRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);

        return Task.FromResult(Execute(
            FinancialAnalysisOperations.Summary,
            request.SessionId,
            cancellationToken,
            () => _pythonInvoker.SummarizeQuantitativeEvidence(requestJson),
            MapEvidenceSummary,
            SafeEvidenceSummaryResponse,
            static (response, execution) => response with { Execution = execution }));
    }

    private TResponse Execute<TResponse>(
        string operation,
        Guid? sessionId,
        CancellationToken cancellationToken,
        Func<string> invoke,
        Func<string, TResponse> map,
        Func<FinancialAnalysisStageExecution, TResponse> failureResponse,
        Func<TResponse, FinancialAnalysisStageExecution, TResponse> attachExecution)
    {
        var stopwatch = Stopwatch.StartNew();
        string responseJson;

        try
        {
            responseJson = invoke();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Fail(
                operation,
                sessionId,
                stopwatch.ElapsedMilliseconds,
                FinancialAnalysisFailureCodes.PythonInvocationFailed,
                exception,
                failureResponse);
        }

        TResponse response;

        try
        {
            response = map(responseJson);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Fail(
                operation,
                sessionId,
                stopwatch.ElapsedMilliseconds,
                FinancialAnalysisFailureCodes.PythonResponseInvalid,
                exception,
                failureResponse);
        }

        var execution = new FinancialAnalysisStageExecution(
            operation,
            FinancialAnalysisExecutionStatus.Succeeded,
            stopwatch.ElapsedMilliseconds);

        LogSuccess(sessionId, execution);

        return attachExecution(response, execution);
    }

    private TResponse Fail<TResponse>(
        string operation,
        Guid? sessionId,
        long durationMilliseconds,
        string failureCode,
        Exception exception,
        Func<FinancialAnalysisStageExecution, TResponse> failureResponse)
    {
        var execution = new FinancialAnalysisStageExecution(
            operation,
            FinancialAnalysisExecutionStatus.Failed,
            durationMilliseconds,
            failureCode);

        _logger.LogError(
            exception,
            "Financial analysis operation {Operation} completed with status {ExecutionStatus} for session {SessionId} in {DurationMilliseconds} ms. Failure code: {FailureCode}",
            execution.Operation,
            execution.Status,
            sessionId,
            execution.DurationMilliseconds,
            execution.FailureCode);

        return failureResponse(execution);
    }

    private void LogSuccess(
        Guid? sessionId,
        FinancialAnalysisStageExecution execution)
    {
        _logger.LogInformation(
            "Financial analysis operation {Operation} completed with status {ExecutionStatus} for session {SessionId} in {DurationMilliseconds} ms. Failure code: {FailureCode}",
            execution.Operation,
            execution.Status,
            sessionId,
            execution.DurationMilliseconds,
            execution.FailureCode);
    }

    private static ComputeFinancialRatiosResponse MapRatios(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        var ratios = new List<FinancialRatio>();

        foreach (var ratio in GetRequiredArray(root, "ratios"))
        {
            ratios.Add(new FinancialRatio(
                Name: GetRequiredString(ratio, "name"),
                Period: GetRequiredString(ratio, "period"),
                Value: GetRequiredDecimal(ratio, "value"),
                Unit: GetString(ratio, "unit"),
                Formula: GetString(ratio, "formula"),
                Inputs: GetStringArray(ratio, "inputs"),
                Interpretation: GetString(ratio, "interpretation"),
                Source: GetString(ratio, "source", defaultValue: "computed")
            ));
        }

        return new ComputeFinancialRatiosResponse(
            Engine: Engine,
            Ratios: ratios,
            Warnings: GetWarningsAndLimitations(root)
        );
    }

    private static ComparePeriodsResponse MapComparisons(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        var comparisons = new List<FinancialPeriodComparison>();

        foreach (var comparison in GetRequiredArray(root, "comparisons"))
        {
            comparisons.Add(new FinancialPeriodComparison(
                MetricName: GetRequiredString(comparison, "metricName"),
                FromPeriod: GetRequiredString(comparison, "fromPeriod", "basePeriod"),
                ToPeriod: GetRequiredString(comparison, "toPeriod", "comparisonPeriod"),
                FromValue: GetRequiredDecimal(comparison, "fromValue", "baseValue"),
                ToValue: GetRequiredDecimal(comparison, "toValue", "comparisonValue"),
                AbsoluteChange: GetRequiredDecimal(comparison, "absoluteChange"),
                PercentageChange: GetNullableDecimal(comparison, "percentageChange"),
                Unit: GetString(comparison, "unit"),
                Interpretation: GetString(comparison, "interpretation", "explanation")
            ));
        }

        return new ComparePeriodsResponse(
            Engine: Engine,
            Comparisons: comparisons,
            Warnings: GetWarningsAndLimitations(root)
        );
    }

    private static DetectFinancialRiskSignalsResponse MapRiskSignals(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        var signals = new List<FinancialRiskSignal>();

        foreach (var signal in GetRequiredArray(root, "signals"))
        {
            var evidence = MapEvidence(signal);

            signals.Add(new FinancialRiskSignal(
                Name: GetRequiredString(signal, "name", "code"),
                Severity: GetRequiredString(signal, "severity"),
                Period: GetRequiredString(signal, "period"),
                Summary: GetRequiredString(signal, "summary"),
                Evidence: evidence,
                Metric: GetNullableString(signal, "metric"),
                Value: GetNullableDecimal(signal, "value"),
                ThresholdCode: GetNullableString(signal, "thresholdCode"),
                ThresholdOperator: GetNullableString(signal, "thresholdOperator"),
                ThresholdValue: GetNullableDecimal(signal, "thresholdValue"),
                Reason: GetNullableString(signal, "reason")
            ));
        }

        var allEvidence = signals
            .SelectMany(signal => signal.Evidence)
            .ToList();
        var riskLevel = ResolveRiskLevel(signals.Select(signal => signal.Severity));
        var warnings = GetWarningsAndLimitations(root);

        return new DetectFinancialRiskSignalsResponse(
            Engine: Engine,
            Signals: signals,
            Result: new FinancialAnalysisToolResult(
                HasRiskSignals: signals.Count > 0,
                RiskLevel: riskLevel,
                Summary: BuildRiskSignalSummary(signals, riskLevel),
                Engine: Engine,
                Evidence: allEvidence,
                Warnings: warnings
            )
        );
    }

    private static SummarizeQuantitativeEvidenceResponse MapEvidenceSummary(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        var narrative = GetRequiredString(root, "summary");
        var evidence = MapEvidence(root);
        var severities = GetRequiredArray(root, "evidence")
            .Select(item => GetString(item, "severity", defaultValue: "Info"));
        var riskLevel = ResolveRiskLevel(severities);
        var warnings = GetWarningsAndLimitations(root);

        return new SummarizeQuantitativeEvidenceResponse(
            Engine: Engine,
            Narrative: narrative,
            Result: new FinancialAnalysisToolResult(
                HasRiskSignals: evidence.Count > 0,
                RiskLevel: riskLevel,
                Summary: narrative,
                Engine: Engine,
                Evidence: evidence,
                Warnings: warnings
            )
        );
    }

    private static List<RiskEvidenceItem> MapEvidence(JsonElement element)
    {
        var evidence = new List<RiskEvidenceItem>();

        foreach (var item in GetRequiredArray(element, "evidence"))
        {
            evidence.Add(new RiskEvidenceItem(
                MetricName: GetRequiredString(item, "metricName"),
                Period: GetRequiredString(item, "period"),
                Value: GetRequiredDecimal(item, "value"),
                Threshold: GetNullableDecimal(item, "threshold"),
                Unit: GetString(item, "unit"),
                Interpretation: GetString(item, "interpretation")
            ));
        }

        return evidence;
    }

    private static ComputeFinancialRatiosResponse SafeRatiosResponse(
        FinancialAnalysisStageExecution execution)
    {
        return new ComputeFinancialRatiosResponse(
            Engine: Engine,
            Ratios: [],
            Warnings:
            [
                "Falló el cálculo de ratios financieros en Python.",
                "No se pudieron calcular los ratios financieros a partir de las métricas provistas."
            ]
        ) { Execution = execution };
    }

    private static ComparePeriodsResponse SafeComparePeriodsResponse(
        FinancialAnalysisStageExecution execution)
    {
        return new ComparePeriodsResponse(
            Engine: Engine,
            Comparisons: [],
            Warnings:
            [
                "Falló la comparación de periodos en Python.",
                "No se pudieron calcular comparaciones entre periodos a partir de las métricas provistas."
            ]
        ) { Execution = execution };
    }

    private static DetectFinancialRiskSignalsResponse SafeRiskSignalsResponse(
        FinancialAnalysisStageExecution execution)
    {
        return new DetectFinancialRiskSignalsResponse(
            Engine: Engine,
            Signals: [],
            Result: new FinancialAnalysisToolResult(
                HasRiskSignals: false,
                RiskLevel: "Unknown",
                Summary: "No se pudieron calcular señales de riesgo financiero a partir de las métricas provistas.",
                Engine: Engine,
                Evidence: [],
                Warnings:
                [
                    "Falló la detección de señales de riesgo financiero en Python.",
                    "No se pudieron calcular señales de riesgo a partir de las métricas provistas."
                ]
            )
        ) { Execution = execution };
    }

    private static SummarizeQuantitativeEvidenceResponse SafeEvidenceSummaryResponse(
        FinancialAnalysisStageExecution execution)
    {
        const string narrative = "No se pudo resumir la evidencia cuantitativa a partir de las métricas provistas.";

        return new SummarizeQuantitativeEvidenceResponse(
            Engine: Engine,
            Narrative: narrative,
            Result: new FinancialAnalysisToolResult(
                HasRiskSignals: false,
                RiskLevel: "Unknown",
                Summary: narrative,
                Engine: Engine,
                Evidence: [],
                Warnings:
                [
                    "Falló el resumen de evidencia cuantitativa en Python.",
                    "No se pudo resumir la evidencia cuantitativa a partir de las métricas provistas."
                ]
            )
        ) { Execution = execution };
    }

    private static string BuildRiskSignalSummary(
        IReadOnlyCollection<FinancialRiskSignal> signals,
        string riskLevel)
    {
        return signals.Count == 0
            ? "No se identificaron señales cuantitativas de riesgo."
            : $"Se identificaron {signals.Count} señal(es) cuantitativas de riesgo con nivel {riskLevel}. Se recomienda revisión humana.";
    }

    private static string ResolveRiskLevel(IEnumerable<string> severities)
    {
        var values = severities.ToArray();

        if (values.Any(severity => IsSeverity(severity, "High")))
        {
            return "High";
        }

        if (values.Any(severity => IsSeverity(severity, "Medium")))
        {
            return "Medium";
        }

        if (values.Any(severity => IsSeverity(severity, "Low")))
        {
            return "Low";
        }

        return "Low";
    }

    private static bool IsSeverity(string severity, string expected)
    {
        return string.Equals(severity, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetWarningsAndLimitations(JsonElement root)
    {
        return GetStringArray(root, "warnings")
            .Concat(GetStringArray(root, "limitations"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<JsonElement> GetRequiredArray(
        JsonElement element,
        string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                $"Required array '{propertyName}' was missing or invalid.");
        }

        return property.EnumerateArray().ToArray();
    }

    private static IReadOnlyList<string> GetStringArray(
        JsonElement element,
        string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : item.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    private static string GetString(
        JsonElement element,
        params string[] propertyNames)
    {
        return GetString(element, propertyNames, defaultValue: string.Empty);
    }

    private static string GetRequiredString(
        JsonElement element,
        params string[] propertyNames)
    {
        if (!TryGetProperty(element, propertyNames, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new JsonException(
                $"Required string '{propertyNames[0]}' was missing or invalid.");
        }

        return property.GetString()!;
    }

    private static string GetString(
        JsonElement element,
        string propertyName,
        string? alternatePropertyName = null,
        string defaultValue = "")
    {
        var propertyNames = alternatePropertyName is null
            ? [propertyName]
            : new[] { propertyName, alternatePropertyName };

        return GetString(element, propertyNames, defaultValue);
    }

    private static string GetString(
        JsonElement element,
        string[] propertyNames,
        string defaultValue)
    {
        if (TryGetProperty(element, propertyNames, out var property))
        {
            return property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? defaultValue
                : property.ToString();
        }

        return defaultValue;
    }

    private static string? GetNullableString(
        JsonElement element,
        params string[] propertyNames)
    {
        if (!TryGetProperty(element, propertyNames, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var value = property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    private static decimal GetRequiredDecimal(
        JsonElement element,
        params string[] propertyNames)
    {
        return GetNullableDecimal(element, propertyNames) ??
            throw new JsonException(
                $"Required decimal '{propertyNames[0]}' was missing or invalid.");
    }

    private static decimal? GetNullableDecimal(
        JsonElement element,
        params string[] propertyNames)
    {
        if (!TryGetProperty(element, propertyNames, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetDecimal(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String &&
            decimal.TryParse(
                property.GetString(),
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool TryGetProperty(
        JsonElement element,
        IReadOnlyList<string> propertyNames,
        out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in propertyNames)
            {
                if (element.TryGetProperty(propertyName, out property))
                {
                    return true;
                }
            }
        }

        property = default;
        return false;
    }
}
