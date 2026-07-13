# Persisted Financial Report Context Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Planner's synthetic financial-report values with a validated, session-persisted report summary propagated unchanged to every agent and tool path.

**Architecture:** Every metrics ingestion contract carries a nullable `FinancialReportSummaryInput`; validation normalizes it to a non-null persisted `FinancialReportSummary` stored at `ContextJson.financialReport` beside structured metrics. A single resolver is shared by start preflight and the orchestrator, which passes one `FinancialReportContext` explicitly to Planner; PDF drafts and frontend state preserve the same contract.

**Tech Stack:** .NET 10, C# 13, ASP.NET Core, EF Core JSON context persistence, xUnit, FluentAssertions, React 19, TypeScript 6, Node test runner.

---

### Task 1: Add and validate the report-summary contract

**Files:**
- Create: `backend/Orchestration.Application/Agents/Shared/FinancialReportSummary.cs`
- Create: `backend/Orchestration.Application/Agents/Shared/FinancialReportSummaryValidation.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsInput.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsCsvInput.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/FinancialMetricsValidationResult.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsValidator.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsCsvParser.cs`
- Test: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsValidatorTests.cs`
- Test: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsCsvParserTests.cs`

- [ ] **Step 1: Write failing validation tests**

Add theories for a missing summary, missing individual fields, negative numeric values, default timestamp, valid zero values, and trimmed name:

```csharp
[Fact]
public void Validate_Should_reject_missing_report_summary()
{
    var result = new StructuredFinancialMetricsValidator().Validate(CreateInput());

    result.IsValid.Should().BeFalse();
    result.Errors.Should().ContainSingle(issue =>
        issue.Code == "REPORT_SUMMARY_REQUIRED");
}

[Fact]
public void Validate_Should_normalize_valid_report_summary()
{
    var submittedAt = DateTimeOffset.Parse("2026-07-12T18:30:00Z");
    var input = CreateInput() with
    {
        ReportSummary = new FinancialReportSummaryInput(
            "  balance-sheet-2025.pdf  ", 0m, 0, submittedAt)
    };

    var result = new StructuredFinancialMetricsValidator().Validate(input);

    result.ReportSummary.Should().Be(new FinancialReportSummary(
        "balance-sheet-2025.pdf", 0m, 0, submittedAt));
}
```

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~StructuredFinancialMetricsValidatorTests|FullyQualifiedName~StructuredFinancialMetricsCsvParserTests"
```

Expected: compile failures because report-summary contracts and validation result do not exist.

- [ ] **Step 3: Implement contracts and validation**

```csharp
public sealed record FinancialReportSummaryInput(
    string? ReportName,
    decimal? TotalAmount,
    int? TransactionCount,
    DateTimeOffset? SubmittedAt);

public sealed record FinancialReportSummary(
    string ReportName,
    decimal TotalAmount,
    int TransactionCount,
    DateTimeOffset SubmittedAt);

public sealed record FinancialReportSummaryValidation(
    FinancialReportSummary? Summary,
    IReadOnlyList<FinancialReportSummaryValidationIssue> Errors)
{
    public bool IsValid => Summary is not null && Errors.Count == 0;
}

public sealed record FinancialReportSummaryValidationIssue(
    string Code,
    string Message);
