using FluentAssertions;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Tests.AnalysisSessions;

public class AnalysisSessionStateMachineTests
{
    [Fact]
    public void Should_allow_start_from_pending()
    {
        var stateMachine = new AnalysisSessionStateMachine();
        var machine = stateMachine.Create(AnalysisSessionStatus.Pending);

        machine.CanFire(AnalysisSessionTrigger.Start).Should().BeTrue();

        machine.Fire(AnalysisSessionTrigger.Start);

        machine.State.Should().Be(AnalysisSessionStatus.DataGathering);
    }

    [Fact]
    public void Should_allow_anomaly_detected_from_data_gathering()
    {
        var stateMachine = new AnalysisSessionStateMachine();
        var machine = stateMachine.Create(AnalysisSessionStatus.DataGathering);

        machine.CanFire(AnalysisSessionTrigger.AnomalyDetected).Should().BeTrue();

        machine.Fire(AnalysisSessionTrigger.AnomalyDetected);

        machine.State.Should().Be(AnalysisSessionStatus.AwaitingHumanApproval);
    }

    [Fact]
    public void Should_allow_human_approval_from_awaiting_human_approval()
    {
        var stateMachine = new AnalysisSessionStateMachine();
        var machine = stateMachine.Create(AnalysisSessionStatus.AwaitingHumanApproval);

        machine.CanFire(AnalysisSessionTrigger.HumanApproved).Should().BeTrue();

        machine.Fire(AnalysisSessionTrigger.HumanApproved);

        machine.State.Should().Be(AnalysisSessionStatus.Completed);
    }

    [Fact]
    public void Should_not_allow_human_approval_from_pending()
    {
        var stateMachine = new AnalysisSessionStateMachine();
        var machine = stateMachine.Create(AnalysisSessionStatus.Pending);

        machine.CanFire(AnalysisSessionTrigger.HumanApproved).Should().BeFalse();
    }
}