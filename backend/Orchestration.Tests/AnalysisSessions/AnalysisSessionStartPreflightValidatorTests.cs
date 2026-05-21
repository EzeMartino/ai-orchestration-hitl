using FluentAssertions;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Tests.AnalysisSessions;

public sealed class AnalysisSessionStartPreflightValidatorTests
{
    [Fact]
    public async Task ValidateAsync_Should_allow_start_when_financial_analysis_is_disabled()
    {
        var validator = CreateValidator(new DataAgentOptions
        {
            FinancialAnalysisToolsEnabled = false,
            RequireSessionFinancialMetrics = true
        });

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
        var validator = CreateValidator(new DataAgentOptions
        {
            FinancialAnalysisToolsEnabled = true,
            RequireSessionFinancialMetrics = false
        });

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
        var session = AnalysisSession.Create();
        session.SetContext(CreateStructuredMetricsContextJson());
        var validator = CreateValidator(new DataAgentOptions
        {
            FinancialAnalysisToolsEnabled = true,
            RequireSessionFinancialMetrics = true
        });

        var result = await validator.ValidateAsync(session, CancellationToken.None);

        result.CanStart.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_Should_block_start_when_required_metrics_are_missing()
    {
        var validator = CreateValidator(new DataAgentOptions
        {
            FinancialAnalysisToolsEnabled = true,
            RequireSessionFinancialMetrics = true
        });

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
        DataAgentOptions options)
    {
        return new AnalysisSessionStartPreflightValidator(Options.Create(options));
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
