using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Persistence;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Domain.Activity;
using Orchestration.Domain.FinancialMetricsExtraction;

namespace Orchestration.Infrastructure.Persistence;

public class OrchestrationDbContext :
    IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>,
    IOrchestrationDbContext,
    IDataProtectionKeyContext
{
    public OrchestrationDbContext(DbContextOptions<OrchestrationDbContext> options)
        : base(options)
    {
    }

    public DbSet<AnalysisSession> AnalysisSessions => Set<AnalysisSession>();
    public DbSet<ActivityEventLog> ActivityEvents => Set<ActivityEventLog>();
    public DbSet<FinancialMetricsExtractionDraft> FinancialMetricsExtractionDrafts =>
        Set<FinancialMetricsExtractionDraft>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public void ClearTrackedChanges()
    {
        ChangeTracker.Clear();
    }

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
                .IsConcurrencyToken()
                .IsRequired();

            builder.Property(x => x.ContextJson)
                .HasColumnName("Context")
                .HasColumnType("jsonb")
                .IsConcurrencyToken()
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

        modelBuilder.Entity<FinancialMetricsExtractionDraft>(builder =>
        {
            builder.ToTable("FinancialMetricsExtractionDrafts");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.SessionId)
                .IsRequired();

            builder.HasOne<AnalysisSession>()
                .WithMany()
                .HasForeignKey(x => x.SessionId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            builder.Property(x => x.UserId)
                .IsRequired();

            builder.HasOne<IdentityUser<Guid>>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            builder.Property(x => x.Status)
                .HasConversion<string>()
                .IsConcurrencyToken()
                .IsRequired();

            builder.Property(x => x.OriginalFileName)
                .HasMaxLength(FinancialMetricsExtractionDraft.OriginalFileNameMaxLength)
                .IsRequired();

            builder.Property(x => x.FileSizeBytes)
                .IsRequired();

            builder.Property(x => x.ContentHash)
                .HasMaxLength(FinancialMetricsExtractionDraft.ContentHashMaxLength)
                .IsRequired();

            builder.Property(x => x.PayloadJson)
                .HasColumnType("jsonb")
                .IsRequired();

            builder.Property(x => x.ReviewedByUserId);

            builder.HasOne<IdentityUser<Guid>>()
                .WithMany()
                .HasForeignKey(x => x.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Property(x => x.CreatedAt)
                .IsRequired();

            builder.Property(x => x.UpdatedAt)
                .IsConcurrencyToken()
                .IsRequired();

            builder.Property(x => x.CompletedAt);

            builder.HasIndex(x => x.SessionId)
                .IsUnique()
                .HasFilter("\"Status\" = 'PendingReview'");

            builder.HasIndex(x => new { x.UserId, x.SessionId, x.Status });
        });
    }
}
