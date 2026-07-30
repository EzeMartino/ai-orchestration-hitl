using FluentAssertions;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Legal;

namespace Orchestration.Tests.Agents.Legal;

public class LegalCompliancePluginTests
{
    [Fact]
    public async Task ReviewFinancialComplianceAsync_Should_propagate_evidence_enrichments_unchanged()
    {
        var enrichments = Array.AsReadOnly(
        [
            CreateEvidenceEnrichment("enrichment-2", rank: 2),
            CreateEvidenceEnrichment("enrichment-1", rank: 1)
        ]);
        var source = new StubRegulatoryKnowledgeSource(
            new RegulatoryReviewResult(
                HasComplianceRisk: false,
                RiskLevel: "NotEstablished",
                Summary: "La evidencia requiere revisión humana.",
                SourceEngine: "Test regulatory source",
                Findings: [],
                Warnings: [],
                RequiresHumanReview: true,
                EvidenceEnrichments: enrichments));
        var plugin = new LegalCompliancePlugin(source);

        var result = await plugin.ReviewFinancialComplianceAsync(
            reportName: "legal-plugin-enrichment-test",
            totalAmount: 100d,
            transactionCount: 1,
            cancellationToken: CancellationToken.None);

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.EvidenceEnrichments.Should().BeSameAs(enrichments);
        result.EvidenceEnrichments.Should().Equal(enrichments);
        result.EvidenceEnrichments.Should()
            .BeAssignableTo<IList<RegulatoryEvidenceEnrichment>>()
            .Which.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public async Task ReviewFinancialComplianceAsync_Should_propagate_evidence_assessment_unchanged()
    {
        var evidenceAssessment = new RegulatoryEvidenceAssessment(
            EvidenceFound: true,
            Relevance: "Strong",
            Applicability: "Applicable",
            EvidenceQuality: "Weak",
            Severity: "Warning",
            RequiresHumanReview: true,
            Reasons: ["La evidencia requiere una decisión humana."]
        );
        var source = new StubRegulatoryKnowledgeSource(
            new RegulatoryReviewResult(
                HasComplianceRisk: false,
                RiskLevel: "NotEstablished",
                Summary: "La evidencia no permite establecer el riesgo.",
                SourceEngine: "Test regulatory source",
                Findings: [],
                Warnings: [],
                EvidenceAssessment: evidenceAssessment
            )
        );
        var plugin = new LegalCompliancePlugin(source);

        var result = await plugin.ReviewFinancialComplianceAsync(
            reportName: "legal-plugin-test-report",
            totalAmount: 100d,
            transactionCount: 1,
            cancellationToken: CancellationToken.None
        );

        result.EvidenceAssessment.Should().BeEquivalentTo(evidenceAssessment);
    }

    private static RegulatoryEvidenceEnrichment CreateEvidenceEnrichment(
        string enrichmentId,
        int rank)
    {
        var citation = new RegulatoryEvidenceCitation(
            Source: "CNV",
            DocumentType: "Normas",
            ResolutionNumber: "622/2013",
            Title: $"Título {rank}",
            Chapter: "I",
            Section: "1",
            Article: $"Artículo {rank}",
            PublicationDate: "2013-09-05",
            Url: $"https://example.test/cnv/{rank}",
            QuotedText: $"Texto citado {rank}");
        return new RegulatoryEvidenceEnrichment(
            EnrichmentId: enrichmentId,
            DocumentId: $"document-{rank}",
            ChunkId: $"chunk-{rank}",
            Rank: rank,
            Score: 0.9,
            Original: new RegulatoryOriginalEvidence($"Fragmento {rank}", citation),
            Document: null,
            Article: new RegulatoryCanonicalArticle(
                citation,
                $"Texto canónico {rank}",
                Confidence: 0.95,
                OriginalTextLength: $"Texto canónico {rank}".Length,
                IsTruncated: false),
            Status: RegulatoryEvidenceEnrichmentStatuses.Partial,
            Limitations: Array.AsReadOnly([$"Limitación {rank}"]));
    }

    private sealed class StubRegulatoryKnowledgeSource(RegulatoryReviewResult result)
        : IRegulatoryKnowledgeSource
    {
        public Task<RegulatoryReviewResult> ReviewAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }
}
