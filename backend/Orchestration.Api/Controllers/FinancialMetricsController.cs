using Microsoft.AspNetCore.Mvc;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Api.Controllers;

[ApiController]
[Route("api/financial-metrics")]
public sealed class FinancialMetricsController : ControllerBase
{
    private readonly IStructuredFinancialMetricsValidator _validator;

    public FinancialMetricsController(
        IStructuredFinancialMetricsValidator validator)
    {
        _validator = validator;
    }

    [HttpPost("validate")]
    public ActionResult<FinancialMetricsValidationResponse> Validate(
        [FromBody] StructuredFinancialMetricsInput input)
    {
        if (input is null)
        {
            return BadRequest();
        }

        var result = _validator.Validate(input);

        return Ok(new FinancialMetricsValidationResponse(
            IsValid: result.IsValid,
            Metrics: result.Metrics,
            Errors: result.Errors,
            Warnings: result.Warnings
        ));
    }
}

public sealed record FinancialMetricsValidationResponse(
    bool IsValid,
    IReadOnlyList<ValidatedFinancialMetric> Metrics,
    IReadOnlyList<FinancialMetricsValidationIssue> Errors,
    IReadOnlyList<FinancialMetricsValidationIssue> Warnings
);
