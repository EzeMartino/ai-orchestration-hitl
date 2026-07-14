# Fail-Closed Financial Analysis Execution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent Python financial-analysis failures from appearing as successful Low-risk assessments while preserving valid partial evidence and forcing human review.

**Architecture:** Add typed stage and aggregate execution metadata beside the existing business-risk fields. The CSnakes adapter validates and records each Python operation, the DataAgent workflow aggregates stage outcomes and sets an explicit review flag, Planner gates on that flag, and the orchestrator persists the result for the frontend to render safely.

**Tech Stack:** .NET 10, C# records and `System.Text.Json`, ASP.NET Core dependency injection and structured logging, CSnakes/Python, xUnit, FluentAssertions, React 19, TypeScript 6, Node test runner, Vite.

**Issue:** [#4 — Prevent financial-analysis execution failures from being classified as Low risk](https://github.com/EzeMartino/ai-orchestration-hitl/issues/4)

**Spec:** `docs/superpowers/specs/2026-07-13-financial-analysis-fail-closed-design.md`

---

## File Structure

### New backend files

- `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/FinancialAnalysisExecutionStatus.cs` — status enum and safe lowercase JSON converter.
- `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/FinancialAnalysisOperations.cs` — stable operation and failure-code constants.
- `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/FinancialAnalysisStageExecution.cs` — one Python-stage outcome.
- `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/FinancialAnalysisExecution.cs` — aggregate status, legacy default, and deterministic aggregation.
- `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/IFinancialAnalysisPythonInvoker.cs` — internal test seam around generated CSnakes calls.
- `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/CSnakesFinancialAnalysisPythonInvoker.cs` — production invoker backed by `IPythonEnvironment`.
- `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/FinancialAnalysisServiceCollectionExtensions.cs` — keeps internal invoker registration inside Infrastructure.
- `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/CSnakesFinancialAnalysisServiceFailureTests.cs` — deterministic invocation, schema, logging, and cancellation failures.
- `backend/Orchestration.Tests/TestCapturingLogger.cs` — reusable structured-log capture helper.

### Modified backend files

- Four financial request records — add a JSON-ignored optional session identifier for log correlation.
- Four financial response records — add non-positional stage execution metadata without breaking constructors.
- `FinancialAnalysisContext.cs` — add aggregate execution metadata with a legacy-safe default.
- `DataAgentResult.cs` — add optional `RequiresHumanReview` independently from `HasAnomaly`.
- `CSnakesFinancialAnalysisService.cs` — invoke, validate, time, log, and map failures safely.
- `DataAgentFinancialAnalysisWorkflow.cs` — propagate session ID, continue useful stages, aggregate outcomes, gate AI review, publish safe activity, and preserve partial evidence.
- `ConfigurableDataAgent.cs` — keep unexpected structured-workflow failures review-required even after legacy fallback.
- `PlannerAgent.cs` — include explicit DataAgent review requirement in HITL gating.
- `ToolExecutionResultMapper.cs` — preserve the flag and fail closed for mapped financial contexts with non-success execution.
- `AnalysisOrchestratorService.cs` — persist execution metadata and completed/inconclusive assessment state.
- `Program.cs` and `PythonAgentTestFixture.cs` — use the Infrastructure registration extension.
- Existing contract, adapter, workflow, Planner, mapper, context, and production-like tests — cover compatibility and end-to-end behavior.

### Frontend and docs

- `frontend/src/utils/financialAnalysisExecution.ts` — pure status normalization, safe labels, code allowlist, and banner view model.
- `frontend/tests/financialAnalysisExecution.test.ts` — dependency-free presenter tests.
- `frontend/src/types/domain.types.ts` — optional execution context types.
- `frontend/src/components/FinancialRiskEvidencePanel.tsx` — compact status banner before evidence.
- `frontend/src/App.css` — accessible status variants.
- `README.md` and `docs/demo-script.md` — execution semantics, HITL behavior, and demo verification.

---

### Task 1: Add Compatible Execution Contracts

**Files:**
- Create: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/FinancialAnalysisExecutionStatus.cs`
- Create: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/FinancialAnalysisOperations.cs`
- Create: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/FinancialAnalysisStageExecution.cs`
- Create: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/FinancialAnalysisExecution.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/ComputeFinancialRatiosRequest.cs:1-6`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/ComparePeriodsRequest.cs:1-8`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/DetectFinancialRiskSignalsRequest.cs:1-12`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/SummarizeQuantitativeEvidenceRequest.cs:1-9`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/ComputeFinancialRatiosResponse.cs:1-7`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/ComparePeriodsResponse.cs:1-7`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/DetectFinancialRiskSignalsResponse.cs:1-7`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/SummarizeQuantitativeEvidenceResponse.cs:1-7`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/FinancialAnalysisContext.cs:7-25`
- Modify: `backend/Orchestration.Application/Agents/Data/DataAgentResult.cs:5-12`
- Test: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/FinancialAnalysisContractsSerializationTests.cs`

- [ ] **Step 1: Write failing compatibility and aggregation tests**

Add tests proving lowercase serialization, unknown-value fallback, old JSON defaults, operation-specific defaults, aggregate rules, ignored `SessionId`, and the trailing review flag:

```csharp
[Fact]
public void Execution_status_Should_round_trip_as_lowercase_string()
{
    var execution = new FinancialAnalysisStageExecution(
        FinancialAnalysisOperations.Signals,
        FinancialAnalysisExecutionStatus.Failed,
        42,
        FinancialAnalysisFailureCodes.PythonInvocationFailed);

    var json = JsonSerializer.Serialize(execution, JsonOptions);
    var roundTrip = JsonSerializer.Deserialize<FinancialAnalysisStageExecution>(json, JsonOptions);

    json.Should().Contain("\"status\":\"failed\"");
    roundTrip.Should().Be(execution);
}

[Fact]
public void Unknown_execution_status_Should_fail_closed_as_legacy_unknown()
{
    const string json = """
    {"operation":"signals","status":"future_status","durationMilliseconds":0}
    """;

    JsonSerializer.Deserialize<FinancialAnalysisStageExecution>(json, JsonOptions)!
        .Status.Should().Be(FinancialAnalysisExecutionStatus.LegacyUnknown);
}

[Fact]
public void Old_signal_response_Should_default_to_operation_specific_legacy_execution()
{
    const string json = """
    {
      "engine":"old-engine",
      "signals":[],
      "result":{"hasRiskSignals":false,"riskLevel":"Low","summary":"none","engine":"old-engine","evidence":[],"warnings":[]}
    }
    """;

    var response = JsonSerializer.Deserialize<DetectFinancialRiskSignalsResponse>(json, JsonOptions)!;

    response.Execution.Operation.Should().Be(FinancialAnalysisOperations.Signals);
    response.Execution.Status.Should().Be(FinancialAnalysisExecutionStatus.LegacyUnknown);
}

[Theory]
[InlineData(FinancialAnalysisOperations.Ratios, FinancialAnalysisExecutionStatus.Failed, FinancialAnalysisExecutionStatus.Degraded)]
[InlineData(FinancialAnalysisOperations.Comparisons, FinancialAnalysisExecutionStatus.Failed, FinancialAnalysisExecutionStatus.Degraded)]
[InlineData(FinancialAnalysisOperations.Signals, FinancialAnalysisExecutionStatus.Failed, FinancialAnalysisExecutionStatus.Failed)]
[InlineData(FinancialAnalysisOperations.Summary, FinancialAnalysisExecutionStatus.Failed, FinancialAnalysisExecutionStatus.Degraded)]
public void Aggregate_Should_apply_fail_closed_stage_rules(
    string failedOperation,
    FinancialAnalysisExecutionStatus failedStatus,
    FinancialAnalysisExecutionStatus expected)
{
    var stages = FinancialAnalysisOperations.All
        .Select(operation => new FinancialAnalysisStageExecution(
            operation,
            operation == failedOperation ? failedStatus : FinancialAnalysisExecutionStatus.Succeeded,
            1))
        .ToArray();

    FinancialAnalysisExecution.FromStages(stages).OverallStatus.Should().Be(expected);
}

[Fact]
public void Request_session_id_Should_not_be_sent_to_python_json()
{
    var request = new ComputeFinancialRatiosRequest([], [], Guid.NewGuid());

    JsonSerializer.Serialize(request, JsonOptions).Should().NotContain("sessionId");
}
```

- [ ] **Step 2: Run the contract tests and confirm RED**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~FinancialAnalysisContractsSerializationTests" --verbosity minimal
```

Expected: FAIL to compile because execution types, constants, response properties, request correlation, and review flag do not exist.

- [ ] **Step 3: Implement the execution model and safe JSON status converter**

Use this exact status shape and converter behavior:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

[JsonConverter(typeof(FinancialAnalysisExecutionStatusJsonConverter))]
public enum FinancialAnalysisExecutionStatus
{
    LegacyUnknown = 0,
    Succeeded,
    Degraded,
    Failed
}

public sealed class FinancialAnalysisExecutionStatusJsonConverter
    : JsonConverter<FinancialAnalysisExecutionStatus>
{
    public override FinancialAnalysisExecutionStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            using var ignored = JsonDocument.ParseValue(ref reader);
            return FinancialAnalysisExecutionStatus.LegacyUnknown;
        }

        return reader.GetString()?.Trim().ToLowerInvariant() switch
        {
            "succeeded" => FinancialAnalysisExecutionStatus.Succeeded,
            "degraded" => FinancialAnalysisExecutionStatus.Degraded,
            "failed" => FinancialAnalysisExecutionStatus.Failed,
            _ => FinancialAnalysisExecutionStatus.LegacyUnknown
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        FinancialAnalysisExecutionStatus value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            FinancialAnalysisExecutionStatus.Succeeded => "succeeded",
            FinancialAnalysisExecutionStatus.Degraded => "degraded",
            FinancialAnalysisExecutionStatus.Failed => "failed",
            _ => "legacy_unknown"
        });
    }
}
```

Add stable constants and aggregate logic:

```csharp
namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public static class FinancialAnalysisOperations
{
    public const string Ratios = "ratios";
    public const string Comparisons = "comparisons";
    public const string Signals = "signals";
    public const string Summary = "summary";

    public static IReadOnlyList<string> All { get; } =
        [Ratios, Comparisons, Signals, Summary];
}

