# Adaptive Plan-Driven Data-to-Legal Execution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute approved plan-driven Data analysis before Legal retrieval, derive audited Legal queries from completed financial evidence, and preserve safe final outputs across partial failure and cancellation.

**Architecture:** Planner normalizes, validates, and applies policy once, then executes trusted catalog stages in canonical Data-to-Legal order. A single argument-free Legal capability invokes aggregate `LegalAgent` review with `provided_only` runtime context; deterministic strategy code derives one to four CNV queries or one explicitly audited generic fallback. Successful and safe aggregate Data/Legal results reach final Planner reasoning without rerunning completed stages.

**Tech Stack:** .NET 10, C#, ASP.NET Core, Semantic Kernel, EF Core, xUnit, FluentAssertions, React/TypeScript, CNV MCP client.

---

## File and Responsibility Map

New focused contracts:

- `backend/Orchestration.Application/Agents/Planner/ToolCalling/ToolPlanProposalProvenance.cs` — typed proposal source and fallback reason.
- `backend/Orchestration.Application/Agents/Planner/ToolCalling/PlannerToolExecutionContext.cs` — trusted report/Data context passed to controlled Legal execution.
- `backend/Orchestration.Application/Agents/Planner/ToolCalling/PlannerStageSafeResults.cs` — explicit review-required Data/Legal results for absent or failed stages.
- `backend/Orchestration.Application/Agents/Legal/Cnv/LegalDataEvidenceContext.cs` — Data status, usable-signal decision, financial execution status, and safe failure metadata.
- `backend/Orchestration.Application/Agents/Legal/Cnv/LegalDataEvidenceClassifier.cs` — deterministic precedence from Data output to Legal evidence state.
- `backend/Orchestration.Application/Agents/Legal/Cnv/LegalCnvQueryPlan.cs` — contextual/fallback query plan before retrieval.
- `backend/Orchestration.Application/Agents/Legal/LegalReviewContext.cs` — `provided_only` vs `provided_or_persisted` resolution mode.
- `backend/Orchestration.Application/Agents/Legal/Regulations/RegulatoryReviewRequest.cs` — report plus trusted Legal review context.

Primary modified units:

- Planner proposal/catalog/normalizer contracts — argument-free Legal envelope and proposal provenance.
- `FinancialAnalysisLegalCnvQueryStrategy` — signal-only classification, fallback reasons, ordered query plan.
- `McpRegulatoryKnowledgeSource` — explicit context resolution, per-query outcomes, partial success, cancellation.
- `ControlledToolExecutor` and mapper — aggregate `LegalAgentResult`, no direct pre-Data MCP query.
- `PlannerAgent` — canonical staged execution, localized safe results, exact event order.
- `AnalysisOrchestratorService` and frontend types — additive durable audit fields without `OutputJson`.

## Task 1: Make Legal Planning an Argument-Free Composite Capability

**Files:**

- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/PlannerToolCatalog.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/DeterministicToolPlanProposalService.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/ToolPlanNormalizer.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/PlannerToolCatalogTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/DeterministicToolPlanProposalServiceTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ToolPlanNormalizerTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ToolPlanValidatorTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/SemanticKernelToolPlanProposalServiceTests.cs`

- [ ] **Step 1: Write failing catalog, proposal, normalization, and validation tests**

Add these assertions to the existing focused test classes:

```csharp
var legal = PlannerToolCatalog.Find(
    PlannerToolCatalog.SearchCnvRegulationName);

legal.Should().NotBeNull();
legal!.Arguments.Should().BeEmpty();
legal.PromptDescription.Should().Contain("análisis financiero completado");
```

```csharp
var legalCall = result.ProposedCalls.Single(call =>
    call.ToolName == PlannerToolCatalog.SearchCnvRegulationName);

legalCall.Arguments.Should().BeEmpty();
```

```csharp
var normalized = new ToolPlanNormalizer().Normalize(new ToolPlan(
[
    new ProposedToolCall(
        PlannerToolCatalog.SearchCnvRegulationName,
        new Dictionary<string, string>(),
        "Primera autorización."),
    new ProposedToolCall(
        PlannerToolCatalog.SearchCnvRegulationName,
        new Dictionary<string, string>(),
        "Autorización duplicada.")
]));

normalized.ProposedCalls.Should().ContainSingle();
normalized.ProposedCalls[0].Reason.Should().Be("Primera autorización.");
```

```csharp
var result = validator.Validate(new ToolPlan(
[
    new ProposedToolCall(
        PlannerToolCatalog.SearchCnvRegulationName,
        new Dictionary<string, string> { ["query"] = "agentes" },
        "Consulta prematura.")
]));

result.ApprovedCalls.Should().BeEmpty();
result.RejectedCalls.Should().ContainSingle()
    .Which.Reason.Should().Be("Argumento no permitido: query.");
```

- [ ] **Step 2: Run focused tests and verify the old query contract fails**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~PlannerToolCatalogTests|FullyQualifiedName~DeterministicToolPlanProposalServiceTests|FullyQualifiedName~ToolPlanNormalizerTests|FullyQualifiedName~ToolPlanValidatorTests|FullyQualifiedName~SemanticKernelToolPlanProposalServiceTests" --verbosity minimal
```

Expected: FAIL because Legal still exposes/requires `query`, deterministic proposal still emits `"agentes"`, and duplicate Legal calls have query-based keys.

- [ ] **Step 3: Replace the Legal catalog definition and deterministic proposal**

Use this catalog definition:

```csharp
new PlannerToolDefinition(
    SearchCnvRegulationName,
    "Autoriza una revisión legal compuesta; la lógica determinista deriva consultas CNV del análisis financiero completado.",
    PlannerToolHandler.SearchCnvRegulation,
    PlannerToolResultKind.LegalAgent,
    PlannerToolSatisfactionKind.LegalReview,
    "LegalAgent",
    Array.Empty<PlannerToolArgumentDefinition>())
```

Use this deterministic call:

```csharp
calls.Add(
    new ProposedToolCall(
        ToolName: PlannerToolCatalog.SearchCnvRegulationName,
        Arguments: new Dictionary<string, string>(),
        Reason: "Autorizar una revisión regulatoria CNV derivada del análisis financiero completado."));
```

Remove the Legal `query|area` special case from `ToolPlanNormalizer.CreateDeduplicationKey`; the existing generic key now deduplicates argument-free Legal calls by canonical tool name. Do not strip unexpected Legal arguments in normalization: validator must reject them.

- [ ] **Step 4: Update valid Semantic Kernel fixtures to emit `arguments: {}`**

Use this Legal call in valid fake chat responses:

```json
{
  "toolName": "legal.search_cnv_regulation",
  "arguments": {},
  "reason": "Autorizar recuperación de evidencia CNV citada."
}
```

Assert the serialized `availableTools` catalog contains an empty Legal arguments array.

- [ ] **Step 5: Run focused tests and verify they pass**

Run the Step 2 command.

Expected: PASS; Legal proposals contain no pre-Data search arguments, duplicates collapse, and hostile query arguments are rejected.

