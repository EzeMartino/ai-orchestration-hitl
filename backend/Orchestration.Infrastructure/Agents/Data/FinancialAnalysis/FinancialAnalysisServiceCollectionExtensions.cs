using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;

public static class FinancialAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddPythonFinancialAnalysis(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<
            IFinancialAnalysisPythonInvoker,
            CSnakesFinancialAnalysisPythonInvoker>();
        services.AddScoped<IPythonFinancialAnalysisService>(provider =>
            new CSnakesFinancialAnalysisService(
                provider.GetRequiredService<IFinancialAnalysisPythonInvoker>(),
                provider.GetRequiredService<ILogger<CSnakesFinancialAnalysisService>>()));

        return services;
    }
}
