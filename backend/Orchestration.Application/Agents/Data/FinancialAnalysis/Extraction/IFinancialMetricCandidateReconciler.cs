using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public interface IFinancialMetricCandidateReconciler
{
    FinancialMetricReconciliationResult Reconcile(
        StructuredFinancialMetricsInput deterministicInput,
        FinancialDocumentExtractionResult? semanticResult,
        FinancialMetricsExtractionOptions options);
}
