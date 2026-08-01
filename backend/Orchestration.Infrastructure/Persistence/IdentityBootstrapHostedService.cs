using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Orchestration.Infrastructure.Persistence;

public sealed class IdentityBootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityBootstrapOptions> options,
    IValidateOptions<IdentityBootstrapOptions> optionsValidator)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var configuredOptions = options.Value;
        if (!configuredOptions.Enabled)
        {
            return;
        }

        EnsureValidConfiguration(configuredOptions);

        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var preparedUsers = configuredOptions.Users
            .Select((user, index) => new PreparedUser(
                index,
                user.Password,
                new IdentityUser<Guid>
                {
                    Id = user.Id ?? Guid.NewGuid(),
                    UserName = user.Email,
                    Email = user.Email,
                    EmailConfirmed = user.EmailConfirmed
                }))
            .ToArray();
        var missingUsers = new List<PreparedUser>();

        foreach (var preparedUser in preparedUsers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existingUser = await userManager.FindByEmailAsync(
                preparedUser.User.Email!);
            if (existingUser is not null)
            {
                continue;
            }

            missingUsers.Add(preparedUser);
        }

        var preparedIds = new HashSet<Guid>();
        foreach (var preparedUser in missingUsers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!preparedIds.Add(preparedUser.User.Id)
                || await userManager.FindByIdAsync(
                    preparedUser.User.Id.ToString()) is not null)
            {
                throw CreateBootstrapException(
                    preparedUser.Index,
                    [new IdentityError { Code = "DuplicateUserId" }]);
            }
        }

        foreach (var preparedUser in missingUsers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var errors = await ValidateUserAsync(userManager, preparedUser);
            if (errors.Count > 0)
            {
                throw CreateBootstrapException(preparedUser.Index, errors);
            }
        }

        foreach (var preparedUser in missingUsers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await userManager.CreateAsync(
                preparedUser.User,
                preparedUser.Password);
            if (!result.Succeeded)
            {
                throw CreateBootstrapException(
                    preparedUser.Index,
                    result.Errors);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    private void EnsureValidConfiguration(IdentityBootstrapOptions configuredOptions)
    {
        var validationResult = optionsValidator.Validate(
            Options.DefaultName,
            configuredOptions);
        if (validationResult.Failed)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(IdentityBootstrapOptions),
                validationResult.Failures);
        }
    }

    private static async Task<IReadOnlyList<IdentityError>> ValidateUserAsync(
        UserManager<IdentityUser<Guid>> userManager,
        PreparedUser preparedUser)
    {
        var errors = new List<IdentityError>();

        foreach (var userValidator in userManager.UserValidators)
        {
            var result = await userValidator.ValidateAsync(
                userManager,
                preparedUser.User);
            if (!result.Succeeded)
            {
                errors.AddRange(result.Errors);
            }
        }

        foreach (var passwordValidator in userManager.PasswordValidators)
        {
            var result = await passwordValidator.ValidateAsync(
                userManager,
                preparedUser.User,
                preparedUser.Password);
            if (!result.Succeeded)
            {
                errors.AddRange(result.Errors);
            }
        }

        return errors;
    }

    private static InvalidOperationException CreateBootstrapException(
        int index,
        IEnumerable<IdentityError> errors) =>
        new(
            $"Identity bootstrap failed for configured user index {index}: "
            + string.Join(
                ", ",
                errors.Select(error => error.Code).Distinct(StringComparer.Ordinal)));

    private sealed record PreparedUser(
        int Index,
        string Password,
        IdentityUser<Guid> User);
}
