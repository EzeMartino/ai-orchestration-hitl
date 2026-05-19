using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;

public sealed class CompositeStructuredFinancialMetricsProvider
    : IStructuredFinancialMetricsProvider
{
    private readonly IStructuredFinancialMetricsProvider _sessionProvider;
    private readonly IStructuredFinancialMetricsProvider _fixtureProvider;
    private readonly DataAgentOptions _options;
    private readonly ILogger<CompositeStructuredFinancialMetricsProvider> _logger;

    public CompositeStructuredFinancialMetricsProvider(
        IStructuredFinancialMetricsProvider sessionProvider,
        IStructuredFinancialMetricsProvider fixtureProvider,
        IOptions<DataAgentOptions> options,
        ILogger<CompositeStructuredFinancialMetricsProvider> logger)
    {
        _sessionProvider = sessionProvider;
        _fixtureProvider = fixtureProvider;
        _options = options.Value;
        _logger = logger;
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
            _logger.LogInformation(
                "Using structured financial metrics from analysis session context."
            );

            return sessionMetrics;
        }

        if (_options.UseFixtureMetricsFallback)
        {
            _logger.LogInformation(
                "Using fixture structured financial metrics fallback."
            );

            return await _fixtureProvider.GetMetricsAsync(report, cancellationToken);
        }

        _logger.LogInformation("No structured financial metrics available.");

        return null;
    }
}
