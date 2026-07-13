namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;

internal interface IFinancialAnalysisPythonInvoker
{
    string ComputeFinancialRatios(string requestJson);

    string ComparePeriods(string requestJson);

    string DetectFinancialRiskSignals(string requestJson);

    string SummarizeQuantitativeEvidence(string requestJson);
}
