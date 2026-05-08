using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Legal.Regulations;

public interface IRegulatoryKnowledgeSource
{
    Task<RegulatoryReviewResult> ReviewAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken
    );
}