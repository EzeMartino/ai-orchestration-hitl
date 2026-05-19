using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Orchestration.Api.Controllers;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Tests.Api;

public sealed class FinancialMetricsControllerTests
{
    [Fact]
    public void Validate_Should_return_valid_result_with_normalized_metrics()
    {
        var controller = CreateController();
        var input = new StructuredFinancialMetricsInput(
            DocumentId: "vista-energy-structured-input",
            Company: "Vista Energy",
            Currency: "USD",
            Unit: "USD_thousand",
            Metrics:
            [
                new StructuredFinancialMetricInput(
                    Name: "Gross Profit",
                    Period: " 2025e ",
                    Value: 924000m,
                    Unit: null,
                    Currency: null,
                    Source: null,
                    SourcePage: 18,
                    Confidence: null
                )
            ]
        );

        var actionResult = controller.Validate(input);

        var response = actionResult.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<FinancialMetricsValidationResponse>()
            .Subject;
        response.IsValid.Should().BeTrue();
        response.Metrics.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Name = "gross_profit",
                Period = "2025E",
                Value = 924000m,
                Unit = "USD_thousand",
                Currency = "USD",
                Source = "structured_input",
                SourcePage = 18,
                Confidence = 0.75m
            });
        response.Errors.Should().BeEmpty();
        response.Warnings.Should().Contain(issue => issue.Code == "METRIC_UNIT_DEFAULTED");
        response.Warnings.Should().Contain(issue => issue.Code == "METRIC_CURRENCY_DEFAULTED");
        response.Warnings.Should().Contain(issue => issue.Code == "METRIC_SOURCE_DEFAULTED");
        response.Warnings.Should().Contain(issue => issue.Code == "METRIC_CONFIDENCE_DEFAULTED");
    }

    [Fact]
    public void Validate_Should_return_ok_with_invalid_financial_input()
    {
        var controller = CreateController();
        var input = new StructuredFinancialMetricsInput(
            DocumentId: "",
            Company: null,
            Currency: null,
            Unit: null,
            Metrics:
            [
                new StructuredFinancialMetricInput(
                    Name: "",
                    Period: "2024A",
                    Value: null,
                    Unit: null,
                    Currency: null,
                    Source: null,
                    SourcePage: null,
                    Confidence: null
                )
            ]
        );

        var actionResult = controller.Validate(input);

        var response = actionResult.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<FinancialMetricsValidationResponse>()
            .Subject;
        response.IsValid.Should().BeFalse();
        response.Metrics.Should().BeEmpty();
        response.Errors.Should().Contain(issue => issue.Code == "DOCUMENT_ID_REQUIRED");
        response.Errors.Should().Contain(issue => issue.Code == "METRIC_NAME_REQUIRED");
        response.Errors.Should().Contain(issue => issue.Code == "METRIC_VALUE_REQUIRED");
        response.Warnings.Should().Contain(issue => issue.Code == "COMPANY_MISSING");
    }

    [Fact]
    public void Validate_Should_return_bad_request_for_null_body()
    {
        var controller = CreateController();

        var actionResult = controller.Validate(null!);

        actionResult.Result.Should().BeOfType<BadRequestResult>();
    }

    [Fact]
    public void Validate_Should_not_depend_on_analysis_sessions_or_workflow_services()
    {
        var constructor = typeof(FinancialMetricsController).GetConstructors()
            .Should()
            .ContainSingle()
            .Subject;

        constructor.GetParameters()
            .Should()
            .ContainSingle(parameter =>
                parameter.ParameterType == typeof(IStructuredFinancialMetricsValidator)
            );
    }

    private static FinancialMetricsController CreateController()
    {
        return new FinancialMetricsController(
            new StructuredFinancialMetricsValidator()
        );
    }
}
