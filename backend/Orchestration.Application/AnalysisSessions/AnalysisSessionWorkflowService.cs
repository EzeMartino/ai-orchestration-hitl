using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Application.AnalysisSessions;

public class AnalysisSessionWorkflowService
{
    private readonly AnalysisSessionStateMachine _stateMachine;

    public AnalysisSessionWorkflowService(AnalysisSessionStateMachine stateMachine)
    {
        _stateMachine = stateMachine;
    }

    public AnalysisSessionStatus ApplyTrigger(
        AnalysisSession session,
        AnalysisSessionTrigger trigger)
    {
        var machine = _stateMachine.Create(session.Status);

        if (!machine.CanFire(trigger))
        {
            throw new InvalidOperationException(
                $"Invalid transition: {session.Status} -> {trigger}"
            );
        }

        machine.Fire(trigger);

        return machine.State;
    }
}