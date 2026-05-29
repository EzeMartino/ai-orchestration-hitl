using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Persistence;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Domain.Activity;

namespace Orchestration.Infrastructure.Persistence;

public class OrchestrationDbContext : IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>, IOrchestrationDbContext
{
    public OrchestrationDbContext(DbContextOptions<OrchestrationDbContext> options)
        : base(options)
    {
    }

    public DbSet<AnalysisSession> AnalysisSessions => Set<AnalysisSession>();
    public DbSet<ActivityEventLog> ActivityEvents => Set<ActivityEventLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AnalysisSession>(builder =>
        {
            builder.ToTable("AnalysisSessions");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.UserId)
                .IsRequired();

            builder.HasOne<IdentityUser<Guid>>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

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

        modelBuilder.Entity<ActivityEventLog>(builder =>
        {
            builder.ToTable("ActivityEvents");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.SessionId)
                .IsRequired();

            builder.Property(x => x.Type)
                .HasMaxLength(150)
                .IsRequired();

            builder.Property(x => x.Agent)
                .HasMaxLength(150)
                .IsRequired();

            builder.Property(x => x.Message)
                .HasMaxLength(2000)
                .IsRequired();

            builder.Property(x => x.Timestamp)
                .IsRequired();

            builder.HasIndex(x => x.SessionId);

            builder.HasIndex(x => new { x.SessionId, x.Timestamp });
        });
    }
}
