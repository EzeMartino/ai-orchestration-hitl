using FluentAssertions;
using Orchestration.Application.FinancialAnalysis.Thresholds;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class FinancialRiskThresholdProfileProviderTests
{
    private readonly InMemoryFinancialRiskThresholdProfileProvider _provider = new();

    [Theory]
    [InlineData("default", "default", false)]
    [InlineData("oil_and_gas", "oil_and_gas", false)]
    [InlineData("strict", "strict", false)]
    [InlineData("demo", "demo", false)]
    [InlineData("default_oil_and_gas_equity_research", "oil_and_gas", false)]
    [InlineData("", "default", false)]
    [InlineData("   ", "default", false)]
    [InlineData(null, "default", false)]
    public void ResolveProfile_Should_resolve_standard_profiles_and_legacy_aliases(
        string? requestedName,
        string expectedResolvedName,
        bool expectedUsedFallback)
    {
        var resolution = _provider.ResolveProfile(requestedName);

        resolution.Should().NotBeNull();
        resolution.RequestedProfile.Should().Be(requestedName);
        resolution.Profile.Should().NotBeNull();
        resolution.Profile.Name.Should().Be(expectedResolvedName);
        resolution.UsedFallback.Should().Be(expectedUsedFallback);
        resolution.Warnings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("unknown_profile")]
    [InlineData("invalid-name")]
    [InlineData("oil-and-gas")]
    public void ResolveProfile_Should_fallback_to_default_with_warning_when_profile_is_unknown(string requestedName)
    {
        var resolution = _provider.ResolveProfile(requestedName); // Wait, let's check ResolveProfile logic

        resolution.Should().NotBeNull();
        resolution.RequestedProfile.Should().Be(requestedName);
        resolution.Profile.Should().NotBeNull();
        resolution.Profile.Name.Should().Be("default");
        resolution.UsedFallback.Should().BeTrue();
        resolution.Warnings.Should().ContainSingle()
            .Which.Should().Be($"Requested threshold profile '{requestedName}' was not found. Fallen back to 'default'.");
    }

    [Fact]
    public void Profiles_Should_have_distinct_values_corresponding_to_definitions()
    {
        var defaultProfile = _provider.ResolveProfile("default").Profile;
        var strictProfile = _provider.ResolveProfile("strict").Profile;
        var demoProfile = _provider.ResolveProfile("demo").Profile;
        var oilAndGasProfile = _provider.ResolveProfile("oil_and_gas").Profile;

        // Verify some properties to assert distinct profiles
        defaultProfile.Thresholds.Should().HaveCount(5);
        strictProfile.Thresholds.Should().HaveCount(5);
        demoProfile.Thresholds.Should().HaveCount(5);
        oilAndGasProfile.Thresholds.Should().HaveCount(5);

        // net_debt_to_ebitda threshold comparisons
        defaultProfile.Thresholds.First(t => t.Metric == "net_debt_to_ebitda").Value.Should().Be(3.0m);
        strictProfile.Thresholds.First(t => t.Metric == "net_debt_to_ebitda").Value.Should().Be(2.5m);
        demoProfile.Thresholds.First(t => t.Metric == "net_debt_to_ebitda").Value.Should().Be(1.0m);
        oilAndGasProfile.Thresholds.First(t => t.Metric == "net_debt_to_ebitda").Value.Should().Be(3.0m);

        // current_ratio threshold comparisons
        defaultProfile.Thresholds.First(t => t.Metric == "current_ratio").Value.Should().Be(1.2m);
        strictProfile.Thresholds.First(t => t.Metric == "current_ratio").Value.Should().Be(1.5m);
        demoProfile.Thresholds.First(t => t.Metric == "current_ratio").Value.Should().Be(3.0m);
        oilAndGasProfile.Thresholds.First(t => t.Metric == "current_ratio").Value.Should().Be(1.0m);
    }
}
