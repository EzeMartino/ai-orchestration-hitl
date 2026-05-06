using Stateless;
using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Application.AnalysisSessions;

public class AnalysisSessionStateMachine
{
    public StateMachine<AnalysisSessionStatus, AnalysisSessionTrigger> Create(AnalysisSessionStatus currentState)
    {
        var machine = new StateMachine<AnalysisSessionStatus, AnalysisSessionTrigger>(currentState);

        machine.Configure(AnalysisSessionStatus.Pending)
            .Permit(AnalysisSessionTrigger.Start, AnalysisSessionStatus.DataGathering);

        machine.Configure(AnalysisSessionStatus.DataGathering)
            .Permit(AnalysisSessionTrigger.DataCollected, AnalysisSessionStatus.Completed)
            .Permit(AnalysisSessionTrigger.AnomalyDetected, AnalysisSessionStatus.AwaitingHumanApproval)
            .Permit(AnalysisSessionTrigger.Fail, AnalysisSessionStatus.Failed);

        machine.Configure(AnalysisSessionStatus.AwaitingHumanApproval)
            .Permit(AnalysisSessionTrigger.HumanApproved, AnalysisSessionStatus.Completed)
            .Permit(AnalysisSessionTrigger.HumanRejected, AnalysisSessionStatus.Failed);

        return machine;
    }
}