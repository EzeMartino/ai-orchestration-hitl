using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Application.Agents.Legal.AiReview;
using Orchestration.Application.Agents.Legal.Regulations;

namespace Orchestration.Tests.Agents.Legal.AiReview;

public sealed class DeterministicLegalAnalysisReviewServiceTests
{
    private readonly DeterministicLegalAnalysisReviewService _service = new();

    [Fact]
    public async Task ReviewAsync_Should_set_UsedLlm_false_and_UsedFallback_true()
    {
        var input = CreateInput(riskSignals: [], cnvEvidence: []);

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
    }

    [Fact]
    public async Task ReviewAsync_Should_not_set_Provider_and_Model()
    {
        var input = CreateInput(riskSignals: [], cnvEvidence: []);

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        result.Provider.Should().BeNull();
        result.Model.Should().BeNull();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task ReviewAsync_Should_create_no_review_areas_when_no_financial_risk_signals()
    {
        var input = CreateInput(riskSignals: [], cnvEvidence: [CreateEvidence(citation: "CNV Art. 42")]);

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        result.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        result.Warnings.Should().Contain("No se proporcionaron señales de riesgo financiero.");
    }

    [Fact]
    public async Task ReviewAsync_Should_create_no_review_areas_when_no_cited_CNV_evidence_exists()
    {
        var signal = CreateSignal(name: "LOW_CURRENT_RATIO", severity: "High");
        var input = CreateInput(
            riskSignals: [signal],
            cnvEvidence: [CreateEvidence(citation: "")] // no citation
        );

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        result.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        result.Warnings.Should().Contain("Había señales de riesgo financiero, pero no había evidencia CNV/Infoleg citada disponible.");
    }

    [Fact]
    public async Task ReviewAsync_Should_create_possible_review_areas_when_financial_signals_and_cited_evidence_exist()
    {
        var signal = CreateSignal(name: "LOW_CURRENT_RATIO", severity: "High");
        var input = CreateInput(
            riskSignals: [signal],
            cnvEvidence: [CreateEvidence(citation: "CNV Art. 42")]
        );

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        result.PossibleRegulatoryReviewAreas.Should().NotBeEmpty();
        var area = result.PossibleRegulatoryReviewAreas.First();
        area.Title.Should().Be("Posible área de revisión de liquidez/divulgación");
        area.Severity.Should().Be("High");
    }

    [Fact]
    public async Task ReviewAsync_Should_use_only_cited_evidence_in_EvidenceCitations()
    {
        var signal = CreateSignal(name: "LOW_CURRENT_RATIO", severity: "High");
        var input = CreateInput(
            riskSignals: [signal],
            cnvEvidence: [
                CreateEvidence(citation: "CNV Art. 42"),
                CreateEvidence(citation: "")
            ]
        );

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        var area = result.PossibleRegulatoryReviewAreas.First();
        area.EvidenceCitations.Should().ContainSingle().Which.Should().Be("CNV Art. 42");
    }

    [Fact]
    public async Task ReviewAsync_Should_ignore_evidence_without_Citation_and_emit_warning()
    {
        var signal = CreateSignal(name: "LOW_CURRENT_RATIO", severity: "High");
        var input = CreateInput(
            riskSignals: [signal],
            cnvEvidence: [
                CreateEvidence(citation: "CNV Art. 42"),
                CreateEvidence(citation: null) // without citation
            ]
        );

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        result.EvidenceReferences.Should().ContainSingle().Which.Citation.Should().Be("CNV Art. 42");
        result.Warnings.Should().Contain("Se ignoró parte de la evidencia CNV/Infoleg porque no tenía cita.");
    }

    [Fact]
    public async Task ReviewAsync_Should_preserve_related_financial_signals()
    {
        var signal1 = CreateSignal(name: "LOW_CURRENT_RATIO", severity: "High");
        var signal2 = CreateSignal(name: "QUICK_RATIO_ALERT", severity: "Medium");
        var input = CreateInput(
            riskSignals: [signal1, signal2],
            cnvEvidence: [CreateEvidence(citation: "CNV Art. 42")]
        );

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        var area = result.PossibleRegulatoryReviewAreas.First();
        area.RelatedFinancialSignals.Should().BeEquivalentTo(["LOW_CURRENT_RATIO", "QUICK_RATIO_ALERT"]);
    }

    [Fact]
    public async Task ReviewAsync_Should_derive_severity_from_financial_signals_highest_severity()
    {
        var signal1 = CreateSignal(name: "LOW_CURRENT_RATIO", severity: "Medium");
        var signal2 = CreateSignal(name: "QUICK_RATIO_ALERT", severity: "High");
        var input = CreateInput(
            riskSignals: [signal1, signal2],
            cnvEvidence: [CreateEvidence(citation: "CNV Art. 42")]
        );

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        var area = result.PossibleRegulatoryReviewAreas.First();
        area.Severity.Should().Be("High");
    }

    [Theory]
    [InlineData("Info")]
    [InlineData("Warning")]
    public async Task ReviewAsync_Should_use_evidence_assessment_severity_instead_of_legacy_signals(
        string assessmentSeverity)
    {
        var input = CreateInput(
            riskSignals: [CreateSignal(name: "LOW_CURRENT_RATIO", severity: "High")],
            cnvEvidence: [CreateEvidence(citation: "CNV Art. 42")],
            evidenceAssessment: CreateAssessment(assessmentSeverity)
        );

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        result.PossibleRegulatoryReviewAreas.Should().OnlyContain(
            area => area.Severity == assessmentSeverity);
    }

    [Fact]
    public async Task ReviewAsync_Should_preserve_legacy_severity_when_evidence_assessment_is_null()
    {
        var input = CreateInput(
            riskSignals: [CreateSignal(name: "LOW_CURRENT_RATIO", severity: "High")],
            cnvEvidence: [CreateEvidence(citation: "CNV Art. 42")],
            evidenceAssessment: null
        );

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        result.PossibleRegulatoryReviewAreas.Should().OnlyContain(area => area.Severity == "High");
    }

    [Fact]
    public async Task ReviewAsync_Should_fail_safe_to_warning_for_invalid_assessment_severity()
    {
        var input = CreateInput(
            riskSignals: [CreateSignal(name: "LOW_CURRENT_RATIO", severity: "High")],
            cnvEvidence: [CreateEvidence(citation: "CNV Art. 42")],
            evidenceAssessment: CreateAssessment("Critical")
        );

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        result.PossibleRegulatoryReviewAreas.Should().OnlyContain(area => area.Severity == "Warning");
    }

    [Fact]
    public async Task ReviewAsync_Should_include_standard_legal_limitations()
    {
        var input = CreateInput(riskSignals: [], cnvEvidence: []);

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        result.Limitations.Should().Contain("Este sistema no brinda asesoramiento legal.");
        result.Limitations.Should().Contain("No se proporciona ninguna conclusión legal o regulatoria definitiva.");
        result.Limitations.Should().Contain("Se requiere revisión legal humana antes de tomar cualquier determinación legal.");
    }

    [Fact]
    public async Task ReviewAsync_Should_include_financial_warnings_and_limitations()
    {
        var input = CreateInput(
            riskSignals: [],
            cnvEvidence: [],
            financialWarnings: ["Fin Warning A"],
            financialLimitations: ["Fin Lim B"]
        );

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        result.Warnings.Should().Contain("Fin Warning A");
        result.Limitations.Should().Contain("Fin Lim B");
    }

    [Fact]
    public async Task ReviewAsync_Should_not_include_forbidden_definitive_legal_language()
    {
        var signal = CreateSignal(name: "LOW_CURRENT_RATIO", severity: "High");
        var input = CreateInput(
            riskSignals: [signal],
            cnvEvidence: [CreateEvidence(citation: "CNV Art. 42")]
        );

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        var textToCheck = (result.ReviewSummary + " " +
                           string.Join(" ", result.Warnings) + " " +
                           string.Join(" ", result.Limitations) + " " +
                           string.Join(" ", result.PossibleRegulatoryReviewAreas.Select(a => a.Description))).ToLowerInvariant();

        foreach (var forbidden in LegalAnalysisAiReviewLanguageRules.ForbiddenLanguage)
        {
            textToCheck.Should().NotContain(forbidden.ToLowerInvariant());
        }
    }

    [Fact]
    public async Task ReviewAsync_Should_not_invent_citations_or_signals()
    {
        var input = CreateInput(riskSignals: [], cnvEvidence: []);

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        result.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        result.EvidenceReferences.Should().BeEmpty();
    }

    [Fact]
    public async Task ReviewAsync_Should_prefer_canonical_article_without_duplicating_original()
    {
        var original = CreateEvidence("CNV Art. 42");
        var unrelated = CreateEvidence("CNV Art. 7") with
        {
            Title = "Otra resolución",
            Snippet = "Evidencia no relacionada."
        };
        var enrichment = CreateEnrichment(
            RegulatoryEvidenceEnrichmentStatuses.Verified,
            article: CreateCanonicalArticle("CNV Art. 99"),
            limitations: ["El artículo fue truncado.", "El artículo fue truncado."]);
        var input = CreateInput(
            riskSignals: [CreateSignal("LOW_CURRENT_RATIO", "High")],
            cnvEvidence: [unrelated, original],
            evidenceAssessment: CreateAssessment("Warning"),
            evidenceEnrichments: [enrichment]);

        var result = await _service.ReviewAsync(input, CancellationToken.None);
        var reversed = await _service.ReviewAsync(
            input with
            {
                CnvEvidence = input.CnvEvidence.Reverse().ToArray(),
                EvidenceEnrichments = input.EvidenceEnrichments!.Reverse().ToArray()
            },
            CancellationToken.None);

        result.EvidenceReferences.Should().Contain(reference =>
            reference.Citation == "CNV Art. 99" &&
            reference.Snippet == "Texto canónico del artículo." &&
            reference.Score == enrichment.Score);
        result.EvidenceReferences.Should().Contain(reference =>
            reference.Citation == "CNV Art. 7");
        result.EvidenceReferences.Should().NotContain(reference =>
            reference.Citation == "CNV Art. 42");
        result.EvidenceReferences.Should().HaveCount(2);
        result.Limitations.Count(value => value == "El artículo fue truncado.")
            .Should().Be(1);
        result.PossibleRegulatoryReviewAreas.Should().ContainSingle()
            .Which.Severity.Should().Be("Warning");
        reversed.EvidenceReferences.Should().Equal(result.EvidenceReferences);
    }

    [Fact]
    public async Task ReviewAsync_Should_use_canonical_document_when_article_is_absent()
    {
        var enrichment = CreateEnrichment(
            RegulatoryEvidenceEnrichmentStatuses.Partial,
            document: CreateCanonicalDocument("CNV Art. 55"),
            limitations: ["No se recuperó el artículo canónico."]);
        var input = CreateInput(
            riskSignals: [CreateSignal("LOW_CURRENT_RATIO", "High")],
            cnvEvidence: [CreateEvidence("CNV Art. 42")],
            evidenceEnrichments: [enrichment]);

        var result = await _service.ReviewAsync(input, CancellationToken.None);

        result.EvidenceReferences.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new LegalEvidenceReference(
                Source: "CNV canónica",
                Title: "Artículo canónico",
                Url: "https://cnv.example/canonical-article",
                Citation: "CNV Art. 55",
                Snippet: "Contexto documental canónico.",
                RegulationArea: "Liquidity",
                Score: enrichment.Score));
        result.Limitations.Should().Contain("No se recuperó el artículo canónico.");
    }