- [ ] **Step 6: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Planner/ToolCalling backend/Orchestration.Tests/Agents/Planner/ToolCalling
git commit -m "feat: make legal plan capability argument-free"
```

## Task 2: Add Typed Proposal Provenance

**Files:**

- Create: `backend/Orchestration.Application/Agents/Planner/ToolCalling/ToolPlanProposalProvenance.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/ToolPlan.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/ToolPlanAuditResult.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/ToolPlanNormalizer.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/DeterministicToolPlanProposalService.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/PlannerAgent.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/SemanticKernelToolPlanResponseParser.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/ToolCallingDiagnosticService.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/DeterministicToolPlanProposalServiceTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/SemanticKernelToolPlanResponseParserTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ToolPlanNormalizerTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ToolCallingDiagnosticServiceTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/PlannerAgentTests.cs`

- [ ] **Step 1: Write failing provenance tests**

Add exact assertions:

```csharp
result.ProposalSource.Should().Be(ToolPlanProposalSource.Deterministic);
result.ProposalFallbackReason.Should().BeNull();
```

```csharp
parsed.ProposalSource.Should().Be(ToolPlanProposalSource.Llm);
parsed.ProposalFallbackReason.Should().BeNull();
```

```csharp
var original = new ToolPlan(
    ProposedCalls: [],
    ProposalSource: ToolPlanProposalSource.DeterministicFallback,
    ProposalFallbackReason: ToolPlanProposalFallbackReason.LlmResponseInvalid);

var normalized = new ToolPlanNormalizer().Normalize(original);

normalized.ProposalSource.Should().Be(original.ProposalSource);
normalized.ProposalFallbackReason.Should().Be(original.ProposalFallbackReason);
```

Add a Shadow-mode Planner assertion that a fake
`DeterministicFallback/LlmResponseInvalid` proposal reaches
`PlannerAgentResult.ToolPlan` unchanged.

- [ ] **Step 2: Run tests and verify the contracts are absent**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~DeterministicToolPlanProposalServiceTests|FullyQualifiedName~SemanticKernelToolPlanResponseParserTests|FullyQualifiedName~ToolPlanNormalizerTests|FullyQualifiedName~ToolCallingDiagnosticServiceTests|FullyQualifiedName~PlannerAgentTests" --verbosity minimal
```

Expected: FAIL to compile because proposal provenance types/properties do not exist.

- [ ] **Step 3: Add the provenance enums and additive record fields**

Create `ToolPlanProposalProvenance.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Orchestration.Application.Agents.Planner.ToolCalling;

[JsonConverter(typeof(JsonStringEnumConverter<ToolPlanProposalSource>))]
public enum ToolPlanProposalSource
{
    [JsonStringEnumMemberName("llm")]
    Llm,

    [JsonStringEnumMemberName("deterministic")]
    Deterministic,

    [JsonStringEnumMemberName("deterministic_fallback")]
    DeterministicFallback
}

[JsonConverter(typeof(JsonStringEnumConverter<ToolPlanProposalFallbackReason>))]
public enum ToolPlanProposalFallbackReason
{
    [JsonStringEnumMemberName("llm_response_invalid")]
    LlmResponseInvalid,

    [JsonStringEnumMemberName("llm_request_failed")]
    LlmRequestFailed,

    [JsonStringEnumMemberName("llm_configuration_failed")]
    LlmConfigurationFailed
}
```

Replace the record declarations with:

```csharp
public sealed record ToolPlan(
    IReadOnlyList<ProposedToolCall> ProposedCalls,
    ToolPlanProposalSource? ProposalSource = null,
    ToolPlanProposalFallbackReason? ProposalFallbackReason = null);
```

```csharp
public sealed record ToolPlanAuditResult(
    IReadOnlyList<ProposedToolCall> ProposedCalls,
    IReadOnlyList<ApprovedToolCall> ApprovedCalls,
    IReadOnlyList<RejectedToolCall> RejectedCalls,
    IReadOnlyList<ToolExecutionResult> ExecutedCalls,
    ToolPlanProposalSource? ProposalSource = null,
    ToolPlanProposalFallbackReason? ProposalFallbackReason = null)
{
    public static ToolPlanAuditResult Empty { get; } = new([], [], [], []);
}
```

Preserve metadata in normalization:

```csharp
return plan with { ProposedCalls = normalizedCalls };
```

Set `Deterministic` in the deterministic proposer and `Llm` in successful parser
output. Copy both normalized fields into diagnostic audit results and
`PlannerAgent.BuildToolPlanAuditAsync` results.

- [ ] **Step 4: Run provenance tests and verify they pass**

Run the Step 2 command.

Expected: PASS; legacy/fake plans may retain null provenance, while real producers identify their source.

- [ ] **Step 5: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Planner backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling backend/Orchestration.Tests/Agents/Planner
git commit -m "feat: add planner proposal provenance"
```

## Task 3: Classify Semantic Proposal Fallbacks and Propagate Cancellation

**Files:**

- Modify: `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/SemanticKernelToolPlanProposalService.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/SemanticKernelToolPlanProposalServiceTests.cs`

- [ ] **Step 1: Write failing invalid-response, request-failure, configuration-failure, and cancellation tests**

Use these core assertions:

```csharp
result.ProposalSource.Should().Be(
    ToolPlanProposalSource.DeterministicFallback);
result.ProposalFallbackReason.Should().Be(
    ToolPlanProposalFallbackReason.LlmResponseInvalid);
```

```csharp
result.ProposalFallbackReason.Should().Be(
    ToolPlanProposalFallbackReason.LlmRequestFailed);
```

```csharp
result.ProposalFallbackReason.Should().Be(
    ToolPlanProposalFallbackReason.LlmConfigurationFailed);
```

```csharp
var act = () => service.ProposeAsync(
    CreateInput(),
    CancellationToken.None);

await act.Should().ThrowAsync<OperationCanceledException>();
```

The last test uses a fake chat service that throws `OperationCanceledException` even when the supplied token is not marked cancelled; the exception must still propagate.

- [ ] **Step 2: Run the Semantic proposer tests and verify failures**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~SemanticKernelToolPlanProposalServiceTests" --verbosity minimal
```

Expected: FAIL because fallback metadata is absent and the current filtered cancellation catch can convert an `OperationCanceledException` into deterministic fallback.

- [ ] **Step 3: Add safe client initialization and reason-aware fallback**

Add nullable client/configuration state and this helper:

```csharp
private async Task<ToolPlan> CreateFallbackPlanAsync(
    ToolPlanProposalInput input,
    ToolPlanProposalFallbackReason reason,
    CancellationToken cancellationToken)
{
    var fallbackPlan = await _fallback.ProposeAsync(input, cancellationToken);

    return fallbackPlan with
    {
        ProposalSource = ToolPlanProposalSource.DeterministicFallback,
        ProposalFallbackReason = reason
    };
}
```

Use this initialization result and factory; do not log configuration values or
exception text:

```csharp
private sealed record ClientInitialization(
    Kernel? Kernel,
    IChatCompletionService? Chat,
    ToolPlanProposalFallbackReason? Failure);

private static ClientInitialization InitializeClient(LlmOptions options)
{
    try
    {
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(
            modelId: options.Model,
            apiKey: options.ApiKey,
            serviceId: options.ServiceId);
        var kernel = builder.Build();

        return new ClientInitialization(
            kernel,
            kernel.GetRequiredService<IChatCompletionService>(
                options.ServiceId),
            null);
    }
    catch
    {
        return new ClientInitialization(
            null,
            null,
            ToolPlanProposalFallbackReason.LlmConfigurationFailed);
    }
}
```

The production constructor assigns all three fields from `InitializeClient`.
The internal test constructor accepts nullable chat/kernel values plus an
optional configuration failure, allowing a deterministic configuration-failure
test without depending on Semantic Kernel internals.

Use this `ProposeAsync` control flow:

```csharp
if (_configurationFailure is { } configurationFailure)
{
    return await CreateFallbackPlanAsync(
        input,
        configurationFailure,
        cancellationToken);
}

try
{
    var response = await _chatCompletionService!
        .GetChatMessageContentAsync(
            history,
            kernel: _kernel,
            cancellationToken: cancellationToken);

    if (!_parser.TryParse(response.Content, out var plan))
    {
        return await CreateFallbackPlanAsync(
            input,
            ToolPlanProposalFallbackReason.LlmResponseInvalid,
            cancellationToken);
    }

    return CanonicalizeSubmittedAt(plan, input);
}
catch (OperationCanceledException)
{
    throw;
}
catch
{
    return await CreateFallbackPlanAsync(
        input,
        ToolPlanProposalFallbackReason.LlmRequestFailed,
        cancellationToken);
}
```

- [ ] **Step 4: Run tests and verify all fallback classes pass**

Run the Step 2 command.

Expected: PASS; cancellation never becomes fallback, and fallback plans carry one safe enum reason.

- [ ] **Step 5: Commit**

```powershell
git add backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/SemanticKernelToolPlanProposalService.cs backend/Orchestration.Tests/Agents/Planner/ToolCalling/SemanticKernelToolPlanProposalServiceTests.cs
git commit -m "feat: classify deterministic proposal fallbacks"
```

## Task 4: Classify Data Evidence and Define Safe Stage Results

**Files:**

- Create: `backend/Orchestration.Application/Agents/Legal/Cnv/LegalDataEvidenceContext.cs`
- Create: `backend/Orchestration.Application/Agents/Legal/Cnv/LegalDataEvidenceClassifier.cs`
- Create: `backend/Orchestration.Application/Agents/Planner/ToolCalling/PlannerStageSafeResults.cs`
- Modify: `backend/Orchestration.Application/Agents/Legal/LegalAgentResult.cs`
- Create: `backend/Orchestration.Tests/Agents/Legal/LegalDataEvidenceClassifierTests.cs`
- Create: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/PlannerStageSafeResultsTests.cs`

- [ ] **Step 1: Write the precedence and safe-result tests**

Cover Data tool failed, stage absent, legacy metadata, missing context, signals failed, empty signals, and degraded context with successful signals. Use these assertions:

```csharp
var result = LegalDataEvidenceClassifier.Classify(
    LegalDataToolStatuses.Executed,
    degradedContextWithSuccessfulSignals);

result.CanUseSignals.Should().BeTrue();
result.FallbackReason.Should().BeNull();
result.FinancialAnalysisStatus.Should().Be(
    FinancialAnalysisExecutionStatus.Degraded);
```

```csharp
var result = LegalDataEvidenceClassifier.Classify(
    LegalDataToolStatuses.Failed,
    succeededContext);

result.CanUseSignals.Should().BeFalse();
result.FallbackReason.Should().Be(
    LegalCnvFallbackReasons.DataToolFailed);
```

```csharp
var data = PlannerStageSafeResults.DataUnavailable();
var legal = PlannerStageSafeResults.LegalUnavailable();

data.RequiresHumanReview.Should().BeTrue();
data.Severity.Should().Be("Unknown");
data.Evidence.Should().BeEmpty();
legal.RequiresHumanReview.Should().BeTrue();
legal.RiskLevel.Should().Be("Unknown");
legal.Evidence.Should().BeEmpty();
```

- [ ] **Step 2: Run the new tests and verify missing types fail compilation**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~LegalDataEvidenceClassifierTests|FullyQualifiedName~PlannerStageSafeResultsTests" --verbosity minimal
```

Expected: FAIL to compile because evidence/fallback contracts, classifier, safe factories, and Legal review flag do not exist.

- [ ] **Step 3: Add the evidence contracts and exact precedence**

Create the contracts:

```csharp
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Legal.Cnv;

public static class LegalDataToolStatuses
{
    public const string Executed = "executed";
    public const string Failed = "failed";
    public const string Absent = "absent";
}

public static class LegalCnvFallbackReasons
{
    public const string DataToolFailed = "data_tool_failed";
    public const string DataStageAbsent = "data_stage_absent";
    public const string FinancialAnalysisMissing = "financial_analysis_missing";
    public const string SignalsStageFailed = "signals_stage_failed";
    public const string NoSpecificSignals = "no_specific_signals";
    public const string SignalsUnmapped = "signals_unmapped";
    public const string LegacyOrAmbiguousExecution = "legacy_or_ambiguous_execution";
}

public sealed record LegalDataStageFailureAudit(
    string Operation,
    string FailureCode);

public sealed record LegalDataEvidenceContext(
    bool CanUseSignals,
    string? FallbackReason,
    string DataToolStatus,
    FinancialAnalysisExecutionStatus? FinancialAnalysisStatus,
    IReadOnlyList<LegalDataStageFailureAudit> FailedStages);
```

Implement `LegalDataEvidenceClassifier.Classify` in this order:

```csharp
if (dataToolStatus == LegalDataToolStatuses.Failed)
    return Fallback(LegalCnvFallbackReasons.DataToolFailed, dataToolStatus, financialAnalysis);
if (dataToolStatus == LegalDataToolStatuses.Absent)
    return Fallback(LegalCnvFallbackReasons.DataStageAbsent, dataToolStatus, financialAnalysis);
if (financialAnalysis is null)
    return Fallback(LegalCnvFallbackReasons.FinancialAnalysisMissing, dataToolStatus, null);
if (financialAnalysis.Execution.OverallStatus == FinancialAnalysisExecutionStatus.LegacyUnknown)
    return Fallback(LegalCnvFallbackReasons.LegacyOrAmbiguousExecution, dataToolStatus, financialAnalysis);

var signalStages = financialAnalysis.Execution.Stages
    .Where(stage => stage.Operation == FinancialAnalysisOperations.Signals)
    .ToArray();
if (signalStages.Length != 1 ||
    signalStages[0].Status == FinancialAnalysisExecutionStatus.LegacyUnknown)
    return Fallback(LegalCnvFallbackReasons.LegacyOrAmbiguousExecution, dataToolStatus, financialAnalysis);

var signalsStage = signalStages[0];
if (signalsStage.Status != FinancialAnalysisExecutionStatus.Succeeded)
    return Fallback(LegalCnvFallbackReasons.SignalsStageFailed, dataToolStatus, financialAnalysis);
if (financialAnalysis.RiskSignals.Count == 0)
    return Fallback(LegalCnvFallbackReasons.NoSpecificSignals, dataToolStatus, financialAnalysis);

return new LegalDataEvidenceContext(
    true,
    null,
    dataToolStatus,
    financialAnalysis.Execution.OverallStatus,
    MapAllowlistedFailures(financialAnalysis.Execution.Stages));
```

