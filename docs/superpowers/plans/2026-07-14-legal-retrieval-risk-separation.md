# Legal Retrieval and Compliance-Risk Separation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Separate MCP evidence retrieval from compliance-risk assessment while preserving evidence traceability and requiring human approval for relevant, legally unassessed evidence.

**Architecture:** Add one shared assessment contract and one deterministic MCP-result assessor. Both direct LegalAgent retrieval and plan-driven tool-result mapping use the assessor, propagate its output through the legal contracts, and keep `HasComplianceRisk=false` until applicability exists. Legal review, Planner approval, persisted context, and frontend rendering consume the same assessment.

**Tech Stack:** .NET 10, C# records, xUnit, FluentAssertions, Semantic Kernel, React 19, TypeScript 6, Node test runner, Vite.

---

## File Map

**Create**

- `backend/Orchestration.Application/Agents/Legal/Regulations/RegulatoryEvidenceAssessment.cs` — public assessment contract.
- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/CnvRegulatoryEvidenceAssessor.cs` — deterministic relevance/quality/applicability policy.
- `backend/Orchestration.Tests/Agents/Legal/CnvRegulatoryEvidenceAssessorTests.cs` — isolated policy coverage.
- `frontend/src/utils/legalEvidenceAssessment.ts` — safe frontend labels.
- `frontend/tests/legalEvidenceAssessment.test.mjs` — formatter regression tests.

**Modify**

- `backend/Orchestration.Application/Agents/Legal/Regulations/RegulatoryReviewResult.cs` — expose assessment.
- `backend/Orchestration.Application/Agents/Legal/AiReview/LegalAnalysisReviewInput.cs` — pass assessment into legal review.
- `backend/Orchestration.Application/Agents/Legal/AiReview/DeterministicLegalAnalysisReviewService.cs` — assessment-bounded severity.
- `backend/Orchestration.Application/Agents/Legal/AiReview/LegalAnalysisAiReviewLanguageRules.cs` — Spanish definitive-language patterns.
- `backend/Orchestration.Application/Agents/Legal/LegalAgentResult.cs` — propagate assessment to Planner.
- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/McpRegulatoryKnowledgeSource.cs` — classify retrieval and stop setting risk from findings.
- `backend/Orchestration.Infrastructure/Agents/Legal/LegalCompliancePluginResult.cs` — plugin contract propagation.
- `backend/Orchestration.Infrastructure/Agents/Legal/LegalCompliancePlugin.cs` — plugin mapping.
- `backend/Orchestration.Infrastructure/Agents/Legal/SemanticKernelLegalAgent.cs` — agent mapping.
- `backend/Orchestration.Infrastructure/Agents/Legal/AiReview/SemanticKernelLegalAnalysisReviewService.cs` — prompt and output normalization.
- `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/ToolExecutionResultMapper.cs` — fix the plan-driven MCP path.
- `backend/Orchestration.Application/Agents/Planner/PlannerAgent.cs` — approval from assessment, not false risk.
- `backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs` — persist assessment.
- `frontend/src/types/domain.types.ts` — assessment type.
- `frontend/src/components/CompliancePanel.tsx` — assessment presentation and safe risk label.
- `frontend/src/App.css` — assessment and `NotEstablished` styles.
- Focused tests under `backend/Orchestration.Tests/Agents/Legal`, `Agents/Planner`, and `AnalysisSessions` — update assertions and propagation coverage.

### Task 1: Add the deterministic evidence-assessment policy

**Files:**

- Create: `backend/Orchestration.Application/Agents/Legal/Regulations/RegulatoryEvidenceAssessment.cs`
- Create: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/CnvRegulatoryEvidenceAssessor.cs`
- Create: `backend/Orchestration.Tests/Agents/Legal/CnvRegulatoryEvidenceAssessorTests.cs`

- [ ] **Step 1: Write failing policy tests**

Create `CnvRegulatoryEvidenceAssessorTests.cs`:

```csharp
using FluentAssertions;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Tests.Agents.Legal;

public sealed class CnvRegulatoryEvidenceAssessorTests
{
    [Fact]
    public void Assess_Should_classify_empty_results_as_no_evidence()
    {
        var result = CnvRegulatoryEvidenceAssessor.Assess([]);

        result.Should().BeEquivalentTo(new
        {
            EvidenceFound = false,
            Relevance = "None",
            Applicability = "NotEstablished",
            EvidenceQuality = "None",
            Severity = "Info",
            RequiresHumanReview = false
        });
    }

    [Fact]
    public void Assess_Should_classify_irrelevant_citation_independently_from_quality()
    {
        var result = CnvRegulatoryEvidenceAssessor.Assess(
            [CreateResult(score: 0.39, citation: CreateCitation())]);

        result.EvidenceFound.Should().BeTrue();
        result.Relevance.Should().Be("None");
        result.EvidenceQuality.Should().Be("Strong");
        result.Severity.Should().Be("Info");
        result.RequiresHumanReview.Should().BeFalse();
    }

    [Fact]
    public void Assess_Should_classify_relevant_incomplete_citation_as_weak_evidence()
    {
        var incomplete = CreateCitation() with { Url = null, QuotedText = null };

        var result = CnvRegulatoryEvidenceAssessor.Assess(
            [CreateResult(score: 0.60, citation: incomplete)]);

        result.Relevance.Should().Be("Weak");
        result.EvidenceQuality.Should().Be("Weak");
        result.Applicability.Should().Be("NotEstablished");
        result.Severity.Should().Be("Warning");
        result.RequiresHumanReview.Should().BeTrue();
    }

    [Fact]
    public void Assess_Should_classify_relevant_complete_citation_as_strong_evidence()
    {
        var result = CnvRegulatoryEvidenceAssessor.Assess(
            [CreateResult(score: 0.75, citation: CreateCitation())]);

        result.Relevance.Should().Be("Strong");
        result.EvidenceQuality.Should().Be("Strong");
        result.Applicability.Should().Be("NotEstablished");
        result.Severity.Should().Be("Warning");
        result.RequiresHumanReview.Should().BeTrue();
        result.Reasons.Should().Contain(reason => reason.Contains("aplicabilidad"));
    }

