using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Infrastructure.Agents.Data;

public sealed class FinancialAnalysisPlugin
{
    private const string Engine = "Semantic Kernel Financial Analysis Plugin";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPythonFinancialAnalysisService _financialAnalysisService;

    public FinancialAnalysisPlugin(
        IPythonFinancialAnalysisService financialAnalysisService)
    {
        _financialAnalysisService = financialAnalysisService;
    }

    [KernelFunction("data_compute_financial_ratios")]
    [Description("Computes financial ratios from structured financial metrics.")]
    public async Task<ComputeFinancialRatiosResponse> ComputeFinancialRatiosAsync(
        [Description("JSON-serialized ComputeFinancialRatiosRequest payload.")]
        string requestJson,
        CancellationToken cancellationToken = default)
    {
        var request = DeserializeRequest<ComputeFinancialRatiosRequest>(requestJson);

        return request is null
            ? FailedRatiosResponse("JSON de solicitud para calcular ratios financieros no válido.")
            : await _financialAnalysisService.ComputeFinancialRatiosAsync(
                request,
                cancellationToken
            );
    }

    [KernelFunction("data_compare_periods")]
    [Description("Compares structured financial metrics across two periods.")]
    public async Task<ComparePeriodsResponse> ComparePeriodsAsync(
        [Description("JSON-serialized ComparePeriodsRequest payload.")]
        string requestJson,
        CancellationToken cancellationToken = default)
    {
        var request = DeserializeRequest<ComparePeriodsRequest>(requestJson);

        return request is null
            ? FailedComparePeriodsResponse("JSON de solicitud para comparar periodos no válido.")
            : await _financialAnalysisService.ComparePeriodsAsync(
                request,
                cancellationToken
            );
    }

    [KernelFunction("data_detect_financial_risk_signals")]
    [Description("Detects financial risk signals from structured metrics, ratios, and comparisons.")]
    public async Task<DetectFinancialRiskSignalsResponse> DetectFinancialRiskSignalsAsync(
        [Description("JSON-serialized DetectFinancialRiskSignalsRequest payload.")]
        string requestJson,
        CancellationToken cancellationToken = default)
    {
        var request = DeserializeRequest<DetectFinancialRiskSignalsRequest>(requestJson);

        return request is null
            ? FailedRiskSignalsResponse("JSON de solicitud para detectar señales de riesgo financiero no válido.")
            : await _financialAnalysisService.DetectFinancialRiskSignalsAsync(
                request,
                cancellationToken
            );
    }

    [KernelFunction("data_summarize_quantitative_evidence")]
    [Description("Summarizes quantitative financial evidence for human review.")]
    public async Task<SummarizeQuantitativeEvidenceResponse> SummarizeQuantitativeEvidenceAsync(
        [Description("JSON-serialized SummarizeQuantitativeEvidenceRequest payload.")]
        string requestJson,
        CancellationToken cancellationToken = default)
    {
        var request = DeserializeRequest<SummarizeQuantitativeEvidenceRequest>(requestJson);

        return request is null
            ? FailedEvidenceSummaryResponse("JSON de solicitud para resumir evidencia cuantitativa no válido.")
            : await _financialAnalysisService.SummarizeQuantitativeEvidenceAsync(
                request,
                cancellationToken
            );
    }

    private static TRequest? DeserializeRequest<TRequest>(string requestJson)
        where TRequest : class
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TRequest>(
                requestJson,
                JsonOptions
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ComputeFinancialRatiosResponse FailedRatiosResponse(string message)
    {
        return new ComputeFinancialRatiosResponse(
            Engine: Engine,
            Ratios: [],
            Warnings:
            [
                message,
                "No se pudieron calcular los ratios financieros a partir de la solicitud provista."
            ]
        );
    }

    private static ComparePeriodsResponse FailedComparePeriodsResponse(string message)
    {
        return new ComparePeriodsResponse(
            Engine: Engine,
            Comparisons: [],
            Warnings:
            [
                message,
                "No se pudieron calcular comparaciones entre periodos financieros a partir de la solicitud provista."
            ]
        );
    }

    private static DetectFinancialRiskSignalsResponse FailedRiskSignalsResponse(string message)
    {
        return new DetectFinancialRiskSignalsResponse(
            Engine: Engine,
            Signals: [],
            Result: new FinancialAnalysisToolResult(
                HasRiskSignals: false,
                RiskLevel: "Low",
                Summary: "No se pudieron calcular señales de riesgo financiero a partir de la solicitud provista.",
                Engine: Engine,
                Evidence: [],
                Warnings:
                [
                    message,
                    "No se pudieron calcular señales de riesgo financiero a partir de la solicitud provista."
                ]
            )
        );
    }

    private static SummarizeQuantitativeEvidenceResponse FailedEvidenceSummaryResponse(string message)
    {
        const string summary = "No se pudo resumir la evidencia cuantitativa a partir de la solicitud provista.";

        return new SummarizeQuantitativeEvidenceResponse(
            Engine: Engine,
            Narrative: summary,
            Result: new FinancialAnalysisToolResult(
                HasRiskSignals: false,
                RiskLevel: "Low",
                Summary: summary,
                Engine: Engine,
                Evidence: [],
                Warnings:
                [
                    message,
                    "No se pudo resumir la evidencia cuantitativa a partir de la solicitud provista."
                ]
            )
        );
    }
}
