# Planner Submission Timestamp Integrity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Guarantee that every controlled Data-tool call uses the exact persisted report submission timestamp and that missing source timestamps fail with specific, auditable errors.

**Architecture:** Keep `FinancialReportContext.SubmittedAt` as the sole source of truth. Canonicalize the timestamp in parsed Semantic Kernel proposals before validation, preserve the existing deterministic direct mapping, and propagate timestamp-specific validation codes through preflight, API audit, and the defensive direct-orchestration path. Prove the complete path with focused tests and one production-like plan-driven E2E case.

**Tech Stack:** .NET 10, C# records, Semantic Kernel, `Microsoft.Extensions.Logging`, xUnit, FluentAssertions, Entity Framework Core in-memory tests.

---

## Scope and Existing Behavior

PR #18 already added `SubmittedAt` to `ToolPlanProposalInput`, both Planner proposal builders, the deterministic proposal, the Semantic Kernel prompt, persisted context, and controlled execution. Do not repeat that work.

This plan closes the remaining gaps:

1. A valid LLM response can currently replace the timestamp with any other parseable value.
2. Missing or invalid persisted timestamps are collapsed into `FINANCIAL_REPORT_SUMMARY_INVALID`; the API audit then hides the cause behind a generic structured-metrics message.
3. Existing E2E coverage proves the deterministic path, not LLM proposal canonicalization through execution.

Do not change activity-event timestamps. Their `DateTimeOffset.UtcNow` values represent event occurrence time and are unrelated to report submission evidence.

---

### Task 1: Canonicalize LLM Data-tool timestamps and log corrections safely

**Files:**
- Modify: `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/SemanticKernelToolPlanProposalService.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/SemanticKernelToolPlanProposalServiceTests.cs`
- Verify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ToolPlanProposalRegistrationTests.cs`

- [ ] **Step 1: Add failing canonicalization cases**

Add logging imports and a theory that feeds the real response parser Data calls with missing, mismatched, malformed, alternate-key, and duplicate timestamp arguments:

```csharp
using Microsoft.Extensions.Logging;

public static TheoryData<string, string> UntrustedSubmittedAtCases => new()
{
    { "{}", "missing" },
    { "{\"submittedAt\":\"2030-01-01T00:00:00Z\"}", "mismatch" },
    { "{\"submittedAt\":\"not-a-date\"}", "malformed" },
    { "{\" SubmittedAt \":\"2024-02-03T04:05:06.0000000+00:00\"}", "mismatch" },
    { "{\"submittedAt\":\"2030-01-01T00:00:00Z\",\"SubmittedAt\":\"2031-01-01T00:00:00Z\"}", "duplicate" }
};

[Theory]
[MemberData(nameof(UntrustedSubmittedAtCases))]
public async Task ProposeAsync_Should_canonicalize_untrusted_data_timestamp(
    string argumentsJson,
    string expectedReason)
{
    var logger = new CapturingLogger<SemanticKernelToolPlanProposalService>();
    var input = CreateInput();
    var service = CreateService(
        toolCallingEnabled: true,
        new FakeChatCompletionService(CreateDataPlan(argumentsJson)),
        logger);

    var result = await service.ProposeAsync(input, CancellationToken.None);

    var dataCall = result.ProposedCalls.Should().ContainSingle().Subject;
    dataCall.Arguments.Should().ContainSingle(pair =>
        pair.Key == "submittedAt" &&
        pair.Value == input.SubmittedAt.ToString("O", CultureInfo.InvariantCulture));
    dataCall.Arguments.Keys.Count(key =>
        string.Equals(key.Trim(), "submittedAt", StringComparison.OrdinalIgnoreCase))
        .Should().Be(1);
    var entry = logger.Entries.Should().ContainSingle().Subject;
    entry.Properties["SessionId"].Should().Be(input.SessionId);
    entry.Properties["ToolName"].Should().Be(PlannerToolCatalog.AnalyzeTransactionsName);
    entry.Properties["FailureCode"].Should().Be("TOOL_PLAN_SUBMITTED_AT_CANONICALIZED");
    entry.Properties["Reason"].Should().Be(expectedReason);
    entry.Properties.Keys.Should().BeEquivalentTo(
        ["SessionId", "ToolName", "FailureCode", "Reason"]);
    entry.Properties.Values.Select(value => value?.ToString()).Should().NotContain(value =>
        value?.Contains("2030", StringComparison.Ordinal) == true ||
        value?.Contains("2031", StringComparison.Ordinal) == true ||
        value?.Contains("not-a-date", StringComparison.Ordinal) == true);
    entry.Message.Should().NotContain("2030").And.NotContain("2031").And.NotContain("not-a-date");
}
```

Add helpers that generate valid parser input and capture structured state without introducing a shared test utility that could conflict with other branches:

```csharp
private static string CreateDataPlan(string argumentsJson)
{
    return $$"""
    {
      "proposedCalls": [
        {
          "toolName": "data.analyze_transactions",
          "arguments": {{argumentsJson}},
          "reason": "Analizar evidencia financiera."
        }
      ]
    }
    """;
}

