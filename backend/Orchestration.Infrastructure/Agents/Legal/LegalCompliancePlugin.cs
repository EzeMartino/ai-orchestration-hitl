using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Legal;

public sealed class LegalCompliancePlugin(
    IRegulatoryKnowledgeSource regulatoryKnowledgeSource)
{
    private readonly IRegulatoryKnowledgeSource _regulatoryKnowledgeSource = regulatoryKnowledgeSource;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    [KernelFunction("review_financial_compliance")]
    [Description("Reviews a financial report summary against compliance rules.")]
    public async Task<LegalCompliancePluginResult> ReviewFinancialComplianceAsync(
        [Description("Name of the financial report.")]
        string reportName,
        [Description("Total amount of all transactions in the report.")]
        double totalAmount,
        [Description("Number of transactions in the report.")]
        int transactionCount,
        [Description("Unique ID of the active analysis session")]
        string? sessionId = null,
        [Description("Optional serialized financial analysis context produced by DataAgent in the current run.")]
        string? financialAnalysisJson = null,
        CancellationToken cancellationToken = default)
    {
        var parsedSessionId = Guid.Empty;
        if (!string.IsNullOrWhiteSpace(sessionId) && Guid.TryParse(sessionId, out var parsedGuid))
        {
            parsedSessionId = parsedGuid;
        }

        var report = new FinancialReportContext(
            SessionId: parsedSessionId,
            ReportName: reportName,
            TotalAmount: Convert.ToDecimal(totalAmount),
            TransactionCount: transactionCount,
            SubmittedAt: DateTimeOffset.UtcNow,
            FinancialAnalysis: DeserializeFinancialAnalysis(financialAnalysisJson)
        );

        var review = await _regulatoryKnowledgeSource.ReviewAsync(
            report,
            cancellationToken
        );

        return new LegalCompliancePluginResult(
            HasComplianceRisk: review.HasComplianceRisk,
            RiskLevel: review.RiskLevel,
            Summary: review.Summary,
            Engine: $"Semantic Kernel + {review.SourceEngine}",
            Evidence: review.Findings
                .Select(x => new LegalComplianceEvidenceResult(
                    x.Regulation,
                    x.Section,
                    x.Finding,
                    x.Source
                ))
                .ToList(),
            Warnings: review.Warnings,
            QueryStrategy: review.QueryStrategy,
            LegalReview: review.LegalReview
        );
    }

    private static FinancialAnalysisContext? DeserializeFinancialAnalysis(
        string? financialAnalysisJson)
    {
        if (string.IsNullOrWhiteSpace(financialAnalysisJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FinancialAnalysisContext>(
                financialAnalysisJson,
                JsonOptions
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
