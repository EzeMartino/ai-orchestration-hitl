using System.Collections.Generic;

namespace Orchestration.Application.Agents.Legal.Cnv;

public sealed record LegalQueryStrategyAudit(
    string Source, // "financial_analysis" or "fallback"
    IReadOnlyList<LegalCnvQueryAudit> Queries
);

public sealed record LegalCnvQueryAudit(
    string Query,
    string? RegulationArea,
    string Reason,
    IReadOnlyList<string> RelatedFinancialSignals
);
