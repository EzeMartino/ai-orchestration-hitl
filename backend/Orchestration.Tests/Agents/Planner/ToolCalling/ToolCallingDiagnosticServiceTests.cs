using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class ToolCallingDiagnosticServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ExecuteAsync_Should_execute_approved_calls_when_dynamic_execution_is_enabled()
    {
        var executor = new FakeControlledToolExecutor();
        var service = CreateService(executor);

        var result = await service.ExecuteAsync(
            new ToolCallingDiagnosticRequest
            {
                ProposedCalls =
                [
                    CreateProposedCall("data.analyze_transactions"),
                    CreateProposedCall("legal.search_cnv_regulation")
                ]
            },
            CancellationToken.None
        );

        result.ProposedCalls.Should().HaveCount(2);
        result.ApprovedCalls.Should().HaveCount(2);
        result.RejectedCalls.Should().BeEmpty();
        result.ExecutedCalls.Should().HaveCount(2);
        result.ExecutedCalls.Should().OnlyContain(call => call.Status == ToolExecutionStatus.Executed);
        JsonSerializer.Serialize(result, JsonOptions).Should().Contain("\"status\":\"Executed\"");
        executor.WasCalled.Should().BeTrue();
        executor.ReceivedCalls.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_Should_reject_unknown_or_unsafe_tools_without_executing()
    {
        var executor = new FakeControlledToolExecutor();
        var service = CreateService(executor);

        var result = await service.ExecuteAsync(
            new ToolCallingDiagnosticRequest
            {
                ProposedCalls =
                [
                    CreateProposedCall("workflow.complete")
                ]
            },
            CancellationToken.None
        );

        result.ApprovedCalls.Should().BeEmpty();
        result.RejectedCalls.Should().ContainSingle().Which.Reason.Should().Be("No se permiten herramientas de transición de workflow.");
        result.ExecutedCalls.Should().BeEmpty();
        executor.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Should_skip_approved_calls_when_dynamic_execution_is_disabled()
    {
        var executor = new FakeControlledToolExecutor();
        var service = CreateService(executor);

        var result = await service.ExecuteAsync(
            new ToolCallingDiagnosticRequest
            {
                DynamicExecutionEnabled = false,
                ProposedCalls =
                [
                    CreateProposedCall("legal.search_cnv_regulation")
                ]
            },
            CancellationToken.None
        );

        result.ApprovedCalls.Should().ContainSingle();
        result.RejectedCalls.Should().BeEmpty();
        result.ExecutedCalls.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ToolExecutionResult(
                ToolName: "legal.search_cnv_regulation",
                Status: ToolExecutionStatus.SkippedDisabled,
                Succeeded: true,
                Summary: "Dynamic tool execution is disabled.",
                Engine: "Tool Execution Policy",
                OutputJson: "{}",
                Error: null
            )
        );
        executor.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Should_copy_normalized_proposal_provenance_to_audit()
    {
        var executor = new FakeControlledToolExecutor();
        var service = CreateService(
            executor,
            new ProvenanceToolPlanNormalizer()
        );

        var result = await service.ExecuteAsync(
            new ToolCallingDiagnosticRequest
            {
                ProposedCalls = [CreateProposedCall("legal.search_cnv_regulation")]
            },
            CancellationToken.None
        );

        result.ProposalSource.Should().Be(ToolPlanProposalSource.DeterministicFallback);
        result.ProposalFallbackReason.Should().Be(ToolPlanProposalFallbackReason.LlmResponseInvalid);
    }

    private static ToolCallingDiagnosticService CreateService(
        FakeControlledToolExecutor executor,
        IToolPlanNormalizer? normalizer = null)
    {
        return new ToolCallingDiagnosticService(
            normalizer ?? new ToolPlanNormalizer(),
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            executor
        );
    }

    private sealed class ProvenanceToolPlanNormalizer : IToolPlanNormalizer
    {
        public ToolPlan Normalize(
            ToolPlan plan)
        {
            return plan with
            {
                ProposalSource = ToolPlanProposalSource.DeterministicFallback,
                ProposalFallbackReason = ToolPlanProposalFallbackReason.LlmResponseInvalid
            };
        }
    }

    private static ProposedToolCall CreateProposedCall(
        string toolName)
    {
        var arguments = string.Equals(
            toolName,
            PlannerToolCatalog.AnalyzeTransactionsName,
            StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, string>
            {
                ["sessionId"] = Guid.Empty.ToString(),
                ["reportName"] = "diagnostic-report",
                ["totalAmount"] = "125000",
                ["transactionCount"] = "42",
                ["submittedAt"] = DateTimeOffset.UnixEpoch.ToString("O")
            }
            : new Dictionary<string, string>();

        return new ProposedToolCall(
            ToolName: toolName,
            Arguments: arguments,
            Reason: "Diagnostic tool call."
        );
    }

    private sealed class FakeControlledToolExecutor : IControlledToolExecutor
    {
        public bool WasCalled { get; private set; }

        public IReadOnlyList<ApprovedToolCall> ReceivedCalls { get; private set; } = [];

        public Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
            IReadOnlyList<ApprovedToolCall> calls,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            ReceivedCalls = calls;

            return Task.FromResult<IReadOnlyList<ToolExecutionResult>>(
                calls.Select(call => new ToolExecutionResult(
                    ToolName: call.ToolName,
                    Status: ToolExecutionStatus.Executed,
                    Succeeded: true,
                    Summary: $"Executed {call.ToolName}.",
                    Engine: "Fake Controlled Tool Executor",
                    OutputJson: "{}",
                    Error: null
                )).ToList()
            );
        }
    }
}
