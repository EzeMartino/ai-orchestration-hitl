using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Domain.FinancialMetricsExtraction;
using Orchestration.Infrastructure.Persistence;

namespace Orchestration.Tests.AnalysisSessions;

public sealed class AnalysisSessionStartPreflightValidatorTests
{
    private const string FinancialMetricsReviewRequiredCode =
        "FINANCIAL_METRICS_REVIEW_REQUIRED";

    [Fact]
    public async Task ValidateAsync_Should_allow_start_when_financial_analysis_is_disabled()
    {
        await using var dbContext = CreateDbContext();
        var validator = CreateValidator(new DataAgentOptions
        {
            FinancialAnalysisToolsEnabled = false,
            RequireSessionFinancialMetrics = true
        }, dbContext);

        var result = await validator.ValidateAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        result.CanStart.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_Should_allow_start_when_session_metrics_are_not_required()
    {
        await using var dbContext = CreateDbContext();
        var validator = CreateValidator(new DataAgentOptions
        {
            FinancialAnalysisToolsEnabled = true,
            RequireSessionFinancialMetrics = false
        }, dbContext);

        var result = await validator.ValidateAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        result.CanStart.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_Should_allow_start_when_required_metrics_exist()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        session.SetContext(CreateStructuredMetricsContextJson());
        var validator = CreateValidator(new DataAgentOptions
        {
            FinancialAnalysisToolsEnabled = true,
            RequireSessionFinancialMetrics = true
        }, dbContext);

        var result = await validator.ValidateAsync(session, CancellationToken.None);

        result.CanStart.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_Should_block_start_while_pdf_review_is_pending()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.NewGuid());
        session.SetContext(CreateStructuredMetricsContextJson());
        dbContext.FinancialMetricsExtractionDrafts.Add(CreateDraft(
            session.Id,
            FinancialMetricsExtractionDraftStatus.PendingReview));
        await dbContext.SaveChangesAsync();

        var validator = CreateValidator(new DataAgentOptions
        {
            FinancialAnalysisToolsEnabled = true,
            RequireSessionFinancialMetrics = true
        }, dbContext);

        var result = await validator.ValidateAsync(session, CancellationToken.None);

        result.CanStart.Should().BeFalse();
        var issue = result.Errors.Should().ContainSingle().Which;
        issue.Code.Should().Be(FinancialMetricsReviewRequiredCode);
        issue.Severity.Should().Be("Error");
        result.Warnings.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task ValidateAsync_Should_block_pending_pdf_review_regardless_of_data_agent_metric_gate(
        bool financialAnalysisToolsEnabled,
        bool requireSessionFinancialMetrics)
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.NewGuid());
        session.SetContext(CreateStructuredMetricsContextJson());
        dbContext.FinancialMetricsExtractionDrafts.Add(CreateDraft(
            session.Id,
            FinancialMetricsExtractionDraftStatus.PendingReview));
        await dbContext.SaveChangesAsync();

        var validator = CreateValidator(new DataAgentOptions
        {
            FinancialAnalysisToolsEnabled = financialAnalysisToolsEnabled,
            RequireSessionFinancialMetrics = requireSessionFinancialMetrics
        }, dbContext);

        var result = await validator.ValidateAsync(session, CancellationToken.None);

        result.CanStart.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(FinancialMetricsReviewRequiredCode);
    }

    [Fact]
    public async Task ValidateAsync_Should_allow_start_when_pdf_review_drafts_are_confirmed_or_discarded()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create(Guid.NewGuid());
        session.SetContext(CreateStructuredMetricsContextJson());
        dbContext.FinancialMetricsExtractionDrafts.Add(CreateDraft(
            session.Id,
            FinancialMetricsExtractionDraftStatus.Confirmed));
        dbContext.FinancialMetricsExtractionDrafts.Add(CreateDraft(
            session.Id,
            FinancialMetricsExtractionDraftStatus.Discarded));
        await dbContext.SaveChangesAsync();

        var validator = CreateValidator(new DataAgentOptions
        {
            FinancialAnalysisToolsEnabled = true,
            RequireSessionFinancialMetrics = true
        }, dbContext);

        var result = await validator.ValidateAsync(session, CancellationToken.None);

        result.CanStart.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_Should_block_start_when_required_metrics_are_missing()
    {
        await using var dbContext = CreateDbContext();
        var validator = CreateValidator(new DataAgentOptions
        {
            FinancialAnalysisToolsEnabled = true,
            RequireSessionFinancialMetrics = true
        }, dbContext);

        var result = await validator.ValidateAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        result.CanStart.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Code = AnalysisSessionStartPreflightValidator
                    .StructuredFinancialMetricsRequiredCode,
                Message = AnalysisSessionStartPreflightValidator
                    .StructuredFinancialMetricsRequiredMessage,
                Severity = "Error"
            });
        result.Warnings.Should().BeEmpty();
    }

    private static AnalysisSessionStartPreflightValidator CreateValidator(
        DataAgentOptions options,
        OrchestrationDbContext dbContext)
    {
        return new AnalysisSessionStartPreflightValidator(
            Options.Create(options),
            dbContext);
    }

    private static OrchestrationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new OrchestrationDbContext(options);
    }

    private static FinancialMetricsExtractionDraft CreateDraft(
        Guid sessionId,
        FinancialMetricsExtractionDraftStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var draft = FinancialMetricsExtractionDraft.Create(
            sessionId,
            Guid.NewGuid(),
            "metrics.pdf",
            1024,
            Guid.NewGuid().ToString("N"),
            "{}",
            now);

        if (status == FinancialMetricsExtractionDraftStatus.Confirmed)
        {
            draft.Confirm(Guid.NewGuid(), now.AddMinutes(1));
        }
        else if (status == FinancialMetricsExtractionDraftStatus.Discarded)
        {
            draft.Discard(Guid.NewGuid(), now.AddMinutes(1));
        }

        return draft;
    }

    private static string CreateStructuredMetricsContextJson()
    {
        return """
            {
              "structuredFinancialMetrics": {
                "documentId": "manual-json-input",
                "company": "Manual Test Co",
                "currency": "USD",
                "unit": "USD_thousand",
                "metrics": [
                  {
                    "name": "revenue",
                    "period": "2024A",
                    "value": 1647768,
                    "unit": "USD_thousand",
                    "statement": "",
                    "source": "manual_upload",
                    "currency": "USD",
                    "sourcePage": 18,
                    "confidence": 0.9
                  }
                ],
                "validationWarnings": [],
                "uploadedAt": "2026-05-21T00:00:00Z"
              }
            }
            """;
    }
}
