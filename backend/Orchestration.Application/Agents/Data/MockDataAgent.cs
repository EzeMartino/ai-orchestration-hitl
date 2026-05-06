using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Data;

public sealed class MockDataAgent : IDataAgent
{
    public Task<DataAgentResult> AnalyzeAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        var result = new DataAgentResult(
            HasAnomaly: true,
            Severity: "High",
            Summary: "Unusual transaction pattern detected in the submitted financial report.",
            Evidence:
            [
                new AnomalyEvidence(
                    Metric: "TransactionAmountZScore",
                    Value: 4.7,
                    Threshold: 3.0,
                    Interpretation: "Transaction amount is significantly above expected range."
                ),
                new AnomalyEvidence(
                    Metric: "VelocityScore",
                    Value: 0.91,
                    Threshold: 0.75,
                    Interpretation: "Transaction frequency increased abnormally in a short time window."
                )
            ]
        );

        return Task.FromResult(result);
    }
}