```

Add `FinancialReportSummaryInput? ReportSummary = null` as the trailing parameter of both input records. Add `FinancialReportSummary? ReportSummary = null` to `FinancialMetricsValidationResult`. Normalize `ReportName.Trim()` and emit exactly:

```text
REPORT_SUMMARY_REQUIRED
REPORT_NAME_REQUIRED
TOTAL_AMOUNT_REQUIRED
TOTAL_AMOUNT_INVALID
TRANSACTION_COUNT_REQUIRED
TRANSACTION_COUNT_INVALID
SUBMITTED_AT_REQUIRED
SUBMITTED_AT_INVALID
```

Implement one pure `FinancialReportSummaryValidator.Validate` method returning this result. `StructuredFinancialMetricsValidator` maps its issues into `FinancialMetricsValidationIssue`; the later persisted-context resolver reuses the same method. The CSV parser copies `input.ReportSummary` into its generated `StructuredFinancialMetricsInput`.

- [ ] **Step 4: Run tests and verify GREEN**

Use the Step 2 command. Expected: selected tests pass.

- [ ] **Step 5: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Shared/FinancialReportSummary.cs backend/Orchestration.Application/Agents/Shared/FinancialReportSummaryValidation.cs backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsValidatorTests.cs backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsCsvParserTests.cs
git commit -m "Add validated financial report summary contract"
```

### Task 2: Persist summary atomically and resolve report context

**Files:**
- Create: `backend/Orchestration.Application/Agents/Shared/FinancialReportContextResolution.cs`
- Create: `backend/Orchestration.Application/Agents/Shared/IFinancialReportContextResolver.cs`
- Create: `backend/Orchestration.Application/Agents/Shared/FinancialReportContextResolver.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/FinancialMetricsSessionSaveResult.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/IStructuredFinancialMetricsSessionService.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsSessionService.cs`
- Test: `backend/Orchestration.Tests/Agents/Shared/FinancialReportContextResolverTests.cs`
- Test: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsSessionServiceTests.cs`

- [ ] **Step 1: Write failing persistence and resolver tests**

```csharp
[Fact]
public async Task SaveAsync_Should_persist_summary_and_metrics_atomically()
{
    var submittedAt = DateTimeOffset.Parse("2026-07-12T18:30:00Z");
    var session = await AddSessionAsync("""{"tenantMarker":"keep"}""");

    var result = await CreateService().SaveAsync(
        session.Id,
        CreateInput(new FinancialReportSummaryInput(
            "report-2025", 842350.75m, 187, submittedAt)),
        CancellationToken.None);

    result!.ReportSummary.Should().Be(new FinancialReportSummary(
        "report-2025", 842350.75m, 187, submittedAt));
    session.ContextJson.Should().Contain("tenantMarker");
    session.ContextJson.Should().Contain("financialReport");
    session.ContextJson.Should().Contain("structuredFinancialMetrics");
}

[Fact]
public void Resolve_Should_return_exact_persisted_values()
{
    var session = CreateSessionWithContext("""
        {"financialReport":{"reportName":"r-1","totalAmount":12.5,
        "transactionCount":3,"submittedAt":"2026-07-12T18:30:00Z"}}
        """);

    var result = new FinancialReportContextResolver().Resolve(session);

    result.IsValid.Should().BeTrue();
    result.Report!.SessionId.Should().Be(session.Id);
    result.Report.ReportName.Should().Be("r-1");
    result.Report.TotalAmount.Should().Be(12.5m);
    result.Report.TransactionCount.Should().Be(3);
}
```

Also assert invalid input leaves previous `ContextJson` byte-for-byte unchanged, unrelated root properties survive, malformed/missing persisted summary returns typed codes, and two sessions never cross-read values.

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~FinancialReportContextResolverTests|FullyQualifiedName~StructuredFinancialMetricsSessionServiceTests"
```

Expected: compile failures for resolver/result types and failing persistence assertions.

- [ ] **Step 3: Implement atomic merge and resolver**

```csharp
public sealed record FinancialReportContextResolution(
    bool IsValid,
    FinancialReportContext? Report,
    string? ErrorCode,
    string? ErrorMessage);

public interface IFinancialReportContextResolver
{
    FinancialReportContextResolution Resolve(AnalysisSession session);
}
```

`StructuredFinancialMetricsSessionService` performs one context update after all validation succeeds:

```csharp
var root = ParseRoot(session.ContextJson);
root["financialReport"] = JsonSerializer.SerializeToNode(
    validationResult.ReportSummary, JsonOptions);
root["structuredFinancialMetrics"] = JsonSerializer.SerializeToNode(
    context, JsonOptions);
session.SetContext(root.ToJsonString(JsonOptions));
```

