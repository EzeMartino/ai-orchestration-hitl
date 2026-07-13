using CSnakes.Runtime;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;

internal sealed class CSnakesFinancialAnalysisPythonInvoker(
    IPythonEnvironment pythonEnvironment) : IFinancialAnalysisPythonInvoker
{
    public string ComputeFinancialRatios(string requestJson)
    {
        return pythonEnvironment.FinancialAnalysis().ComputeFinancialRatios(requestJson);
    }

    public string ComparePeriods(string requestJson)
    {
        return pythonEnvironment.FinancialAnalysis().ComparePeriods(requestJson);
    }

    public string DetectFinancialRiskSignals(string requestJson)
    {
        return pythonEnvironment.FinancialAnalysis().DetectFinancialRiskSignals(requestJson);
    }

    public string SummarizeQuantitativeEvidence(string requestJson)
    {
        return pythonEnvironment.FinancialAnalysis().SummarizeQuantitativeEvidence(requestJson);
    }
}
