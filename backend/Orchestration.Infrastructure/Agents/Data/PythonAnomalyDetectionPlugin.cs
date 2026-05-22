using System.ComponentModel;
using Microsoft.SemanticKernel;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Data;

public sealed class PythonAnomalyDetectionPlugin(CSnakesDataAgent dataAgent)
{
    private readonly CSnakesDataAgent _dataAgent = dataAgent;

    [KernelFunction("analyze_financial_transactions")]
    [Description("Runs Python-based financial anomaly detection through CSnakes.")]
    public async Task<PythonAnomalyDetectionPluginResult> AnalyzeFinancialTransactionsAsync(
        [Description("Total amount of all transactions in the financial report.")]
        double totalAmount,
        [Description("Number of transactions in the financial report.")]
        int transactionCount,
        CancellationToken cancellationToken = default)
    {
        var report = new FinancialReportContext(
            SessionId: Guid.Empty,
            ReportName: "semantic-kernel-plugin-report",
            TotalAmount: Convert.ToDecimal(totalAmount),
            TransactionCount: transactionCount,
            SubmittedAt: DateTimeOffset.UtcNow
        );

        var result = await _dataAgent.AnalyzeAsync(
            report,
            cancellationToken
        );

        return new PythonAnomalyDetectionPluginResult(
            HasAnomaly: result.HasAnomaly,
            Severity: result.Severity,
            Summary: result.Summary,
            Engine: "Semantic Kernel + " + result.Engine,
            Evidence: result.Evidence
                .Select(x => new PythonAnomalyEvidenceResult(
                    x.Metric,
                    x.Value,
                    x.Threshold,
                    x.Interpretation
                ))
                .ToList()
        );
    }
}