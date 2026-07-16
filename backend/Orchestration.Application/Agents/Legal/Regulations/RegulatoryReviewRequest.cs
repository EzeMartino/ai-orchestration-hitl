using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Legal.Regulations;

public sealed record RegulatoryReviewRequest(
    FinancialReportContext Report,
    LegalReviewContext Context
);