private sealed record CapturedLog(
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> Properties);

private sealed class CapturingLogger<T> : ILogger<T>
{
    public List<CapturedLog> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> values
            ? values.Where(pair => pair.Key != "{OriginalFormat}")
                .ToDictionary(pair => pair.Key, pair => pair.Value)
            : new Dictionary<string, object?>();
        Entries.Add(new CapturedLog(logLevel, formatter(state, exception), properties));
    }
}
```

Extend both `CreateService` overloads with a final optional logger parameter:

```csharp
private static SemanticKernelToolPlanProposalService CreateService(
    bool toolCallingEnabled,
    string chatContent,
    ILogger<SemanticKernelToolPlanProposalService>? logger = null)

private static SemanticKernelToolPlanProposalService CreateService(
    bool toolCallingEnabled,
    FakeChatCompletionService chatCompletionService,
    ILogger<SemanticKernelToolPlanProposalService>? logger = null)
```

The string overload forwards the logger. The service overload passes
`logger ?? NullLogger<SemanticKernelToolPlanProposalService>.Instance` as the
new constructor's last argument. Add `using System.Globalization;` and
`using Microsoft.Extensions.Logging.Abstractions;` to the test file.

- [ ] **Step 2: Add failing exact, multiple-call, and fallback controls**

Add controls proving no corrective log for an already canonical value, every Data call is corrected, Legal arguments remain untouched, and deterministic fallback uses the same input:

```csharp
[Fact]
public async Task ProposeAsync_Should_leave_exact_timestamp_without_correction_log()
{
    var logger = new CapturingLogger<SemanticKernelToolPlanProposalService>();
    var canonical = SubmittedAt.ToString("O", CultureInfo.InvariantCulture);
    var service = CreateService(
        true,
        new FakeChatCompletionService(CreateDataPlan(
            $$"""{"submittedAt":"{{canonical}}"}""")),
        logger);

    var result = await service.ProposeAsync(CreateInput(), CancellationToken.None);

    result.ProposedCalls.Single().Arguments["submittedAt"].Should().Be(canonical);
    logger.Entries.Should().BeEmpty();
}

[Fact]
public async Task ProposeAsync_Should_canonicalize_each_data_call_and_leave_legal_call_unchanged()
{
    var canonical = SubmittedAt.ToString("O", CultureInfo.InvariantCulture);
    var logger = new CapturingLogger<SemanticKernelToolPlanProposalService>();
    var service = CreateService(true, """
    {
      "proposedCalls": [
        {
          "toolName": " DATA.ANALYZE_TRANSACTIONS ",
          "arguments": { "submittedAt": "2030-01-01T00:00:00Z" },
          "reason": "Analizar datos."
        },
        {
          "toolName": "data.analyze_transactions",
          "arguments": {},
          "reason": "Verificar datos."
        },
        {
          "toolName": "legal.search_cnv_regulation",
          "arguments": { "query": "submittedAt 2030" },
          "reason": "Recuperar normativa."
        }
      ]
    }
    """, logger);

    var result = await service.ProposeAsync(CreateInput(), CancellationToken.None);

    result.ProposedCalls.Where(call =>
        string.Equals(call.ToolName.Trim(), PlannerToolCatalog.AnalyzeTransactionsName,
            StringComparison.OrdinalIgnoreCase))
        .Should().OnlyContain(call => call.Arguments["submittedAt"] == canonical);
    result.ProposedCalls.Single(call => call.ToolName == PlannerToolCatalog.SearchCnvRegulationName)
        .Arguments["query"].Should().Be("submittedAt 2030");
    logger.Entries.Should().HaveCount(2);
}

[Fact]
public async Task ProposeAsync_Should_leave_legal_only_plan_unchanged_without_log()
{
    var logger = new CapturingLogger<SemanticKernelToolPlanProposalService>();
    var service = CreateService(true, """
    {
      "proposedCalls": [
        {
          "toolName": "legal.search_cnv_regulation",
          "arguments": { "query": "submittedAt 2030" },
          "reason": "Recuperar normativa."
        }
      ]
    }
    """, logger);

    var result = await service.ProposeAsync(CreateInput(), CancellationToken.None);

    result.ProposedCalls.Should().ContainSingle()
        .Which.Arguments["query"].Should().Be("submittedAt 2030");
    logger.Entries.Should().BeEmpty();
}