    private static CnvRegulationSearchResult CreateResult(
        double score,
        CnvRegulationCitation? citation) =>
        new(
            DocumentId: "doc-1",
            ChunkId: "chunk-1",
            Title: "Norma CNV",
            Chapter: null,
            Section: null,
            Article: "Artículo 1",
            Source: "CNV",
            Url: "https://www.argentina.gob.ar/cnv",
            Snippet: "Texto recuperado.",
            Score: score,
            Citations: citation is null ? [] : [citation]);

    private static CnvRegulationCitation CreateCitation() =>
        new(
            Source: "CNV",
            DocumentType: "Resolución General",
            ResolutionNumber: "123/2026",
            Title: "Norma CNV",
            Chapter: null,
            Section: null,
            Article: "Artículo 1",
            PublicationDate: "2026-01-01",
            Url: "https://www.argentina.gob.ar/cnv",
            QuotedText: "Texto normativo citado.");
}
```

- [ ] **Step 2: Run the policy tests and verify RED**

Run:

```powershell
$out = Join-Path $env:TEMP 'aihitl-issue7-task1-red'
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "FullyQualifiedName~CnvRegulatoryEvidenceAssessorTests" -p:OutputPath=$out -m:1 -nr:false --no-restore
```

Expected: FAIL with missing `CnvRegulatoryEvidenceAssessor`.

- [ ] **Step 3: Add the shared contract**

Create `RegulatoryEvidenceAssessment.cs`:

```csharp
namespace Orchestration.Application.Agents.Legal.Regulations;

public sealed record RegulatoryEvidenceAssessment(
    bool EvidenceFound,
    string Relevance,
    string Applicability,
    string EvidenceQuality,
    string Severity,
    bool RequiresHumanReview,
    IReadOnlyList<string> Reasons);
```

- [ ] **Step 4: Implement the assessor**

Create `CnvRegulatoryEvidenceAssessor.cs`:

```csharp
using Orchestration.Application.Agents.Legal.Regulations;

namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

internal static class CnvRegulatoryEvidenceAssessor
{
    internal const double WeakRelevanceThreshold = 0.40;
    internal const double StrongRelevanceThreshold = 0.75;

    internal static bool IsRelevant(CnvRegulationSearchResult result) =>
        ClassifyRelevance(result.Score) is "Weak" or "Strong";

    internal static RegulatoryEvidenceAssessment Assess(
        IReadOnlyList<CnvRegulationSearchResult> results)
    {
        if (results.Count == 0)
        {
            return new RegulatoryEvidenceAssessment(
                EvidenceFound: false,
                Relevance: "None",
                Applicability: "NotEstablished",
                EvidenceQuality: "None",
                Severity: "Info",
                RequiresHumanReview: false,
                Reasons: ["La búsqueda MCP no devolvió evidencia regulatoria."]);
        }

        var relevance = MaxLevel(results.Select(result => ClassifyRelevance(result.Score)));
        var quality = MaxLevel(results.Select(ClassifyEvidenceQuality));
        var requiresHumanReview = relevance is "Weak" or "Strong";
        var reasons = new List<string>
        {
            relevance switch
            {
                "Strong" => "La puntuación de relevancia de recuperación alcanzó al menos 0,75.",
                "Weak" => "La puntuación de relevancia de recuperación quedó entre 0,40 y 0,75.",
                _ => "La puntuación de relevancia de recuperación fue inferior a 0,40."
            },
            quality switch
            {
                "Strong" => "Existe una cita con fuente, título, localizador, URL y texto citado.",
                "Weak" => "Existe una cita, pero sus datos de trazabilidad están incompletos.",
                _ => "No se recuperaron citas normativas."
            },
            "La recuperación no establece aplicabilidad legal."
        };

        return new RegulatoryEvidenceAssessment(
            EvidenceFound: true,
            Relevance: relevance,
            Applicability: "NotEstablished",
            EvidenceQuality: quality,
            Severity: requiresHumanReview ? "Warning" : "Info",
            RequiresHumanReview: requiresHumanReview,
            Reasons: reasons);
    }

    private static string ClassifyRelevance(double score)
    {
        if (!double.IsFinite(score) || score < WeakRelevanceThreshold)
        {
            return "None";
        }

        return score < StrongRelevanceThreshold ? "Weak" : "Strong";
    }

    private static string ClassifyEvidenceQuality(CnvRegulationSearchResult result)
    {
        if (result.Citations is not { Count: > 0 })
        {
            return "None";
        }

        return result.Citations.Any(IsStrongCitation) ? "Strong" : "Weak";
    }

    private static bool IsStrongCitation(CnvRegulationCitation citation)
    {
        var hasLocator =
            !string.IsNullOrWhiteSpace(citation.Article) ||
            !string.IsNullOrWhiteSpace(citation.Section) ||
            !string.IsNullOrWhiteSpace(citation.Chapter);

        return
            !string.IsNullOrWhiteSpace(citation.Source) &&
            !string.IsNullOrWhiteSpace(citation.Title) &&
            hasLocator &&
            !string.IsNullOrWhiteSpace(citation.Url) &&
            !string.IsNullOrWhiteSpace(citation.QuotedText);
    }

    private static string MaxLevel(IEnumerable<string> levels)
    {
        var values = levels.ToArray();
        if (values.Contains("Strong")) return "Strong";
        if (values.Contains("Weak")) return "Weak";
        return "None";
    }
}
```

- [ ] **Step 5: Run the policy tests and verify GREEN**

Run the Step 2 command with output path `aihitl-issue7-task1-green`.

Expected: 4 passed, 0 failed.

- [ ] **Step 6: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Legal/Regulations/RegulatoryEvidenceAssessment.cs backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/CnvRegulatoryEvidenceAssessor.cs backend/Orchestration.Tests/Agents/Legal/CnvRegulatoryEvidenceAssessorTests.cs
git commit -m "feat: assess retrieved regulatory evidence"
```

### Task 2: Fix both MCP retrieval-to-risk paths

