using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Legal.Regulations;

namespace Orchestration.Tests.Agents.Legal;

public sealed class RegulatoryEvidenceEnrichmentContractsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void RegulatoryEvidenceEnrichment_Should_round_trip_original_and_bounded_canonical_contexts_separately()
    {
        var originalCitation = new RegulatoryEvidenceCitation(
            Source: "search-result",
            DocumentType: null,
            ResolutionNumber: null,
            Title: "Original result title",
            Chapter: null,
            Section: null,
            Article: "42",
            PublicationDate: null,
            Url: "https://search.example/original",
            QuotedText: "Original quoted text");
        var canonicalCitation = new RegulatoryEvidenceCitation(
            Source: "CNV",
            DocumentType: "General Rules",
            ResolutionNumber: "622/2013",
            Title: "Canonical CNV rules",
            Chapter: "III",
            Section: "4",
            Article: "42",
            PublicationDate: "2013-09-05",
            Url: "https://cnv.example/rules/42",
            QuotedText: "Canonical article quoted text");
        var enrichment = new RegulatoryEvidenceEnrichment(
            EnrichmentId: "enrichment-1",
            DocumentId: "document-1",
            ChunkId: "chunk-1",
            Rank: 1,
            Score: 0.98,
            Original: new RegulatoryOriginalEvidence("Original result snippet", originalCitation),
            Document: new RegulatoryCanonicalDocument(
                Id: "canonical-document-1",
                Source: "CNV",
                DocumentType: "General Rules",
                ResolutionNumber: "622/2013",
                Title: "Canonical CNV rules",
                PublicationDate: "2013-09-05",
                EffectiveDate: "2013-09-05",
                Url: "https://cnv.example/rules",
                Status: "current",
                RequiresReview: false,
                RetrievedAt: "2026-07-21T12:00:00Z",
                Text: "Bounded canonical document context",
                OriginalTextLength: 12001,
                IsTruncated: true,
                Metadata: new Dictionary<string, string> { ["jurisdiction"] = "AR" },
                Citations: [canonicalCitation]),
            Article: new RegulatoryCanonicalArticle(
                Citation: canonicalCitation,
                Text: "Bounded canonical article context",
                Confidence: 0.99,
                OriginalTextLength: 6001,
                IsTruncated: true),
            Status: RegulatoryEvidenceEnrichmentStatuses.Partial,
            Limitations: ["Document text was truncated."]);

        var json = JsonSerializer.Serialize(enrichment, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<RegulatoryEvidenceEnrichment>(json, JsonOptions);
        using var document = JsonDocument.Parse(json);

        roundTripped.Should().BeEquivalentTo(enrichment);
        document.RootElement.GetProperty("original").GetProperty("citation").GetProperty("source").GetString()
            .Should().Be("search-result");
        document.RootElement.GetProperty("document").GetProperty("citations")[0].GetProperty("source").GetString()
            .Should().Be("CNV");
        document.RootElement.GetProperty("article").GetProperty("citation").GetProperty("title").GetString()
            .Should().Be("Canonical CNV rules");
    }
}
