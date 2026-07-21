using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Legal;

public interface ILegalAgent
{
    Task<LegalAgentResult> ReviewAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken
    );

    Task<LegalAgentResult> ReviewAsync(
        FinancialReportContext report,
        LegalReviewContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context is not
            {
                ResolutionMode: FinancialAnalysisResolutionMode.ProvidedOrPersisted,
                DataEvidence: null
            })
        {
            throw new InvalidOperationException(
                "Legacy legal agent implementations support only the default review context.");
        }

        return ReviewAsync(report, cancellationToken);
    }
}