**Files:**

- Modify: `backend/Orchestration.Application/Agents/Legal/Regulations/RegulatoryReviewResult.cs`
- Modify: `backend/Orchestration.Application/Agents/Legal/AiReview/LegalAnalysisReviewInput.cs`
- Modify: `backend/Orchestration.Application/Agents/Legal/LegalAgentResult.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/McpRegulatoryKnowledgeSource.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/ToolExecutionResultMapper.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/McpRegulatoryKnowledgeSourceTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/FallbackCnvRegulationMcpClient.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ToolExecutionResultMapperTests.cs`

- [ ] **Step 1: Replace old risk assertions with retrieval-assessment assertions**

In `McpRegulatoryKnowledgeSourceTests`, replace the cited-result expectation and add weak/strong/empty cases. Each case must assert the top-level distinction:

```csharp
result.HasComplianceRisk.Should().BeFalse();
result.RiskLevel.Should().Be("NotEstablished");
result.EvidenceAssessment.Should().NotBeNull();
result.EvidenceAssessment!.EvidenceFound.Should().BeTrue();
result.EvidenceAssessment.Applicability.Should().Be("NotEstablished");
```

Use `CreateResult` scores `0.20`, `0.60`, and `0.90` for irrelevant, weak, and strong relevance. Add an `EmptyCnvRegulationMcpClient` returning `Results: []`. Assert:

```csharp
// Irrelevant cited result
result.EvidenceAssessment!.Relevance.Should().Be("None");
result.EvidenceAssessment.Severity.Should().Be("Info");
result.LegalReview!.PossibleRegulatoryReviewAreas.Should().BeEmpty();

// Weak incomplete citation
result.EvidenceAssessment!.Relevance.Should().Be("Weak");
result.EvidenceAssessment.EvidenceQuality.Should().Be("Weak");
result.EvidenceAssessment.RequiresHumanReview.Should().BeTrue();

// Strong complete citation
result.EvidenceAssessment!.Relevance.Should().Be("Strong");
result.EvidenceAssessment.EvidenceQuality.Should().Be("Strong");
result.EvidenceAssessment.RequiresHumanReview.Should().BeTrue();

// Empty
result.EvidenceAssessment!.EvidenceFound.Should().BeFalse();
result.EvidenceAssessment.Relevance.Should().Be("None");
result.Findings.Should().BeEmpty();
```

Change the test helper signature and score assignment so each scenario is explicit:

```csharp
private static CnvRegulationSearchResult CreateResult(
    string documentId,
    string title,
    string snippet,
    IReadOnlyList<CnvRegulationCitation> citations,
    double score = 0.90)
{
    return new CnvRegulationSearchResult(
        DocumentId: documentId,
        ChunkId: $"{documentId}-chunk",
        Title: title,
        Chapter: "Capitulo I",
        Section: null,
        Article: "Articulo 1",
        Source: "Infoleg",
        Url: "https://servicios.infoleg.gob.ar/",
        Snippet: snippet,
        Score: score,
        Citations: citations);
}
```

Update `FallbackCnvRegulationMcpClient.ReviewAsync_Should_use_fallback_queries_until_cited_results_are_found` for its existing `0.075` score:

```csharp
result.HasComplianceRisk.Should().BeFalse();
result.RiskLevel.Should().Be("NotEstablished");
result.Findings.Should().NotBeEmpty();
result.EvidenceAssessment!.Relevance.Should().Be("None");
result.EvidenceAssessment.RequiresHumanReview.Should().BeFalse();
```

In `ToolExecutionResultMapperTests`, change the cited-result test to assert:

```csharp
result!.HasComplianceRisk.Should().BeFalse();
result.RiskLevel.Should().Be("NotEstablished");
result.EvidenceAssessment.Should().NotBeNull();
result.EvidenceAssessment!.Relevance.Should().Be("Strong");
result.EvidenceAssessment.RequiresHumanReview.Should().BeTrue();
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
$out = Join-Path $env:TEMP 'aihitl-issue7-task2-red'
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "FullyQualifiedName~McpRegulatoryKnowledgeSourceTests|FullyQualifiedName~ToolExecutionResultMapperTests" -p:OutputPath=$out -m:1 -nr:false --no-restore
```

Expected: compile/assertion failure because assessment members are absent and cited evidence still sets `Medium` risk.

- [ ] **Step 3: Extend result and review-input contracts**

Add a trailing member to `RegulatoryReviewResult` and `LegalAgentResult`:

```csharp
RegulatoryEvidenceAssessment? EvidenceAssessment = null
```

Import `Orchestration.Application.Agents.Legal.Regulations` in
`LegalAgentResult.cs`.

Add a trailing member to `LegalAnalysisReviewInput` and import the regulations namespace:

```csharp
RegulatoryEvidenceAssessment? EvidenceAssessment = null
```

- [ ] **Step 4: Integrate assessment into `McpRegulatoryKnowledgeSource`**

Extend the private outcome:

```csharp
private sealed record CnvSearchReviewResult(
    List<RegulatoryFinding> Findings,
    List<LegalEvidenceReference> EvidenceReferences,
    List<string> Warnings,
    RegulatoryEvidenceAssessment EvidenceAssessment);
```

In `SearchFindingsAsync`, initialize the result collection:

```csharp
var retrievedResults = new List<CnvRegulationSearchResult>();
```

At the start of each `foreach (var result in response.Results)` body:

```csharp
retrievedResults.Add(result);
```

Continue mapping every cited result into `Findings`, but add a `LegalEvidenceReference` only when:

```csharp
CnvRegulatoryEvidenceAssessor.IsRelevant(result)
```

After all queries:

```csharp
var assessment = CnvRegulatoryEvidenceAssessor.Assess(retrievedResults);

if (assessment.RequiresHumanReview)
{
    warnings.Add("Se recuperó evidencia regulatoria potencialmente relevante. Su aplicabilidad no está determinada y requiere revisión legal humana.");
}
else if (assessment.EvidenceFound)
{
    warnings.Add("Se recuperó evidencia regulatoria, pero no alcanzó el umbral de relevancia para crear un área de revisión.");
}
else
{
    warnings.Add("La búsqueda MCP no recuperó evidencia regulatoria; esto no establece ausencia de riesgo ni constituye una conclusión legal.");
}
```

