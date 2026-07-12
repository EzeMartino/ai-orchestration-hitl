using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Extraction;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class LocalPdfTemporaryDirectorySweeperTests
{
    [Fact]
    public void SweepOnce_Should_delete_only_stale_matching_directories()
    {
        var root = Directory.CreateTempSubdirectory(
            "ai-orchestration-hitl-sweeper-tests-").FullName;
        var stale = Directory.CreateDirectory(Path.Combine(
            root,
            LocalPdfTemporaryDirectorySweeper.DirectoryPrefix + "stale"));
        var fresh = Directory.CreateDirectory(Path.Combine(
            root,
            LocalPdfTemporaryDirectorySweeper.DirectoryPrefix + "fresh"));
        var unrelated = Directory.CreateDirectory(Path.Combine(root, "unrelated"));
        var now = DateTimeOffset.UtcNow;
        stale.LastWriteTimeUtc = now.UtcDateTime.AddHours(-2);

        try
        {
            LocalPdfTemporaryDirectorySweeper.SweepOnce(
                root,
                now,
                TimeSpan.FromHours(1),
                NullLogger.Instance);

            Directory.Exists(stale.FullName).Should().BeFalse();
            Directory.Exists(fresh.FullName).Should().BeTrue();
            Directory.Exists(unrelated.FullName).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