Map unknown stage failure codes to `FinancialAnalysisFailureCodes.UnexpectedFailure`; never copy arbitrary failure text into audit.

- [ ] **Step 4: Add safe aggregate factories and Legal review flag**

Append `bool RequiresHumanReview = false` to `LegalAgentResult`. Create:

```csharp
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;

namespace Orchestration.Application.Agents.Planner.ToolCalling;

public static class PlannerStageSafeResults
{
    public static DataAgentResult DataUnavailable() => new(
        HasAnomaly: false,
        Severity: "Unknown",
        Summary: "El análisis de datos no produjo un resultado utilizable.",
        Engine: "Controlled Tool Executor",
        Evidence: [],
        FinancialAnalysis: null,
        RequiresHumanReview: true);

    public static LegalAgentResult LegalUnavailable() => new(
        HasComplianceRisk: false,
        RiskLevel: "Unknown",
        Summary: "La revisión legal no produjo un resultado utilizable.",
        Engine: "Controlled Tool Executor",
        Evidence: [],
        Warnings:
        [
            "No hubo evidencia legal automatizada utilizable; se requiere revisión legal humana."
        ],
        RequiresHumanReview: true);
}
```

- [ ] **Step 5: Run classifier/safe-result tests and verify they pass**

Run the Step 2 command.

Expected: PASS with exact fallback precedence and no fabricated anomaly/compliance evidence.

- [ ] **Step 6: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Legal backend/Orchestration.Application/Agents/Planner/ToolCalling backend/Orchestration.Tests/Agents/Legal backend/Orchestration.Tests/Agents/Planner/ToolCalling
git commit -m "feat: classify staged data evidence safely"
```

## Task 5: Produce Signal-Only Legal Query Plans

**Files:**

- Create: `backend/Orchestration.Application/Agents/Legal/Cnv/LegalCnvQueryPlan.cs`
- Modify: `backend/Orchestration.Application/Agents/Legal/Cnv/ILegalCnvQueryStrategy.cs`
- Modify: `backend/Orchestration.Application/Agents/Legal/Cnv/FinancialAnalysisLegalCnvQueryStrategy.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/FinancialAnalysisLegalCnvQueryStrategyTests.cs`

- [ ] **Step 1: Rewrite strategy tests around `BuildPlan`**

Ensure test contexts use explicit execution metadata. A succeeded helper uses:

```csharp
private static FinancialAnalysisExecution SucceededExecution() =>
    FinancialAnalysisExecution.FromStages(
        FinancialAnalysisOperations.All.Select(operation =>
            new FinancialAnalysisStageExecution(
                operation,
                FinancialAnalysisExecutionStatus.Succeeded,
                1)));
```

Add these assertions:

```csharp
var plan = _strategy.BuildPlan(context, evidence);

plan.Source.Should().Be(LegalCnvQuerySources.Contextual);
plan.FallbackReason.Should().BeNull();
plan.Queries.Should().HaveCountLessThanOrEqualTo(4);
```

```csharp
var plan = _strategy.BuildPlan(
    contextWithUnsupportedSignal,
    usableEvidence);

plan.Source.Should().Be(LegalCnvQuerySources.Fallback);
plan.FallbackReason.Should().Be(LegalCnvFallbackReasons.SignalsUnmapped);
plan.Queries.Should().ContainSingle();
```

```csharp
var baseline = _strategy.BuildPlan(contextWithLiquiditySignal, usableEvidence);
var warned = _strategy.BuildPlan(
    contextWithLiquiditySignalAndWarnings,
    usableEvidence);

warned.Queries.Should().BeEquivalentTo(
    baseline.Queries,
    options => options.WithStrictOrdering());
```

- [ ] **Step 2: Run strategy tests and verify the old API/quality branch fails**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~FinancialAnalysisLegalCnvQueryStrategyTests" --verbosity minimal
```

Expected: FAIL because `BuildPlan` and plan metadata do not exist, while warnings/limitations can still add queries.

- [ ] **Step 3: Add the query-plan contract and compatible strategy entry point**

Create:

```csharp
namespace Orchestration.Application.Agents.Legal.Cnv;

public static class LegalCnvQuerySources
{
    public const string Contextual = "contextual";
    public const string Fallback = "fallback";
}

public sealed record LegalCnvQueryPlan(
    string StrategyVersion,
    string Source,
    string? FallbackReason,
    LegalDataEvidenceContext DataEvidence,
    IReadOnlyList<LegalCnvQuery> Queries);
```

Add the query-plan method while retaining a temporary compatibility adapter:

```csharp
LegalCnvQueryPlan BuildPlan(
    FinancialAnalysisContext? financialAnalysis,
    LegalDataEvidenceContext dataEvidence);

IReadOnlyList<LegalCnvQuery> BuildQueries(
    FinancialAnalysisContext? financialAnalysis) =>
    BuildPlan(
        financialAnalysis,
        LegalDataEvidenceClassifier.Classify(
            LegalDataToolStatuses.Executed,
            financialAnalysis)).Queries;
```

In `BuildPlan`, return the existing deterministic generic query immediately when
`dataEvidence.CanUseSignals` is false. Otherwise run only the existing
`MapSignalToQueries` loop. Delete the warning/limitation-only branch. If mapped
queries are empty, return the generic query with `signals_unmapped`. Use a stable
strategy version such as `financial_analysis_v2`. Implement the class's legacy
`BuildQueries` method as the same adapter until Task 6 moves the production
caller to `BuildPlan`.

- [ ] **Step 4: Run strategy tests and verify they pass**

Run the Step 2 command.

Expected: PASS; only validated signals drive contextual queries, unsupported signals have an explicit fallback, and ordering/dedup/four-query cap remain intact.

