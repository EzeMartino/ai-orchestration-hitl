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
        var result = _validator.Validate(null, new CnvRegulationMcpOptions());

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