public static class FinancialAnalysisFailureCodes
{
    public const string PythonInvocationFailed = "PYTHON_INVOCATION_FAILED";
    public const string PythonResponseInvalid = "PYTHON_RESPONSE_INVALID";
    public const string UnexpectedFailure = "FINANCIAL_ANALYSIS_UNEXPECTED_FAILURE";
}

public sealed record FinancialAnalysisStageExecution(
    string Operation,
    FinancialAnalysisExecutionStatus Status,
    long DurationMilliseconds,
    string? FailureCode = null)
{
    public static FinancialAnalysisStageExecution LegacyUnknown(string operation) =>
        new(operation, FinancialAnalysisExecutionStatus.LegacyUnknown, 0);
}

public sealed record FinancialAnalysisExecution(
    FinancialAnalysisExecutionStatus OverallStatus,
    IReadOnlyList<FinancialAnalysisStageExecution> Stages)
{
    public static FinancialAnalysisExecution LegacyUnknown { get; } =
        new(FinancialAnalysisExecutionStatus.LegacyUnknown, []);

    public static FinancialAnalysisExecution FromStages(
        IEnumerable<FinancialAnalysisStageExecution> stages)
    {
        var supplied = stages
            .GroupBy(stage => stage.Operation, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var normalized = FinancialAnalysisOperations.All
            .Select(operation => supplied.TryGetValue(operation, out var stage)
                ? stage
                : FinancialAnalysisStageExecution.LegacyUnknown(operation))
            .ToArray();
        var signals = normalized.Single(stage => stage.Operation == FinancialAnalysisOperations.Signals);
        var overall = signals.Status == FinancialAnalysisExecutionStatus.Failed
            ? FinancialAnalysisExecutionStatus.Failed
            : normalized.All(stage => stage.Status == FinancialAnalysisExecutionStatus.Succeeded)
                ? FinancialAnalysisExecutionStatus.Succeeded
                : FinancialAnalysisExecutionStatus.Degraded;

        return new FinancialAnalysisExecution(overall, normalized);
    }
}
```

- [ ] **Step 4: Add non-breaking request, response, context, and DataAgent properties**

Add `[property: JsonIgnore] Guid? SessionId = null` as the final positional parameter on all four request records. Add these exact non-positional properties to the responses:

```csharp
public FinancialAnalysisStageExecution Execution { get; init; } =
    FinancialAnalysisStageExecution.LegacyUnknown(FinancialAnalysisOperations.Ratios);

public FinancialAnalysisStageExecution Execution { get; init; } =
    FinancialAnalysisStageExecution.LegacyUnknown(FinancialAnalysisOperations.Comparisons);

public FinancialAnalysisStageExecution Execution { get; init; } =
    FinancialAnalysisStageExecution.LegacyUnknown(FinancialAnalysisOperations.Signals);

public FinancialAnalysisStageExecution Execution { get; init; } =
    FinancialAnalysisStageExecution.LegacyUnknown(FinancialAnalysisOperations.Summary);
```

Add this property inside `FinancialAnalysisContext`:

```csharp
public FinancialAnalysisExecution Execution { get; init; } =
    FinancialAnalysisExecution.LegacyUnknown;
```

Add the trailing DataAgent parameter:

```csharp
public sealed record DataAgentResult(
    bool HasAnomaly,
    string Severity,
    string Summary,
    string Engine,
    IReadOnlyList<AnomalyEvidence> Evidence,
    FinancialAnalysisContext? FinancialAnalysis = null,
    bool RequiresHumanReview = false);
```

- [ ] **Step 5: Run contract tests and confirm GREEN**

Run the Step 2 command.

Expected: PASS; existing positional constructor coverage continues compiling.

- [ ] **Step 6: Commit the contract slice**

```powershell
git add backend/Orchestration.Application backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/FinancialAnalysisContractsSerializationTests.cs
git commit -m "Add financial analysis execution contracts"
```

---

### Task 2: Make CSnakes Invocation Failures Observable and Testable

**Files:**
- Create: `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/IFinancialAnalysisPythonInvoker.cs`
- Create: `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/CSnakesFinancialAnalysisPythonInvoker.cs`
- Create: `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/FinancialAnalysisServiceCollectionExtensions.cs`
- Create: `backend/Orchestration.Tests/TestCapturingLogger.cs`
- Create: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/CSnakesFinancialAnalysisServiceFailureTests.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/CSnakesFinancialAnalysisService.cs:7-313`
- Modify: `backend/Orchestration.Api/Program.cs:82`
- Modify: `backend/Orchestration.Tests/Agents/Data/PythonAgentTestFixture.cs:49`
- Modify: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/CSnakesFinancialAnalysisServiceTests.cs`

- [ ] **Step 1: Write one failing invocation test per operation plus logging and cancellation tests**

Create a fake invoker whose selected operation throws `InvalidOperationException("sensitive adapter detail")`. Assert each response has `Failed`, the stable invocation code, non-negative duration, safe warnings, and no leaked exception text. For signals and summary also assert `RiskLevel == "Unknown"`.

```csharp
[Theory]
[InlineData(FinancialAnalysisOperations.Ratios)]
[InlineData(FinancialAnalysisOperations.Comparisons)]
[InlineData(FinancialAnalysisOperations.Signals)]
[InlineData(FinancialAnalysisOperations.Summary)]
public async Task Operation_Should_mark_execution_failed_when_python_invocation_throws(
    string operation)
{
    var invoker = new FakeFinancialAnalysisPythonInvoker { ThrowOperation = operation };
    var logger = new TestCapturingLogger<CSnakesFinancialAnalysisService>();
    var service = new CSnakesFinancialAnalysisService(invoker, logger);
    var sessionId = Guid.NewGuid();

    var response = await InvokeAsync(service, operation, sessionId);

    response.Execution.Status.Should().Be(FinancialAnalysisExecutionStatus.Failed);
    response.Execution.FailureCode.Should().Be(FinancialAnalysisFailureCodes.PythonInvocationFailed);
    response.Execution.DurationMilliseconds.Should().BeGreaterThanOrEqualTo(0);
    response.Warnings.Should().NotContain(message => message.Contains("sensitive adapter detail"));
    if (operation is FinancialAnalysisOperations.Signals or FinancialAnalysisOperations.Summary)
    {
        response.RiskLevel.Should().Be("Unknown");
    }
    logger.Entries.Should().ContainSingle(entry =>
        entry.Level == LogLevel.Error &&
        entry.Properties["Operation"]?.ToString() == operation &&
        entry.Properties["SessionId"]?.ToString() == sessionId.ToString() &&
        entry.Properties["FailureCode"]?.ToString() == FinancialAnalysisFailureCodes.PythonInvocationFailed);
}

[Fact]
public async Task ComputeFinancialRatiosAsync_Should_propagate_pre_cancellation()
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    var service = new CSnakesFinancialAnalysisService(
        new FakeFinancialAnalysisPythonInvoker(),
        new TestCapturingLogger<CSnakesFinancialAnalysisService>());

    var action = () => service.ComputeFinancialRatiosAsync(
        new ComputeFinancialRatiosRequest([], []),
        cts.Token);

    await action.Should().ThrowAsync<OperationCanceledException>();
}

