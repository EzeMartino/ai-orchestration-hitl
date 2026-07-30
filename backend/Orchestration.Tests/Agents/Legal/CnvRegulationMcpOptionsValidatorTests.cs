using FluentAssertions;
using Microsoft.Extensions.Options;
using Orchestration.Infrastructure.Agents.Legal.Regulations;

namespace Orchestration.Tests.Agents.Legal;

public sealed class CnvRegulationMcpOptionsValidatorTests
{
    private readonly CnvRegulationMcpOptionsValidator _validator = new();

    [Fact]
    public void Validate_Should_accept_defaults_and_a_null_name()
    {
        var options = new CnvRegulationMcpOptions();

        options.MaxEnrichedHits.Should().Be(2);
        options.MaxDocumentContextCharacters.Should().Be(12_000);
        options.MaxArticleContextCharacters.Should().Be(6_000);

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeFalse();
    }

    [Fact]
    public void Validate_Should_accept_zero_context_limits()
    {
        var result = _validator.Validate(Options.DefaultName, new CnvRegulationMcpOptions
        {
            MaxEnrichedHits = 0,
            MaxDocumentContextCharacters = 0,
            MaxArticleContextCharacters = 0
        });

        result.Failed.Should().BeFalse();
    }

    [Fact]
    public void Validate_Should_reject_required_when_mcp_is_disabled()
    {
        var result = _validator.Validate(Options.DefaultName, new CnvRegulationMcpOptions
        {
            Required = true,
            Enabled = false
        });

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain("Required cannot be true when Enabled is false.");
    }

    [Theory]
    [MemberData(nameof(EnabledInvalidConfigurations))]
    public void Validate_Should_require_a_runnable_configuration_when_enabled(
        CnvRegulationMcpOptions options,
        string expectedFailure)
    {
        var result = _validator.Validate(Options.DefaultName, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(expectedFailure);
    }

    public static IEnumerable<object[]> EnabledInvalidConfigurations()
    {
        yield return [new CnvRegulationMcpOptions { Enabled = true, Command = " ", Args = ["run"] }, "Command must be configured when Enabled is true."];
        yield return [new CnvRegulationMcpOptions { Enabled = true, Args = null! }, "Args must contain at least one value when Enabled is true."];
        yield return [new CnvRegulationMcpOptions { Enabled = true, Args = [] }, "Args must contain at least one value when Enabled is true."];
        yield return [new CnvRegulationMcpOptions { Enabled = true, Args = ["run"], ConnectionTimeoutSeconds = 0 }, "ConnectionTimeoutSeconds must be greater than zero when Enabled is true."];
        yield return [new CnvRegulationMcpOptions { Enabled = true, Args = ["run"], ToolCallTimeoutSeconds = 0 }, "ToolCallTimeoutSeconds must be greater than zero when Enabled is true."];
    }

    [Theory]
    [InlineData(-1, 12_000, 6_000, "MaxEnrichedHits must be between 0 and 2 (inclusive).")]
    [InlineData(3, 12_000, 6_000, "MaxEnrichedHits must be between 0 and 2 (inclusive).")]
    [InlineData(2, -1, 6_000, "MaxDocumentContextCharacters must be between 0 and 12000 (inclusive).")]
    [InlineData(2, 12_001, 6_000, "MaxDocumentContextCharacters must be between 0 and 12000 (inclusive).")]
    [InlineData(2, 12_000, -1, "MaxArticleContextCharacters must be between 0 and 6000 (inclusive).")]
    [InlineData(2, 12_000, 6_001, "MaxArticleContextCharacters must be between 0 and 6000 (inclusive).")]
    public void Validate_Should_reject_out_of_range_enrichment_limits(
        int maxEnrichedHits,
        int maxDocumentContextCharacters,
        int maxArticleContextCharacters,
        string expectedFailure)
    {
        var result = _validator.Validate(Options.DefaultName, new CnvRegulationMcpOptions
        {
            MaxEnrichedHits = maxEnrichedHits,
            MaxDocumentContextCharacters = maxDocumentContextCharacters,
            MaxArticleContextCharacters = maxArticleContextCharacters
        });

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Be(expectedFailure);
    }
}
