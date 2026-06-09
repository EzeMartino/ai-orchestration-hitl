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

    private static LegalAnalysisReviewInput CreateInput(
        IReadOnlyList<FinancialRiskSignal> riskSignals,
        IReadOnlyList<LegalEvidenceReference> cnvEvidence,
        IReadOnlyList<string>? financialWarnings = null,
        IReadOnlyList<string>? financialLimitations = null)
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
            CnvEvidence: cnvEvidence
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
}
