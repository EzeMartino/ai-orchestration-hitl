using System.Threading;
using System.Threading.Tasks;

namespace Orchestration.Application.Agents.Legal.AiReview;

public interface ILegalAnalysisReviewService
{
    Task<LegalAnalysisReviewResult> ReviewAsync(
        LegalAnalysisReviewInput input,
        CancellationToken cancellationToken);
}
