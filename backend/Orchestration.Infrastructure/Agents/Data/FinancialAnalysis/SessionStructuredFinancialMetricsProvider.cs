using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;

public sealed class SessionStructuredFinancialMetricsProvider
    : IStructuredFinancialMetricsProvider
{
    private readonly IStructuredFinancialMetricsSessionService _sessionService;

    public SessionStructuredFinancialMetricsProvider(
        IStructuredFinancialMetricsSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public async Task<StructuredFinancialMetricsDocument?> GetMetricsAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        var context = await _sessionService.GetAsync(
            report.SessionId,
            cancellationToken
        );

        return context is null
            ? null
            : new StructuredFinancialMetricsDocument(
                DocumentId: context.DocumentId,
                Company: context.Company ?? "",
                Currency: context.Currency ?? "",
                Unit: context.Unit ?? "",
                Metrics: context.Metrics
            );
    }
}