    [Fact]
    public async Task ReviewAsync_Should_ignore_conflict_and_unavailable_enrichments_defensively()
    {
        var input = CreateInput(
            riskSignals: [],
            cnvEvidence: [CreateEvidence("CNV Art. 42")],
            evidenceEnrichments:
            [
                CreateEnrichment(
                    RegulatoryEvidenceEnrichmentStatuses.Conflict,
                    article: CreateCanonicalArticle("SECRETO-CONFLICTO"),
                    limitations: ["SECRETO-LIMITACION-CONFLICTO"]),
                CreateEnrichment(
                    RegulatoryEvidenceEnrichmentStatuses.Unavailable,
                    document: CreateCanonicalDocument("SECRETO-NO-DISPONIBLE"),
                    limitations: ["SECRETO-LIMITACION-NO-DISPONIBLE"])
            ]);

        var result = await _service.ReviewAsync(input, CancellationToken.None);
        var rendered = string.Join(" ", result.EvidenceReferences
            .SelectMany(reference => new[]
            {
                reference.Source,
                reference.Title,
                reference.Citation,
                reference.Snippet
            })
            .Concat(result.Limitations));

        result.EvidenceReferences.Should().ContainSingle()
            .Which.Citation.Should().Be("CNV Art. 42");
        result.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        rendered.Should().NotContain("SECRETO");
    }

