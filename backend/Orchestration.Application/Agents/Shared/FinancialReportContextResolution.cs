namespace Orchestration.Application.Agents.Shared;

public sealed record FinancialReportContextResolution(
    bool IsValid,
    FinancialReportContext? Report,
    string? ErrorCode,
    string? ErrorMessage);
