using Microsoft.EntityFrameworkCore;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Domain.Activity;
using Orchestration.Domain.FinancialMetricsExtraction;

namespace Orchestration.Application.Persistence;

public interface IOrchestrationDbContext
{
    DbSet<AnalysisSession> AnalysisSessions { get; }
    DbSet<ActivityEventLog> ActivityEvents { get; }
    DbSet<FinancialMetricsExtractionDraft> FinancialMetricsExtractionDrafts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
