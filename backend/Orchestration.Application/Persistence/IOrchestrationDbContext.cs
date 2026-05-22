using Microsoft.EntityFrameworkCore;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Domain.Activity;

namespace Orchestration.Application.Persistence;

public interface IOrchestrationDbContext
{
    DbSet<AnalysisSession> AnalysisSessions { get; }
    DbSet<ActivityEventLog> ActivityEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
