using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Application.AnalysisSessions;

public interface IAnalysisSessionStartPreflightValidator
{
    Task<AnalysisSessionStartPreflightResult> ValidateAsync(
        AnalysisSession session,
        CancellationToken cancellationToken);
}
