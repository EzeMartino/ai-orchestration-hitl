using System.Text.Json;
using System.Text.Json.Nodes;
using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Application.Agents.Shared;

public sealed class FinancialReportContextResolver : IFinancialReportContextResolver
{
    public const string RequiredCode = "FINANCIAL_REPORT_SUMMARY_REQUIRED";
    public const string InvalidCode = "FINANCIAL_REPORT_SUMMARY_INVALID";

    private const string ContextPropertyName = "financialReport";
    private const string RequiredMessage = "Financial report summary is required.";
    private const string InvalidMessage = "Financial report summary is invalid.";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    public FinancialReportContextResolution Resolve(AnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrWhiteSpace(session.ContextJson))
        {
            return Required();
        }

        try
        {
            var root = JsonNode.Parse(session.ContextJson) as JsonObject;

            if (root is null)
            {
                return Invalid();
            }

            if (!root.TryGetPropertyValue(ContextPropertyName, out var reportNode))
            {
                return Required();
            }

            if (reportNode is null)
            {
                return Invalid();
            }

            var input = reportNode.Deserialize<FinancialReportSummaryInput>(JsonOptions);
            var validation = FinancialReportSummaryValidator.Validate(input);

            if (!validation.IsValid || validation.Summary is null)
            {
                var submittedAtError = validation.Errors.FirstOrDefault(error =>
                    error.Code is FinancialReportSummaryValidator.SubmittedAtRequiredCode
                        or FinancialReportSummaryValidator.SubmittedAtInvalidCode);

                if (submittedAtError is not null)
                {
                    return Invalid(submittedAtError.Code, submittedAtError.Message);
                }

                return Invalid();
            }

            var summary = validation.Summary;

            return new FinancialReportContextResolution(
                IsValid: true,
                Report: new FinancialReportContext(
                    SessionId: session.Id,
                    ReportName: summary.ReportName,
                    TotalAmount: summary.TotalAmount,
                    TransactionCount: summary.TransactionCount,
                    SubmittedAt: summary.SubmittedAt),
                ErrorCode: null,
                ErrorMessage: null);
        }
        catch (JsonException)
        {
            return Invalid();
        }
        catch (NotSupportedException)
        {
            return Invalid();
        }
    }

    private static FinancialReportContextResolution Required()
    {
        return new FinancialReportContextResolution(
            IsValid: false,
            Report: null,
            ErrorCode: RequiredCode,
            ErrorMessage: RequiredMessage);
    }

    private static FinancialReportContextResolution Invalid(
        string code = InvalidCode,
        string message = InvalidMessage)
    {
        return new FinancialReportContextResolution(
            IsValid: false,
            Report: null,
            ErrorCode: code,
            ErrorMessage: message);
    }
}
