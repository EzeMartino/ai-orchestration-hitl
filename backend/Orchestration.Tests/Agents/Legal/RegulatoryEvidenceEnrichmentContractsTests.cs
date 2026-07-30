using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Legal.Cnv;
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

    [Fact]
    public void LegalQueryStrategyAudit_Should_deserialize_prior_payload_without_enrichments()
    {
        const string json = """
        {
          "strategyVersion": "v1",
          "source": "cnv_mcp",
          "fallbackReason": null,
          "dataToolStatus": "Unavailable",
          "financialAnalysisStatus": null,
          "failedStages": [],
          "queries": []
        }
        """;

        var audit = JsonSerializer.Deserialize<LegalQueryStrategyAudit>(json, JsonOptions);

        audit.Should().NotBeNull();
        audit!.Enrichments.Should().BeNull();
    }

    [Fact]
    public void LegalQueryStrategyAudit_Should_round_trip_populated_enrichments()
    {
        var enrichment = new LegalCnvEnrichmentAudit(
            EnrichmentId: "enrichment-1",
            Rank: 1,
            CandidateKey: "document-1:chunk-1",
            Score: 0.98,
            ContributingQueryIndices: [0, 2],
            Document: new LegalCnvEnrichmentStageAudit(
                Selected: true,
                Attempted: true,
                FromCache: false,
                Status: LegalCnvEnrichmentStageStatuses.Succeeded,
                OriginalTextLength: 12001,
                IsTruncated: true),
            Article: new LegalCnvEnrichmentStageAudit(
                Selected: true,
                Attempted: false,
                FromCache: true,
                Status: LegalCnvEnrichmentStageStatuses.NotAttempted,
                OriginalTextLength: 6001,
                IsTruncated: true),
            Status: RegulatoryEvidenceEnrichmentStatuses.Partial,
            LimitationCodes: ["document_truncated"]);
        var audit = new LegalQueryStrategyAudit(
            StrategyVersion: "v1",
            Source: "cnv_mcp",
            FallbackReason: null,
            DataToolStatus: "Unavailable",
            FinancialAnalysisStatus: null,
            FailedStages: [],
            Queries: [],
            Enrichments: [enrichment]);

        var json = JsonSerializer.Serialize(audit, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<LegalQueryStrategyAudit>(json, JsonOptions);

        roundTripped.Should().BeEquivalentTo(audit);
        roundTripped!.Enrichments.Should().ContainSingle().Which.Document.Status
            .Should().Be(LegalCnvEnrichmentStageStatuses.Succeeded);
        roundTripped.Enrichments.Should().ContainSingle().Which.Article.Status
            .Should().Be(LegalCnvEnrichmentStageStatuses.NotAttempted);
        roundTripped.Enrichments.Should().ContainSingle().Which.LimitationCodes
            .Should().BeEquivalentTo(["document_truncated"]);
    }
}