Extend `FinancialMetricsSessionSaveResult` with trailing `FinancialReportSummary? ReportSummary = null`, and add `GetReportSummaryAsync(Guid, CancellationToken)`. Resolver parses `financialReport`, calls `FinancialReportSummaryValidator.Validate`, returns `FINANCIAL_REPORT_SUMMARY_REQUIRED` when absent and `FINANCIAL_REPORT_SUMMARY_INVALID` for malformed/invalid values, then constructs `FinancialReportContext` using `session.Id` plus persisted fields.

- [ ] **Step 4: Run tests and verify GREEN**

Use Step 2 command. Expected: selected tests pass.

- [ ] **Step 5: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Shared backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input backend/Orchestration.Tests/Agents/Shared backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsSessionServiceTests.cs
git commit -m "Persist and resolve financial report context"
```

### Task 3: Carry summary through JSON, CSV, and multipart APIs

**Files:**
- Modify: `backend/Orchestration.Api/Controllers/AnalysisSessionController.cs`
- Test: `backend/Orchestration.Tests/Api/AnalysisSessionFinancialMetricsControllerTests.cs`
- Test: `backend/Orchestration.Tests/Api/AnalysisSessionControllerIsolationTests.cs`

- [ ] **Step 1: Write failing API round-trip tests**

Add JSON paste, CSV paste, JSON file, and CSV file tests using `842350.75m`, `187`, and `2026-07-12T18:30:00Z`. Assert save and GET responses return exact normalized values. Add a two-user test proving User A cannot read or overwrite User B's summary.

```csharp
response.ReportSummary.Should().BeEquivalentTo(new
{
    ReportName = "balance-sheet-2025.pdf",
    TotalAmount = 842350.75m,
    TransactionCount = 187,
    SubmittedAt = DateTimeOffset.Parse("2026-07-12T18:30:00Z")
});
```

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~AnalysisSessionFinancialMetricsControllerTests|FullyQualifiedName~AnalysisSessionControllerIsolationTests"
```

Expected: response/form contracts lack summary fields and file routes drop them.

- [ ] **Step 3: Implement API propagation**

Add these nullable properties to `StructuredFinancialMetricsFileUploadRequest`:

```csharp
public string? ReportName { get; init; }
public decimal? TotalAmount { get; init; }
public int? TransactionCount { get; init; }
public DateTimeOffset? SubmittedAt { get; init; }
```

Use one helper for CSV/PDF multipart paths:

```csharp
private static FinancialReportSummaryInput CreateReportSummary(
    StructuredFinancialMetricsFileUploadRequest request) =>
    new(request.ReportName, request.TotalAmount,
        request.TransactionCount, request.SubmittedAt);
```

JSON files use their embedded `reportSummary` and never form fallback. Add trailing `FinancialReportSummary? ReportSummary = null` to save/file/GET response records and return normalized persisted values.

- [ ] **Step 4: Run tests and verify GREEN**

Use Step 2 command. Expected: selected API/isolation tests pass.

- [ ] **Step 5: Commit**

```powershell
git add backend/Orchestration.Api/Controllers/AnalysisSessionController.cs backend/Orchestration.Tests/Api/AnalysisSessionFinancialMetricsControllerTests.cs backend/Orchestration.Tests/Api/AnalysisSessionControllerIsolationTests.cs
git commit -m "Accept report summary on ingestion APIs"
```

### Task 4: Block invalid starts and pass one resolved report to Planner

