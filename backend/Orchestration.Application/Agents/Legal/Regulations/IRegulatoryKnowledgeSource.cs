using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Legal.Regulations;

public interface IRegulatoryKnowledgeSource
{
    Task<RegulatoryReviewResult> ReviewAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken
    );

    Task<RegulatoryReviewResult> ReviewAsync(
        RegulatoryReviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Report);
        ArgumentNullException.ThrowIfNull(request.Context);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Context is not
            {
                ResolutionMode: FinancialAnalysisResolutionMode.ProvidedOrPersisted,
                DataEvidence: null
            })
        {
            throw new InvalidOperationException(
                "Legacy regulatory knowledge sources support only the default review context.");
        }

        return ReviewAsync(request.Report, cancellationToken);
    }
}