Replace the existing `if (uniqueFindings.Count > 0)` warning block with this
assessment-driven block; do not retain count-based human-review inference.

Return `EvidenceAssessment: assessment`. In `ReviewAsync`, pass it into `LegalAnalysisReviewInput`, then return:

```csharp
return new RegulatoryReviewResult(
    HasComplianceRisk: false,
    RiskLevel: "NotEstablished",
    Summary: outcome.EvidenceAssessment.RequiresHumanReview
        ? "Se recuperó evidencia regulatoria potencialmente relevante como posible área de revisión. La aplicabilidad no está determinada."
        : outcome.EvidenceAssessment.EvidenceFound
            ? "Se recuperó evidencia regulatoria, pero no se estableció relevancia ni aplicabilidad para una evaluación de cumplimiento."
            : "No se recuperó evidencia regulatoria. La ausencia de resultados no constituye una evaluación de cumplimiento.",
    SourceEngine: "MCP CNV Regulation Server",
    Findings: outcome.Findings,
    Warnings: warnings.Distinct().ToList(),
    QueryStrategy: audit,
    LegalReview: legalReviewResult,
    EvidenceAssessment: outcome.EvidenceAssessment);
```

- [ ] **Step 5: Apply the same assessor in `ToolExecutionResultMapper`**

Replace `hasRisk` derivation with:

```csharp
var assessment = CnvRegulatoryEvidenceAssessor.Assess(results);
var warnings = (response.Warnings ?? []).ToList();
if (assessment.RequiresHumanReview)
{
    warnings.Add(HumanReviewWarning);
}

return new LegalAgentResult(
    HasComplianceRisk: false,
    RiskLevel: "NotEstablished",
    Summary: assessment.RequiresHumanReview
        ? "Se recuperó evidencia regulatoria potencialmente relevante como posible área de revisión. La aplicabilidad no está determinada."
        : assessment.EvidenceFound
            ? "Se recuperó evidencia regulatoria, pero no se estableció relevancia ni aplicabilidad para una evaluación de cumplimiento."
            : "No se recuperó evidencia regulatoria. La ausencia de resultados no constituye una evaluación de cumplimiento.",
    Engine: LegalEngine,
    Evidence: evidence,
    Warnings: warnings,
    EvidenceAssessment: assessment);
```

Do not change `MockRegulatoryKnowledgeSource`; it models explicit mock policy rather than MCP retrieval.

- [ ] **Step 6: Run focused tests and verify GREEN**

Run the Step 2 command with output path `aihitl-issue7-task2-green`.

Expected: all selected tests pass.

- [ ] **Step 7: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Legal/Regulations/RegulatoryReviewResult.cs backend/Orchestration.Application/Agents/Legal/AiReview/LegalAnalysisReviewInput.cs backend/Orchestration.Application/Agents/Legal/LegalAgentResult.cs backend/Orchestration.Infrastructure/Agents/Legal/Regulations/McpRegulatoryKnowledgeSource.cs backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/ToolExecutionResultMapper.cs backend/Orchestration.Tests/Agents/Legal/McpRegulatoryKnowledgeSourceTests.cs backend/Orchestration.Tests/Agents/Legal/FallbackCnvRegulationMcpClient.cs backend/Orchestration.Tests/Agents/Planner/ToolCalling/ToolExecutionResultMapperTests.cs
git commit -m "fix: separate legal retrieval from compliance risk"
```

### Task 3: Propagate assessment and preserve human approval

**Files:**

- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/LegalCompliancePluginResult.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/LegalCompliancePlugin.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/SemanticKernelLegalAgent.cs`
- Modify: `backend/Orchestration.Application/Agents/Planner/PlannerAgent.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/SemanticKernelLegalAgentTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/PlannerAgentTests.cs`

- [ ] **Step 1: Write failing propagation and approval tests**

Extend the existing fake legal-result helper with an optional assessment. Add this Planner test:

```csharp
[Fact]
public async Task RunAsync_Should_require_human_approval_for_relevant_unassessed_legal_evidence()
{
    var assessment = new RegulatoryEvidenceAssessment(
        EvidenceFound: true,
        Relevance: "Strong",
        Applicability: "NotEstablished",
        EvidenceQuality: "Strong",
        Severity: "Warning",
        RequiresHumanReview: true,
        Reasons: ["La recuperación no establece aplicabilidad legal."]);
    var legalResult = CreateLegalResult(hasComplianceRisk: false) with
    {
        RiskLevel = "NotEstablished",
        EvidenceAssessment = assessment
    };
    var plannerAgent = new PlannerAgent(
        new FakeDataAgent(CreateDataResult(hasAnomaly: false)),
        new FakeLegalAgent(legalResult),
        new FakeActivityEventPublisher(),
        new FakePlannerReasoningService(),
        new FakeToolPlanProposalService(),
        new ToolPlanNormalizer(),
        new ToolPlanValidator(),
        new ToolExecutionPolicy(),
        new FakeControlledToolExecutor(),
        new FakeToolExecutionResultMapper(),
        new ToolCallingOptions());

    var result = await plannerAgent.RunAsync(
        TestFinancialReport.CreateContext(),
        CancellationToken.None);

    result.RequiresHumanApproval.Should().BeTrue();
    result.LegalResult.HasComplianceRisk.Should().BeFalse();
    result.LegalResult.EvidenceAssessment.Should().BeSameAs(assessment);
}
```

In `SemanticKernelLegalAgentTests`, register this test source and assert the returned assessment is equivalent:

```csharp
private sealed class AssessedRegulatoryKnowledgeSource : IRegulatoryKnowledgeSource
{
    public Task<RegulatoryReviewResult> ReviewAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        var assessment = new RegulatoryEvidenceAssessment(
            true, "Strong", "NotEstablished", "Strong", "Warning", true,
            ["La recuperación no establece aplicabilidad legal."]);

        return Task.FromResult(new RegulatoryReviewResult(
            HasComplianceRisk: false,
            RiskLevel: "NotEstablished",
            Summary: "Posible área de revisión.",
            SourceEngine: "Test MCP",
            Findings: [],
            Warnings: [],
            EvidenceAssessment: assessment));
    }
}
```

Add this test using the existing dependency-injection path:

```csharp
[Fact]
public async Task ReviewAsync_Should_propagate_retrieval_assessment_without_declaring_risk()
{
    var services = new ServiceCollection();
    services.AddScoped<IRegulatoryKnowledgeSource, AssessedRegulatoryKnowledgeSource>();
    services.AddScoped<LegalCompliancePlugin>();
    services.AddScoped<ILegalAgent, SemanticKernelLegalAgent>();
    await using var serviceProvider = services.BuildServiceProvider();
    var agent = serviceProvider.GetRequiredService<ILegalAgent>();

    var result = await agent.ReviewAsync(
        new FinancialReportContext(
            Guid.NewGuid(), "legal-assessment-test", 1m, 1, DateTimeOffset.UtcNow),
        CancellationToken.None);

    result.HasComplianceRisk.Should().BeFalse();
    result.RiskLevel.Should().Be("NotEstablished");
    result.EvidenceAssessment.Should().NotBeNull();
    result.EvidenceAssessment!.RequiresHumanReview.Should().BeTrue();
}
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
$out = Join-Path $env:TEMP 'aihitl-issue7-task3-red'
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "FullyQualifiedName~SemanticKernelLegalAgentTests|FullyQualifiedName~PlannerAgentTests" -p:OutputPath=$out -m:1 -nr:false --no-restore
```

Expected: FAIL because agent contracts do not expose assessment and Planner ignores it.

- [ ] **Step 3: Add trailing optional contract members and mappings**

`LegalAgentResult` already exposes the trailing member from Task 2. Add the same trailing member to `LegalCompliancePluginResult`:

```csharp
RegulatoryEvidenceAssessment? EvidenceAssessment = null
```

Import `Orchestration.Application.Agents.Legal.Regulations`. Map the value in `LegalCompliancePlugin`:

```csharp
EvidenceAssessment: review.EvidenceAssessment
```

Map it in `SemanticKernelLegalAgent`:

```csharp
EvidenceAssessment: pluginResult.EvidenceAssessment
```

- [ ] **Step 4: Extend Planner approval**

Replace the approval expression with:

```csharp
var requiresHumanApproval =
    dataResult.HasAnomaly ||
    dataResult.RequiresHumanReview ||
    legalResult.HasComplianceRisk ||
    legalResult.EvidenceAssessment?.RequiresHumanReview == true;
```

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the Step 2 command with output path `aihitl-issue7-task3-green`.

Expected: all selected tests pass.

- [ ] **Step 6: Commit**

```powershell
git add backend/Orchestration.Infrastructure/Agents/Legal/LegalCompliancePluginResult.cs backend/Orchestration.Infrastructure/Agents/Legal/LegalCompliancePlugin.cs backend/Orchestration.Infrastructure/Agents/Legal/SemanticKernelLegalAgent.cs backend/Orchestration.Application/Agents/Planner/PlannerAgent.cs backend/Orchestration.Tests/Agents/Legal/SemanticKernelLegalAgentTests.cs backend/Orchestration.Tests/Agents/Planner/PlannerAgentTests.cs
git commit -m "feat: require review for unassessed legal evidence"
```

### Task 4: Bound deterministic and LLM legal-review severity

**Files:**

- Modify: `backend/Orchestration.Application/Agents/Legal/AiReview/DeterministicLegalAnalysisReviewService.cs`
- Modify: `backend/Orchestration.Application/Agents/Legal/AiReview/LegalAnalysisAiReviewLanguageRules.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/AiReview/SemanticKernelLegalAnalysisReviewService.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/AiReview/DeterministicLegalAnalysisReviewServiceTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/AiReview/SemanticKernelLegalAnalysisReviewServiceTests.cs`

- [ ] **Step 1: Write failing severity and Spanish-language tests**

Add a deterministic test whose input carries a `Warning` assessment while the financial signal is `High`:

```csharp
[Fact]
public async Task ReviewAsync_Should_use_evidence_assessment_severity_instead_of_financial_severity()
{
    var input = CreateInput(
        riskSignals: [CreateSignal("LOW_CURRENT_RATIO", "High")],
        cnvEvidence: [CreateEvidence("CNV Art. 42")],
        evidenceAssessment: CreateAssessment("Warning"));

    var result = await _service.ReviewAsync(input, CancellationToken.None);

    result.PossibleRegulatoryReviewAreas.Should().ContainSingle();
    result.PossibleRegulatoryReviewAreas[0].Severity.Should().Be("Warning");
}
```

Extend the existing test input helper with a trailing
`RegulatoryEvidenceAssessment? evidenceAssessment = null` parameter and pass
`EvidenceAssessment: evidenceAssessment` to `LegalAnalysisReviewInput`. Add:

```csharp
private static RegulatoryEvidenceAssessment CreateAssessment(string severity) =>
    new(
        EvidenceFound: true,
        Relevance: "Strong",
        Applicability: "NotEstablished",
        EvidenceQuality: "Strong",
        Severity: severity,
        RequiresHumanReview: true,
        Reasons: ["La recuperación no establece aplicabilidad legal."]);
```

Extend the Semantic Kernel test `CreateInput` helper with the same optional
assessment parameter and pass it to the contract. Add:

```csharp
[Fact]
public async Task ReviewAsync_Should_bound_LLM_severity_to_evidence_assessment()
{
    var chat = new FakeChatCompletionService(CreateValidResponse());
    var service = CreateService(chat);

    var result = await service.ReviewAsync(
        CreateInput(evidenceAssessment: CreateAssessment("Warning")),
        CancellationToken.None);

    result.UsedLlm.Should().BeTrue();
    result.PossibleRegulatoryReviewAreas.Should().ContainSingle();
    result.PossibleRegulatoryReviewAreas[0].Severity.Should().Be("Warning");
}
```