**Files:**
- Modify: `backend/Orchestration.Api/Program.cs`
- Modify: `backend/Orchestration.Application/AnalysisSessions/AnalysisSessionStartPreflightValidator.cs`
- Modify: `backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/IPlannerAgent.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/PlannerAgent.cs`
- Test: `backend/Orchestration.Tests/AnalysisSessions/AnalysisSessionStartPreflightValidatorTests.cs`
- Test: `backend/Orchestration.Tests/AnalysisSessions/AnalysisOrchestratorContextTests.cs`
- Test: `backend/Orchestration.Tests/Agents/Planner/PlannerAgentTests.cs`

- [ ] **Step 1: Write failing preflight/orchestration/propagation tests**

```csharp
[Fact]
public async Task ValidateAsync_Should_block_missing_report_summary()
{
    var result = await CreateValidator().ValidateAsync(
        AnalysisSession.Create(TestUserId), CancellationToken.None);

    result.CanStart.Should().BeFalse();
    result.Errors.Should().ContainSingle(issue =>
        issue.Code == "FINANCIAL_REPORT_SUMMARY_REQUIRED");
}

[Fact]
public async Task StartAnalysisAsync_Should_not_invoke_planner_when_summary_is_invalid()
{
    var planner = new CapturingPlannerAgent();
    var session = await AddSessionAsync("""{"financialReport":{}}""");

    var result = await CreateOrchestrator(planner).StartAnalysisAsync(
        session.Id, CancellationToken.None);

    result!.Status.Should().Be("Failed");
    planner.ReceivedReport.Should().BeNull();
}
```

Update Planner tests so DataAgent, LegalAgent, reasoning service, and proposal service capture their report/input. Assert exact equality of all five persisted fields and that financial-analysis enrichment changes only `FinancialAnalysis`.

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~AnalysisSessionStartPreflightValidatorTests|FullyQualifiedName~AnalysisOrchestratorContextTests|FullyQualifiedName~PlannerAgentTests"
```

Expected: missing summaries are allowed, orchestrator invokes Planner, and Planner still constructs demo values.

- [ ] **Step 3: Implement shared resolution boundary**

Register resolver:

```csharp
builder.Services.AddScoped<IFinancialReportContextResolver,
    FinancialReportContextResolver>();
```

Preflight maps resolver failures to `FINANCIAL_REPORT_SUMMARY_REQUIRED` or `FINANCIAL_REPORT_SUMMARY_INVALID` independently of DataAgent feature flags. Orchestrator resolves before workflow transition; on failure it publishes `financial_report_context_invalid`, calls `session.MarkFailed(errorMessage)`, saves, and returns without Planner.

Change Planner contract:

```csharp
public interface IPlannerAgent
{
    Task<PlannerAgentResult> RunAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken);
}
```

Pass the resolved report from orchestrator. Remove `PlannerAgent.BuildReportContext`; derive `sessionId` from `report.SessionId`. Keep:

```csharp
var legalReport = dataResult.FinancialAnalysis is null
    ? report
    : report with { FinancialAnalysis = dataResult.FinancialAnalysis };
```

- [ ] **Step 4: Run tests and verify GREEN**

Use Step 2 command. Expected: selected tests pass.

- [ ] **Step 5: Commit**

```powershell
git add backend/Orchestration.Api/Program.cs backend/Orchestration.Application/AnalysisSessions backend/Orchestration.Application/Agents/Planner backend/Orchestration.Tests/AnalysisSessions backend/Orchestration.Tests/Agents/Planner/PlannerAgentTests.cs
git commit -m "Resolve persisted report before Planner execution"
```

### Task 5: Preserve persisted timestamp through tool planning and execution

**Files:**
- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/ToolPlanProposalInput.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/DeterministicToolPlanProposalService.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/PlannerAgent.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/SemanticKernelToolPlanProposalService.cs`
- Test: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/DeterministicToolPlanProposalServiceTests.cs`
- Test: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/SemanticKernelToolPlanProposalServiceTests.cs`
- Test: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ControlledToolExecutorTests.cs`

- [ ] **Step 1: Write failing timestamp traceability tests**

```csharp
[Fact]
public async Task ProposeAsync_Should_use_input_submitted_at_without_clock_substitution()
{
    var submittedAt = DateTimeOffset.Parse("2024-02-03T04:05:06Z");
    var plan = await CreateService().ProposeAsync(
        CreateInput(submittedAt), CancellationToken.None);

    plan.ProposedCalls.Single(call =>
        call.ToolName == PlannerToolCatalog.AnalyzeTransactionsName)
        .Arguments["submittedAt"].Should().Be("2024-02-03T04:05:06.0000000+00:00");
}
```

Assert Semantic Kernel prompt includes the same value and controlled executor reconstructs the exact report.

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~DeterministicToolPlanProposalServiceTests|FullyQualifiedName~SemanticKernelToolPlanProposalServiceTests|FullyQualifiedName~ControlledToolExecutorTests"
```

