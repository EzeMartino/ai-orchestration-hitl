namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record FinancialRatio(
    string Name,
    string Period,
    decimal Value,
    string Unit,
    string Formula,
    IReadOnlyList<string> Inputs,
    string Interpretation
);
