using FluentAssertions;
using Orchestration.Infrastructure.Persistence;

namespace Orchestration.Tests.Persistence;

public sealed class DatabaseMigrationHostedServiceTests
{
    [Fact]
    public async Task StartAsync_DelegatesToMigratorWithCancellationToken()
    {
        var migrator = new RecordingMigrator();
        var service = new DatabaseMigrationHostedService(migrator);
        using var cancellation = new CancellationTokenSource();

        await service.StartAsync(cancellation.Token);

        migrator.CallCount.Should().Be(1);
        migrator.CancellationToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task StopAsync_CompletesWithoutMigrating()
    {
        var migrator = new RecordingMigrator();
        var service = new DatabaseMigrationHostedService(migrator);

        await service.StopAsync(CancellationToken.None);

        migrator.CallCount.Should().Be(0);
    }

    private sealed class RecordingMigrator : IOrchestrationDatabaseMigrator
    {
        public int CallCount { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task MigrateAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