[Fact]
public async Task ProposeAsync_Invalid_output_fallback_Should_preserve_exact_timestamp()
{
    var input = CreateInput();
    var service = CreateService(true, "not-json");

    var result = await service.ProposeAsync(input, CancellationToken.None);

    result.ProposedCalls.Single(call =>
            call.ToolName == PlannerToolCatalog.AnalyzeTransactionsName)
        .Arguments["submittedAt"]
        .Should().Be(input.SubmittedAt.ToString("O", CultureInfo.InvariantCulture));
}
```

- [ ] **Step 3: Run focused tests and confirm RED**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~SemanticKernelToolPlanProposalServiceTests" --verbosity minimal
```

Expected: compile fails first because the service has no logger parameter. This is the initial RED state.

- [ ] **Step 4: Inject the logger and canonicalize parsed plans**

In `SemanticKernelToolPlanProposalService`, add:

```csharp
private const string SubmittedAtArgumentName = "submittedAt";
private const string SubmittedAtCanonicalizedCode =
    "TOOL_PLAN_SUBMITTED_AT_CANONICALIZED";

private readonly ILogger<SemanticKernelToolPlanProposalService> _logger;
```

Add `using Microsoft.Extensions.Logging;` and `using Microsoft.Extensions.Logging.Abstractions;`. Add an optional logger to both constructors and assign `_logger = logger ?? NullLogger<SemanticKernelToolPlanProposalService>.Instance`. The optional default preserves the existing bare `ServiceCollection` registration test while normal application DI supplies the registered logger. Use these exact signatures:

```csharp
public SemanticKernelToolPlanProposalService(
    IOptions<LlmOptions> llmOptions,
    IOptions<ToolCallingOptions> toolCallingOptions,
    DeterministicToolPlanProposalService fallback,
    SemanticKernelToolPlanResponseParser parser,
    ILogger<SemanticKernelToolPlanProposalService>? logger = null)

internal SemanticKernelToolPlanProposalService(
    ToolCallingOptions toolCallingOptions,
    DeterministicToolPlanProposalService fallback,
    SemanticKernelToolPlanResponseParser parser,
    IChatCompletionService chatCompletionService,
    ILogger<SemanticKernelToolPlanProposalService>? logger = null)
```

Update direct test construction to pass `NullLogger` or the capturing logger as the last argument. Before adding canonicalization, rerun the Step 3 command: tests must now compile and fail because parsed LLM arguments remain unchanged. This is the behavioral RED state.

Change the successful parser branch:

```csharp
if (!_parser.TryParse(response.Content, out var plan))
{
    return await CreateFallbackPlanAsync(input, cancellationToken);
}

return CanonicalizeSubmittedAt(plan, input);
```

Add the focused canonicalizer:

```csharp
private ToolPlan CanonicalizeSubmittedAt(
    ToolPlan plan,
    ToolPlanProposalInput input)
{
    var canonicalValue = input.SubmittedAt.ToString(
        "O",
        CultureInfo.InvariantCulture);
    var calls = plan.ProposedCalls
        .Select(call => CanonicalizeSubmittedAt(call, input, canonicalValue))
        .ToArray();

    return new ToolPlan(calls);
}

private ProposedToolCall CanonicalizeSubmittedAt(
    ProposedToolCall call,
    ToolPlanProposalInput input,
    string canonicalValue)
{
    if (!string.Equals(
            call.ToolName.Trim(),
            PlannerToolCatalog.AnalyzeTransactionsName,
            StringComparison.OrdinalIgnoreCase))
    {
        return call;
    }

    var timestampArguments = call.Arguments
        .Where(pair => IsSubmittedAtKey(pair.Key))
        .ToArray();
    var reason = ResolveCanonicalizationReason(
        timestampArguments,
        canonicalValue);

    if (reason is null)
    {
        return call;
    }

    var arguments = call.Arguments
        .Where(pair => !IsSubmittedAtKey(pair.Key))
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    arguments[SubmittedAtArgumentName] = canonicalValue;

    _logger.LogWarning(
        "Canonicalized trusted tool argument for {SessionId} {ToolName} {FailureCode} {Reason}.",
        input.SessionId,
        PlannerToolCatalog.AnalyzeTransactionsName,
        SubmittedAtCanonicalizedCode,
        reason);

    return call with { Arguments = arguments };
}

private static bool IsSubmittedAtKey(string key)
{
    return string.Equals(
        key.Trim(),
        SubmittedAtArgumentName,
        StringComparison.OrdinalIgnoreCase);
}

private static string? ResolveCanonicalizationReason(
    IReadOnlyList<KeyValuePair<string, string>> timestampArguments,
    string canonicalValue)
{
    if (timestampArguments.Count == 0)
    {
        return "missing";
    }

    if (timestampArguments.Count > 1)
    {
        return "duplicate";
    }

    var argument = timestampArguments[0];

    if (!DateTimeOffset.TryParse(
            argument.Value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out _))
    {
        return "malformed";
    }

    return argument.Key == SubmittedAtArgumentName &&
           argument.Value == canonicalValue
        ? null
        : "mismatch";
}
```

