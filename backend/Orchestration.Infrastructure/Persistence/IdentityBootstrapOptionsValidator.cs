using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Orchestration.Infrastructure.Persistence;

public sealed class IdentityBootstrapOptionsValidator(
    IOptions<IdentityOptions> identityOptions)
    : IValidateOptions<IdentityBootstrapOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        IdentityBootstrapOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (options.Users.Count == 0)
        {
            failures.Add("Identity bootstrap requires at least one configured user when enabled.");
        }

        var normalizedEmails = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < options.Users.Count; index++)
        {
            var user = options.Users[index];

            if (user.Id == Guid.Empty)
            {
                failures.Add(
                    $"Identity bootstrap user at index {index} has an empty explicit ID.");
            }

            if (string.IsNullOrWhiteSpace(user.Email)
                || !new EmailAddressAttribute().IsValid(user.Email))
            {
                failures.Add(
                    $"Identity bootstrap user at index {index} has an invalid email.");
            }
            else if (!normalizedEmails.Add(NormalizeEmail(user.Email)))
            {
                failures.Add(
                    $"Identity bootstrap user at index {index} duplicates another configured email.");
            }

            if (!SatisfiesPasswordPolicy(
                    user.Password,
                    identityOptions.Value.Password))
            {
                failures.Add(
                    $"Identity bootstrap user at index {index} does not satisfy the configured Identity password policy.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static string NormalizeEmail(string email) =>
        email.Normalize().ToUpperInvariant();

    private static bool SatisfiesPasswordPolicy(
        string password,
        PasswordOptions options)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < options.RequiredLength
            || password.Distinct().Count() < options.RequiredUniqueChars)
        {
            return false;
        }

        return (!options.RequireDigit || password.Any(char.IsDigit))
            && (!options.RequireLowercase || password.Any(char.IsLower))
            && (!options.RequireUppercase || password.Any(char.IsUpper))
            && (!options.RequireNonAlphanumeric
                || password.Any(character => !char.IsLetterOrDigit(character)));
    }
}
