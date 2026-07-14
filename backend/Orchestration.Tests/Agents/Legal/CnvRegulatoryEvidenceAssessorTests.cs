using FluentAssertions;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Tests.Agents.Legal;

public sealed class CnvRegulatoryEvidenceAssessorTests
{
    public static TheoryData<double, string, string, bool, bool> RelevanceBoundaryCases =>
        new()
        {
            { 0.39, "None", "Info", false, false },
            { 0.40, "Weak", "Warning", true, true },
            { double.BitDecrement(0.75), "Weak", "Warning", true, true },
            { 0.75, "Strong", "Warning", true, true }
        };

    [Fact]
    public void Assess_Should_classify_empty_results_as_no_evidence()
    {
        var result = CnvRegulatoryEvidenceAssessor.Assess([]);

        result.Should().BeEquivalentTo(new
        {
            EvidenceFound = false,
            Relevance = "None",
            Applicability = "NotEstablished",
            EvidenceQuality = "None",
            Severity = "Info",
            RequiresHumanReview = false
        });
        result.Reasons.Should().ContainSingle(reason =>
            reason.Contains("no devolvió evidencia regulatoria", StringComparison.OrdinalIgnoreCase) &&
            reason.Contains("no establece aplicabilidad legal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Assess_Should_classify_irrelevant_citation_independently_from_quality()
    {
        var searchResult = CreateResult(score: 0.39, citation: CreateCitation());

        var result = CnvRegulatoryEvidenceAssessor.Assess([searchResult]);

        CnvRegulatoryEvidenceAssessor.IsRelevant(searchResult).Should().BeFalse();
        result.EvidenceFound.Should().BeTrue();
        result.Relevance.Should().Be("None");
        result.EvidenceQuality.Should().Be("Strong");
        result.Severity.Should().Be("Info");
        result.RequiresHumanReview.Should().BeFalse();
    }

    [Fact]
    public void Assess_Should_classify_relevant_incomplete_citation_as_weak_evidence()
    {
        var incomplete = CreateCitation() with { Url = null, QuotedText = null };
        var searchResult = CreateResult(score: 0.60, citation: incomplete);

        var result = CnvRegulatoryEvidenceAssessor.Assess([searchResult]);

        CnvRegulatoryEvidenceAssessor.IsRelevant(searchResult).Should().BeTrue();
        result.Relevance.Should().Be("Weak");
        result.EvidenceQuality.Should().Be("Weak");
        result.Applicability.Should().Be("NotEstablished");
        result.Severity.Should().Be("Warning");
        result.RequiresHumanReview.Should().BeTrue();
        result.Reasons.Should().Contain(
            "La puntuación de relevancia de recuperación fue al menos 0,40 y menor que 0,75.");
    }

    [Fact]
    public void Assess_Should_classify_relevant_complete_citation_as_strong_evidence()
    {
        var result = CnvRegulatoryEvidenceAssessor.Assess(
            [CreateResult(score: 0.75, citation: CreateCitation())]);

        result.Relevance.Should().Be("Strong");
        result.EvidenceQuality.Should().Be("Strong");
        result.Applicability.Should().Be("NotEstablished");
        result.Severity.Should().Be("Warning");
        result.RequiresHumanReview.Should().BeTrue();
        result.Reasons.Should().Contain(reason =>
            reason.Contains("aplicabilidad", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(RelevanceBoundaryCases))]
    public void Assess_Should_classify_relevance_at_thresholds(
        double score,
        string expectedRelevance,
        string expectedSeverity,
        bool expectedHumanReview,
        bool expectedIsRelevant)
    {
        var searchResult = CreateResult(score, CreateCitation());

        var result = CnvRegulatoryEvidenceAssessor.Assess([searchResult]);

        result.Relevance.Should().Be(expectedRelevance);
        result.Severity.Should().Be(expectedSeverity);
        result.RequiresHumanReview.Should().Be(expectedHumanReview);
        CnvRegulatoryEvidenceAssessor.IsRelevant(searchResult).Should().Be(expectedIsRelevant);
    }

    [Theory]
    [InlineData("Complete", "Strong")]
    [InlineData("Source", "Weak")]
    [InlineData("Title", "Weak")]
    [InlineData("Locator", "Weak")]
    [InlineData("Url", "Weak")]
    [InlineData("QuotedText", "Weak")]
    public void Assess_Should_require_every_citation_field_for_strong_quality(
        string citationVariant,
        string expectedQuality)
    {
        var citation = citationVariant switch
        {
            "Complete" => CreateCitation(),
            "Source" => CreateCitation() with { Source = " " },
            "Title" => CreateCitation() with { Title = " " },
            "Locator" => CreateCitation() with
            {
                Article = null,
                Section = null,
                Chapter = null
            },
            "Url" => CreateCitation() with { Url = null },
            "QuotedText" => CreateCitation() with { QuotedText = null },
            _ => throw new ArgumentOutOfRangeException(nameof(citationVariant))
        };

        var result = CnvRegulatoryEvidenceAssessor.Assess(
            [CreateResult(score: 0.75, citation)]);

        result.EvidenceQuality.Should().Be(expectedQuality);
    }

    private static CnvRegulationSearchResult CreateResult(
        double score,
        CnvRegulationCitation? citation) =>
        new(
            DocumentId: "doc-1",
            ChunkId: "chunk-1",
            Title: "Norma CNV",
            Chapter: null,
            Section: null,
            Article: "Artículo 1",
            Source: "CNV",
            Url: "https://www.argentina.gob.ar/cnv",
            Snippet: "Texto recuperado.",
            Score: score,
            Citations: citation is null ? [] : [citation]);

    private static CnvRegulationCitation CreateCitation() =>
        new(
            Source: "CNV",
            DocumentType: "Resolución General",
            ResolutionNumber: "123/2026",
            Title: "Norma CNV",
            Chapter: null,
            Section: null,
            Article: "Artículo 1",
            PublicationDate: "2026-01-01",
            Url: "https://www.argentina.gob.ar/cnv",
            QuotedText: "Texto normativo citado.");
}