- [ ] **Step 5: Run focused proposal tests and confirm GREEN**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~SemanticKernelToolPlanProposalServiceTests|FullyQualifiedName~DeterministicToolPlanProposalServiceTests|FullyQualifiedName~ToolPlanProposalRegistrationTests" --verbosity minimal
```

Expected: all proposal and registration tests pass; bare DI still resolves the Semantic Kernel service, exact values retain seven fractional digits and original offsets, and logs contain only the four allowlisted structured properties with no untrusted timestamp.

- [ ] **Step 6: Commit canonicalization**

```powershell
git add backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/SemanticKernelToolPlanProposalService.cs backend/Orchestration.Tests/Agents/Planner/ToolCalling/SemanticKernelToolPlanProposalServiceTests.cs
git commit -m "Canonicalize planner submission timestamps"
```

---

### Task 2: Preserve timestamp-specific source validation and audit

**Files:**
- Modify: `backend/Orchestration.Application/Agents/Shared/FinancialReportSummaryValidation.cs`
- Modify: `backend/Orchestration.Application/Agents/Shared/FinancialReportContextResolver.cs`
- Modify: `backend/Orchestration.Api/Controllers/AnalysisSessionController.cs`
- Modify: `backend/Orchestration.Tests/Agents/Shared/FinancialReportContextResolverTests.cs`
- Modify: `backend/Orchestration.Tests/AnalysisSessions/AnalysisSessionStartPreflightValidatorTests.cs`
- Modify: `backend/Orchestration.Tests/AnalysisSessions/AnalysisOrchestratorContextTests.cs`
- Modify: `backend/Orchestration.Tests/Api/AnalysisSessionFinancialMetricsControllerTests.cs`

- [ ] **Step 1: Write failing resolver and preflight tests**

Add resolver cases that keep other report fields valid so the timestamp is the only failure:

```csharp
[Theory]
[InlineData(
    "{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1}}",
    "SUBMITTED_AT_REQUIRED")]
[InlineData(
    "{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":null}}",
    "SUBMITTED_AT_REQUIRED")]
[InlineData(
    "{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":\"not-a-date\"}}",
    "SUBMITTED_AT_INVALID")]
[InlineData(
    "{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":123}}",
    "SUBMITTED_AT_INVALID")]
[InlineData(
    "{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":{}}}",
    "SUBMITTED_AT_INVALID")]
[InlineData(
    "{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":true}}",
    "SUBMITTED_AT_INVALID")]
[InlineData(
    "{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":[]}}",
    "SUBMITTED_AT_INVALID")]
[InlineData(
    "{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":\"0001-01-01T00:00:00+00:00\"}}",
    "SUBMITTED_AT_INVALID")]
