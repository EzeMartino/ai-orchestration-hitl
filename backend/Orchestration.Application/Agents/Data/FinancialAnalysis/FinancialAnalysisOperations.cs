namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public static class FinancialAnalysisOperations
{
    public const string Ratios = "ratios";
    public const string Comparisons = "comparisons";
    public const string Signals = "signals";
    public const string Summary = "summary";

    public static IReadOnlyList<string> All { get; } =
    [
        Ratios,
        Comparisons,
        Signals,
        Summary
    ];
}

public static class FinancialAnalysisFailureCodes
{
    public const string PythonInvocationFailed = "PYTHON_INVOCATION_FAILED";
    public const string PythonResponseInvalid = "PYTHON_RESPONSE_INVALID";
    public const string UnexpectedFailure = "FINANCIAL_ANALYSIS_UNEXPECTED_FAILURE";
}
