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
    [InlineData("{\"financialReport\":{\"reportName\":\" \",\"totalAmount\":-1,\"transactionCount\":-1,\"submittedAt\":\"2026-07-12T18:30:00Z\"}}")]
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

    [Theory]
    [InlineData("{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1}}", FinancialReportSummaryValidator.SubmittedAtRequiredCode, FinancialReportSummaryValidator.SubmittedAtRequiredMessage)]
    [InlineData("{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":null}}", FinancialReportSummaryValidator.SubmittedAtRequiredCode, FinancialReportSummaryValidator.SubmittedAtRequiredMessage)]
    [InlineData("{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":\"not-a-date\"}}", FinancialReportSummaryValidator.SubmittedAtInvalidCode, FinancialReportSummaryValidator.SubmittedAtInvalidMessage)]
    [InlineData("{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":123}}", FinancialReportSummaryValidator.SubmittedAtInvalidCode, FinancialReportSummaryValidator.SubmittedAtInvalidMessage)]
    [InlineData("{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":{}}}", FinancialReportSummaryValidator.SubmittedAtInvalidCode, FinancialReportSummaryValidator.SubmittedAtInvalidMessage)]
    [InlineData("{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":true}}", FinancialReportSummaryValidator.SubmittedAtInvalidCode, FinancialReportSummaryValidator.SubmittedAtInvalidMessage)]
    [InlineData("{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":[]}}", FinancialReportSummaryValidator.SubmittedAtInvalidCode, FinancialReportSummaryValidator.SubmittedAtInvalidMessage)]
    [InlineData("{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":\"0001-01-01T00:00:00+00:00\"}}", FinancialReportSummaryValidator.SubmittedAtInvalidCode, FinancialReportSummaryValidator.SubmittedAtInvalidMessage)]
    public void Resolve_InvalidSubmittedAt_ShouldReturnSpecificFailure(
        string contextJson,
        string expectedCode,
        string expectedMessage)
    {
        var session = AnalysisSession.Create(Guid.NewGuid());
        session.SetContext(contextJson);

        var result = new FinancialReportContextResolver().Resolve(session);

        result.IsValid.Should().BeFalse();
        result.Report.Should().BeNull();
        result.ErrorCode.Should().Be(expectedCode);
        result.ErrorMessage.Should().Be(expectedMessage).And.NotBeNullOrWhiteSpace();
    }
}