public void Resolve_InvalidSubmittedAt_ShouldReturnSpecificFailure(
    string contextJson,
    string expectedCode)
{
    var session = AnalysisSession.Create(Guid.NewGuid());
    session.SetContext(contextJson);

    var result = new FinancialReportContextResolver().Resolve(session);

    result.IsValid.Should().BeFalse();
    result.Report.Should().BeNull();
    result.ErrorCode.Should().Be(expectedCode);
    result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
}
```

Change the existing generic-invalid resolver test's valid timestamp field from `0001-...` to `2026-07-12T18:30:00Z` so it still proves generic validation for report name/amount/count.

Add a preflight assertion:

```csharp
[Fact]
public async Task ValidateAsync_MissingSubmittedAt_ShouldReturnSpecificCode()
{
    await using var dbContext = CreateDbContext();
    var session = AnalysisSession.Create(Guid.NewGuid());
    session.SetContext(
        """{"financialReport":{"reportName":"report.pdf","totalAmount":1,"transactionCount":1}}""");
    var validator = CreateValidator(new DataAgentOptions(), dbContext);

    var result = await validator.ValidateAsync(session, CancellationToken.None);

    result.CanStart.Should().BeFalse();
    result.Errors.Should().ContainSingle().Which.Should().BeEquivalentTo(new
    {
        Code = "SUBMITTED_AT_REQUIRED",
        Message = "Submission date is required.",
        Severity = "Error"
    });
}
```

- [ ] **Step 2: Write failing orchestration and API audit tests**

Add to `AnalysisOrchestratorContextTests`:

```csharp
[Fact]
public async Task StartAnalysisAsync_MissingSubmittedAt_ShouldFailAndAuditSpecificCause()
{
    await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
    var session = AnalysisSession.Create(Guid.NewGuid());
    session.SetContext(
        """{"financialReport":{"reportName":"report.pdf","totalAmount":1,"transactionCount":1}}""");
    dbContext.AnalysisSessions.Add(session);
    await dbContext.SaveChangesAsync();
    var publisher = new FakeActivityEventPublisher();
    var planner = new CapturingPlannerAgent();
    var orchestrator = new AnalysisOrchestratorService(
        dbContext,
        new AnalysisSessionWorkflowService(new AnalysisSessionStateMachine()),
        publisher,
        planner,
        new FinancialReportContextResolver());

    var result = await orchestrator.StartAnalysisAsync(
        session.Id,
        CancellationToken.None);

    result!.Status.Should().Be(nameof(AnalysisSessionStatus.Failed));
    await dbContext.Entry(session).ReloadAsync();
    session.FailureReason.Should().Be("Submission date is required.");
    planner.RunCalls.Should().Be(0);
    publisher.PublishedEvents.Should().ContainSingle(evt =>
        evt.Type == "financial_report_context_invalid" &&
        evt.Message == "Submission date is required." &&
        !evt.Message.Contains("submittedAt", StringComparison.OrdinalIgnoreCase));
}
```

Add a controller theory proving the real `StartSession` preflight path publishes the specific safe message instead of the current generic structured-metrics text:

```csharp
[Theory]
[InlineData(
    "{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1}}",
    "SUBMITTED_AT_REQUIRED",
    "Submission date is required.",
    null)]
[InlineData(
    "{\"financialReport\":{\"reportName\":\"report.pdf\",\"totalAmount\":1,\"transactionCount\":1,\"submittedAt\":\"private-raw-date\"}}",
    "SUBMITTED_AT_INVALID",
    "Submission date must be valid.",
    "private-raw-date")]
public async Task StartSession_InvalidSubmittedAt_ShouldAuditSpecificSafeReason(
    string contextJson,
    string expectedCode,
    string expectedMessage,
    string? unsafeValue)
{
    await using var dbContext = CreateDbContext();
    var session = AnalysisSession.Create(
        Guid.Parse("00000000-0000-0000-0000-000000000001"));
    session.SetContext(contextJson);
    dbContext.AnalysisSessions.Add(session);
    await dbContext.SaveChangesAsync();
    var publisher = new FakeActivityEventPublisher();
    var planner = new FakePlannerAgent();
    var controller = CreateController(
        dbContext,
        publisher: publisher,
        orchestrator: CreateOrchestrator(dbContext, publisher, planner));

    var result = await controller.StartSession(session.Id, CancellationToken.None);

    var preflight = result.Should().BeOfType<ConflictObjectResult>()
        .Which.Value.Should().BeOfType<AnalysisSessionStartPreflightResult>()
        .Subject;
    preflight.Errors.Should().ContainSingle(issue =>
        issue.Code == expectedCode && issue.Message == expectedMessage);
    session.Status.Should().Be(AnalysisSessionStatus.Pending);
    planner.RunCalls.Should().Be(0);
    var audit = publisher.PublishedEvents.Should().ContainSingle().Subject;
    audit.Type.Should().Be("analysis_start_blocked");
    audit.Message.Should().Be(expectedMessage);
    if (unsafeValue is not null)
    {
        audit.Message.Should().NotContain(unsafeValue);
    }
}
```

- [ ] **Step 3: Run focused validation tests and confirm RED**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~FinancialReportContextResolverTests|FullyQualifiedName~AnalysisSessionStartPreflightValidatorTests|FullyQualifiedName~AnalysisOrchestratorContextTests|FullyQualifiedName~AnalysisSessionFinancialMetricsControllerTests.StartSession_InvalidSubmittedAt" --verbosity minimal
```

Expected: resolver/preflight assertions fail with the generic summary code, direct orchestration uses the generic message, and the API audit uses the generic structured-metrics message. Tests compile because the initial RED assertions use stable literals.

