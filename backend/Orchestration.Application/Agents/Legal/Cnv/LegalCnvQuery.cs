using System.Collections.Generic;

namespace Orchestration.Application.Agents.Legal.Cnv;

public sealed record LegalCnvQuery(
    string Query,
    string? RegulationArea,
    string Reason,
    IReadOnlyList<string> RelatedFinancialSignals
);