Expected: `ToolPlanProposalInput` has no timestamp and deterministic proposal uses `UtcNow`.

- [ ] **Step 3: Implement timestamp propagation**

Add `DateTimeOffset SubmittedAt` after `TransactionCount` in `ToolPlanProposalInput`. Both Planner input builders pass `report.SubmittedAt`. Deterministic proposal uses:

```csharp
["submittedAt"] = input.SubmittedAt.ToString(
    "O", CultureInfo.InvariantCulture)
```

Semantic prompt serializes `submittedAt = input.SubmittedAt.ToString("O", CultureInfo.InvariantCulture)`.

- [ ] **Step 4: Run tests and verify GREEN**

Use Step 2 command. Expected: selected tool tests pass.

- [ ] **Step 5: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Planner backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling backend/Orchestration.Tests/Agents/Planner/ToolCalling
git commit -m "Propagate persisted report timestamp through tools"
```

### Task 6: Preserve summary through accepted and reviewed PDFs

**Files:**
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Extraction/StructuredFinancialMetricsPdfIngestionModels.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Extraction/StructuredFinancialMetricsPdfIngestionService.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Extraction/FinancialMetricsExtractionDraftModels.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Extraction/FinancialMetricsExtractionDraftService.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Extraction/IFinancialMetricsExtractionDraftService.cs`
- Modify: `backend/Orchestration.Api/Controllers/AnalysisSessionController.cs`
- Test: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsPdfIngestionServiceTests.cs`
- Test: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/FinancialMetricsExtractionDraftServiceTests.cs`
- Test: `backend/Orchestration.Tests/Api/AnalysisSessionFinancialMetricsControllerTests.cs`

- [ ] **Step 1: Write failing accepted/review/v1 tests**

Cover accepted PDF exact persistence, review-required draft payload schema 2, operator correction at confirmation, and schema-1 draft confirmation without summary returning `FINANCIAL_REPORT_SUMMARY_REQUIRED`.

```csharp
confirmed.Draft!.Payload.SchemaVersion.Should().Be(2);
confirmed.Draft.Payload.ProposedInput.ReportSummary.Should().BeEquivalentTo(
    correctedSummary);
saveService.ReceivedRequest!.Input.ReportSummary.Should().BeEquivalentTo(
    correctedSummary);
```

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~StructuredFinancialMetricsPdfIngestionServiceTests|FullyQualifiedName~FinancialMetricsExtractionDraftServiceTests|FullyQualifiedName~AnalysisSessionFinancialMetricsControllerTests"
```

Expected: PDF request/draft/confirmation contracts drop summary and schema remains 1.

- [ ] **Step 3: Implement PDF draft contract version 2**

Add `FinancialReportSummaryInput? ReportSummary` to the PDF ingestion request. Store it in `FinancialMetricsExtractionDraftPayload.ProposedInput.ReportSummary`, then set:

```csharp
public const int CurrentSchemaVersion = 2;
```

Add confirmation contract:

```csharp
public sealed record ConfirmFinancialMetricsExtractionDraftRequest(
    FinancialReportSummaryInput? ReportSummary);