    private static LegalAnalysisReviewInput CreateInput(
        IReadOnlyList<FinancialRiskSignal> riskSignals,
        IReadOnlyList<LegalEvidenceReference> cnvEvidence,
        IReadOnlyList<string>? financialWarnings = null,
        IReadOnlyList<string>? financialLimitations = null,
        RegulatoryEvidenceAssessment? evidenceAssessment = null,
        IReadOnlyList<RegulatoryEvidenceEnrichment>? evidenceEnrichments = null)
    {
        return new LegalAnalysisReviewInput(
            SessionId: "33333333-3333-3333-3333-333333333333",
            Company: "Test Co",
            DocumentId: "doc-123",
            MetricsInputSource: "session_context",
            MetricsProvenance: null,
            FinancialAiReview: null,
            FinancialRiskSignals: riskSignals,
            FinancialRiskEvidence: Array.Empty<RiskEvidenceItem>(),
            FinancialWarnings: financialWarnings ?? Array.Empty<string>(),
            FinancialLimitations: financialLimitations ?? Array.Empty<string>(),
            CnvEvidence: cnvEvidence,
            EvidenceAssessment: evidenceAssessment,
            EvidenceEnrichments: evidenceEnrichments
        );
    }

    private static RegulatoryEvidenceAssessment CreateAssessment(string severity)
    {
        return new RegulatoryEvidenceAssessment(
            EvidenceFound: true,
            Relevance: "Strong",
            Applicability: "NotEstablished",
            EvidenceQuality: "Strong",
            Severity: severity,
            RequiresHumanReview: true,
            Reasons: ["Applicability was not established."]
        );
    }

