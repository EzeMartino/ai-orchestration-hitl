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
        result.RejectedCalls.Should().ContainSingle().Which.Reason.Should().Be("Workflow transition tools are not allowed.");
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

    private static ToolCallingDiagnosticService CreateService(
        FakeControlledToolExecutor executor)
    {
        return new ToolCallingDiagnosticService(
            new ToolPlanNormalizer(),
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            executor
        );
    }

    private static ProposedToolCall CreateProposedCall(
        string toolName)
    {
        return new ProposedToolCall(
            ToolName: toolName,
            Arguments: new Dictionary<string, string>
            {
                ["query"] = "agentes",
                ["sessionId"] = Guid.Empty.ToString()
            },
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
