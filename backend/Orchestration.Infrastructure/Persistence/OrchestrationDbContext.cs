using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Persistence;
using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Infrastructure.Persistence;

public class OrchestrationDbContext : DbContext, IOrchestrationDbContext
{
    public OrchestrationDbContext(DbContextOptions<OrchestrationDbContext> options)
        : base(options)
    {
    }

    public DbSet<AnalysisSession> AnalysisSessions => Set<AnalysisSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnalysisSession>(builder =>
        {
            builder.ToTable("AnalysisSessions");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Status)
                .HasConversion<string>()
                .IsRequired();

            builder.Property(x => x.ContextJson)
                .HasColumnName("Context")
                .HasColumnType("jsonb")
                .IsRequired();

            builder.Property(x => x.CurrentAgent)
                .HasMaxLength(100);

            builder.Property(x => x.FailureReason)
                .HasMaxLength(1000);

            builder.Property(x => x.CreatedAt)
                .IsRequired();

            builder.Property(x => x.UpdatedAt)
                .IsRequired();
        });
    }
}
