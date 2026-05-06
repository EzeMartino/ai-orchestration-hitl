namespace Orchestration.Domain.AnalysisSessions;
public enum AnalysisSessionStatus
{
    Pending,
    DataGathering,
    AwaitingHumanApproval,
    Completed,
    Failed
}
