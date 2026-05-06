
namespace Orchestration.Domain.AnalysisSessions;
public enum AnalysisSessionTrigger
{
    Start,
    DataCollected,
    AnomalyDetected,
    HumanApproved,
    HumanRejected,
    Complete,
    Fail
}