- [ ] **Step 5: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Legal/Cnv backend/Orchestration.Tests/Agents/Legal/FinancialAnalysisLegalCnvQueryStrategyTests.cs
git commit -m "feat: derive auditable legal query plans from data signals"
```

## Task 6: Make Financial Context Resolution Explicit Through Legal Contracts

**Files:**

- Create: `backend/Orchestration.Application/Agents/Legal/LegalReviewContext.cs`
- Create: `backend/Orchestration.Application/Agents/Legal/Regulations/RegulatoryReviewRequest.cs`
- Modify: `backend/Orchestration.Application/Agents/Legal/ILegalAgent.cs`
- Modify: `backend/Orchestration.Application/Agents/Legal/MockLegalAgent.cs`
- Modify: `backend/Orchestration.Application/Agents/Legal/Regulations/IRegulatoryKnowledgeSource.cs`
- Modify: `backend/Orchestration.Application/Agents/Legal/Regulations/RegulatoryReviewResult.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/SemanticKernelLegalAgent.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/LegalCompliancePlugin.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/LegalCompliancePluginResult.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/MockRegulatoryKnowledgeSource.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/McpRegulatoryKnowledgeSource.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/McpRegulatoryKnowledgeSourceTests.cs`

- [ ] **Step 1: Write a failing stale-context isolation test**

Seed a session with persisted financial signals, then call the MCP source with no provided context and `ProvidedOnly`:

```csharp
var request = new RegulatoryReviewRequest(
    Report: CreateReport() with { FinancialAnalysis = null },
    Context: new LegalReviewContext(
        FinancialAnalysisResolutionMode.ProvidedOnly,
        LegalDataEvidenceClassifier.Classify(
            LegalDataToolStatuses.Failed,
            null)));

var result = await source.ReviewAsync(request, CancellationToken.None);

mcpClient.ReceivedRequests.Should().ContainSingle(request =>
    request.Query == "régimen informativo estados financieros emisoras");
