using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;

public sealed class CompositeStructuredFinancialMetricsProvider
    : IStructuredFinancialMetricsProvider
{
    private readonly SessionStructuredFinancialMetricsProvider _sessionProvider;
    private readonly FixtureStructuredFinancialMetricsProvider _fixtureProvider;
    private readonly DataAgentOptions _options;

    public CompositeStructuredFinancialMetricsProvider(
        SessionStructuredFinancialMetricsProvider sessionProvider,
        FixtureStructuredFinancialMetricsProvider fixtureProvider,
        IOptions<DataAgentOptions> options)
    {
        _sessionProvider = sessionProvider;
        _fixtureProvider = fixtureProvider;
        _options = options.Value;
    }

    public async Task<StructuredFinancialMetricsDocument?> GetMetricsAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        var sessionMetrics = await _sessionProvider.GetMetricsAsync(
            report,
            cancellationToken
        );

        if (sessionMetrics is not null)
        {
            return sessionMetrics;
        }

        return _options.UseFixtureMetricsFallback
            ? await _fixtureProvider.GetMetricsAsync(report, cancellationToken)
            : null;
    }
}