```

Change `ConfirmAsync` and controller endpoint to accept it. Version 2 draft pre-populates summary; confirmation replaces `ProposedInput.ReportSummary` with the validated operator value before one staged save. Version 1 remains deserializable with null summary and fails confirmation safely when remediation is omitted. Do not add an EF migration.

- [ ] **Step 4: Run tests and verify GREEN**

Use Step 2 command. Expected: selected PDF/draft/controller tests pass.

- [ ] **Step 5: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Extraction backend/Orchestration.Api/Controllers/AnalysisSessionController.cs backend/Orchestration.Tests/Agents/Data/FinancialAnalysis backend/Orchestration.Tests/Api/AnalysisSessionFinancialMetricsControllerTests.cs
git commit -m "Persist report summary through PDF review"
```

### Task 7: Add shared frontend summary state and review remediation

**Files:**
- Create: `frontend/src/utils/financialReportSummary.ts`
- Create: `frontend/tests/financialReportSummary.test.ts`
- Modify: `frontend/src/types/domain.types.ts`
- Modify: `frontend/src/services/api.ts`
- Modify: `frontend/src/hooks/useAnalysisSession.ts`
- Modify: `frontend/src/components/StructuredFinancialMetricsPanel.tsx`
- Modify: `frontend/src/components/FinancialMetricsReviewPanel.tsx`
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/App.css`
- Modify: `frontend/tests/financialMetricsReview.test.ts`

- [ ] **Step 1: Write failing pure validation/state tests**

```typescript
test("validates and normalizes an explicit report summary", () => {
  const result = validateFinancialReportSummary({
    reportName: "  balance-sheet-2025.pdf  ",
    totalAmount: "842350.75",
    transactionCount: "187",
    submittedAt: "2026-07-12T18:30:00Z",
  });

  assert.deepEqual(result, {
    ok: true,
    value: {
      reportName: "balance-sheet-2025.pdf",
      totalAmount: 842350.75,
      transactionCount: 187,
      submittedAt: "2026-07-12T18:30:00.000Z",
    },
  });
});

