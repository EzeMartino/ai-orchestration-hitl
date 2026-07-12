# Agent Tool Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve GitHub issue #2 by making one typed catalog the production source of truth for every Planner-callable tool and removing unreachable duplicate tool paths.

**Architecture:** Add an Application-layer `PlannerToolCatalog` containing the two aggregate tools that can produce complete `DataAgentResult` and `LegalAgentResult` outputs. Each definition owns canonical name, prompt description, argument schema, handler kind, result kind, satisfaction policy, and audit actor. Prompt generation, deterministic fallback, validation, execution policy, controlled dispatch, and result mapping resolve behavior through this catalog; configuration remains a mandatory allowlist filter.

**Tech Stack:** .NET 10, C# 13, ASP.NET Core DI, Semantic Kernel, xUnit, FluentAssertions.

---

### Task 1: Define the typed production catalog

**Files:**
- Create: `backend/Orchestration.Application/Agents/Planner/ToolCalling/PlannerToolDefinition.cs`
- Create: `backend/Orchestration.Application/Agents/Planner/ToolCalling/PlannerToolCatalog.cs`
- Create: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/PlannerToolCatalogTests.cs`

- [ ] **Step 1: Write failing catalog tests**

```csharp
[Fact]
public void All_Should_define_only_composable_tools_with_unique_names()
{
    PlannerToolCatalog.All.Should().HaveCount(2);
    PlannerToolCatalog.All.Select(tool => tool.Name).Should().OnlyHaveUniqueItems();
    PlannerToolCatalog.All.Should().OnlyContain(tool =>
        tool.Arguments.Count > 0 &&
        !string.IsNullOrWhiteSpace(tool.PromptDescription) &&
        !string.IsNullOrWhiteSpace(tool.AuditActor));
}

[Theory]
[InlineData("data.analyze_transactions", PlannerToolHandler.AnalyzeTransactions, PlannerToolResultKind.DataAgent)]
[InlineData("legal.search_cnv_regulation", PlannerToolHandler.SearchCnvRegulation, PlannerToolResultKind.LegalAgent)]
public void Find_Should_return_canonical_definition(
    string name,
    PlannerToolHandler handler,
    PlannerToolResultKind resultKind)
{
    var definition = PlannerToolCatalog.Find(name);
    definition.Should().NotBeNull();
    definition!.Handler.Should().Be(handler);
    definition.ResultKind.Should().Be(resultKind);
}
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter FullyQualifiedName~PlannerToolCatalogTests
```

Expected: compile failure because `PlannerToolCatalog` does not exist.

- [ ] **Step 3: Implement definitions and catalog**

```csharp
public enum PlannerToolHandler { AnalyzeTransactions, SearchCnvRegulation }
public enum PlannerToolResultKind { DataAgent, LegalAgent }
public enum PlannerToolSatisfactionKind { DataAnalysis, LegalReview }
public enum PlannerToolArgumentType { String, Guid, Decimal, Integer, DateTimeOffset, Boolean }

public sealed record PlannerToolArgumentDefinition(
    string Name,
    PlannerToolArgumentType Type,
    bool Required,
    string Description);

public sealed record PlannerToolDefinition(
    string Name,
    string PromptDescription,
    PlannerToolHandler Handler,
    PlannerToolResultKind ResultKind,
    PlannerToolSatisfactionKind SatisfactionKind,
    string AuditActor,
    IReadOnlyList<PlannerToolArgumentDefinition> Arguments);
```

`PlannerToolCatalog.All` contains exactly `data.analyze_transactions` and `legal.search_cnv_regulation`; `Find` compares names case-insensitively; `GetAllowed` intersects catalog entries with `ToolCallingOptions.AllowedTools`.

- [ ] **Step 4: Run catalog tests and verify GREEN**

Use the command from Step 2. Expected: all `PlannerToolCatalogTests` pass.

### Task 2: Drive proposal, validation, and policy from the catalog

**Files:**
- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/DeterministicToolPlanProposalService.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/ToolPlanValidator.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/ToolExecutionPolicy.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/SemanticKernelToolPlanProposalService.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/DeterministicToolPlanProposalServiceTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ToolPlanValidatorTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ToolExecutionPolicyTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/SemanticKernelToolPlanProposalServiceTests.cs`

- [ ] **Step 1: Add failing behavior tests**

```csharp
[Fact]
public async Task ProposeAsync_Should_only_propose_catalog_tools_allowed_by_configuration()
{
    var options = new ToolCallingOptions
    {
        Enabled = true,
        AllowedTools = ["legal.search_cnv_regulation"]
    };
    var result = await new DeterministicToolPlanProposalService(options)
        .ProposeAsync(CreateInput(), CancellationToken.None);
    result.ProposedCalls.Should().ContainSingle()
        .Which.ToolName.Should().Be("legal.search_cnv_regulation");
}

[Fact]
public void Validate_Should_reject_catalog_tool_missing_required_argument()
{
    var validator = new ToolPlanValidator(new ToolCallingOptions
    {
        AllowedTools = ["legal.search_cnv_regulation"]
    });
    var result = validator.Validate(CreatePlan(CreateCall(
        "legal.search_cnv_regulation",
        new Dictionary<string, string>())));
    result.RejectedCalls.Should().ContainSingle();
}
```