- [ ] **Step 4: Define stable timestamp constants in the existing validator**

Add constants to `FinancialReportSummaryValidator` and use them in the two existing timestamp validation branches:

```csharp
public const string SubmittedAtRequiredCode = "SUBMITTED_AT_REQUIRED";
public const string SubmittedAtRequiredMessage = "Submission date is required.";
public const string SubmittedAtInvalidCode = "SUBMITTED_AT_INVALID";
public const string SubmittedAtInvalidMessage = "Submission date must be valid.";
```

Replace timestamp issue construction with:

```csharp
errors.Add(new FinancialReportSummaryValidationIssue(
    SubmittedAtRequiredCode,
    SubmittedAtRequiredMessage));
```

and:

```csharp
errors.Add(new FinancialReportSummaryValidationIssue(
    SubmittedAtInvalidCode,
    SubmittedAtInvalidMessage));
```

Replace matching literals in the new tests with these constants once they exist.

- [ ] **Step 5: Preserve timestamp errors in the resolver**

Replace the generic invalid result after summary validation with:

```csharp
if (!validation.IsValid || validation.Summary is null)
{
    var timestampError = validation.Errors.FirstOrDefault(error =>
        error.Code is FinancialReportSummaryValidator.SubmittedAtRequiredCode
            or FinancialReportSummaryValidator.SubmittedAtInvalidCode);

    return timestampError is null
        ? Invalid()
        : Invalid(timestampError.Code, timestampError.Message);
}
```

Generalize the private helper without changing existing callers:

```csharp
private static FinancialReportContextResolution Invalid(
    string code = InvalidCode,
    string message = InvalidMessage)
{
    return new FinancialReportContextResolution(
        IsValid: false,
        Report: null,
        ErrorCode: code,
        ErrorMessage: message);
}
```

- [ ] **Step 6: Preserve the specific timestamp reason in API preflight audit**

In `AnalysisSessionController`, retain the current generic start-blocked message as a constant for all unrelated preflight failures. Select only the timestamp-specific message when present:

```csharp
private const string GenericStartBlockedMessage =
    "Inicio de análisis bloqueado: se requieren métricas financieras estructuradas pero no están presentes.";

private static string ResolveStartBlockedAuditMessage(
    AnalysisSessionStartPreflightResult preflight)
{
    var timestampError = preflight.Errors.FirstOrDefault(error =>
        error.Code is FinancialReportSummaryValidator.SubmittedAtRequiredCode
            or FinancialReportSummaryValidator.SubmittedAtInvalidCode);

    return timestampError?.Message ?? GenericStartBlockedMessage;
}
```

Use `ResolveStartBlockedAuditMessage(preflight)` as the `analysis_start_blocked` event message. Do not include the submitted value, serialized context, or full preflight object.

- [ ] **Step 7: Run focused tests and confirm GREEN**

Run the Step 3 command.

Expected: specific source errors flow unchanged into preflight, failure reason, and safe activity audit; unrelated invalid summaries remain generic.

- [ ] **Step 8: Commit source validation and audit**

```powershell
git add backend/Orchestration.Application/Agents/Shared/FinancialReportSummaryValidation.cs backend/Orchestration.Application/Agents/Shared/FinancialReportContextResolver.cs backend/Orchestration.Api/Controllers/AnalysisSessionController.cs backend/Orchestration.Tests/Agents/Shared/FinancialReportContextResolverTests.cs backend/Orchestration.Tests/AnalysisSessions/AnalysisSessionStartPreflightValidatorTests.cs backend/Orchestration.Tests/AnalysisSessions/AnalysisOrchestratorContextTests.cs backend/Orchestration.Tests/Api/AnalysisSessionFinancialMetricsControllerTests.cs
git commit -m "Audit missing report submission timestamps"
```

---

### Task 3: Prove persisted LLM timestamps through plan-driven execution

**Files:**
- Modify: `backend/Orchestration.Tests/AnalysisSessions/ProductionLikeWorkflowE2ETests.cs`

- [ ] **Step 1: Add production-like LLM plan-driven E2E coverage**

Add Semantic Kernel imports:

```csharp
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
```

Add an E2E test that creates the session before building the fake LLM response, allowing all non-timestamp trusted arguments to match while the LLM timestamp is intentionally wrong:

