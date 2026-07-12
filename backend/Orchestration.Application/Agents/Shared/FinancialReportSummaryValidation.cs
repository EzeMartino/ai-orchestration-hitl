namespace Orchestration.Application.Agents.Shared;

public sealed record FinancialReportSummaryValidation(
    FinancialReportSummary? Summary,
    IReadOnlyList<FinancialReportSummaryValidationIssue> Errors)
{
    public bool IsValid => Summary is not null && Errors.Count == 0;
}

public sealed record FinancialReportSummaryValidationIssue(
    string Code,
    string Message);

public static class FinancialReportSummaryValidator
{
    public static FinancialReportSummaryValidation Validate(
        FinancialReportSummaryInput? input)
    {
        if (input is null)
        {
            return Invalid(
                new FinancialReportSummaryValidationIssue(
                    "REPORT_SUMMARY_REQUIRED",
                    "Report summary is required."));
        }

        var errors = new List<FinancialReportSummaryValidationIssue>();

        if (string.IsNullOrWhiteSpace(input.ReportName))
        {
            errors.Add(new FinancialReportSummaryValidationIssue(
                "REPORT_NAME_REQUIRED",
                "Report name is required."));
        }

        if (input.TotalAmount is null)
        {
            errors.Add(new FinancialReportSummaryValidationIssue(
                "TOTAL_AMOUNT_REQUIRED",
                "Total amount is required."));
        }
        else if (input.TotalAmount < 0m)
        {
            errors.Add(new FinancialReportSummaryValidationIssue(
                "TOTAL_AMOUNT_INVALID",
                "Total amount cannot be negative."));
        }

        if (input.TransactionCount is null)
        {
            errors.Add(new FinancialReportSummaryValidationIssue(
                "TRANSACTION_COUNT_REQUIRED",
                "Transaction count is required."));
        }
        else if (input.TransactionCount < 0)
        {
            errors.Add(new FinancialReportSummaryValidationIssue(
                "TRANSACTION_COUNT_INVALID",
                "Transaction count cannot be negative."));
        }

        if (input.SubmittedAt is null)
        {
            errors.Add(new FinancialReportSummaryValidationIssue(
                "SUBMITTED_AT_REQUIRED",
                "Submission date is required."));
        }
        else if (input.SubmittedAt.Value == default)
        {
            errors.Add(new FinancialReportSummaryValidationIssue(
                "SUBMITTED_AT_INVALID",
                "Submission date must be valid."));
        }

        if (errors.Count > 0)
        {
            return new FinancialReportSummaryValidation(null, errors);
        }

        return new FinancialReportSummaryValidation(
            new FinancialReportSummary(
                input.ReportName!.Trim(),
                input.TotalAmount!.Value,
                input.TransactionCount!.Value,
                input.SubmittedAt!.Value),
            []);
    }

    private static FinancialReportSummaryValidation Invalid(
        FinancialReportSummaryValidationIssue error)
    {
        return new FinancialReportSummaryValidation(null, [error]);
    }
}