Add this helper to `SemanticKernelLegalAnalysisReviewServiceTests`:

```csharp
private static RegulatoryEvidenceAssessment CreateAssessment(string severity) =>
    new(
        EvidenceFound: true,
        Relevance: "Strong",
        Applicability: "NotEstablished",
        EvidenceQuality: "Strong",
        Severity: severity,
        RequiresHumanReview: true,
        Reasons: ["La recuperación no establece aplicabilidad legal."]);
```

Add `[Theory]` rows for Spanish definitive claims:

```csharp
[Theory]
[InlineData("La conducta es ilegal.")]
[InlineData("Incumplimiento confirmado.")]
[InlineData("La empresa cometió fraude.")]
public async Task ReviewAsync_Should_fallback_for_Spanish_definitive_legal_claims(string summary)
{
    var response = CreateValidResponse().Replace(
        "Valid review summary.",
        summary,
        StringComparison.Ordinal);
    var service = CreateService(new FakeChatCompletionService(response));

    var result = await service.ReviewAsync(CreateInput(), CancellationToken.None);

    result.UsedFallback.Should().BeTrue();
    result.FailureReason.Should().Be("forbidden_language");
}
```

Add the disclaimer regression:

```csharp
[Fact]
public async Task ReviewAsync_Should_allow_advisory_disclaimer()
{
    var response = CreateValidResponse().Replace(
        "Valid review summary.",
        "No constituye asesoramiento legal.",
        StringComparison.Ordinal);
    var service = CreateService(new FakeChatCompletionService(response));

    var result = await service.ReviewAsync(CreateInput(), CancellationToken.None);

    result.UsedLlm.Should().BeTrue();
    result.FailureReason.Should().BeNull();
}
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
$out = Join-Path $env:TEMP 'aihitl-issue7-task4-red'
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "FullyQualifiedName~DeterministicLegalAnalysisReviewServiceTests|FullyQualifiedName~SemanticKernelLegalAnalysisReviewServiceTests" -p:OutputPath=$out -m:1 -nr:false --no-restore
```

Expected: severity remains `High` and Spanish definitive claims are not rejected.

- [ ] **Step 3: Bound deterministic severity**

Change the severity helper signature:

```csharp
private static string DetermineSeverity(
    IReadOnlyList<FinancialRiskSignal> signals,
    RegulatoryEvidenceAssessment? assessment)
{
    if (assessment is not null)
    {
        return assessment.Severity;
    }

    return DetermineFinancialSignalSeverity(signals);
}
```

Rename the existing body to `DetermineFinancialSignalSeverity`. Change the call and helper signature exactly as follows:

```csharp
var reviewAreas = BuildReviewAreas(
    riskSignals,
    citedEvidence,
    input.EvidenceAssessment);

private static IReadOnlyList<PossibleRegulatoryReviewArea> BuildReviewAreas(
    IReadOnlyList<FinancialRiskSignal> riskSignals,
    IReadOnlyList<LegalEvidenceReference> citedEvidence,
    RegulatoryEvidenceAssessment? assessment)
```

Inside the group projection call `DetermineSeverity(signalsInGroup, assessment)`. This preserves compatibility for non-MCP callers while MCP-backed review is bounded by assessment.

- [ ] **Step 4: Normalize LLM output and strengthen its prompt**

Add these rules to `SystemPrompt`:

```text
- Retrieved evidence is not a compliance-risk finding.
- Base severity on the supplied evidence assessment: relevance, applicability, and evidence quality.
- When applicability is NotEstablished, severity must not exceed Warning.
```

Include `input.EvidenceAssessment` in `BuildUserPrompt`. During area sanitization:

```csharp
var severity = input.EvidenceAssessment?.Severity ?? area.Severity;
sanitizedAreas.Add(area with
{
    EvidenceCitations = validCitations,
    Severity = severity
});
```

- [ ] **Step 5: Add Spanish forbidden patterns**

Append exact lowercase patterns that do not match disclaimers:

```csharp
"es ilegal",
"infringe la normativa",
"incumplimiento confirmado",
"violación legal confirmada",
"culpable",
"cometió fraude"
```

- [ ] **Step 6: Run focused tests and verify GREEN**

Run the Step 2 command with output path `aihitl-issue7-task4-green`.

Expected: all selected tests pass.

- [ ] **Step 7: Commit**

```powershell
git add backend/Orchestration.Application/Agents/Legal/AiReview/DeterministicLegalAnalysisReviewService.cs backend/Orchestration.Application/Agents/Legal/AiReview/LegalAnalysisAiReviewLanguageRules.cs backend/Orchestration.Infrastructure/Agents/Legal/AiReview/SemanticKernelLegalAnalysisReviewService.cs backend/Orchestration.Tests/Agents/Legal/AiReview/DeterministicLegalAnalysisReviewServiceTests.cs backend/Orchestration.Tests/Agents/Legal/AiReview/SemanticKernelLegalAnalysisReviewServiceTests.cs
git commit -m "fix: bound legal review severity to evidence assessment"
```

### Task 5: Persist and render the assessment

**Files:**

- Modify: `backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs`
- Modify: `backend/Orchestration.Tests/AnalysisSessions/AnalysisOrchestratorContextTests.cs`
- Modify: `frontend/src/types/domain.types.ts`
- Create: `frontend/src/utils/legalEvidenceAssessment.ts`
- Create: `frontend/tests/legalEvidenceAssessment.test.mjs`
- Modify: `frontend/src/components/CompliancePanel.tsx`
- Modify: `frontend/src/App.css`

- [ ] **Step 1: Write failing persisted-context test**

Extend `CreatePlannerResult` with a trailing
`RegulatoryEvidenceAssessment? evidenceAssessment = null` parameter and pass
`EvidenceAssessment: evidenceAssessment` to its `LegalAgentResult`. Add:

```csharp
[Fact]
public void BuildAnalysisContext_Should_include_regulatory_evidence_assessment()
{
    var evidenceAssessment = new RegulatoryEvidenceAssessment(
        true, "Strong", "NotEstablished", "Strong", "Warning", true,
        ["La recuperación no establece aplicabilidad legal."]);
    var plannerResult = CreatePlannerResult(
        ToolPlanAuditResult.Empty,
        evidenceAssessment: evidenceAssessment);

    var contextJson = AnalysisOrchestratorService.BuildAnalysisContext(plannerResult);

    using var document = JsonDocument.Parse(contextJson);
    var assessment = document.RootElement
        .GetProperty("compliance")
        .GetProperty("evidenceAssessment");
    assessment.GetProperty("evidenceFound").GetBoolean().Should().BeTrue();
    assessment.GetProperty("relevance").GetString().Should().Be("Strong");
    assessment.GetProperty("applicability").GetString().Should().Be("NotEstablished");
    assessment.GetProperty("evidenceQuality").GetString().Should().Be("Strong");
    assessment.GetProperty("severity").GetString().Should().Be("Warning");
    assessment.GetProperty("requiresHumanReview").GetBoolean().Should().BeTrue();
    assessment.GetProperty("reasons").GetArrayLength().Should().BeGreaterThan(0);
}
```

- [ ] **Step 2: Run context test and verify RED**

```powershell
$out = Join-Path $env:TEMP 'aihitl-issue7-task5-context-red'
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "FullyQualifiedName~AnalysisOrchestratorContextTests" -p:OutputPath=$out -m:1 -nr:false --no-restore
```

Expected: `evidenceAssessment` is absent.

- [ ] **Step 3: Serialize the assessment**

Inside the `compliance` object in `BuildAnalysisContext`, add:

```csharp
evidenceAssessment = plannerResult.LegalResult.EvidenceAssessment is null
    ? null
    : new
    {
        evidenceFound = plannerResult.LegalResult.EvidenceAssessment.EvidenceFound,
        relevance = plannerResult.LegalResult.EvidenceAssessment.Relevance,
        applicability = plannerResult.LegalResult.EvidenceAssessment.Applicability,
        evidenceQuality = plannerResult.LegalResult.EvidenceAssessment.EvidenceQuality,
        severity = plannerResult.LegalResult.EvidenceAssessment.Severity,
        requiresHumanReview = plannerResult.LegalResult.EvidenceAssessment.RequiresHumanReview,
        reasons = plannerResult.LegalResult.EvidenceAssessment.Reasons
    },
```

Run the Step 2 command with output path `aihitl-issue7-task5-context-green`. Expected: pass.

- [ ] **Step 4: Write failing frontend formatter tests**

Create `frontend/tests/legalEvidenceAssessment.test.mjs`:

```javascript
import assert from "node:assert/strict";
import test from "node:test";
import {
  formatLegalAssessmentValue,
  formatLegalRiskLevel,
} from "../src/utils/legalEvidenceAssessment.ts";

test("formats internal legal assessment values for operators", () => {
  assert.equal(formatLegalRiskLevel("NotEstablished"), "No determinado");
  assert.equal(formatLegalAssessmentValue("Strong"), "Fuerte");
  assert.equal(formatLegalAssessmentValue("Weak"), "Débil");
  assert.equal(formatLegalAssessmentValue("None"), "Ninguna");
  assert.equal(formatLegalAssessmentValue("Warning"), "Revisión requerida");
});

test("preserves unknown future values", () => {
  assert.equal(formatLegalAssessmentValue("FutureValue"), "FutureValue");
});
```

- [ ] **Step 5: Run formatter tests and verify RED**

```powershell
node --experimental-strip-types --test frontend/tests/legalEvidenceAssessment.test.mjs
```

Expected: FAIL with missing module `legalEvidenceAssessment.ts`.

- [ ] **Step 6: Add frontend type and formatter**

Add to `domain.types.ts`:

```typescript
export type RegulatoryEvidenceAssessment = {
  evidenceFound: boolean;
  relevance: "None" | "Weak" | "Strong";
  applicability: "NotEstablished";
  evidenceQuality: "None" | "Weak" | "Strong";
  severity: "Info" | "Warning";
  requiresHumanReview: boolean;
  reasons: string[];
};
```

Add `evidenceAssessment?: RegulatoryEvidenceAssessment | null` to `ComplianceContext`.

Create `legalEvidenceAssessment.ts`:

```typescript
const assessmentLabels: Readonly<Record<string, string>> = {
  None: "Ninguna",
  Weak: "Débil",
  Strong: "Fuerte",
  NotEstablished: "No determinada",
  Info: "Informativa",
  Warning: "Revisión requerida",
};

export function formatLegalAssessmentValue(value: string): string {
  return assessmentLabels[value] ?? value;
}

export function formatLegalRiskLevel(value: string): string {
  return value === "NotEstablished" ? "No determinado" : value;
}
```

Run the Step 5 command. Expected: 2 passed, 0 failed.

- [ ] **Step 7: Render an explicit assessment block**

In `CompliancePanel.tsx`, import the formatters and replace the raw badge label with `formatLegalRiskLevel(compliance.riskLevel)`. When `compliance.evidenceAssessment` exists, render:

```tsx
<section className="legalEvidenceAssessment" aria-labelledby="legal-evidence-assessment-title">
  <div>
    <p className="complianceEyebrow">Estado de la evidencia recuperada</p>
    <h3 id="legal-evidence-assessment-title">
      Recuperación distinta de evaluación de cumplimiento
    </h3>
    <p>
      La evidencia encontrada puede indicar un área posible de revisión; no determina
      aplicabilidad, incumplimiento ni asesoramiento legal.
    </p>
  </div>
  <dl className="legalEvidenceAssessmentGrid">
    <div><dt>Evidencia encontrada</dt><dd>{assessment.evidenceFound ? "Sí" : "No"}</dd></div>
    <div><dt>Relevancia</dt><dd>{formatLegalAssessmentValue(assessment.relevance)}</dd></div>
    <div><dt>Aplicabilidad</dt><dd>{formatLegalAssessmentValue(assessment.applicability)}</dd></div>
    <div><dt>Calidad</dt><dd>{formatLegalAssessmentValue(assessment.evidenceQuality)}</dd></div>
    <div><dt>Severidad</dt><dd>{formatLegalAssessmentValue(assessment.severity)}</dd></div>
    <div><dt>Revisión humana</dt><dd>{assessment.requiresHumanReview ? "Requerida" : "No requerida por la recuperación"}</dd></div>
  </dl>
  {assessment.reasons.length > 0 && (
    <ul>{assessment.reasons.map((reason) => <li key={reason}>{reason}</li>)}</ul>
  )}
</section>
```