    private static FinancialRiskSignal CreateSignal(string name, string severity)
    {
        return new FinancialRiskSignal(
            Name: name,
            Severity: severity,
            Period: "2024",
            Summary: "Signal warning",
            Evidence: [
                new RiskEvidenceItem(
                    MetricName: "current_ratio",
                    Period: "2024",
                    Value: 0.65m,
                    Threshold: 1.0m,
                    Unit: "ratio",
                    Interpretation: "Low current ratio"
                )
            ]
        );
    }

    private static LegalEvidenceReference CreateEvidence(string? citation)
    {
        return new LegalEvidenceReference(
            Source: "CNV",
            Title: "General Resolution 923",
            Url: "http://cnv.gob.ar/923",
            Citation: citation,
            Snippet: "Entities must maintain compliance.",
            RegulationArea: "Liquidity",
            Score: 0.88
        );
    }

    private static RegulatoryEvidenceEnrichment CreateEnrichment(
        string status,
        RegulatoryCanonicalDocument? document = null,
        RegulatoryCanonicalArticle? article = null,
        IReadOnlyList<string>? limitations = null)
    {
        return new RegulatoryEvidenceEnrichment(
            EnrichmentId: $"enrichment-{status}",
            DocumentId: "document-1",
            ChunkId: "chunk-1",
            Rank: 1,
            Score: 0.96,
            Original: new RegulatoryOriginalEvidence(
                "Entities must maintain compliance.",
                CreateCanonicalCitation("CNV Art. 42") with
                {
                    Source = "CNV",
                    Title = "General Resolution 923",
                    Url = "http://cnv.gob.ar/923"
                }),
            Document: document,
            Article: article,
            Status: status,
            Limitations: limitations ?? []);
    }

    private static RegulatoryCanonicalDocument CreateCanonicalDocument(string citation)
    {
        return new RegulatoryCanonicalDocument(
            Id: "document-1",
            Source: "CNV canónica",
            DocumentType: "Resolución General",
            ResolutionNumber: "923/2022",
            Title: "Documento canónico",
            PublicationDate: "2022-03-01",
            EffectiveDate: "2022-04-01",
            Url: "https://cnv.example/canonical-document",
            Status: "vigente",
            RequiresReview: true,
            RetrievedAt: "2026-07-26T00:00:00Z",
            Text: "Contexto documental canónico.",
            OriginalTextLength: 29,
            IsTruncated: false,
            Metadata: new Dictionary<string, string>(),
            Citations: [CreateCanonicalCitation(citation)]);
    }

    private static RegulatoryCanonicalArticle CreateCanonicalArticle(string citation)
    {
        return new RegulatoryCanonicalArticle(
            Citation: CreateCanonicalCitation(citation),
            Text: "Texto canónico del artículo.",
            Confidence: 0.98,
            OriginalTextLength: 28,
            IsTruncated: false);
    }

    private static RegulatoryEvidenceCitation CreateCanonicalCitation(string article)
    {
        return new RegulatoryEvidenceCitation(
            Source: "CNV canónica",
            DocumentType: "Resolución General",
            ResolutionNumber: "923/2022",
            Title: "Artículo canónico",
            Chapter: "I",
            Section: "1",
            Article: article,
            PublicationDate: "2022-03-01",
            Url: "https://cnv.example/canonical-article",
            QuotedText: null);
    }
}