Also assert the Semantic Kernel user payload serializes `PlannerToolCatalog.GetAllowed(options)` with argument names/types and that policy uses `SatisfactionKind` rather than hard-coded names.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "FullyQualifiedName~DeterministicToolPlanProposalServiceTests|FullyQualifiedName~ToolPlanValidatorTests|FullyQualifiedName~ToolExecutionPolicyTests|FullyQualifiedName~SemanticKernelToolPlanProposalServiceTests"
```

Expected: new allowlist/schema/prompt assertions fail.

- [ ] **Step 3: Implement catalog consumption**

- Deterministic proposer checks `PlannerToolCatalog.GetAllowed(_options)` before adding each call.
- Semantic proposer removes hard-coded allowed names and serializes catalog definitions into `availableTools`.
- Validator requires both a known catalog entry and configured allowlist membership, then checks all required catalog arguments.
- Policy resolves `PlannerToolCatalog.Find(call.ToolName)?.SatisfactionKind` to decide already-satisfied status.
- Unknown tools remain rejected; workflow, approval, operational, and legal-conclusion denial messages remain unchanged.

- [ ] **Step 4: Run focused tests and verify GREEN**

Use the command from Step 2. Expected: all selected tests pass.

### Task 3: Drive execution and result mapping from catalog descriptors

**Files:**
- Modify: `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/ControlledToolExecutor.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/ToolExecutionResultMapper.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ControlledToolExecutorTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ToolExecutionResultMapperTests.cs`

- [ ] **Step 1: Add failing descriptor-dispatch tests**

```csharp
[Theory]
[InlineData("DATA.ANALYZE_TRANSACTIONS", PlannerToolHandler.AnalyzeTransactions)]
[InlineData("LEGAL.SEARCH_CNV_REGULATION", PlannerToolHandler.SearchCnvRegulation)]
public void Find_Should_support_case_insensitive_dispatch(
    string toolName,
    PlannerToolHandler expectedHandler)
{
    PlannerToolCatalog.Find(toolName)!.Handler.Should().Be(expectedHandler);
}
```

Update executor tests to construct it without `IPythonFinancialAnalysisService`. Add mapper assertions that it selects definitions by `PlannerToolResultKind`.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "FullyQualifiedName~ControlledToolExecutorTests|FullyQualifiedName~ToolExecutionResultMapperTests|FullyQualifiedName~PlannerToolCatalogTests"
```

Expected: constructor/descriptor assertions fail against the old hard-coded switches.

- [ ] **Step 3: Implement descriptor dispatch and remove granular handlers**

- Executor resolves the definition once and switches on `definition.Handler`.
- Remove the four `data.compute_*`, `data.compare_*`, `data.detect_*`, and `data.summarize_*` constants/methods.
- Remove `IPythonFinancialAnalysisService` from executor constructor.
- Mapper finds successful calls using definitions whose `ResultKind` is DataAgent or LegalAgent.
- Unsupported names return the existing safe failed result.

- [ ] **Step 4: Run focused tests and verify GREEN**

Use the command from Step 2. Expected: all selected tests pass.

### Task 4: Remove orphaned plugin/configuration and update documentation

**Files:**
- Delete: `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysisPlugin.cs`
- Delete: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/FinancialAnalysisPluginTests.cs`
- Modify: `backend/Orchestration.Api/Program.cs:115-116`
- Modify: `backend/Orchestration.Tests/Agents/Data/PythonAgentTestFixture.cs:51`
- Modify: `backend/Orchestration.Application/Agents/Planner/ToolCalling/ToolCallingOptions.cs:12`
- Modify: `backend/Orchestration.Api/appsettings.json:15-23`
- Modify: `backend/Orchestration.AppHost/AppHost.cs:11-12,128`
- Modify: `README.md:89-100,680-690,1394`

- [ ] **Step 1: Add/adjust failing configuration tests**

Replace granular-tool validator tests with assertions that every configured tool must exist in `PlannerToolCatalog` and remove expectations for `ToolCallingOptions.FinancialAnalysisToolsEnabled`.

- [ ] **Step 2: Run Planner/Data tests and verify RED**

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "FullyQualifiedName~ToolCalling|FullyQualifiedName~FinancialAnalysisPluginTests|FullyQualifiedName~PythonAgentTestFixture"
```

Expected: old granular/plugin/config expectations fail until dead paths are removed.

- [ ] **Step 3: Remove dead production paths**

- Delete orphaned plugin and its dedicated tests.
- Remove its API/test DI registrations.
- Remove only `ToolCalling.FinancialAnalysisToolsEnabled`; retain the distinct `DataAgent.FinancialAnalysisToolsEnabled` workflow flag.
- Remove ToolCalling environment propagation from AppHost.
- Update README to state that Planner exposes the two aggregate catalog tools; structured financial analysis remains internal to DataAgent.

- [ ] **Step 4: Run full verification**

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj
dotnet build backend/Orchestration.slnx --no-restore
rg -n "data\.compute_financial_ratios|data\.compare_periods|data\.detect_financial_risk_signals|data\.summarize_quantitative_evidence|ToolCalling__FinancialAnalysisToolsEnabled|FinancialAnalysisPlugin" backend README.md
```

Expected: tests/build exit 0; `rg` returns no matches for removed production paths.

### Task 5: Final review and issue traceability

**Files:**
- Modify: `docs/superpowers/plans/2026-07-12-agent-tool-catalog.md`

- [ ] **Step 1: Review diff and catalog invariants**

```powershell
git diff --check
git diff --stat
git status --short
```

Expected: no whitespace errors; only issue #2 files changed.

- [ ] **Step 2: Record verification evidence on GitHub issue #2**

Post a comment listing catalog tools, removed paths, test count, build result, and branch name. Do not close the issue until all acceptance criteria are verified.

## Completion evidence

- [x] Tasks 1-4 implemented through focused RED/GREEN cycles.
- [x] Full suite: 1065 passed, 0 failed, 0 skipped.
- [x] Solution build: 0 warnings, 0 errors.
- [x] Removed production paths return no search matches.
- [x] Final diff and issue traceability reviewed on `codex/agent-audit-p0-remediation`.
