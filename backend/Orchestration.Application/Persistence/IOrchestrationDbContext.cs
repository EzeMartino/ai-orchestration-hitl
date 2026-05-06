using Microsoft.EntityFrameworkCore;
using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Application.Persistence;

public interface IOrchestrationDbContext
{
    DbSet<AnalysisSession> AnalysisSessions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
