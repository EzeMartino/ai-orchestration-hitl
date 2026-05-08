using FluentAssertions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Shared;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Tests.Agents;

namespace Orchestration.Tests.Agents.Planner;

public class PlannerAgentTests
{
    [Fact]
    public async Task RunAsync_Should_require_human_approval_when_data_agent_detects_anomaly()
    {
        var dataAgent = new FakeDataAgent(
            new DataAgentResult(
                HasAnomaly: true,
                Severity: "High",
                Summary: "Anomaly detected.",
                Engine: "TestEngine",
                Evidence:
                [
                    new AnomalyEvidence(
                        Metric: "TransactionAmountZScore",
                        Value: 4.5,
                        Threshold: 3.0,
                        Interpretation: "Above threshold."
                    )
                ]
            )
        );

        var legalAgent = new FakeLegalAgent(
            new LegalAgentResult(
                HasComplianceRisk: false,
                RiskLevel: "Low",
                Summary: "No compliance risk.",
                Engine: "TestEngine",
                Evidence: []
            )
        );

        var publisher = new FakeActivityEventPublisher();

        var plannerAgent = new PlannerAgent(
            dataAgent,
            legalAgent,
            publisher
        );

        var session = AnalysisSession.Create();

        var result = await plannerAgent.RunAsync(
            session,
            CancellationToken.None
        );

        result.RequiresHumanApproval.Should().BeTrue();
        result.DataResult.HasAnomaly.Should().BeTrue();
        result.LegalResult.HasComplianceRisk.Should().BeFalse();

        dataAgent.WasCalled.Should().BeTrue();
        legalAgent.WasCalled.Should().BeTrue();

        publisher.PublishedEvents
            .Should()
            .Contain(x => x.Type == "tool_executed" && x.Agent == "DataAgent");

        publisher.PublishedEvents
            .Should()
            .Contain(x => x.Agent == "LegalAgent");
    }

    [Fact]
    public async Task RunAsync_Should_require_human_approval_when_legal_agent_detects_compliance_risk()
    {
        var dataAgent = new FakeDataAgent(
            new DataAgentResult(
                HasAnomaly: false,
                Severity: "Low",
                Summary: "No anomaly detected.",
                Engine: "TestEngine",
                Evidence: []
            )
        );

        var legalAgent = new FakeLegalAgent(
            new LegalAgentResult(
                HasComplianceRisk: true,
                RiskLevel: "Medium",
                Summary: "Compliance review required.",
                Engine: "TestEngine",
                Evidence:
                [
                    new LegalEvidence(
                        Regulation: "Internal AML Policy",
                        Section: "Transaction Monitoring",
                        Finding: "Human review required.",
                        Source: "Test source"
                    )
                ]
            )
        );

        var publisher = new FakeActivityEventPublisher();

        var plannerAgent = new PlannerAgent(
            dataAgent,
            legalAgent,
            publisher
        );

        var session = AnalysisSession.Create();

        var result = await plannerAgent.RunAsync(
            session,
            CancellationToken.None
        );

        result.RequiresHumanApproval.Should().BeTrue();
        result.DataResult.HasAnomaly.Should().BeFalse();
        result.LegalResult.HasComplianceRisk.Should().BeTrue();
    }

    private sealed class FakeDataAgent : IDataAgent
    {
        private readonly DataAgentResult _result;

        public bool WasCalled { get; private set; }

        public FakeDataAgent(DataAgentResult result)
        {
            _result = result;
        }

        public Task<DataAgentResult> AnalyzeAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            WasCalled = true;

            return Task.FromResult(_result);
        }
    }

    private sealed class FakeLegalAgent : ILegalAgent
    {
        private readonly LegalAgentResult _result;

        public bool WasCalled { get; private set; }

        public FakeLegalAgent(LegalAgentResult result)
        {
            _result = result;
        }

        public Task<LegalAgentResult> ReviewAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            WasCalled = true;

            return Task.FromResult(_result);
        }
    }
}