```csharp
[Fact]
public async Task ProductionLikeWorkflow_LlmPlan_ShouldExecutePersistedSubmittedAt()
{
    await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
    var publisher = new PersistingActivityEventPublisher(dbContext);
    var userId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    var session = AnalysisSession.Create(userId);
    dbContext.AnalysisSessions.Add(session);
    await dbContext.SaveChangesAsync();
    var expectedSummary = new FinancialReportSummaryInput(
        "llm-plan-report.pdf",
        333333.33m,
        33,
        new DateTimeOffset(2026, 7, 12, 18, 30, 0, TimeSpan.FromHours(-3)));
    var toolOptions = new ToolCallingOptions
    {
        Enabled = true,
        ExecutionMode = ToolCallingExecutionMode.PlanDriven
    };
    var chat = new StaticChatCompletionService($$"""
    {
      "proposedCalls": [
        {
          "toolName": "data.analyze_transactions",
          "arguments": {
            "sessionId": "{{session.Id}}",
            "reportName": "llm-plan-report.pdf",
            "totalAmount": "333333.33",
            "transactionCount": "33",
            "submittedAt": "2030-01-01T00:00:00Z"
          },
          "reason": "Analizar evidencia financiera."
        },
        {
          "toolName": "legal.search_cnv_regulation",
          "arguments": {
            "query": "agentes",
            "area": "Agentes",
            "limit": "5",
            "requiresReview": "true"
          },
          "reason": "Recuperar evidencia regulatoria."
        }
      ]
    }
    """);
    var proposalService = new SemanticKernelToolPlanProposalService(
        toolOptions,
        new DeterministicToolPlanProposalService(toolOptions),
        new SemanticKernelToolPlanResponseParser(),
        chat,
        NullLogger<SemanticKernelToolPlanProposalService>.Instance);
    FinancialReportContext? executedReport = null;
    var controller = CreateController(
        dbContext,
        publisher,
        new FakeProductionPythonFinancialAnalysisService(),
        new FakeProductionCnvRegulationMcpClient(),
        ToolCallingExecutionMode.PlanDriven,
        proposalService,
        report => executedReport = report);

    await controller.SaveFinancialMetrics(
        session.Id,
        CreateStructuredMetricsInput(reportSummary: expectedSummary),
        CancellationToken.None);
    var startResult = await controller.StartSession(session.Id, CancellationToken.None);

    startResult.Should().BeOfType<OkObjectResult>();
    executedReport.Should().NotBeNull();
    var observedReport = executedReport!;
    observedReport.SubmittedAt.Should().Be(expectedSummary.SubmittedAt!.Value);
    observedReport.SubmittedAt.ToString("O", CultureInfo.InvariantCulture)
        .Should().Be("2026-07-12T18:30:00.0000000-03:00");
    var persisted = await dbContext.AnalysisSessions.AsNoTracking()
        .SingleAsync(item => item.Id == session.Id);
    using var context = JsonDocument.Parse(persisted.ContextJson);
    var dataCall = context.RootElement.GetProperty("toolPlan")
        .GetProperty("proposedCalls")
        .EnumerateArray()
        .Single(call => call.GetProperty("toolName").GetString() ==
            PlannerToolCatalog.AnalyzeTransactionsName);
    dataCall.GetProperty("arguments").GetProperty("submittedAt").GetString()
        .Should().Be("2026-07-12T18:30:00.0000000-03:00");
    persisted.ContextJson.Should().NotContain("2030-01-01");
}
```

- [ ] **Step 2: Extend the production-like fixture with injectable proposal and observation**

Extend `CreateController`:

```csharp
private static AnalysisSessionsController CreateController(
    OrchestrationDbContext dbContext,
    PersistingActivityEventPublisher activityPublisher,
    IPythonFinancialAnalysisService pythonService,
    ICnvRegulationMcpClient cnvClient,
    ToolCallingExecutionMode executionMode = ToolCallingExecutionMode.Shadow,
    IToolPlanProposalService? proposalService = null,
    Action<FinancialReportContext>? dataReportObserver = null)
```

After constructing `ConfigurableDataAgent`, wrap it only when observation is requested:

```csharp
IDataAgent dataAgent = new ConfigurableDataAgent(
    new ThrowingLegacyDataAgent(),
    financialWorkflow,
    Options.Create(dataAgentOptions),
    NullLogger<ConfigurableDataAgent>.Instance);

if (dataReportObserver is not null)
{
    dataAgent = new ObservingDataAgent(dataAgent, dataReportObserver);
}
```

Use the supplied service in `PlannerAgent`:

```csharp
proposalService ?? new DeterministicToolPlanProposalService(toolCallingOptions)
```

Add the decorator:

