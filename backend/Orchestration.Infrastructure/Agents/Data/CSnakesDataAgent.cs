using CSnakes.Runtime;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Data;

public sealed class CSnakesDataAgent(IPythonEnvironment pythonEnvironment) : ILegacyDataAgent
{
    private readonly IPythonEnvironment _pythonEnvironment = pythonEnvironment;

    public Task<DataAgentResult> AnalyzeAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var module = _pythonEnvironment.AnomalyDetection();

        var (hasAnomaly, severity, summary, evidence) = module.AnalyzeTransactions(
            (double)report.TotalAmount,
            report.TransactionCount
        );

        var evidenceItems = new List<AnomalyEvidence>();

        foreach (var item in evidence)
        {
            var (metric, value, threshold, interpretation) = item;

            evidenceItems.Add(
                new AnomalyEvidence(
                    Metric: metric,
                    Value: value,
                    Threshold: threshold,
                    Interpretation: interpretation
                )
            );
        }

        var result = new DataAgentResult(
            HasAnomaly: hasAnomaly,
            Severity: severity,
            Summary: summary,
            Engine: "Python/CSnakes",
            Evidence: evidenceItems
        );

        return Task.FromResult(result);
    }
}
