using Orchestration.Application.Agents.Shared;

namespace Orchestration.Tests;

internal static class TestReportSummary
{
    internal static FinancialReportSummaryInput Input { get; } = new(
        ReportName: "Test financial report",
        TotalAmount: 1250.50m,
        TransactionCount: 7,
        SubmittedAt: new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero));
}