```csharp
private sealed class ObservingDataAgent(
    IDataAgent inner,
    Action<FinancialReportContext> observer) : IDataAgent
{
    public Task<DataAgentResult> AnalyzeAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        observer(report);
        return inner.AnalyzeAsync(report, cancellationToken);
    }
}
```

Add the fake chat service:

```csharp
private sealed class StaticChatCompletionService(string content)
    : IChatCompletionService
{
    public IReadOnlyDictionary<string, object?> Attributes { get; } =
        new Dictionary<string, object?>();

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChatMessageContent> response =
        [
            new ChatMessageContent(AuthorRole.Assistant, content)
        ];
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent>
        GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
```

- [ ] **Step 3: Run the production-like E2E test**

Task 1's focused tests already demonstrated the pre-fix LLM substitution and drove the production change. This E2E test adds cross-component proof without introducing another production behavior.

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductionLikeWorkflowE2ETests.ProductionLikeWorkflow_LlmPlan_ShouldExecutePersistedSubmittedAt" --verbosity minimal
```

Expected: one test passes; the observed report and persisted plan contain the source offset and never contain `2030-01-01`.

- [ ] **Step 4: Run Planner and controlled-executor regression tests**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~PlannerAgentTests|FullyQualifiedName~ControlledToolExecutorTests|FullyQualifiedName~ProductionLikeWorkflowE2ETests" --verbosity minimal
```

Expected: all tests pass; deterministic two-session isolation remains intact.

- [ ] **Step 5: Commit E2E coverage**

```powershell
git add backend/Orchestration.Tests/AnalysisSessions/ProductionLikeWorkflowE2ETests.cs
git commit -m "Prove planner timestamp integrity end to end"
```

---

### Task 4: Document the invariant and run full verification

**Files:**
- Modify: `README.md:734-737`

- [ ] **Step 1: Document trusted timestamp canonicalization**

Replace the paragraph around the persisted `financialReport` block with:

```markdown
`financialReport` contains the caller-supplied `reportName`, `totalAmount`,
`transactionCount`, and `submittedAt`. These values remain session-scoped and
survive the complete workflow. The Planner and deterministic tool proposal
consume them directly. Semantic Kernel proposals treat the persisted
`submittedAt` as trusted context: missing, malformed, duplicated, or different
LLM values are replaced before validation and execution. Missing source
timestamps block startup with `SUBMITTED_AT_REQUIRED` or
`SUBMITTED_AT_INVALID`; no current-time fallback is used.
```

- [ ] **Step 2: Run all focused suites fresh**

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~SemanticKernelToolPlanProposalServiceTests|FullyQualifiedName~DeterministicToolPlanProposalServiceTests|FullyQualifiedName~ToolPlanProposalRegistrationTests|FullyQualifiedName~ControlledToolExecutorTests|FullyQualifiedName~PlannerAgentTests|FullyQualifiedName~FinancialReportContextResolverTests|FullyQualifiedName~AnalysisSessionStartPreflightValidatorTests|FullyQualifiedName~AnalysisOrchestratorContextTests|FullyQualifiedName~AnalysisSessionFinancialMetricsControllerTests.StartSession_InvalidSubmittedAt|FullyQualifiedName~ProductionLikeWorkflowE2ETests" --verbosity minimal
```

Expected: zero failures. Existing `CS0618` warnings from legacy test setup may appear during test compilation; do not introduce new warnings.

- [ ] **Step 3: Run the full backend suite and build**

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --verbosity minimal
dotnet build backend/Orchestration.slnx --no-restore --verbosity minimal
```

Expected: every test passes and build reports zero errors. Compare warnings with a clean `main` baseline when any appear; this branch must introduce no new warning.

- [ ] **Step 4: Scan production planner code for current-time fallback and verify scope**

```powershell
rg -n "DateTimeOffset\.UtcNow|submittedAt" backend/Orchestration.Application/Agents/Planner backend/Orchestration.Infrastructure/Agents/Planner
git diff --check
git status --short --branch
```

Expected: no `DateTimeOffset.UtcNow` is used to construct a Planner tool argument. Any remaining `UtcNow` occurrence is inspected and confirmed to timestamp an activity event only. Diff check succeeds and the worktree contains only intended README changes before the docs commit.

- [ ] **Step 5: Commit documentation**

```powershell
git add README.md
git commit -m "Document trusted planner timestamp handling"
```

- [ ] **Step 6: Confirm final branch state**

```powershell
git status --short --branch
git log --oneline origin/main..HEAD
```

Expected: clean worktree on `codex/p1-real-submission-timestamp`. Commits are limited to the approved design and implementation plan, canonicalization, explicit source audit, E2E coverage, and documentation.