test("rejects missing and negative report fields", () => {
  const result = validateFinancialReportSummary({
    reportName: "",
    totalAmount: "-1",
    transactionCount: "-2",
    submittedAt: "",
  });

  assert.equal(result.ok, false);
  assert.deepEqual(result.errors.map(error => error.code), [
    "REPORT_NAME_REQUIRED",
    "TOTAL_AMOUNT_INVALID",
    "TRANSACTION_COUNT_INVALID",
    "SUBMITTED_AT_REQUIRED",
  ]);
});
```

Extend review utility tests so schema-1 drafts require manual summary and schema-2 drafts pre-populate it.

- [ ] **Step 2: Run tests and verify RED**

```powershell
node --test frontend/tests/financialReportSummary.test.ts frontend/tests/financialMetricsReview.test.ts
```

Expected: module/types and review behavior do not exist.

- [ ] **Step 3: Implement types, state, UI, and API payloads**

Add types:

```typescript
export type FinancialReportSummaryInput = {
  reportName: string;
  totalAmount: number;
  transactionCount: number;
  submittedAt: string;
};
```

`StructuredFinancialMetricsPanel` owns one summary form above the mode selector and attaches its validated value to JSON/CSV payloads or multipart metadata. File upload appends:

```typescript
formData.append("ReportName", metadata.reportSummary.reportName);
formData.append("TotalAmount", String(metadata.reportSummary.totalAmount));
formData.append("TransactionCount", String(metadata.reportSummary.transactionCount));
formData.append("SubmittedAt", metadata.reportSummary.submittedAt);
```

Review UI reads draft summary when present, requires explicit fields for schema 1, and sends `ConfirmFinancialMetricsExtractionDraftRequest`. Session changes reset/reload summary state. Display backend save/preflight errors without converting them to successful readiness.

- [ ] **Step 4: Run tests and build**

```powershell
node --test frontend/tests/*.test.ts
npm run build --prefix frontend
```

Expected: all frontend tests pass and TypeScript/Vite build exits 0.

- [ ] **Step 5: Commit**

```powershell
git add frontend/src frontend/tests
git commit -m "Collect explicit financial report summary"
```

### Task 8: Complete isolation, E2E, documentation, and verification

**Files:**
- Modify: `backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs`
- Modify: `backend/Orchestration.Tests/AnalysisSessions/ProductionLikeWorkflowE2ETests.cs`
- Modify: `backend/Orchestration.Tests/AnalysisSessions/AnalysisOrchestratorContextTests.cs`
- Modify: `frontend/public/templates/structured-financial-metrics-sample.json`
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-12-persisted-financial-report-context.md`

- [ ] **Step 1: Write failing production-like isolation test**

Create two sessions with distinct summaries and metrics. Run both workflows and assert each final `ContextJson.financialReport`, Data/Legal evidence, and tool audit retains only its own five values.

```csharp
firstContext["financialReport"]!["reportName"]!.GetValue<string>()
    .Should().Be("session-one-report");
secondContext["financialReport"]!["reportName"]!.GetValue<string>()
    .Should().Be("session-two-report");
firstContext.ToJsonString().Should().NotContain("session-two-report");
secondContext.ToJsonString().Should().NotContain("session-one-report");
```

- [ ] **Step 2: Run E2E test and verify RED**

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductionLikeWorkflowE2ETests"
```

Expected: old fixtures omit summary or final propagation assertions fail.

- [ ] **Step 3: Update fixtures and documentation**

Seed explicit summary values in production-like fixtures. Extend README JSON/CSV/file examples with `reportSummary`, document stable missing/invalid codes, and state that Planner never derives or defaults report values.

- [ ] **Step 4: Run complete verification**

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --verbosity minimal
dotnet build backend/Orchestration.slnx --no-restore --verbosity minimal
node --test frontend/tests/*.test.ts
npm run build --prefix frontend
git diff --check
rg -n "TotalAmount: 125000m|TransactionCount: 42|financial-report-\{session\.Id\}|DateTimeOffset\.UtcNow\.ToString\(\"O\"" backend/Orchestration.Application backend/Orchestration.Infrastructure
```

Expected: all commands exit 0; residue scan returns no production matches.

- [x] **Step 5: Record issue evidence and commit**

Comment on issue #3 with test counts, build results, branch, propagation proof, and residue scan. Keep issue open until PR integration.

```powershell
git add README.md backend/Orchestration.Tests/AnalysisSessions docs/superpowers/plans/2026-07-12-persisted-financial-report-context.md
git commit -m "Document persisted report context verification"
```

#### Completion evidence (2026-07-12)

- RED: focused production-like/context run first failed compilation for the new
  parameterized fixture/assertion helpers, then failed 2 of 15 tests because
  final orchestration context dropped the persisted `financialReport` root.
- GREEN: the focused filter passed 15 of 15 after the context merge preserved
  both `financialReport` and `structuredFinancialMetrics`.
- `dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --verbosity minimal`
  passed 1116 of 1116 tests (0 failed, 0 skipped).
- `dotnet build backend/Orchestration.slnx --no-restore --verbosity minimal`
  completed with 0 warnings and 0 errors.
- `node --test frontend/tests/*.test.ts` passed 24 of 24 tests.
- `npm run build --prefix frontend` completed TypeScript and Vite production
  builds successfully (60 modules transformed).
- `git diff --check` exited 0.
- Production residue scan for `TotalAmount: 125000m`, `TransactionCount: 42`,
  `financial-report-{session.Id}`, and runtime `UtcNow` tool-input formatting
  returned 0 matches in Application and Infrastructure.
- Final evidence was recorded on GitHub issue #3:
  `https://github.com/EzeMartino/ai-orchestration-hitl/issues/3#issuecomment-4953679554`.
