using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orchestration.Infrastructure.Persistence;

namespace Orchestration.Tests.Persistence;

public sealed class PersistentDataProtectionTests
{
    [Fact]
    public void OrchestrationDbContext_ImplementsDataProtectionKeyContractAndIncludesEntity()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase($"data-protection-model-{Guid.NewGuid():N}")
            .Options;
        using var dbContext = new OrchestrationDbContext(options);

        dbContext.Should().BeAssignableTo<IDataProtectionKeyContext>();
        dbContext.Model.FindEntityType(typeof(DataProtectionKey)).Should().NotBeNull();
    }

    [Fact]
    public void AddPersistentDataProtection_UsesStableApplicationDiscriminator()
    {
        var services = CreateServices();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var options = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;

        options.ApplicationDiscriminator.Should().Be("ai-orchestration-hitl");
    }

    [Fact]
    public void AddPersistentDataProtection_ProtectsUnprotectsAndPersistsKeyRow()
    {
        var services = CreateServices();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        var protector = provider
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("persistent-data-protection-test");

        var protectedValue = protector.Protect("payload");
        var roundTrip = protector.Unprotect(protectedValue);

        roundTrip.Should().Be("payload");
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        dbContext.DataProtectionKeys.Should().ContainSingle();
        dbContext.DataProtectionKeys.Single().Xml.Should().NotBeNullOrWhiteSpace();
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        var databaseName = $"data-protection-{Guid.NewGuid():N}";
        services.AddLogging();
        services.AddDbContext<OrchestrationDbContext>(builder =>
            builder.UseInMemoryDatabase(databaseName));
        services.AddPersistentDataProtection();
        return services;
    }
}