Bind `const assessment = compliance.evidenceAssessment;` near `legalReview`. Add these styles to `App.css`:

```css
.risk-NotEstablished {
  background: #eff6ff;
  color: #1d4ed8;
  border-color: #bfdbfe;
}

.legalEvidenceAssessment {
  margin-top: 1rem;
  padding: 1rem;
  border: 1px solid rgba(129, 140, 248, 0.35);
  border-radius: 16px;
  background: rgba(30, 41, 59, 0.72);
}

.legalEvidenceAssessment h3 {
  margin: 0;
  color: #f1f5f9;
}

.legalEvidenceAssessment p,
.legalEvidenceAssessment li {
  color: #cbd5e1;
}

.legalEvidenceAssessmentGrid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 0.75rem;
  margin: 1rem 0;
}

.legalEvidenceAssessmentGrid div {
  padding: 0.75rem;
  border-radius: 12px;
  background: rgba(15, 23, 42, 0.6);
}

.legalEvidenceAssessmentGrid dt {
  color: #94a3b8;
  font-size: 0.75rem;
  font-weight: 800;
  text-transform: uppercase;
}

.legalEvidenceAssessmentGrid dd {
  margin: 0.3rem 0 0;
  color: #f8fafc;
  font-weight: 800;
}
```

- [ ] **Step 8: Verify frontend tests and build**

```powershell
node --experimental-strip-types --test frontend/tests/legalEvidenceAssessment.test.mjs
npm run build --prefix frontend
```

Expected: formatter tests pass; TypeScript and Vite build exit 0.

- [ ] **Step 9: Commit**

```powershell
git add backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs backend/Orchestration.Tests/AnalysisSessions/AnalysisOrchestratorContextTests.cs frontend/src/types/domain.types.ts frontend/src/utils/legalEvidenceAssessment.ts frontend/tests/legalEvidenceAssessment.test.mjs frontend/src/components/CompliancePanel.tsx frontend/src/App.css
git commit -m "feat: expose legal evidence assessment"
```

### Task 6: Update production-like expectations and run full verification

**Files:**

- Modify: `backend/Orchestration.Tests/AnalysisSessions/ProductionLikeWorkflowE2ETests.cs`

- [ ] **Step 1: Update production-like legal assertions**

For MCP-backed final contexts, replace `riskDetected == true` with:

```csharp
compliance.GetProperty("riskDetected").GetBoolean().Should().BeFalse();
compliance.GetProperty("riskLevel").GetString().Should().Be("NotEstablished");
var assessment = compliance.GetProperty("evidenceAssessment");
assessment.GetProperty("evidenceFound").GetBoolean().Should().BeTrue();
assessment.GetProperty("applicability").GetString().Should().Be("NotEstablished");
assessment.GetProperty("requiresHumanReview").GetBoolean().Should().BeTrue();
```

Keep evidence and citation assertions. Add an assertion that final Planner state still requires approval. Do not change mock-policy tests that intentionally model a real risk decision.

- [ ] **Step 2: Run focused legal and workflow suites**

```powershell
$out = Join-Path $env:TEMP 'aihitl-issue7-focused'
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "FullyQualifiedName~Agents.Legal|FullyQualifiedName~PlannerAgentTests|FullyQualifiedName~ToolExecutionResultMapperTests|FullyQualifiedName~AnalysisOrchestratorContextTests|FullyQualifiedName~ProductionLikeWorkflowE2ETests" -p:OutputPath=$out -m:1 -nr:false --no-restore
```

Expected: all selected tests pass. Existing package and obsolete-API warnings may remain; no test failures.

- [ ] **Step 3: Run the full backend suite**

```powershell
$out = Join-Path $env:TEMP 'aihitl-issue7-full'
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj -p:OutputPath=$out -m:1 -nr:false --no-restore
```

Expected: 0 failed.

- [ ] **Step 4: Run frontend verification**

```powershell
node --experimental-strip-types --test frontend/tests/legalEvidenceAssessment.test.mjs
npm run build --prefix frontend
```

Expected: tests and build exit 0.

- [ ] **Step 5: Inspect legal language and diff hygiene**

```powershell
rg -n -i "hascompliancerisk:\s*findings|risklevel:\s*hasrisk|se encontr[oó] evidencia.*riesgo|incumplimiento confirmado|violaci[oó]n legal confirmada" backend frontend --glob "*.cs" --glob "*.ts" --glob "*.tsx"
git diff --check
git status --short
```

Expected: no retrieval-count-to-risk assignment; definitive phrases appear only in forbidden-language rules/tests; `git diff --check` exits 0.

- [ ] **Step 6: Review requirements against the issue**

Verify each acceptance criterion directly:

- Retrieval and compliance risk are separate fields.
- Evidence is labeled as retrieved/possible review area.
- Relevance, applicability, quality, and severity are explicit.
- No output declares a violation or offers legal advice.
- Relevant uncertain evidence requires Planner approval.
- Irrelevant, weak, strong, and empty cases are covered.

- [ ] **Step 7: Commit final regression updates**

```powershell
git add backend/Orchestration.Tests/AnalysisSessions/ProductionLikeWorkflowE2ETests.cs
git commit -m "test: verify legal retrieval risk separation"
```

- [ ] **Step 8: Final branch audit**

```powershell
git status --short --branch
git log --oneline --decorate -6
git diff main...HEAD --stat
```

Expected: clean worktree on `codex/issue-7-legal-citation-risk`; commits limited to issue #7 design, implementation, UI propagation, and tests.