mcpClient.ReceivedRequests.Should().NotContain(request =>
    request.Query.Contains("liquidez", StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 2: Run the Legal source tests and verify explicit context APIs are missing**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~McpRegulatoryKnowledgeSourceTests|FullyQualifiedName~SemanticKernelLegalAgentTests" --verbosity minimal
```

Expected: FAIL to compile because explicit resolution/request contracts do not exist.

- [ ] **Step 3: Add review/request contracts and overloads**

Create:

```csharp
using Orchestration.Application.Agents.Legal.Cnv;

namespace Orchestration.Application.Agents.Legal;

public enum FinancialAnalysisResolutionMode
{
    ProvidedOrPersisted,
    ProvidedOnly
}

public sealed record LegalReviewContext(
    FinancialAnalysisResolutionMode ResolutionMode,
    LegalDataEvidenceContext? DataEvidence = null)
{
    public static LegalReviewContext Default { get; } = new(
        FinancialAnalysisResolutionMode.ProvidedOrPersisted);
}
```

```csharp
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Legal.Regulations;

public sealed record RegulatoryReviewRequest(
    FinancialReportContext Report,
    LegalReviewContext Context);
```

Keep the existing `ILegalAgent.ReviewAsync(report, cancellationToken)` method and
add a default interface overload so existing test doubles remain compatible:

```csharp
Task<LegalAgentResult> ReviewAsync(
    FinancialReportContext report,
    LegalReviewContext context,
    CancellationToken cancellationToken) =>
    ReviewAsync(report, cancellationToken);
```

The old Semantic overload delegates with `LegalReviewContext.Default`; its new
overload preserves the supplied context. Apply the same default-interface
pattern to `IRegulatoryKnowledgeSource`, using `RegulatoryReviewRequest` for the
new overload. `McpRegulatoryKnowledgeSource` overrides the request method;
legacy/mock sources may use the safe default delegation.

- [ ] **Step 4: Thread mode/evidence through Semantic Kernel plugin invocation**

Add these kernel arguments:

```csharp
["allowPersistedFinancialAnalysisFallback"] =
    context.ResolutionMode == FinancialAnalysisResolutionMode.ProvidedOrPersisted,
["dataEvidenceJson"] = context.DataEvidence is null
    ? null
    : JsonSerializer.Serialize(context.DataEvidence, JsonOptions)
```

The plugin accepts `bool allowPersistedFinancialAnalysisFallback = true` and
`string? dataEvidenceJson = null`, safely deserializes only
`LegalDataEvidenceContext`, and creates `RegulatoryReviewRequest`.

In `McpRegulatoryKnowledgeSource`, resolve financial analysis exactly as:

```csharp
var financialAnalysis = request.Report.FinancialAnalysis;
if (financialAnalysis is null &&
    request.Context.ResolutionMode == FinancialAnalysisResolutionMode.ProvidedOrPersisted)
{
    financialAnalysis = await TryLoadFinancialAnalysisAsync(
        request.Report.SessionId,
        cancellationToken);
}

var dataEvidence = request.Context.DataEvidence
    ?? LegalDataEvidenceClassifier.Classify(
        LegalDataToolStatuses.Executed,
        financialAnalysis);
var queryPlan = _queryStrategy.BuildPlan(financialAnalysis, dataEvidence);
```

Use `queryPlan.Queries` for retrieval and the existing two-field audit shape in
this commit. Remove the temporary `BuildQueries` method from the interface and
strategy after all production/test callers use `BuildPlan`; Task 7 upgrades the
final audit shape together with per-query outcomes.

- [ ] **Step 5: Add `RequiresHumanReview` through Legal aggregate records**

Append `bool RequiresHumanReview = false` to `RegulatoryReviewResult` and
`LegalCompliancePluginResult`; copy it in plugin and `SemanticKernelLegalAgent`
mapping. This preserves default compatibility for existing constructors.

- [ ] **Step 6: Run focused Legal contract tests and verify they pass**

Run the Step 2 command.

Expected: PASS; `ProvidedOnly` never reloads persisted Data, while legacy/default callers retain current fallback loading.

- [ ] **Step 7: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Legal backend/Orchestration.Infrastructure/Agents/Legal backend/Orchestration.Tests/Agents/Legal
git commit -m "feat: make legal financial context resolution explicit"
```

## Task 7: Aggregate Per-Query Outcomes and Preserve Partial Legal Evidence

**Files:**

- Modify: `backend/Orchestration.Application/Agents/Legal/Cnv/LegalQueryStrategyAudit.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/McpRegulatoryKnowledgeSource.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/McpRegulatoryKnowledgeSourceTests.cs`

- [ ] **Step 1: Write failing partial, total, uncited, activity, and cancellation tests**

Create fakes that succeed once then throw, always throw, return uncited results,
and throw `OperationCanceledException`. Assert:

```csharp
partial.Findings.Should().NotBeEmpty();
partial.RequiresHumanReview.Should().BeTrue();
partialAudit.Queries.Should().Contain(query =>
    query.ExecutionStatus == "failed");
partialAudit.Queries.Should().Contain(query =>
    query.ExecutionStatus == "succeeded" &&
    query.CitedEvidenceCount > 0);
```

```csharp
total.RiskLevel.Should().Be("Unknown");
total.Findings.Should().BeEmpty();
total.RequiresHumanReview.Should().BeTrue();
```

```csharp
var act = () => source.ReviewAsync(request, cancellationToken);
await act.Should().ThrowAsync<OperationCanceledException>();
publisher.PublishedEvents.Should().NotContain(evt =>
    evt.Type is "legal_cnv_queries_derived" or
        "legal_cnv_query_fallback_used");
```

- [ ] **Step 2: Run MCP source tests and verify current catch/audit behavior fails**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~McpRegulatoryKnowledgeSourceTests" --verbosity minimal
```

Expected: FAIL because query outcomes are not recorded, all-query failure is treated as normal Low risk, and broad catches can swallow cancellation.

- [ ] **Step 3: Extend internal search outcome and capture every query**

First replace `LegalQueryStrategyAudit` and its nested query record:

```csharp
public sealed record LegalQueryStrategyAudit(
    string StrategyVersion,
    string Source,
    string? FallbackReason,
    string DataToolStatus,
    FinancialAnalysisExecutionStatus? FinancialAnalysisStatus,
    IReadOnlyList<LegalDataStageFailureAudit> FailedStages,
    IReadOnlyList<LegalCnvQueryAudit> Queries);

public sealed record LegalCnvQueryAudit(
    int Index,
    int Total,
    string Query,
    string? RegulationArea,
    string Reason,
    IReadOnlyList<string> RelatedFinancialSignals,
    string ExecutionStatus,
    int ResultCount,
    int CitedEvidenceCount);
```

Use this internal result shape:

```csharp
private sealed record CnvSearchReviewResult(
    List<RegulatoryFinding> Findings,
    List<LegalEvidenceReference> EvidenceReferences,
    List<string> Warnings,
    List<LegalCnvQueryAudit> QueryAudits,
    int CompletedQueries,
    int FailedQueries);
```

For each indexed query, add one audit entry. Successful requests use the MCP
result count and total citation count; failed requests use zero counts and
`ExecutionStatus = "failed"`. Add this catch ordering around every MCP call and
activity publish:

```csharp
catch (OperationCanceledException)
{
    throw;
}
catch (Exception ex)
{
    _logger.LogWarning(
        ex,
        "CNV MCP search failed for query index {QueryIndex}.",
        queryIndex);
    warnings.Add(
        "La búsqueda en la CNV a través de MCP falló para una consulta.");
}
```

Do not include query text in exception logs.

- [ ] **Step 4: Construct final audit and aggregate status after all queries**

Build `LegalQueryStrategyAudit` from `LegalCnvQueryPlan` plus query audits. Use:

```csharp
var allQueriesFailed = outcome.CompletedQueries == 0 &&
    outcome.FailedQueries > 0;
var requiresHumanReview =
    queryPlan.Source == LegalCnvQuerySources.Fallback ||
    outcome.FailedQueries > 0;

var riskLevel = allQueriesFailed
    ? "Unknown"
    : outcome.Findings.Count > 0 ? "Medium" : "Low";
```

Skip Legal AI review when all queries failed and return a stable not-run reason
such as `cnv_queries_failed`. Publish `legal_cnv_queries_derived` or
`legal_cnv_query_fallback_used` only after the aggregate result is complete.

- [ ] **Step 5: Run MCP source tests and verify they pass**

Run the Step 2 command.

Expected: PASS; partial citations survive, total retrieval failure is Unknown/review-required, uncited success remains normal Low, and cancellation propagates without Legal audit events.

- [ ] **Step 6: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Legal/Cnv/LegalQueryStrategyAudit.cs backend/Orchestration.Infrastructure/Agents/Legal/Regulations/McpRegulatoryKnowledgeSource.cs backend/Orchestration.Tests/Agents/Legal/McpRegulatoryKnowledgeSourceTests.cs
git commit -m "feat: preserve evidence across partial legal failures"
```

## Task 8: Execute Aggregate Legal Review Through the Controlled Executor

**Files:**

- Create: `backend/Orchestration.Application/Agents/Planner/ToolCalling/PlannerToolExecutionContext.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/IControlledToolExecutor.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/ControlledToolExecutor.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/ToolExecutionResultMapper.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ControlledToolExecutorTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ToolExecutionResultMapperTests.cs`
- Modify: test fakes implementing `IControlledToolExecutor` under `backend/Orchestration.Tests`

- [ ] **Step 1: Write failing trusted-context and aggregate-mapper tests**

Use an argument-free approved Legal call and this runtime context:

```csharp
var runtimeContext = new PlannerToolExecutionContext(
    Report: report,
    DataResult: dataResult,
    DataEvidence: LegalDataEvidenceClassifier.Classify(
        LegalDataToolStatuses.Executed,
        dataResult.FinancialAnalysis));

var results = await executor.ExecuteAsync(
    [CreateCall(PlannerToolCatalog.SearchCnvRegulationName, new Dictionary<string, string>())],
    CancellationToken.None,
    runtimeContext);

legalAgent.ReceivedReport!.FinancialAnalysis
    .Should().BeSameAs(dataResult.FinancialAnalysis);
legalAgent.ReceivedContext!.ResolutionMode
    .Should().Be(FinancialAnalysisResolutionMode.ProvidedOnly);
```

Deserialize the output with the real mapper and assert `QueryStrategy`,
`LegalReview`, and `RequiresHumanReview` match the original aggregate result.
Also assert Legal execution without runtime context returns a failed
`ToolExecutionResult` and never calls `ILegalAgent`.

- [ ] **Step 2: Run executor/mapper tests and verify old direct-MCP behavior fails**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~ControlledToolExecutorTests|FullyQualifiedName~ToolExecutionResultMapperTests|FullyQualifiedName~ToolCallingDiagnosticServiceTests" --verbosity minimal
```

Expected: FAIL because executor still requires `query`, calls MCP directly, and mapper expects raw `CnvRegulationSearchResponse`.

- [ ] **Step 3: Add runtime context and compatible executor signature**

Create:

```csharp
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record PlannerToolExecutionContext(
    FinancialReportContext Report,
    DataAgentResult DataResult,
    LegalDataEvidenceContext DataEvidence);
```

Change the interface and all implementations/fakes to:

```csharp
Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
    IReadOnlyList<ApprovedToolCall> calls,
    CancellationToken cancellationToken,
    PlannerToolExecutionContext? runtimeContext = null);
```

Diagnostics continue using null context; dynamic Legal diagnostic execution
fails safely instead of performing an unaudited generic search.

- [ ] **Step 4: Replace direct MCP Legal execution with aggregate `ILegalAgent`**

Replace the executor's MCP dependency with `ILegalAgent`. Implement Legal handling:

```csharp
if (runtimeContext is null)
{
    return Failed(
        call.ToolName,
        "Trusted planner runtime context is required.");
}

var legalReport = runtimeContext.Report with
{
    FinancialAnalysis = runtimeContext.DataResult.FinancialAnalysis
};
var legalResult = await _legalAgent.ReviewAsync(
    legalReport,
    new LegalReviewContext(
        FinancialAnalysisResolutionMode.ProvidedOnly,
        runtimeContext.DataEvidence),
    cancellationToken);

return Succeeded(
    call.ToolName,
    legalResult.Summary,
    legalResult.Engine,
    legalResult);
```

Retain unconditional `OperationCanceledException` propagation.

- [ ] **Step 5: Replace Legal mapper reconstruction with aggregate deserialization**

Use the same successful-call lookup as Data and deserialize directly:

```csharp
return JsonSerializer.Deserialize<LegalAgentResult>(
    call.OutputJson,
    JsonOptions);
```

Return null on malformed JSON. Remove raw CNV response mapping code now owned by `LegalAgent`.

- [ ] **Step 6: Run executor/mapper tests and verify they pass**

Run the Step 2 command.

Expected: PASS; only trusted context enables composite Legal execution, aggregate metadata survives mapping, and diagnostics cannot bypass staging.

- [ ] **Step 7: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Planner/ToolCalling backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling backend/Orchestration.Tests
git commit -m "feat: execute aggregate legal review with data context"
```

## Task 9: Stage Planner Execution Data Before Legal

**Files:**

- Modify: `backend/Orchestration.Application/Agents/Planner/PlannerAgent.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/PlannerAgentTests.cs`

- [ ] **Step 1: Write failing staged-order, final-input, safe-failure, and cancellation tests**

Add tests that use a recording executor. Core assertions:

```csharp
trace.Should().Equal(
    "data_started",
    "data_completed",
    "legal_started",
    "legal_completed",
    "reasoning_started");

result.ToolPlan.ProposedCalls.Select(call => call.ToolName)
    .Should().Equal(
        PlannerToolCatalog.SearchCnvRegulationName,
        PlannerToolCatalog.AnalyzeTransactionsName);
result.ToolPlan.ExecutedCalls.Select(call => call.ToolName)
    .Should().Equal(
        PlannerToolCatalog.AnalyzeTransactionsName,
        PlannerToolCatalog.SearchCnvRegulationName);
```

```csharp
executor.LegalRuntimeContext!.DataResult
    .Should().BeEquivalentTo(mappedDataResult);
reasoning.Input!.DataSummary.Should().Be(mappedDataResult.Summary);
reasoning.Input.LegalSummary.Should().Be(mappedLegalResult.Summary);
```

For Data execution failure, assert Legal still runs once with
`data_tool_failed`, final Data is safe/review-required, and no Data retry occurs.
For absent/rejected Legal, assert no Legal execution and safe Legal output. For
cancellation between stages, assert Legal/reasoning/completion are absent and
`OperationCanceledException` propagates.

- [ ] **Step 2: Run Planner tests and verify flat-batch behavior fails**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~PlannerAgentTests" --verbosity minimal
```

Expected: FAIL because Data and Legal are submitted together, mapping occurs after both, successful stages are rerun on mapping failure, and plan events publish after execution.

- [ ] **Step 3: Split plan preparation from stage execution**

In `RunPlanDrivenModeAsync`, perform normalize/validate/policy once. Preserve
normalized proposed/approved/rejected order. Publish proposal, validation, and
rejection events before execution. Partition policy decisions with:

```csharp
private static bool HasSatisfactionKind(
    ToolExecutionPolicyDecision decision,
    PlannerToolSatisfactionKind kind) =>
    PlannerToolCatalog.Find(decision.Call.ToolName)?.SatisfactionKind == kind;
```

Execute all Data decisions before all Legal decisions; within each stage retain
original relative order. Concatenate stage audits into `ExecutedCalls` in actual
execution order.

- [ ] **Step 4: Map/classify Data before constructing Legal runtime context**

Use this stage resolution:

```csharp
var dataResult = _executionResultMapper.TryMapDataResult(dataAudit)
    ?? PlannerStageSafeResults.DataUnavailable();
var dataToolStatus = dataDecisions.Count == 0
    ? LegalDataToolStatuses.Absent
    : dataAudit.Any(call => call.Succeeded)
        ? LegalDataToolStatuses.Executed
        : LegalDataToolStatuses.Failed;
var dataEvidence = LegalDataEvidenceClassifier.Classify(
    dataToolStatus,
    dataResult.FinancialAnalysis);
var runtimeContext = new PlannerToolExecutionContext(
    report,
    dataResult,
    dataEvidence);
```

Publish each Data execution event immediately, call
`cancellationToken.ThrowIfCancellationRequested()`, then execute approved Legal
decisions with `runtimeContext`.

- [ ] **Step 5: Resolve Legal locally and remove the full deterministic rerun**

Use:

```csharp
var legalResult = _executionResultMapper.TryMapLegalResult(legalAudit)
    ?? PlannerStageSafeResults.LegalUnavailable();
```

Do not call `RunDeterministicAgentsAsync` from plan-driven failure handling.
Publish Legal execution events immediately. Generate reasoning only after both
aggregate results exist. Update `CompletePlannerAsync`:

```csharp
var requiresHumanApproval =
    dataResult.HasAnomaly ||
    dataResult.RequiresHumanReview ||
    legalResult.HasComplianceRisk ||
    legalResult.RequiresHumanReview;
```

- [ ] **Step 6: Preserve proposal metadata in final plan audit**

Construct:

```csharp
var toolPlan = new ToolPlanAuditResult(
    ProposedCalls: normalizedPlan.ProposedCalls,
    ApprovedCalls: validationResult.ApprovedCalls,
    RejectedCalls: validationResult.RejectedCalls,
    ExecutedCalls: [.. dataAudit, .. legalAudit],
    ProposalSource: normalizedPlan.ProposalSource,
    ProposalFallbackReason: normalizedPlan.ProposalFallbackReason);
```

Split current combined event publishing into plan-level validation events and
one per-stage execution-event method. Do not emit completion-related events
after cancellation.

- [ ] **Step 7: Run Planner tests and verify they pass**

Run the Step 2 command.

Expected: PASS with canonical Data-to-Legal temporal order, immutable original call lists, localized safe failures, exact final reasoning inputs, and cancellation boundaries.

- [ ] **Step 8: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Planner/PlannerAgent.cs backend/Orchestration.Tests/Agents/Planner/PlannerAgentTests.cs
git commit -m "feat: stage plan-driven data before legal execution"
```

## Task 10: Persist Additive Audit Metadata and Update Frontend Types

**Files:**

- Modify: `backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs`
- Modify: `backend/Orchestration.Tests/AnalysisSessions/AnalysisOrchestratorContextTests.cs`
- Modify: `frontend/src/types/domain.types.ts`

- [ ] **Step 1: Write failing persistence assertions**

In `AnalysisOrchestratorContextTests`, parse persisted `ContextJson` and assert:

```csharp
root.GetProperty("toolPlan").GetProperty("proposalSource")
    .GetString().Should().Be("deterministic_fallback");
root.GetProperty("toolPlan").GetProperty("proposalFallbackReason")
    .GetString().Should().Be("llm_response_invalid");
root.GetProperty("compliance").GetProperty("requiresHumanReview")
    .GetBoolean().Should().BeTrue();
root.GetProperty("compliance").GetProperty("queryStrategy")
    .GetProperty("fallbackReason").GetString()
    .Should().Be("signals_stage_failed");
root.GetRawText().Should().NotContain("outputJson");
```

- [ ] **Step 2: Run persistence tests and verify additive fields are absent**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~AnalysisOrchestratorContextTests" --verbosity minimal
```

Expected: FAIL because proposal provenance and Legal review-required state are not included in persisted anonymous objects.

- [ ] **Step 3: Add fields to persistence mapping without tool outputs**

Add to `toolPlan`:

```csharp
proposalSource = plannerResult.ToolPlan.ProposalSource,
proposalFallbackReason = plannerResult.ToolPlan.ProposalFallbackReason,
```

Add to `compliance`:

```csharp
requiresHumanReview = plannerResult.LegalResult.RequiresHumanReview,
```

Keep `queryStrategy = plannerResult.LegalResult.QueryStrategy`. Do not add
`ToolExecutionResult.OutputJson` to the executed-call projection.

- [ ] **Step 4: Add optional frontend compatibility fields**

Update TypeScript types:

```typescript
export type ToolPlanContext = {
  proposalSource?: "llm" | "deterministic" | "deterministic_fallback" | null;
  proposalFallbackReason?:
    | "llm_response_invalid"
    | "llm_request_failed"
    | "llm_configuration_failed"
    | null;
  proposedCalls: ProposedToolCallContext[];
  approvedCalls: ApprovedToolCallContext[];
  rejectedCalls: RejectedToolCallContext[];
  executedCalls: ExecutedToolCallContext[];
};
```

Add `requiresHumanReview?: boolean` and `queryStrategy?: unknown` to
`ComplianceContext`. Do not redesign panels in this issue.

- [ ] **Step 5: Run persistence and frontend checks**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~AnalysisOrchestratorContextTests" --verbosity minimal
npm --prefix frontend run build
```

Expected: both commands PASS; persisted audit survives reload and frontend accepts additive fields.

- [ ] **Step 6: Commit**

```powershell
git add backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs backend/Orchestration.Tests/AnalysisSessions/AnalysisOrchestratorContextTests.cs frontend/src/types/domain.types.ts
git commit -m "feat: persist staged planner and legal audit metadata"
```

## Task 11: Prove the Pipeline End to End and Run Full Regression

**Files:**

- Modify: `backend/Orchestration.Tests/AnalysisSessions/ProductionLikeWorkflowE2ETests.cs`

- [ ] **Step 1: Extend plan-driven E2E with contextual, fallback, hostile-order, and cancellation cases**

Use the real Planner, executor, mapper, Data/Legal services, database, and fake CNV MCP client. Assert contextual requests are not `"agentes"` and are tied to emitted signals:

```csharp
mcpClient.ReceivedRequests.Should().HaveCountGreaterThanOrEqualTo(1);
mcpClient.ReceivedRequests.Should().HaveCountLessThanOrEqualTo(4);
mcpClient.ReceivedRequests.Should().Contain(request =>
    request.Query.Contains("liquidez", StringComparison.OrdinalIgnoreCase));
mcpClient.ReceivedRequests.Should().NotContain(request =>
    request.Query == "agentes");
```

Reload the session and assert:

```csharp
activityTypes.Should().ContainInOrder(
    "tool_plan_proposed",
    "tool_plan_validated",
    "tool_call_executed",
    "legal_cnv_queries_derived",
    "tool_call_executed",
    "planner_reasoning_completed",
    "agent_completed");
contextJson.Should().Contain("\"source\":\"contextual\"");
contextJson.Should().NotContain("outputJson");
```

Add a no-usable-signals case asserting one generic request and durable fallback
reason, a Legal-first hostile proposal asserting actual Data-first execution,
and cancellation between stages asserting no Legal/reasoning/completion events
or workflow/approval mutation. Rerun the existing Shadow-mode scenario and
assert its executor remains unused while deterministic Data and Legal agents
still run once each.

- [ ] **Step 2: Run the E2E slice and verify failures before final fixes**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductionLikeWorkflowE2ETests" --verbosity minimal
```

Expected on the first run: FAIL until the E2E fixture supplies succeeded
financial-analysis stage metadata, uses an argument-free Legal call, and
constructs the updated controlled executor with `ILegalAgent`. Make only those
three fixture changes in this test file, then rerun until PASS.

- [ ] **Step 3: Run all focused Issue #6 suites**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~PlannerAgentTests|FullyQualifiedName~DeterministicToolPlanProposalServiceTests|FullyQualifiedName~SemanticKernelToolPlanProposalServiceTests|FullyQualifiedName~ToolPlanNormalizerTests|FullyQualifiedName~ToolPlanValidatorTests" --verbosity minimal
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~ControlledToolExecutorTests|FullyQualifiedName~ToolExecutionResultMapperTests|FullyQualifiedName~FinancialAnalysisLegalCnvQueryStrategyTests|FullyQualifiedName~McpRegulatoryKnowledgeSourceTests" --verbosity minimal
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~AnalysisOrchestratorContextTests|FullyQualifiedName~ProductionLikeWorkflowE2ETests" --verbosity minimal
```

Expected: all focused suites PASS with zero failures.

- [ ] **Step 4: Run full backend, frontend, build, and whitespace verification**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --verbosity minimal
dotnet build backend/Orchestration.slnx --no-restore --verbosity minimal
npm --prefix frontend run build
git diff --check
```

Expected: all commands exit 0; test output reports zero failures; build/typecheck report zero errors; `git diff --check` emits no warnings.

If locked `Orchestration.*.dll` files produce `MSB3026`, `MSB3027`, or access
errors, rerun the same test/build command with a unique temporary output path,
`-m:1 -nr:false`, and the repository's configured Python-home environment. Do
not terminate the user's running application.

- [ ] **Step 5: Review persisted safety invariants**

Inspect the E2E session JSON and activity rows. Confirm:

```text
No OutputJson field
No raw prompt or model response
No uncited Legal snippet in evidence
No workflow/approval event produced by tool execution
Exactly one approved Legal Planner call
One to four deterministic Legal query audit entries
Final reasoning after final Data and Legal results
```

- [ ] **Step 6: Commit E2E coverage and fixture updates**

```powershell
git add backend/Orchestration.Tests/AnalysisSessions/ProductionLikeWorkflowE2ETests.cs
git commit -m "test: cover adaptive staged data to legal execution"
```

## Final Acceptance Checklist

- [ ] Legal query generation receives completed Data `FinancialAnalysisContext`.
- [ ] `Degraded` Data uses contextual retrieval only when signals stage succeeded.
- [ ] Missing/unusable Data uses one generic query with one stable fallback reason.
- [ ] One approved Legal call owns one to four audited deterministic subqueries.
- [ ] Original proposed/approved/rejected call lists remain unchanged after validation.
- [ ] Actual execution/audit order is Data then Legal, independent of LLM order.
- [ ] Partial Legal failure preserves successful cited evidence and forces review.
- [ ] Total stage failures create safe Unknown outputs without fabricated risk.
- [ ] Cancellation propagates and prevents downstream stages/fallback/completion.
- [ ] Planner reasoning receives exact final Data and Legal aggregate outputs.
- [ ] Proposal and Legal fallback provenance persist without `OutputJson`.
- [ ] Workflow and approval state cannot be changed by tool execution.
- [ ] Shadow mode remains behaviorally unchanged.