private sealed record OperationInvocationResult(
    FinancialAnalysisStageExecution Execution,
    IReadOnlyList<string> Warnings,
    string? RiskLevel = null);

private static async Task<OperationInvocationResult> InvokeAsync(
    CSnakesFinancialAnalysisService service,
    string operation,
    Guid sessionId)
{
    return operation switch
    {
        FinancialAnalysisOperations.Ratios => await Ratios(),
        FinancialAnalysisOperations.Comparisons => await Comparisons(),
        FinancialAnalysisOperations.Signals => await Signals(),
        FinancialAnalysisOperations.Summary => await Summary(),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    async Task<OperationInvocationResult> Ratios()
    {
        var response = await service.ComputeFinancialRatiosAsync(
            new ComputeFinancialRatiosRequest([], [], sessionId),
            CancellationToken.None);
        return new(response.Execution, response.Warnings);
    }

    async Task<OperationInvocationResult> Comparisons()
    {
        var response = await service.ComparePeriodsAsync(
            new ComparePeriodsRequest([], "2024A", "2025E", [], sessionId),
            CancellationToken.None);
        return new(response.Execution, response.Warnings);
    }

    async Task<OperationInvocationResult> Signals()
    {
        var response = await service.DetectFinancialRiskSignalsAsync(
            new DetectFinancialRiskSignalsRequest([], [], [], SessionId: sessionId),
            CancellationToken.None);
        return new(response.Execution, response.Result.Warnings, response.Result.RiskLevel);
    }

    async Task<OperationInvocationResult> Summary()
    {
        var response = await service.SummarizeQuantitativeEvidenceAsync(
            new SummarizeQuantitativeEvidenceRequest([], [], [], [], SessionId: sessionId),
            CancellationToken.None);
        return new(response.Execution, response.Result.Warnings, response.Result.RiskLevel);
    }
}

private sealed class FakeFinancialAnalysisPythonInvoker : IFinancialAnalysisPythonInvoker
{
    public string? ThrowOperation { get; init; }
    public string? OverrideOperation { get; init; }
    public string? OverrideResponseJson { get; init; }

    public string ComputeFinancialRatios(string requestJson) =>
        Invoke(FinancialAnalysisOperations.Ratios, "{\"ratios\":[],\"warnings\":[]}");

    public string ComparePeriods(string requestJson) =>
        Invoke(FinancialAnalysisOperations.Comparisons, "{\"comparisons\":[],\"warnings\":[]}");

    public string DetectFinancialRiskSignals(string requestJson) =>
        Invoke(FinancialAnalysisOperations.Signals, "{\"signals\":[],\"warnings\":[]}");

    public string SummarizeQuantitativeEvidence(string requestJson) =>
        Invoke(FinancialAnalysisOperations.Summary, "{\"summary\":\"Sin evidencia.\",\"evidence\":[],\"warnings\":[]}");

    private string Invoke(string operation, string defaultJson)
    {
        if (operation == ThrowOperation)
        {
            throw new InvalidOperationException("sensitive adapter detail");
        }

        return operation == OverrideOperation
            ? OverrideResponseJson ?? throw new InvalidOperationException("Override JSON is required.")
            : defaultJson;
    }
}
```

- [ ] **Step 2: Run failure tests and confirm RED**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~CSnakesFinancialAnalysisServiceFailureTests" --verbosity minimal
```

Expected: FAIL because the invoker seam, logger constructor, execution metadata assignment, and safe Unknown-risk fallback are absent.

- [ ] **Step 3: Add the internal invoker and registration extension**

```csharp
internal interface IFinancialAnalysisPythonInvoker
{
    string ComputeFinancialRatios(string requestJson);
    string ComparePeriods(string requestJson);
    string DetectFinancialRiskSignals(string requestJson);
    string SummarizeQuantitativeEvidence(string requestJson);
}

internal sealed class CSnakesFinancialAnalysisPythonInvoker(IPythonEnvironment environment)
    : IFinancialAnalysisPythonInvoker
{
    public string ComputeFinancialRatios(string requestJson) =>
        environment.FinancialAnalysis().ComputeFinancialRatios(requestJson);

    public string ComparePeriods(string requestJson) =>
        environment.FinancialAnalysis().ComparePeriods(requestJson);

    public string DetectFinancialRiskSignals(string requestJson) =>
        environment.FinancialAnalysis().DetectFinancialRiskSignals(requestJson);

    public string SummarizeQuantitativeEvidence(string requestJson) =>
        environment.FinancialAnalysis().SummarizeQuantitativeEvidence(requestJson);
}

public static class FinancialAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddPythonFinancialAnalysis(this IServiceCollection services)
    {
        services.AddScoped<IFinancialAnalysisPythonInvoker, CSnakesFinancialAnalysisPythonInvoker>();
        services.AddScoped<IPythonFinancialAnalysisService, CSnakesFinancialAnalysisService>();
        return services;
    }
}
```

Replace the direct service registrations in API and Python fixture with `AddPythonFinancialAnalysis()`.

- [ ] **Step 4: Refactor the service through separate invocation and mapping boundaries**

Inject `IFinancialAnalysisPythonInvoker` and `ILogger<CSnakesFinancialAnalysisService>`. Use one generic helper whose invocation catch maps to `PYTHON_INVOCATION_FAILED`, whose mapping catch maps to `PYTHON_RESPONSE_INVALID`, and whose success path attaches `Succeeded`:

```csharp
private TResponse Execute<TResponse>(
    string operation,
    Guid? sessionId,
    CancellationToken cancellationToken,
    Func<string> invoke,
    Func<string, TResponse> map,
    Func<FinancialAnalysisStageExecution, TResponse> failure,
    Func<TResponse, FinancialAnalysisStageExecution, TResponse> attach)
{
    var started = Stopwatch.GetTimestamp();
    string responseJson;

    try
    {
        responseJson = invoke();
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        return Fail(
            ex,
            operation,
            sessionId,
            started,
            FinancialAnalysisFailureCodes.PythonInvocationFailed,
            failure);
    }

    try
    {
        var response = map(responseJson);
        var execution = new FinancialAnalysisStageExecution(
            operation,
            FinancialAnalysisExecutionStatus.Succeeded,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        _logger.LogInformation(
            "Financial analysis operation {Operation} completed for session {SessionId} after {DurationMilliseconds} ms with status {ExecutionStatus}.",
            operation,
            sessionId,
            execution.DurationMilliseconds,
            execution.Status);
        return attach(response, execution);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        return Fail(
            ex,
            operation,
            sessionId,
            started,
            FinancialAnalysisFailureCodes.PythonResponseInvalid,
            failure);
    }
}

private TResponse Fail<TResponse>(
    Exception exception,
    string operation,
    Guid? sessionId,
    long started,
    string failureCode,
    Func<FinancialAnalysisStageExecution, TResponse> failure)
{
    var execution = new FinancialAnalysisStageExecution(
        operation,
        FinancialAnalysisExecutionStatus.Failed,
        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        failureCode);
    _logger.LogError(
        exception,
        "Financial analysis operation {Operation} failed for session {SessionId} after {DurationMilliseconds} ms with code {FailureCode}.",
        operation,
        sessionId,
        execution.DurationMilliseconds,
        failureCode);
    return failure(execution);
}
```

Each public method still calls `cancellationToken.ThrowIfCancellationRequested()` before `Execute`. Serialize the request, call the operation-specific invoker method, map the response, and attach metadata with a record `with` expression. Do not log serialized request or response JSON.

Change safe signals and summary fallbacks to `RiskLevel: "Unknown"`, and pass the failed execution into every safe response's `Execution` property.

- [ ] **Step 5: Add the structured logger test helper**

Implement `TestCapturingLogger<T>` without console or file output:

```csharp
using Microsoft.Extensions.Logging;

namespace Orchestration.Tests;

public sealed record TestLogEntry(
    LogLevel Level,
    Exception? Exception,
    string Message,
    IReadOnlyDictionary<string, object?> Properties);

public sealed class TestCapturingLogger<T> : ILogger<T>
{
    public List<TestLogEntry> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
        NoopDisposable.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> values
            ? values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);
        Entries.Add(new TestLogEntry(
            logLevel,
            exception,
            formatter(state, exception),
            properties));
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();
        public void Dispose() { }
    }
}
```

- [ ] **Step 6: Add success metadata assertions to real-Python integration tests**

In one existing test per operation, assert:

```csharp
response.Execution.Status.Should().Be(FinancialAnalysisExecutionStatus.Succeeded);
response.Execution.FailureCode.Should().BeNull();
response.Execution.DurationMilliseconds.Should().BeGreaterThanOrEqualTo(0);
```

- [ ] **Step 7: Run adapter tests and confirm GREEN**

Run the Step 2 command, then:

```powershell
$env:ORCHESTRATION_TEST_PYTHON_HOME=(Resolve-Path 'python-agents\data_agent').Path
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~CSnakesFinancialAnalysisServiceTests" --verbosity minimal
```

Expected: both suites PASS.

- [ ] **Step 8: Commit the observable adapter slice**

```powershell
git add backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis backend/Orchestration.Api/Program.cs backend/Orchestration.Tests/Agents/Data backend/Orchestration.Tests/TestCapturingLogger.cs
git commit -m "Expose financial analysis execution failures"
```

---

### Task 3: Reject Invalid Python Response Shapes Without Rejecting Valid Empty Results

**Files:**
- Modify: `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/CSnakesFinancialAnalysisService.cs:103-245,351-501`
- Modify: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/CSnakesFinancialAnalysisServiceFailureTests.cs`

- [ ] **Step 1: Write failing schema tests for all four operations**

Add these exact cases:

```csharp
[Theory]
[InlineData(FinancialAnalysisOperations.Ratios, "{}")]
[InlineData(FinancialAnalysisOperations.Comparisons, "{\"comparisons\":{}}")]
[InlineData(FinancialAnalysisOperations.Signals, "{\"signals\":null}")]
[InlineData(FinancialAnalysisOperations.Summary, "{\"evidence\":[]}")]
public async Task Invalid_response_shape_Should_return_failed_execution(
    string operation,
    string responseJson)
{
    var invoker = new FakeFinancialAnalysisPythonInvoker
    {
        OverrideOperation = operation,
        OverrideResponseJson = responseJson
    };
    var service = new CSnakesFinancialAnalysisService(
        invoker,
        new TestCapturingLogger<CSnakesFinancialAnalysisService>());

    var response = await InvokeAsync(service, operation, Guid.NewGuid());

    response.Execution.Status.Should().Be(FinancialAnalysisExecutionStatus.Failed);
    response.Execution.FailureCode.Should().Be(FinancialAnalysisFailureCodes.PythonResponseInvalid);
}

[Fact]
public async Task Empty_signal_array_Should_remain_a_successful_low_risk_result()
{
    var invoker = new FakeFinancialAnalysisPythonInvoker
    {
        OverrideOperation = FinancialAnalysisOperations.Signals,
        OverrideResponseJson = "{\"signals\":[],\"warnings\":[]}"
    };
    var service = new CSnakesFinancialAnalysisService(
        invoker,
        new TestCapturingLogger<CSnakesFinancialAnalysisService>());

    var response = await service.DetectFinancialRiskSignalsAsync(
        new DetectFinancialRiskSignalsRequest([], [], [], SessionId: Guid.NewGuid()),
        CancellationToken.None);

    response.Execution.Status.Should().Be(FinancialAnalysisExecutionStatus.Succeeded);
    response.Result.RiskLevel.Should().Be("Low");
    response.Signals.Should().BeEmpty();
}
```

Add one malformed numeric test per item-bearing response so invalid required values do not silently become zero.

- [ ] **Step 2: Run schema tests and confirm RED**

Run the Task 2 Step 2 command.

Expected: new tests FAIL because permissive helpers still map missing arrays to empty values and invalid decimals to zero.

- [ ] **Step 3: Add strict required-value helpers**

```csharp
private static IReadOnlyList<JsonElement> GetRequiredArray(
    JsonElement element,
    string propertyName)
{
    if (element.ValueKind != JsonValueKind.Object ||
        !element.TryGetProperty(propertyName, out var property) ||
        property.ValueKind != JsonValueKind.Array)
    {
        throw new JsonException($"Required array '{propertyName}' was missing or invalid.");
    }

    return property.EnumerateArray().ToArray();
}

private static string GetRequiredString(
    JsonElement element,
    params string[] propertyNames)
{
    if (!TryGetProperty(element, propertyNames, out var property) ||
        property.ValueKind != JsonValueKind.String ||
        string.IsNullOrWhiteSpace(property.GetString()))
    {
        throw new JsonException($"Required string '{propertyNames[0]}' was missing or invalid.");
    }

    return property.GetString()!;
}

private static decimal GetRequiredDecimal(
    JsonElement element,
    params string[] propertyNames)
{
    var value = GetNullableDecimal(element, propertyNames);
    return value ?? throw new JsonException(
        $"Required decimal '{propertyNames[0]}' was missing or invalid.");
}
```

Use `GetRequiredArray` for `ratios`, `comparisons`, `signals`, and summary `evidence`. Require summary `summary` text. Use required strings and decimals for identity/value fields; keep threshold, reason, source, warnings, limitations, and other documented optional fields nullable or defaulted.

- [ ] **Step 4: Run schema, adapter, and real-Python tests**

Run both Task 2 Step 7 commands.

Expected: PASS, including successful explicit empty arrays and current Python payloads.

- [ ] **Step 5: Commit strict response validation**

```powershell
git add backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/CSnakesFinancialAnalysisService.cs backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/CSnakesFinancialAnalysisServiceFailureTests.cs
git commit -m "Validate Python financial analysis responses"
```

---

### Task 4: Aggregate Partial Execution and Force DataAgent Review

**Files:**
- Modify: `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/DataAgentFinancialAnalysisWorkflow.cs:77-227`
- Modify: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/DataAgentFinancialAnalysisWorkflowTests.cs:16-633`

- [ ] **Step 1: Extend the workflow fake and write failing stage-policy tests**

Give `FakePythonFinancialAnalysisService` four configurable execution properties defaulting to explicit success. Attach them to returned responses. Add tests:

```csharp
[Theory]
[InlineData(FinancialAnalysisOperations.Ratios)]
[InlineData(FinancialAnalysisOperations.Comparisons)]
[InlineData(FinancialAnalysisOperations.Summary)]
public async Task AnalyzeAsync_Should_preserve_evidence_and_require_review_when_non_signal_stage_fails(
    string failedOperation)
{
    var service = new FakePythonFinancialAnalysisService { FailedOperation = failedOperation };
    var workflow = CreateWorkflow(
        new FakeStructuredFinancialMetricsProvider(CreateMetricsDocument()),
        service);

    var result = await workflow.AnalyzeAsync(CreateReport(), CancellationToken.None);

    result.RequiresHumanReview.Should().BeTrue();
    result.FinancialAnalysis!.Execution.OverallStatus
        .Should().Be(FinancialAnalysisExecutionStatus.Degraded);
    result.FinancialAnalysis.RiskSignals.Should().NotBeEmpty();
}

[Fact]
public async Task AnalyzeAsync_Should_fail_closed_when_signal_stage_fails()
{
    var publisher = new FakeActivityEventPublisher();
    var aiReview = new FakeDataAgentAiReviewService();
    var service = new FakePythonFinancialAnalysisService
    {
        FailedOperation = FinancialAnalysisOperations.Signals
    };
    var workflow = CreateWorkflow(
        new FakeStructuredFinancialMetricsProvider(CreateMetricsDocument()),
        service,
        publisher,
        aiReviewService: aiReview);

    var result = await workflow.AnalyzeAsync(CreateReport(), CancellationToken.None);

    result.HasAnomaly.Should().BeFalse();
    result.Severity.Should().Be("Unknown");
    result.RequiresHumanReview.Should().BeTrue();
    result.FinancialAnalysis!.Execution.OverallStatus
        .Should().Be(FinancialAnalysisExecutionStatus.Failed);
    result.FinancialAnalysis.AiReview!.FailureReason
        .Should().Be("financial_analysis_execution_failed");
    aiReview.Calls.Should().Be(0);
    publisher.PublishedEvents.Should().ContainSingle(item =>
        item.Type == "financial_analysis_execution_failed");
}
```

Extend the existing warning-only zero-signal test to assert `RequiresHumanReview == false` and aggregate `Succeeded`. Add assertions that every request received the report's exact `SessionId`.

- [ ] **Step 2: Run workflow tests and confirm RED**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~DataAgentFinancialAnalysisWorkflowTests" --verbosity minimal
```

Expected: FAIL because the workflow does not propagate session IDs, aggregate execution, set review state, skip AI review, or publish failure activity.

- [ ] **Step 3: Propagate session correlation and aggregate all stage responses**

Pass `report.SessionId` into every request. After summary returns, add:

```csharp
var execution = FinancialAnalysisExecution.FromStages(
[
    ratios.Execution,
    comparisons.Execution,
    signals.Execution,
    summary.Execution
]);
var signalsSucceeded =
    signals.Execution.Status == FinancialAnalysisExecutionStatus.Succeeded;
var requiresHumanReview =
    execution.OverallStatus != FinancialAnalysisExecutionStatus.Succeeded;
var hasAnomaly = signalsSucceeded &&
    signals.Signals.Any(signal => IsMediumOrHigh(signal.Severity));
var severity = signalsSucceeded
    ? ResolveSeverity(signals.Signals.Select(signal => signal.Severity))
    : "Unknown";
```

Preserve warnings from all responses. Add one incomplete-analysis limitation when aggregate status is not succeeded. Set the result context with:

```csharp
var financialAnalysis = new FinancialAnalysisContext(
    Engine: Engine,
    DocumentId: metricsDocument.DocumentId,
    Company: metricsDocument.Company,
    Ratios: ratios.Ratios,
    Comparisons: comparisons.Comparisons,
    RiskSignals: signals.Signals,
    RiskEvidence: summary.Result.Evidence,
    Warnings: warnings,
    Limitations: limitations,
    MetricsInputSource: metricsDocument.InputSource,
    MetricsProvenance: metricsDocument.Provenance,
    AiReview: aiReview,
    ThresholdProfile: resolvedProfileName,
    ThresholdsUsed: resolvedThresholds)
{
    Execution = execution
};
```

Return `RequiresHumanReview: requiresHumanReview`. Set it to `true` in both existing missing-metrics safe results as well.

- [ ] **Step 4: Gate AI review and publish one safe activity event**

Use:

```csharp
var aiReview = signalsSucceeded
    ? await _aiReviewService.ReviewAsync(
        new FinancialAnalysisAiReviewInput(
            SessionId: report.SessionId.ToString(),
            DocumentId: metricsDocument.DocumentId,
            Company: metricsDocument.Company,
            MetricsInputSource: metricsDocument.InputSource,
            MetricsProvenance: metricsDocument.Provenance,
            Ratios: ratios.Ratios,
            PeriodComparisons: comparisons.Comparisons,
            RiskSignals: signals.Signals,
            RiskEvidence: summary.Result.Evidence,
            Warnings: warnings,
            Limitations: limitations,
            ThresholdProfile: resolvedProfileName,
            ThresholdsUsed: resolvedThresholds),
        cancellationToken)
    : FinancialAnalysisAiReviewResults.NotRun("financial_analysis_execution_failed");
```

When execution is degraded or failed, publish one `ActivityEvent` whose type matches the aggregate state and whose message contains only failed operation names and stable codes. Do not include exception messages or metric values.

- [ ] **Step 5: Run workflow tests and confirm GREEN**

Run the Step 2 command.

Expected: PASS for every independent stage failure, partial-output preservation, AI gating, session propagation, safe activity, and successful zero-signal control.

- [ ] **Step 6: Commit workflow aggregation**

```powershell
git add backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/DataAgentFinancialAnalysisWorkflow.cs backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/DataAgentFinancialAnalysisWorkflowTests.cs
git commit -m "Fail closed on incomplete financial analysis"
```

---

### Task 5: Preserve Review Requirements Through Fallback, Plan-Driven Mapping, and Planner

**Files:**
- Modify: `backend/Orchestration.Infrastructure/Agents/Data/ConfigurableDataAgent.cs:48-90`
- Modify: `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/ToolExecutionResultMapper.cs:18-41`
- Modify: `backend/Orchestration.Application/Agents/Planner/PlannerAgent.cs:317-348`
- Modify: `backend/Orchestration.Tests/Agents/Data/ConfigurableDataAgentTests.cs:61-169`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ToolExecutionResultMapperTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/PlannerAgentTests.cs`

- [ ] **Step 1: Write failing fallback, mapper, and Planner tests**

Add:

```csharp
[Fact]
public async Task AnalyzeAsync_Should_preserve_review_requirement_after_healthy_legacy_fallback()
{
    var legacy = new FakeLegacyDataAgent
    {
        Result = new DataAgentResult(false, "Low", "Legacy clean.", "Legacy", [])
    };
    var workflow = new FakeFinancialAnalysisWorkflow { ThrowOnAnalyze = true };
    var agent = CreateAgent(legacy, workflow, new DataAgentOptions
    {
        FinancialAnalysisToolsEnabled = true,
        UseLegacyAnomalyDetectionFallback = true
    });

    var result = await agent.AnalyzeAsync(CreateReport(), CancellationToken.None);

    result.HasAnomaly.Should().BeFalse();
    result.RequiresHumanReview.Should().BeTrue();
    result.Summary.Should().Contain("No se pudo completar el análisis financiero estructurado");
}

[Fact]
public async Task RunAsync_Should_require_human_approval_when_data_requires_review_without_anomaly()
{
    var data = CreateDataResult(hasAnomaly: false) with { RequiresHumanReview = true };
    var planner = CreatePlannerAgent(data, CreateLegalResult(hasComplianceRisk: false));

    var result = await planner.RunAsync(TestFinancialReport.CreateContext(), CancellationToken.None);

    result.RequiresHumanApproval.Should().BeTrue();
}
```

Add mapper tests showing explicit `RequiresHumanReview` round-trips and a mapped `FinancialAnalysis.Execution` of `failed`, `degraded`, or `legacy_unknown` is normalized to review-required.

- [ ] **Step 2: Run focused tests and confirm RED**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~ConfigurableDataAgentTests|FullyQualifiedName~ToolExecutionResultMapperTests|FullyQualifiedName~PlannerAgentTests" --verbosity minimal
```

Expected: FAIL because fallback can erase the structured failure, mapper trusts old false defaults, and Planner ignores the new flag.

- [ ] **Step 3: Make fallback and no-fallback results explicitly review-required**

For legacy fallback:

```csharp
return fallback with
{
    Engine = $"{fallback.Engine} (respaldo legacy)",
    Summary = $"{fallback.Summary} No se pudo completar el análisis financiero estructurado; se requiere revisión humana.",
    RequiresHumanReview = true
};
```

Log the unexpected catch with structured `SessionId` and `FailureCode = FINANCIAL_ANALYSIS_UNEXPECTED_FAILURE`. For no fallback, return `HasAnomaly: false`, `Severity: "Unknown"`, and `RequiresHumanReview: true`.

- [ ] **Step 4: Fail closed when mapping plan-driven DataAgent output**

After deserialization:

```csharp
var result = JsonSerializer.Deserialize<DataAgentResult>(call.OutputJson, JsonOptions);
if (result?.FinancialAnalysis is { } financialAnalysis &&
    financialAnalysis.Execution.OverallStatus != FinancialAnalysisExecutionStatus.Succeeded)
{
    return result with { RequiresHumanReview = true };
}

return result;
```

Do not force review for legacy DataAgent results whose `FinancialAnalysis` is null.

- [ ] **Step 5: Add the Planner review flag to the HITL condition**

```csharp
var requiresHumanApproval =
    dataResult.HasAnomaly ||
    dataResult.RequiresHumanReview ||
    legalResult.HasComplianceRisk;
```

- [ ] **Step 6: Run focused tests and confirm GREEN**

Run the Step 2 command.

Expected: PASS across deterministic, fallback, and plan-driven Planner paths.

- [ ] **Step 7: Commit the review gate slice**

```powershell
git add backend/Orchestration.Application/Agents/Planner/PlannerAgent.cs backend/Orchestration.Infrastructure/Agents/Data/ConfigurableDataAgent.cs backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/ToolExecutionResultMapper.cs backend/Orchestration.Tests/Agents/Data/ConfigurableDataAgentTests.cs backend/Orchestration.Tests/Agents/Planner
git commit -m "Force review for incomplete data analysis"
```

---

### Task 6: Persist Execution Evidence and Prove API Reload Behavior

**Files:**
- Modify: `backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs:291-480`
- Modify: `backend/Orchestration.Tests/AnalysisSessions/AnalysisOrchestratorContextTests.cs:265-492`
- Modify: `backend/Orchestration.Tests/AnalysisSessions/ProductionLikeWorkflowE2ETests.cs:49-188,583-680,889-1042`

- [ ] **Step 1: Write failing context and compatibility tests**

Build a degraded `FinancialAnalysisContext` with a failed ratio stage and assert:

```csharp
var root = JsonDocument.Parse(contextJson).RootElement;
var anomaly = root.GetProperty("anomaly");
var execution = root.GetProperty("financialAnalysis").GetProperty("execution");

anomaly.GetProperty("assessmentStatus").GetString().Should().Be("inconclusive");
anomaly.GetProperty("requiresHumanReview").GetBoolean().Should().BeTrue();
execution.GetProperty("overallStatus").GetString().Should().Be("degraded");
execution.GetProperty("stages").EnumerateArray().Should().Contain(stage =>
    stage.GetProperty("operation").GetString() == FinancialAnalysisOperations.Ratios &&
    stage.GetProperty("failureCode").GetString() == FinancialAnalysisFailureCodes.PythonInvocationFailed);
```

Extend old-context deserialization to assert `FinancialAnalysisExecutionStatus.LegacyUnknown` when `execution` is missing.

- [ ] **Step 2: Write a failing production-like degraded/no-business-risk test**

Configure the production fake to return explicit successful stage metadata by default. Add a mode where ratios fail, signals return a successful empty array, and LegalAgent has no evidence. Start the session and assert:

```csharp
startedSession.Status.Should().Be(AnalysisSessionStatus.AwaitingHumanApproval);
startedContext.RootElement
    .GetProperty("financialAnalysis")
    .GetProperty("execution")
    .GetProperty("overallStatus")
    .GetString()
    .Should().Be("degraded");
startedContext.RootElement
    .GetProperty("anomaly")
    .GetProperty("detected")
    .GetBoolean()
    .Should().BeFalse();
```

Call `GetSession`, read `AnalysisSessionDto.ContextJson`, and repeat the execution assertions to prove API reload does not lose metadata.

- [ ] **Step 3: Run context and E2E tests and confirm RED**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~AnalysisOrchestratorContextTests|FullyQualifiedName~ProductionLikeWorkflowE2ETests" --verbosity minimal
```

Expected: FAIL because anonymous context mapping omits execution/assessment fields and production fakes default to legacy-unknown metadata.

- [ ] **Step 4: Persist assessment and execution fields explicitly**

Extend the anomaly object:

```csharp
assessmentStatus = plannerResult.DataResult.RequiresHumanReview
    ? "inconclusive"
    : "completed",
requiresHumanReview = plannerResult.DataResult.RequiresHumanReview,
```

Extend the financial-analysis object:

```csharp
execution = new
{
    overallStatus = plannerResult.DataResult.FinancialAnalysis.Execution.OverallStatus,
    stages = plannerResult.DataResult.FinancialAnalysis.Execution.Stages.Select(stage => new
    {
        operation = stage.Operation,
        status = stage.Status,
        durationMilliseconds = stage.DurationMilliseconds,
        failureCode = stage.FailureCode
    })
},
```

The enum converter writes lowercase strings. Preserve `financialReport` and `structuredFinancialMetrics` using the existing JSON root merge.

- [ ] **Step 5: Mark all production fake responses explicitly successful and add degraded mode**

Attach `new FinancialAnalysisStageExecution(operation, Succeeded, 1)` to each default response. In degraded mode, return an empty ratio response with `Failed` and `PYTHON_INVOCATION_FAILED`, while signals remain explicit successful/empty.

- [ ] **Step 6: Run context and E2E tests and confirm GREEN**

Run the Step 3 command.

Expected: PASS; degraded technical execution pauses for HITL without manufacturing an anomaly, and API reload retains stage evidence.

- [ ] **Step 7: Commit persistence and E2E coverage**

```powershell
git add backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs backend/Orchestration.Tests/AnalysisSessions
git commit -m "Persist financial analysis execution status"
```

---

### Task 7: Render Safe Execution Status in the Financial Panel

**Files:**
- Create: `frontend/src/utils/financialAnalysisExecution.ts`
- Create: `frontend/tests/financialAnalysisExecution.test.ts`
- Modify: `frontend/src/types/domain.types.ts:222-237`
- Modify: `frontend/src/components/FinancialRiskEvidencePanel.tsx:1-203`
- Modify: `frontend/src/App.css:1452-1521`

- [ ] **Step 1: Write failing pure presenter tests**

```typescript
import assert from "node:assert/strict";
import test from "node:test";
import { getFinancialAnalysisExecutionBanner } from "../src/utils/financialAnalysisExecution.ts";

test("successful execution has no banner", () => {
  assert.equal(
    getFinancialAnalysisExecutionBanner({ overallStatus: "succeeded", stages: [] }),
    null,
  );
});

test("degraded execution requires review and keeps only safe stage labels", () => {
  const banner = getFinancialAnalysisExecutionBanner({
    overallStatus: "degraded",
    stages: [
      {
        operation: "ratios",
        status: "failed",
        durationMilliseconds: 12,
        failureCode: "PYTHON_INVOCATION_FAILED",
      },
    ],
  });

  assert.equal(banner?.tone, "warning");
  assert.match(banner?.title ?? "", /revisión humana/i);
  assert.deepEqual(banner?.affectedStages, ["Ratios financieros — PYTHON_INVOCATION_FAILED"]);
});

test("unknown failure codes are not rendered", () => {
  const banner = getFinancialAnalysisExecutionBanner({
    overallStatus: "failed",
    stages: [{
      operation: "signals",
      status: "failed",
      durationMilliseconds: 1,
      failureCode: "raw exception from provider",
    }],
  });

  assert.deepEqual(banner?.affectedStages, ["Señales de riesgo"]);
});

test("missing or unknown execution is neutral historical metadata", () => {
  assert.equal(getFinancialAnalysisExecutionBanner(undefined)?.tone, "neutral");
  assert.equal(
    getFinancialAnalysisExecutionBanner({ overallStatus: "future", stages: [] })?.tone,
    "neutral",
  );
});
```

- [ ] **Step 2: Run the presenter test and confirm RED**

Run:

```powershell
node --test frontend/tests/financialAnalysisExecution.test.ts
```

Expected: FAIL because the utility and types do not exist.

- [ ] **Step 3: Add optional frontend execution types**

```typescript
export type FinancialAnalysisExecutionStatus =
  | "legacy_unknown"
  | "succeeded"
  | "degraded"
  | "failed"
  | string;

export type FinancialAnalysisStageExecutionContext = {
  operation: string;
  status: FinancialAnalysisExecutionStatus;
  durationMilliseconds: number;
  failureCode?: string | null;
};

export type FinancialAnalysisExecutionContext = {
  overallStatus: FinancialAnalysisExecutionStatus;
  stages: FinancialAnalysisStageExecutionContext[];
};
```

Add `execution?: FinancialAnalysisExecutionContext | null` to `FinancialAnalysisContext`.

- [ ] **Step 4: Implement a safe banner presenter**

The utility must:

- return `null` only for `succeeded`;
- normalize missing/unrecognized status to `legacy_unknown`;
- use fixed Spanish labels for the four allowed operation names;
- render only `PYTHON_INVOCATION_FAILED` and `PYTHON_RESPONSE_INVALID` codes;
- return warning, danger, or neutral tone plus title, description, and affected stages.

Use exact titles from the spec:

```typescript
const titles = {
  degraded: "Análisis incompleto — revisión humana requerida.",
  failed: "No se pudo completar la evaluación de riesgo — revisión humana requerida.",
  legacy_unknown: "No hay metadatos de ejecución disponibles para este análisis histórico.",
} as const;
```

- [ ] **Step 5: Render the banner before source warnings**

In `FinancialRiskEvidencePanel`, calculate the view model and render:

```tsx
{executionBanner && (
  <div
    className={`financialExecutionBanner financialExecution-${executionBanner.tone}`}
    role={executionBanner.tone === "danger" ? "alert" : "status"}
  >
    <strong>{executionBanner.title}</strong>
    <p>{executionBanner.description}</p>
    {executionBanner.affectedStages.length > 0 && (
      <ul>
        {executionBanner.affectedStages.map((stage) => <li key={stage}>{stage}</li>)}
      </ul>
    )}
  </div>
)}
```

Add visually distinct warning/danger/neutral styles near `.financialSourceWarning`, using existing foreground/background contrast tokens and visible borders. Do not hide valid evidence when the banner is present.

- [ ] **Step 6: Run frontend tests and build**

Run:

```powershell
node --test frontend/tests/*.test.ts
npm run build --prefix frontend
```

Expected: all Node tests PASS and TypeScript/Vite build succeeds.

- [ ] **Step 7: Commit the frontend status slice**

```powershell
git add frontend/src frontend/tests
git commit -m "Show incomplete financial analysis status"
```

---

### Task 8: Document Semantics and Run Full Verification

**Files:**
- Modify: `README.md:375-413,613-707,1550-1570`
- Modify: `docs/demo-script.md:140-180`

- [ ] **Step 1: Document execution status separately from risk**

Add concise tables covering:

| Execution | Meaning | Business risk | HITL |
|---|---|---|---|
| `succeeded` | All required stages completed | Calculated normally | Based on business evidence |
| `degraded` | Some required stages failed, valid evidence retained | Partial calculation may exist | Required |
| `failed` | Signal assessment unavailable | `Unknown` | Required |
| `legacy_unknown` | Historical metadata absent | Do not infer success | Required for new executions |

Document stable failure codes, structured log fields, safe warning rules, and that warnings alone do not imply technical failure.

- [ ] **Step 2: Update the demo verification path**

Add checks that a successful run has no degraded banner, a simulated degraded run pauses for review, and reloading the session preserves `financialAnalysis.execution`.

- [ ] **Step 3: Run Python, backend, frontend, and repository checks**

Run fresh commands from repository root:

```powershell
python -m unittest python-agents/tests/test_financial_analysis.py
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --verbosity minimal
dotnet build backend/Orchestration.slnx --no-restore --verbosity minimal
node --test frontend/tests/*.test.ts
npm run build --prefix frontend
git diff --check
```

Expected:

- Python financial-analysis tests PASS.
- Backend test suite reports zero failures.
- Backend build reports zero errors.
- Frontend Node tests report zero failures.
- Frontend build succeeds.
- `git diff --check` exits 0.

- [ ] **Step 4: Scan production code for the unsafe fallback**

Run:

```powershell
rg -n "SafeRiskSignalsResponse|SafeEvidenceSummaryResponse|RiskLevel:\s*\"Low\"" backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis
```

Expected: safe failure methods may remain, but neither failed signals nor failed summary constructs `RiskLevel: "Low"`.

- [ ] **Step 5: Request two-stage subagent review**

Dispatch a spec-compliance reviewer first. After all spec findings are resolved, dispatch a code-quality reviewer. Re-run every affected focused suite after fixes, then rerun Step 3.

- [ ] **Step 6: Commit documentation and final evidence**

```powershell
git add README.md docs/demo-script.md
git commit -m "Document fail-closed financial analysis behavior"
```

- [ ] **Step 7: Confirm final branch state**

Run:

```powershell
git status --short --branch
git log --oneline origin/main..HEAD
```

Expected: clean worktree on `codex/p0-financial-analysis-fail-closed`; commits are limited to issue #4 design, implementation, tests, UI, and documentation.
