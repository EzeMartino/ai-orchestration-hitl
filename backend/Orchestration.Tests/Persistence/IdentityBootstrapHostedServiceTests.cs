using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orchestration.Infrastructure.Persistence;

namespace Orchestration.Tests.Persistence;

public sealed class IdentityBootstrapHostedServiceTests
{
    [Fact]
    public async Task StartAsync_Disabled_DoesNotCreateServiceScope()
    {
        var scopeFactory = new ThrowingScopeFactory();
        var options = Options.Create(new IdentityBootstrapOptions
        {
            Enabled = false,
            Users = []
        });
        var service = new IdentityBootstrapHostedService(
            scopeFactory,
            options,
            CreateOptionsValidator());

        await service.StartAsync(CancellationToken.None);

        scopeFactory.CreateScopeCalled.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_EnabledValidConfiguration_CreatesMissingUserWithConfiguredValues()
    {
        var configuredId = Guid.NewGuid();
        var options = EnabledOptions(new IdentityBootstrapUserOptions
        {
            Id = configuredId,
            Email = "bootstrap@example.com",
            Password = "Valid-Bootstrap9!",
            EmailConfirmed = true
        });
        await using var provider = CreateProvider(options);

        await StartAsync(provider);

        using var scope = provider.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var user = await userManager.FindByEmailAsync("bootstrap@example.com");
        user.Should().NotBeNull();
        user!.Id.Should().Be(configuredId);
        user.UserName.Should().Be("bootstrap@example.com");
        user.Email.Should().Be("bootstrap@example.com");
        user.EmailConfirmed.Should().BeTrue();
        (await userManager.CheckPasswordAsync(user, "Valid-Bootstrap9!")).Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_MissingOptionalId_GeneratesNonEmptyGuid()
    {
        var options = EnabledOptions(ValidUser());
        await using var provider = CreateProvider(options);

        await StartAsync(provider);

        using var scope = provider.CreateScope();
        var user = await scope.ServiceProvider
            .GetRequiredService<UserManager<IdentityUser<Guid>>>()
            .FindByEmailAsync("bootstrap@example.com");
        user.Should().NotBeNull();
        user!.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task StartAsync_ExistingUser_RemainsEntirelyUnchanged()
    {
        var existingId = Guid.NewGuid();
        const string existingPassword = "Existing-Bootstrap9!";
        var options = EnabledOptions(new IdentityBootstrapUserOptions
        {
            Id = Guid.NewGuid(),
            Email = "BOOTSTRAP@example.com",
            Password = "Different-Bootstrap9!",
            EmailConfirmed = true
        });
        await using var provider = CreateProvider(options);
        string originalHash;

        using (var setupScope = provider.CreateScope())
        {
            var userManager = setupScope.ServiceProvider
                .GetRequiredService<UserManager<IdentityUser<Guid>>>();
            var existingUser = new IdentityUser<Guid>
            {
                Id = existingId,
                UserName = "bootstrap@example.com",
                Email = "bootstrap@example.com",
                EmailConfirmed = false
            };
            var result = await userManager.CreateAsync(existingUser, existingPassword);
            result.Succeeded.Should().BeTrue();
            originalHash = existingUser.PasswordHash!;
        }

        await StartAsync(provider);

        using var verificationScope = provider.CreateScope();
        var verificationManager = verificationScope.ServiceProvider
            .GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var unchanged = await verificationManager.FindByEmailAsync("bootstrap@example.com");
        unchanged.Should().NotBeNull();
        unchanged!.Id.Should().Be(existingId);
        unchanged.Email.Should().Be("bootstrap@example.com");
        unchanged.UserName.Should().Be("bootstrap@example.com");
        unchanged.EmailConfirmed.Should().BeFalse();
        unchanged.PasswordHash.Should().Be(originalHash);
        (await verificationManager.CheckPasswordAsync(unchanged, existingPassword)).Should().BeTrue();
        (await verificationManager.CheckPasswordAsync(unchanged, "Different-Bootstrap9!")).Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_CreateFailure_ThrowsFixedSecretSafeMessage()
    {
        const string configuredEmail = "bootstrap@example.com";
        const string configuredPassword = "Valid-Bootstrap9!";
        var options = EnabledOptions(new IdentityBootstrapUserOptions
        {
            Email = configuredEmail,
            Password = configuredPassword
        });
        await using var provider = CreateProvider(
            options,
            identityBuilder => identityBuilder.AddUserValidator<FailOnSecondValidationUserValidator>());

        var action = () => StartAsync(provider);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should()
            .Be("Identity bootstrap failed for configured user index 0: InjectedFailure");
        exception.Which.Message.Should().NotContain(configuredEmail);
        exception.Which.Message.Should().NotContain(configuredPassword);
    }

    [Fact]
    public async Task StartAsync_InvalidLaterUser_CreatesNoAccounts()
    {
        var options = EnabledOptions(
            ValidUser(email: "first@example.com"),
            ValidUser(email: "second@example.com", password: "weak"));
        await using var provider = CreateProvider(options);

        var action = () => StartAsync(provider);

        await action.Should().ThrowAsync<OptionsValidationException>();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        (await dbContext.Users.CountAsync()).Should().Be(0);
    }

    private static async Task StartAsync(ServiceProvider provider)
    {
        var service = provider.GetRequiredService<IdentityBootstrapHostedService>();
        await service.StartAsync(CancellationToken.None);
    }

    private static ServiceProvider CreateProvider(
        IdentityBootstrapOptions options,
        Action<IdentityBuilder>? configureIdentity = null)
    {
        var services = new ServiceCollection();
        var databaseName = $"identity-bootstrap-{Guid.NewGuid():N}";
        services.AddLogging();
        services.AddDbContext<OrchestrationDbContext>(builder =>
            builder.UseInMemoryDatabase(databaseName));
        var identityBuilder = services
            .AddIdentityCore<IdentityUser<Guid>>()
            .AddEntityFrameworkStores<OrchestrationDbContext>();
        configureIdentity?.Invoke(identityBuilder);
        services.AddSingleton<IOptions<IdentityBootstrapOptions>>(Options.Create(options));
        services.AddSingleton<IValidateOptions<IdentityBootstrapOptions>>(
            CreateOptionsValidator());
        services.AddSingleton<IdentityBootstrapHostedService>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static IdentityBootstrapOptionsValidator CreateOptionsValidator() =>
        new(Options.Create(new IdentityOptions()));

    private static IdentityBootstrapOptions EnabledOptions(
        params IdentityBootstrapUserOptions[] users) =>
        new()
        {
            Enabled = true,
            Users = users
        };

    private static IdentityBootstrapUserOptions ValidUser(
        string email = "bootstrap@example.com",
        string password = "Valid-Bootstrap9!") =>
        new()
        {
            Email = email,
            Password = password,
            EmailConfirmed = true
        };

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public bool CreateScopeCalled { get; private set; }

        public IServiceScope CreateScope()
        {
            CreateScopeCalled = true;
            throw new InvalidOperationException("Disabled bootstrap must not create a scope.");
        }
    }

    private sealed class FailOnSecondValidationUserValidator : IUserValidator<IdentityUser<Guid>>
    {
        private int _validationCount;

        public Task<IdentityResult> ValidateAsync(
            UserManager<IdentityUser<Guid>> manager,
            IdentityUser<Guid> user)
        {
            _validationCount++;
            return Task.FromResult(_validationCount == 1
                ? IdentityResult.Success
                : IdentityResult.Failed(new IdentityError
                {
                    Code = "InjectedFailure",
                    Description = "Sensitive failure description"
                }));
        }
    }
}
