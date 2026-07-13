using FluentAssertions;
using Orchestration.Application.Agents.Shared;
using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Tests.Agents.Shared;

public sealed class FinancialReportContextResolverTests
{
    [Fact]
    public void Resolve_ValidPersistedSummary_ShouldReturnExactReportContext()
    {
        var session = AnalysisSession.Create(Guid.NewGuid());
        var submittedAt = new DateTimeOffset(2026, 7, 12, 18, 30, 0, TimeSpan.Zero);
        session.SetContext("""
            {
              "financialReport": {
                "reportName": "  balance-sheet-2025.pdf  ",
                "totalAmount": 842350.75,
                "transactionCount": 187,
                "submittedAt": "2026-07-12T18:30:00Z"
              }
            }
            """);

        var result = new FinancialReportContextResolver().Resolve(session);

        result.IsValid.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
        result.Report.Should().Be(new FinancialReportContext(
            session.Id,
            "balance-sheet-2025.pdf",
            842350.75m,
            187,
            submittedAt));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"structuredFinancialMetrics\":{}}")]
    public void Resolve_MissingSummary_ShouldReturnRequiredFailure(string contextJson)
    {
        var session = AnalysisSession.Create(Guid.NewGuid());
        session.SetContext(contextJson);

        var result = new FinancialReportContextResolver().Resolve(session);

        result.IsValid.Should().BeFalse();
        result.Report.Should().BeNull();
        result.ErrorCode.Should().Be("FINANCIAL_REPORT_SUMMARY_REQUIRED");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{\"financialReport\":\"invalid\"}")]
    [InlineData("{\"financialReport\":{\"reportName\":\" \",\"totalAmount\":-1,\"transactionCount\":-1,\"submittedAt\":\"0001-01-01T00:00:00Z\"}}")]
    public void Resolve_MalformedOrInvalidSummary_ShouldReturnInvalidFailure(string contextJson)
    {
        var session = AnalysisSession.Create(Guid.NewGuid());
        session.SetContext(contextJson);

        var result = new FinancialReportContextResolver().Resolve(session);

        result.IsValid.Should().BeFalse();
        result.Report.Should().BeNull();
        result.ErrorCode.Should().Be("FINANCIAL_REPORT_SUMMARY_INVALID");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }
}
