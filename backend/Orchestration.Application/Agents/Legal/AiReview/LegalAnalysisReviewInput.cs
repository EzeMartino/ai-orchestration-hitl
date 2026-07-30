using System.Collections.Generic;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Application.Agents.Legal.Regulations;

namespace Orchestration.Application.Agents.Legal.AiReview;

public sealed record LegalAnalysisReviewInput(
    string SessionId,
    string? Company,
    string? DocumentId,
    string? MetricsInputSource,
    StructuredFinancialMetricsProvenance? MetricsProvenance,
    FinancialAnalysisAiReviewResult? FinancialAiReview,
    IReadOnlyList<FinancialRiskSignal> FinancialRiskSignals,
    IReadOnlyList<RiskEvidenceItem> FinancialRiskEvidence,
    IReadOnlyList<string> FinancialWarnings,
    IReadOnlyList<string> FinancialLimitations,
    IReadOnlyList<LegalEvidenceReference> CnvEvidence,
    RegulatoryEvidenceAssessment? EvidenceAssessment = null,
    IReadOnlyList<RegulatoryEvidenceEnrichment>? EvidenceEnrichments = null
);
