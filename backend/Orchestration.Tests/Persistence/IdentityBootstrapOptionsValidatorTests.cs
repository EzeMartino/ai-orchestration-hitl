using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Orchestration.Infrastructure.Persistence;

namespace Orchestration.Tests.Persistence;

public sealed class IdentityBootstrapOptionsValidatorTests
{
    private readonly IdentityBootstrapOptionsValidator _validator =
        new(Options.Create(new IdentityOptions()));

    [Fact]
    public void Validate_DisabledWithNoUsers_Succeeds()
    {
        var options = new IdentityBootstrapOptions
        {
            Enabled = false,
            Users = []
        };

        var result = _validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_EnabledWithNoUsers_Fails()
    {
        var options = new IdentityBootstrapOptions
        {
            Enabled = true,
            Users = []
        };

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "Valid-Bootstrap9!")]
    [InlineData("bootstrap@example.com", "")]
    public void Validate_PartialUser_Fails(string email, string password)
    {
        var options = EnabledOptions(new IdentityBootstrapUserOptions
        {
            Email = email,
            Password = password
        });

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    public void Validate_BlankOrInvalidEmail_Fails(string email)
    {
        var options = EnabledOptions(ValidUser(email: email));

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("weak")]
    [InlineData("alllowercase1!")]
    [InlineData("ALLUPPERCASE1!")]
    [InlineData("NoDigitsHere!")]
    [InlineData("NoSymbolsHere1")]
    public void Validate_BlankOrWeakPassword_FailsAgainstDefaultIdentityRules(string password)
    {
        var options = EnabledOptions(ValidUser(password: password));

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_ExplicitEmptyId_Fails()
    {
        var options = EnabledOptions(ValidUser(id: Guid.Empty));

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_DuplicateNormalizedEmails_Fails()
    {
        var options = EnabledOptions(
            ValidUser(email: "Bootstrap.User@example.com"),
            ValidUser(email: "bootstrap.user@EXAMPLE.COM"));

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_FailureMessages_NeverContainConfiguredPassword()
    {
        const string configuredPassword = "configured-password-secret";
        var options = EnabledOptions(ValidUser(password: configuredPassword));

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        string.Join(" ", result.Failures!).Should().NotContain(configuredPassword);
    }

    [Fact]
    public void Validate_UsesEffectiveIdentityPasswordOptions()
    {
        var identityOptions = new IdentityOptions();
        identityOptions.Password.RequiredLength = 20;
        var validator = new IdentityBootstrapOptionsValidator(Options.Create(identityOptions));
        var options = EnabledOptions(ValidUser(password: "Valid-Bootstrap9!"));

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    private static IdentityBootstrapOptions EnabledOptions(
        params IdentityBootstrapUserOptions[] users) =>
        new()
        {
            Enabled = true,
            Users = users
        };

    private static IdentityBootstrapUserOptions ValidUser(
        string email = "bootstrap@example.com",
        string password = "Valid-Bootstrap9!",
        Guid? id = null) =>
        new()
        {
            Id = id,
            Email = email,
            Password = password,
            EmailConfirmed = true
        };
}
