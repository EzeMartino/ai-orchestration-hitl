# CNV Search-Hit Enrichment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enrich at most two deterministic CNV search hits with bounded canonical document/article context while preserving original retrieval evidence and never converting retrieval into a compliance-risk conclusion.

**Architecture:** Harden the MCP retrieval contracts first, then add typed orchestration transport DTOs and a dedicated `CnvRegulatoryHitEnricher`. The Legal source invokes that pipeline once after all searches, passes only non-conflicting canonical context to review, propagates the complete audit/result through direct and plan-driven paths, persists it, and renders it separately in the Legal UI.

**Tech Stack:** .NET 10, C# records, MCP .NET client/server, Semantic Kernel, xUnit, FluentAssertions, React 19, TypeScript 6, Node test runner, Vite.

---

## File map

- `tools/CnvRegulation.McpServer/src/CnvRegulation.Application/Contracts/GetRegulationDocumentResponse.cs` — explicit document found/missing contract.
- `tools/CnvRegulation.McpServer/src/CnvRegulation.Application/Contracts/GetRegulationArticleResponse.cs` — explicit article found/missing contract.
- `tools/CnvRegulation.McpServer/src/CnvRegulation.Infrastructure/InMemory/InMemoryRegulationDocumentService.cs` — repository-only document retrieval.
- `tools/CnvRegulation.McpServer/src/CnvRegulation.Infrastructure/InMemory/InMemoryRegulationArticleService.cs` — repository-only article retrieval.
- `tools/CnvRegulation.McpServer/src/CnvRegulation.McpServer/CnvRegulationTools.cs` — accurate read-only tool descriptions.
- `backend/Orchestration.Application/Agents/Legal/Regulations/RegulatoryEvidenceEnrichment.cs` — stable application contracts and status codes.
- `backend/Orchestration.Application/Agents/Legal/Cnv/LegalQueryStrategyAudit.cs` — enrichment audit appended to query audit.
- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/CnvRegulationRetrievalContracts.cs` — orchestration-owned document/article transport DTOs.
- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/ICnvRegulationMcpClient.cs` — typed retrieval methods.
- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/CnvRegulationStdioMcpClient.cs` — shared typed tool-call path, timeout, cancellation, and reset behavior.
- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/CnvRegulationMcpOptions.cs` — bounded enrichment configuration.
- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/CnvRegulationMcpOptionsValidator.cs` — startup validation.
- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/CnvRegulatoryHitEnricher.cs` — deterministic selection, canonical verification, truncation, cache, and audit.
- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/McpRegulatoryKnowledgeSource.cs` — post-search integration, review input, warnings, and activity event.
- `backend/Orchestration.Application/Agents/Legal/AiReview/LegalAnalysisReviewInput.cs` — original and canonical evidence kept separate.
- `backend/Orchestration.Application/Agents/Legal/AiReview/DeterministicLegalAnalysisReviewService.cs` — verified-reference preference and limitation propagation.
- `backend/Orchestration.Infrastructure/Agents/Legal/AiReview/SemanticKernelLegalAnalysisReviewService.cs` — separate safe prompt sections and allowlist.
- `backend/Orchestration.Application/Agents/Legal/Regulations/RegulatoryReviewResult.cs`, `backend/Orchestration.Infrastructure/Agents/Legal/LegalCompliancePluginResult.cs`, `backend/Orchestration.Application/Agents/Legal/LegalAgentResult.cs` — trailing compatible propagation field.
- `backend/Orchestration.Infrastructure/Agents/Legal/LegalCompliancePlugin.cs`, `backend/Orchestration.Infrastructure/Agents/Legal/SemanticKernelLegalAgent.cs` — direct Legal propagation.
- `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/ToolExecutionResultMapper.cs` — fail-closed validation and deterministic multi-call aggregation.
- `backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs` — persisted `compliance.evidenceEnrichments` and audit projection.
- `frontend/src/types/domain.types.ts` — nullable enrichment context types.
- `frontend/src/utils/regulatoryEvidenceEnrichment.ts` — Spanish stable status formatting.
- `frontend/src/components/CompliancePanel.tsx` — accessible, collapsed canonical-context presentation.

### Task 1: Make MCP document/article absence explicit

**Files:**
- Modify: `tools/CnvRegulation.McpServer/tests/CnvRegulation.Application.Tests/InMemoryRegulationDocumentServiceTests.cs`
- Modify: `tools/CnvRegulation.McpServer/tests/CnvRegulation.Application.Tests/InMemoryRegulationArticleServiceTests.cs`
- Modify: `tools/CnvRegulation.McpServer/tests/CnvRegulation.McpServer.Tests/CnvRegulationToolsRegistrationTests.cs`
- Modify: `tools/CnvRegulation.McpServer/src/CnvRegulation.Application/Contracts/GetRegulationDocumentResponse.cs`
- Modify: `tools/CnvRegulation.McpServer/src/CnvRegulation.Application/Contracts/GetRegulationArticleResponse.cs`
- Modify: `tools/CnvRegulation.McpServer/src/CnvRegulation.Infrastructure/InMemory/InMemoryRegulationDocumentService.cs`
- Modify: `tools/CnvRegulation.McpServer/src/CnvRegulation.Infrastructure/InMemory/InMemoryRegulationArticleService.cs`
- Modify: `tools/CnvRegulation.McpServer/src/CnvRegulation.McpServer/CnvRegulationTools.cs`

- [ ] **Step 1: Replace fallback expectations with found/missing contract tests**

Add assertions equivalent to:

```csharp
response.Found.Should().BeTrue();
response.Document.Should().NotBeNull();

var missing = await service.GetDocumentAsync(
    new GetRegulationDocumentRequest { DocumentId = "missing-document" },
    CancellationToken.None);

missing.Found.Should().BeFalse();
missing.Document.Should().BeNull();
missing.Citations.Should().BeEmpty();
missing.Warnings.Should().ContainSingle()
    .Which.Should().Contain("No se encontró", StringComparison.OrdinalIgnoreCase);
```

For articles, replace `GetArticleAsync_ShouldFallbackToMock_WhenArticleDoesNotExist` with:

```csharp
response.Found.Should().BeFalse();
response.Text.Should().BeNull();
response.Citation.Should().BeNull();
response.Confidence.Should().Be(0);
response.Warnings.Should().ContainSingle();
response.Warnings.Should().NotContain(w =>
    w.Contains("Mock regulatory text", StringComparison.OrdinalIgnoreCase));
```

Extend registration tests to inspect `McpServerToolAttribute` and `DescriptionAttribute` for `get_cnv_document` and `get_cnv_article`, asserting `ReadOnly=true`, `Destructive=false`, `Idempotent=true`, `UseStructuredContent=true`, and no description containing `mock` or `placeholder`.

- [ ] **Step 2: Run focused MCP tests and verify RED**

Run:

```powershell
dotnet test tools/CnvRegulation.McpServer/tests/CnvRegulation.Application.Tests/CnvRegulation.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~InMemoryRegulationDocumentServiceTests|FullyQualifiedName~InMemoryRegulationArticleServiceTests"
dotnet test tools/CnvRegulation.McpServer/tests/CnvRegulation.McpServer.Tests/CnvRegulation.McpServer.Tests.csproj --no-restore --filter FullyQualifiedName~CnvRegulationToolsRegistrationTests
```

Expected: FAIL because `Found` does not exist, missing requests still synthesize mock content, and tool descriptions mention mock data.

- [ ] **Step 3: Implement nullable explicit response contracts**

Use these shapes:

```csharp
public sealed class GetRegulationDocumentResponse
{
    public required bool Found { get; init; }
    public RegulationDocument? Document { get; init; }
    public required IReadOnlyList<RegulationCitation> Citations { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class GetRegulationArticleResponse
{
    public required bool Found { get; init; }
    public string? Text { get; init; }
    public RegulationCitation? Citation { get; init; }
    public double Confidence { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
```

Return repository data only. The missing document branch must be:

```csharp
if (document is null)
{
    return new GetRegulationDocumentResponse
    {
        Found = false,
        Document = null,
        Citations = [],
        Warnings = [$"No se encontró el documento regulatorio '{documentId}'."]
    };
}
```

The missing article branch must be:

```csharp
return new GetRegulationArticleResponse
{
    Found = false,
    Text = null,
    Citation = null,
    Confidence = 0,
    Warnings = [$"No se encontró el artículo regulatorio '{request.Article?.Trim()}'."]
};
```

Set `Found=true` in both successful branches. Update tool XML/description text to say “repository-backed CNV regulatory document/article” and preserve existing MCP safety annotations.

- [ ] **Step 4: Run focused MCP tests and verify GREEN**

Run:

```powershell
dotnet test tools/CnvRegulation.McpServer/tests/CnvRegulation.Application.Tests/CnvRegulation.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~InMemoryRegulationDocumentServiceTests|FullyQualifiedName~InMemoryRegulationArticleServiceTests"
dotnet test tools/CnvRegulation.McpServer/tests/CnvRegulation.McpServer.Tests/CnvRegulation.McpServer.Tests.csproj --no-restore --filter FullyQualifiedName~CnvRegulationToolsRegistrationTests
```

Expected: document/article tests pass; registration tests pass; no unknown request returns placeholder text.

- [ ] **Step 5: Commit server contract hardening**

```powershell
git add tools/CnvRegulation.McpServer
git commit -m "fix: make missing CNV retrieval explicit"
```

### Task 2: Define enrichment contracts, audit, and safe options

**Files:**
- Create: `backend/Orchestration.Application/Agents/Legal/Regulations/RegulatoryEvidenceEnrichment.cs`
- Modify: `backend/Orchestration.Application/Agents/Legal/Cnv/LegalQueryStrategyAudit.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/CnvRegulationMcpOptions.cs`
- Create: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/CnvRegulationMcpOptionsValidator.cs`
- Modify: `backend/Orchestration.Api/Program.cs`
- Create: `backend/Orchestration.Tests/Agents/Legal/CnvRegulationMcpOptionsValidatorTests.cs`
- Create: `backend/Orchestration.Tests/Agents/Legal/RegulatoryEvidenceEnrichmentContractsTests.cs`

- [ ] **Step 1: Write contract round-trip and option-boundary tests**

Add a JSON round-trip fixture for `RegulatoryEvidenceEnrichment` with one original citation, bounded canonical document, bounded canonical article, `Partial`, and one limitation. Assert every nested field survives and the serialized `original`, `document`, and `article` objects remain distinct.

Add validator cases:

```csharp
[Theory]
[InlineData(-1, 12000, 6000)]
[InlineData(3, 12000, 6000)]
[InlineData(2, -1, 6000)]
[InlineData(2, 12001, 6000)]
[InlineData(2, 12000, -1)]
[InlineData(2, 12000, 6001)]
public void Validate_Should_reject_unsafe_enrichment_bounds(
    int maxHits,
    int maxDocumentCharacters,
    int maxArticleCharacters)
{
    var result = new CnvRegulationMcpOptionsValidator().Validate(
        null,
        new CnvRegulationMcpOptions
        {
            MaxEnrichedHits = maxHits,
            MaxDocumentContextCharacters = maxDocumentCharacters,
            MaxArticleContextCharacters = maxArticleCharacters
        });

    result.Failed.Should().BeTrue();
}
```

Also test defaults and zero values as valid.

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~CnvRegulationMcpOptionsValidatorTests|FullyQualifiedName~RegulatoryEvidenceEnrichmentContractsTests"
```

Expected: FAIL because enrichment contracts, options, and validator do not exist.

- [ ] **Step 3: Add stable application contracts and status constants**

Create immutable records with these exact names and fields:

```csharp
public sealed record RegulatoryEvidenceCitation(
    string Source,
    string? DocumentType,
    string? ResolutionNumber,
    string Title,
    string? Chapter,
    string? Section,
    string? Article,
    string? PublicationDate,
    string? Url,
    string? QuotedText);

public sealed record RegulatoryOriginalEvidence(
    string Snippet,
    RegulatoryEvidenceCitation Citation);

public sealed record RegulatoryCanonicalDocument(
    string Id,
    string Source,
    string DocumentType,
    string? ResolutionNumber,
    string Title,
    string? PublicationDate,
    string? EffectiveDate,
    string Url,
    string Status,
    bool RequiresReview,
    string? RetrievedAt,
    string Text,
    int OriginalTextLength,
    bool IsTruncated,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<RegulatoryEvidenceCitation> Citations);

public sealed record RegulatoryCanonicalArticle(
    RegulatoryEvidenceCitation Citation,
    string Text,
    double Confidence,
    int OriginalTextLength,
    bool IsTruncated);

public sealed record RegulatoryEvidenceEnrichment(
    string EnrichmentId,
    string DocumentId,
    string? ChunkId,
    int Rank,
    double Score,
    RegulatoryOriginalEvidence Original,
    RegulatoryCanonicalDocument? Document,
    RegulatoryCanonicalArticle? Article,
    string Status,
    IReadOnlyList<string> Limitations);

public static class RegulatoryEvidenceEnrichmentStatuses
{
    public const string Verified = "Verified";
    public const string Partial = "Partial";
    public const string Conflict = "Conflict";
    public const string Unavailable = "Unavailable";
}
```

Extend `LegalQueryStrategyAudit` with trailing `IReadOnlyList<LegalCnvEnrichmentAudit>? Enrichments = null` and define:

```csharp
public sealed record LegalCnvEnrichmentAudit(
    string EnrichmentId,
    int Rank,
    string CandidateKey,
    double Score,
    IReadOnlyList<int> ContributingQueryIndices,
    LegalCnvEnrichmentStageAudit Document,
    LegalCnvEnrichmentStageAudit Article,
    string Status,
    IReadOnlyList<string> LimitationCodes);

public sealed record LegalCnvEnrichmentStageAudit(
    bool Selected,
    bool Attempted,
    bool FromCache,
    string Status,
    int? OriginalTextLength,
    bool IsTruncated);
```

Add `LegalCnvEnrichmentStageStatuses` constants for `NotApplicable`, `NotAttempted`, `Succeeded`, `Missing`, `TimedOut`, `Malformed`, `Failed`, and `Conflict`.

- [ ] **Step 4: Add options and startup validation**

Append defaults:

```csharp
public int MaxEnrichedHits { get; init; } = 2;
public int MaxDocumentContextCharacters { get; init; } = 12_000;
public int MaxArticleContextCharacters { get; init; } = 6_000;
```

Implement `IValidateOptions<CnvRegulationMcpOptions>` with inclusive ranges `0..2`, `0..12000`, and `0..6000`. Register it before MCP consumers:

```csharp
builder.Services
    .AddOptions<CnvRegulationMcpOptions>()
    .Bind(builder.Configuration.GetSection(CnvRegulationMcpOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<CnvRegulationMcpOptions>,
    CnvRegulationMcpOptionsValidator>();
```

Remove the older duplicate `Configure<CnvRegulationMcpOptions>` call.

- [ ] **Step 5: Run contract/validator tests and verify GREEN**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~CnvRegulationMcpOptionsValidatorTests|FullyQualifiedName~RegulatoryEvidenceEnrichmentContractsTests"
```

Expected: all selected tests pass, including zero/default bounds and legacy JSON.

- [ ] **Step 6: Commit contracts and configuration**

```powershell
git add backend/Orchestration.Application backend/Orchestration.Infrastructure/Agents/Legal/Regulations/CnvRegulationMcpOptions.cs backend/Orchestration.Infrastructure/Agents/Legal/Regulations/CnvRegulationMcpOptionsValidator.cs backend/Orchestration.Api/Program.cs backend/Orchestration.Tests/Agents/Legal
git commit -m "feat: define regulatory enrichment contracts"
```

### Task 3: Add typed document/article MCP calls

**Files:**
- Create: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/CnvRegulationRetrievalContracts.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/ICnvRegulationMcpClient.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/CnvRegulationStdioMcpClient.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/CnvRegulationStdioMcpClientTests.cs`
- Modify: every test fake implementing `ICnvRegulationMcpClient` under `backend/Orchestration.Tests`

- [ ] **Step 1: Write client tests through an internal tool-call seam**

Add an internal constructor accepting:

```csharp
Func<string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<CallToolResult>> toolCallOverride
```

Use it to cover:

```csharp
await client.GetDocumentAsync(
    new CnvRegulationDocumentRequest("doc-1"),
    CancellationToken.None);

calls.Should().ContainSingle(call =>
    call.Name == "get_cnv_document" &&
    (string)call.Arguments["documentId"]! == "doc-1");

await client.GetArticleAsync(
    new CnvRegulationArticleRequest("Artículo 4", "Título VII", null, "Sección 2"),
    CancellationToken.None);

calls.Should().ContainSingle(call =>
    call.Name == "get_cnv_article" &&
    (string)call.Arguments["article"]! == "Artículo 4" &&
    (string)call.Arguments["title"]! == "Título VII" &&
    !call.Arguments.ContainsKey("chapter") &&
    (string)call.Arguments["section"]! == "Sección 2");
```

Build `CallToolResult` fixtures for structured JSON, text JSON fallback, `Found=false`, malformed JSON, `IsError=true`, a never-completing call canceled by internal timeout, and caller cancellation. Assert internal timeout increments `ResetCount`; caller cancellation propagates and does not get translated to a limitation by the client.

- [ ] **Step 2: Run client tests and verify RED**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter FullyQualifiedName~CnvRegulationStdioMcpClientTests
```

Expected: FAIL because typed methods, DTOs, and injection seam do not exist.

- [ ] **Step 3: Define orchestration-owned transport DTOs**

Use exact request names:

```csharp
public sealed record CnvRegulationDocumentRequest(string DocumentId);
public sealed record CnvRegulationArticleRequest(
    string Article,
    string? Title = null,
    string? Chapter = null,
    string? Section = null);
```

Define response DTOs mirroring server JSON, including nullable content and required collections:

```csharp
public sealed record CnvRegulationDocumentResponse(
    bool Found,
    CnvRegulationDocument? Document,
    IReadOnlyList<CnvRegulationCitation>? Citations,
    IReadOnlyList<string>? Warnings);

public sealed record CnvRegulationArticleResponse(
    bool Found,
    string? Text,
    CnvRegulationCitation? Citation,
    double Confidence,
    IReadOnlyList<string>? Warnings);
```

`CnvRegulationDocument` contains `Id`, `Source`, `DocumentType`, `ResolutionNumber`, `Title`, string date fields, `Url`, `Status`, `RequiresReview`, `RetrievedAt`, `IReadOnlyDictionary<string, string>? Metadata`, and `Text`.

- [ ] **Step 4: Refactor one shared typed call path**

Add interface methods:

```csharp
Task<CnvRegulationDocumentResponse> GetDocumentAsync(
    CnvRegulationDocumentRequest request,
    CancellationToken cancellationToken);

Task<CnvRegulationArticleResponse> GetArticleAsync(
    CnvRegulationArticleRequest request,
    CancellationToken cancellationToken);
```

Factor search/document/article through:

```csharp
private Task<TResponse> CallToolAsync<TResponse>(
    string toolName,
    IReadOnlyDictionary<string, object?> arguments,
    CancellationToken cancellationToken)
```

The helper owns disposal checks, `_lock`, connection setup, linked timeout, `IsError`, structured/text deserialization, transport reset, `_lastError`, and telemetry. It must distinguish:

```csharp
catch (OperationCanceledException) when (
    timeoutCts.IsCancellationRequested &&
    !cancellationToken.IsCancellationRequested)
{
    await ResetConnectionAsync("Tool call timed out");
    throw new TimeoutException(
        $"La herramienta MCP '{toolName}' excedió el tiempo de espera configurado.");
}
```

The normal `OperationCanceledException` path rethrows unchanged. Do not retry.

- [ ] **Step 5: Update all test fakes explicitly**

Every fake implements both new methods. Fakes unrelated to enrichment return explicit missing responses:

```csharp
public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
    CnvRegulationDocumentRequest request,
    CancellationToken cancellationToken) =>
    Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

public Task<CnvRegulationArticleResponse> GetArticleAsync(
    CnvRegulationArticleRequest request,
    CancellationToken cancellationToken) =>
    Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));
```

- [ ] **Step 6: Run client and compile-surface tests**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~CnvRegulationStdioMcpClientTests|FullyQualifiedName~DiagnosticsControllerTests|FullyQualifiedName~ProductionLikeWorkflowE2ETests"
```

Expected: selected tests pass; exact tool names/arguments, timeout reset, caller cancellation, malformed data, errors, and `Found=false` are covered.

- [ ] **Step 7: Commit typed client support**

```powershell
git add backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp backend/Orchestration.Tests
git commit -m "feat: add typed CNV retrieval client"
```

### Task 4: Implement deterministic bounded enrichment

**Files:**
- Create: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/CnvRegulatoryHitEnricher.cs`
- Create: `backend/Orchestration.Tests/Agents/Legal/CnvRegulatoryHitEnricherTests.cs`

- [ ] **Step 1: Write selector/ranking/budget tests**

Define test inputs as `CnvRegulationEnrichmentHit(QueryIndex, Result)` and assert:

```csharp
result.Enrichments.Select(item => item.DocumentId)
    .Should().Equal("doc-a", "doc-b");
client.DocumentCalls.Should().HaveCount(2);
client.ArticleCalls.Should().HaveCount(2);
```

Cover aggregate ordering across query indices, score descending tie-breakers, `NaN`/infinity/below-`0.40` exclusion, blank document/citation exclusion, article > section > chapter > fallback primary-citation precedence, normalized dedupe, contributing query indices, maximum two candidates/four calls, and one document call for two articles in the same document.

- [ ] **Step 2: Write outcome/failure/cancellation tests**

Cover `Verified`, document-only `Partial`, article-only `Partial`, `Unavailable`, document ID/source/resolution conflict, article locator/source/resolution conflict, missing, malformed, `TimeoutException`, ordinary failure, caller cancellation, document-conflict article skip, truncation lengths/flags, and all three zero limits.

Use assertions such as:

```csharp
enrichment.Status.Should().Be(RegulatoryEvidenceEnrichmentStatuses.Conflict);
enrichment.Document.Should().NotBeNull("conflict content remains auditable");
enrichment.Limitations.Should().Contain(message =>
    message.Contains("no pudo verificarse", StringComparison.OrdinalIgnoreCase));
audit.Article.Attempted.Should().BeFalse();

await act.Should().ThrowAsync<OperationCanceledException>();
```

- [ ] **Step 3: Run enricher tests and verify RED**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter FullyQualifiedName~CnvRegulatoryHitEnricherTests
```

Expected: FAIL because enricher/result/input types do not exist.

- [ ] **Step 4: Implement deterministic candidate construction**

Create:

```csharp
internal sealed record CnvRegulationEnrichmentHit(
    int QueryIndex,
    CnvRegulationSearchResult Result);

internal sealed record CnvRegulatoryHitEnrichmentResult(
    IReadOnlyList<RegulatoryEvidenceEnrichment> Enrichments,
    IReadOnlyList<LegalCnvEnrichmentAudit> Audits,
    IReadOnlyList<string> Warnings);
```

Give `CnvRegulatoryHitEnricher` this constructor so transport and limits remain injected and testable:

```csharp
public sealed class CnvRegulatoryHitEnricher(
    ICnvRegulationMcpClient client,
    IOptions<CnvRegulationMcpOptions> options,
    ILogger<CnvRegulatoryHitEnricher> logger)
```

Eligibility is exactly:

```csharp
double.IsFinite(hit.Result.Score) &&
hit.Result.Score >= 0.40 &&
!string.IsNullOrWhiteSpace(hit.Result.DocumentId) &&
hit.Result.Citations is { Count: > 0 }
```

Build the stable candidate key from normalized document ID plus normalized primary locator. Sort score descending, normalized document ID, locator, then chunk ID, all ordinal; take `MaxEnrichedHits`. Generate `EnrichmentId` deterministically from the candidate key using SHA-256 and lowercase hex rather than `Guid.NewGuid()`.

- [ ] **Step 5: Implement sequential verification and bounded snapshots**

For each selected candidate:

1. Check caller cancellation.
2. Retrieve/cache document when document limit is nonzero.
3. If document identity conflicts, mark conflict and skip article.
4. Retrieve article when locator and article limit exist.
5. Convert successes to application snapshots only after validation.
6. Preserve conflict snapshots for audit but expose them with `Status=Conflict` so review filtering can exclude them.

Use one truncation helper:

```csharp
private static (string Text, int OriginalLength, bool IsTruncated) Bound(
    string text,
    int maxCharacters)
{
    var length = text.Length;
    return length <= maxCharacters
        ? (text, length, false)
        : (text[..maxCharacters], length, true);
}
```

Identity normalization removes diacritics, whitespace, punctuation for locators, trims, and uses uppercase invariant. Exceptions map to `TimedOut` only for `TimeoutException`, `Failed` for ordinary failures, and rethrow caller cancellation. Add Spanish limitations that describe unavailable/partial verification without saying risk is absent, applicability is established, or a violation exists.

- [ ] **Step 6: Run enricher tests and verify GREEN**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter FullyQualifiedName~CnvRegulatoryHitEnricherTests
```

Expected: all deterministic ordering, budget, cache, state, malformed, conflict, timeout, and cancellation cases pass.

- [ ] **Step 7: Commit the enrichment pipeline**

```powershell
git add backend/Orchestration.Infrastructure/Agents/Legal/Regulations/CnvRegulatoryHitEnricher.cs backend/Orchestration.Tests/Agents/Legal/CnvRegulatoryHitEnricherTests.cs
git commit -m "feat: verify selected CNV search hits"
```

### Task 5: Integrate enrichment into Legal review and observability

**Files:**
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/McpRegulatoryKnowledgeSource.cs`
- Modify: `backend/Orchestration.Application/Agents/Legal/AiReview/LegalAnalysisReviewInput.cs`
- Modify: `backend/Orchestration.Application/Agents/Legal/AiReview/DeterministicLegalAnalysisReviewService.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/AiReview/SemanticKernelLegalAnalysisReviewService.cs`
- Modify: `backend/Orchestration.Api/Program.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/McpRegulatoryKnowledgeSourceTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/AiReview/LegalAnalysisReviewContractsTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/AiReview/DeterministicLegalAnalysisReviewServiceTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/AiReview/SemanticKernelLegalAnalysisReviewServiceTests.cs`

- [ ] **Step 1: Write direct-source integration tests**

Add scenarios proving:

- all query search responses are collected before the first retrieval call;
- the global top two, not two per query, are enriched;
- original findings/citations survive unavailable/conflict retrieval;
- only `Verified`/`Partial` non-conflicting canonical context reaches `LegalAnalysisReviewInput`;
- conflict forces human review and keeps `HasComplianceRisk=false`, `RiskLevel=NotEstablished`, and `Applicability=NotEstablished`;
- a cancellation token stops enrichment/review;
- exactly one `legal_cnv_enrichment_completed` event reports selected/verified/partial/conflict/unavailable counts;
- logs contain IDs/status/counts but not the canonical text fixture secret.

- [ ] **Step 2: Write deterministic and semantic review tests**

For deterministic review, pass original evidence plus a verified article and assert the verified article citation is preferred, limitations are appended, and severity is unchanged from issue #7 policy.

For semantic review, capture the prompt and assert separate keys:

```json
{
  "cnvEvidence": [],
  "canonicalRegulatoryContext": []
}
```

Assert conflict content is absent, canonical references are allowlisted, and the prompt contains the disclaimer “no determina aplicabilidad, incumplimiento, riesgo ni asesoramiento legal”.

- [ ] **Step 3: Run Legal integration tests and verify RED**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~McpRegulatoryKnowledgeSourceTests|FullyQualifiedName~LegalAnalysisReviewContractsTests|FullyQualifiedName~DeterministicLegalAnalysisReviewServiceTests|FullyQualifiedName~SemanticKernelLegalAnalysisReviewServiceTests"
```

Expected: FAIL because post-search enrichment and review input propagation do not exist.

- [ ] **Step 4: Invoke enricher once after aggregate search**

Extend `CnvSearchReviewResult` with `IReadOnlyList<CnvRegulationEnrichmentHit> EnrichmentHits`; retain the one-based query index with every result. After `SearchFindingsAsync` completes:

```csharp
var enrichment = await _hitEnricher.EnrichAsync(
    outcome.EnrichmentHits,
    cancellationToken);
```

Add `CnvRegulatoryHitEnricher` to the `McpRegulatoryKnowledgeSource` primary constructor, register it with `builder.Services.AddScoped<CnvRegulatoryHitEnricher>()`, and pass the real or test enricher at every direct source-construction site.

Append `enrichment.Audits` to `LegalQueryStrategyAudit.Enrichments`, `enrichment.Warnings` to result warnings, and `enrichment.Enrichments` to review/result contracts. Compute `safeCanonicalContext` with statuses `Verified` or `Partial`; never pass `Conflict` or `Unavailable` content to review.

Set `RequiresHumanReview` when current issue #7 rules require it or any selected enrichment is `Conflict`, `Partial`, or `Unavailable`. Never change the fixed `HasComplianceRisk=false`, `RiskLevel=NotEstablished`, relevance, applicability, or severity policy.

- [ ] **Step 5: Add review contract behavior**

Append to `LegalAnalysisReviewInput`:

```csharp
IReadOnlyList<RegulatoryEvidenceEnrichment>? EvidenceEnrichments = null
```

Deterministic reference precedence is canonical article, canonical document citation, then original citation. Enrichment limitations join existing limitations with ordinal dedupe. Semantic prompt serialization uses separate `cnvEvidence` and `canonicalRegulatoryContext` arrays; its system/user instructions explicitly forbid treating verification as applicability, non-compliance, risk, or legal advice.

- [ ] **Step 6: Publish bounded activity/log data**

Publish one event after enrichment:

```csharp
new ActivityEvent(
    report.SessionId,
    "legal_cnv_enrichment_completed",
    "LegalAgent",
    $"Verificación de contexto CNV: {selected} seleccionados, {verified} verificados, {partial} parciales, {conflict} con conflicto y {unavailable} no disponibles.",
    DateTimeOffset.UtcNow)
```

Like other activity publication, propagate cancellation and degrade publisher failures to a warning log. Structured logs include `EnrichmentId`, tool/stage, rank, status, and counts only.

- [ ] **Step 7: Run Legal integration tests and verify GREEN**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~McpRegulatoryKnowledgeSourceTests|FullyQualifiedName~LegalAnalysisReviewContractsTests|FullyQualifiedName~DeterministicLegalAnalysisReviewServiceTests|FullyQualifiedName~SemanticKernelLegalAnalysisReviewServiceTests"
```

Expected: selected tests pass and issue #7 legal-risk semantics remain unchanged.

- [ ] **Step 8: Commit Legal integration**

```powershell
git add backend/Orchestration.Application/Agents/Legal backend/Orchestration.Infrastructure/Agents/Legal backend/Orchestration.Tests/Agents/Legal
git commit -m "feat: add canonical context to legal review"
```

### Task 6: Propagate and aggregate enrichments across Legal calls

**Files:**
- Modify: `backend/Orchestration.Application/Agents/Legal/Regulations/RegulatoryReviewResult.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/LegalCompliancePluginResult.cs`
- Modify: `backend/Orchestration.Application/Agents/Legal/LegalAgentResult.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/LegalCompliancePlugin.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/SemanticKernelLegalAgent.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Planner/ToolCalling/ToolExecutionResultMapper.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/LegalCompliancePluginTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Legal/SemanticKernelLegalAgentTests.cs`
- Modify: `backend/Orchestration.Tests/Agents/Planner/ToolCalling/ToolExecutionResultMapperTests.cs`

- [ ] **Step 1: Write direct propagation and mapper aggregation tests**

Append one enrichment to a `RegulatoryReviewResult`; assert plugin and `SemanticKernelLegalAgent` return the same immutable values. For mapper payloads, cover valid/null/malformed collections and order-independent two-call merge. Assert:

```csharp
firstOrder.EvidenceEnrichments.Should().BeEquivalentTo(
    reverseOrder.EvidenceEnrichments,
    options => options.WithStrictOrdering());
```

Duplicate `EnrichmentId` chooses the richer non-conflicting outcome (`Verified` > `Partial` > `Unavailable`), unions limitations, and yields `Conflict` if either duplicate conflicts or stable identity differs.

- [ ] **Step 2: Run propagation/mapper tests and verify RED**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~LegalCompliancePluginTests|FullyQualifiedName~SemanticKernelLegalAgentTests|FullyQualifiedName~ToolExecutionResultMapperTests"
```

Expected: FAIL because result records and mapper payloads omit enrichments.

- [ ] **Step 3: Add trailing compatible fields and direct propagation**

Append to all three result records:

```csharp
IReadOnlyList<RegulatoryEvidenceEnrichment>? EvidenceEnrichments = null
```

Pass `review.EvidenceEnrichments` through `LegalCompliancePlugin`, then `pluginResult.EvidenceEnrichments` through `SemanticKernelLegalAgent`. Do not reclassify status or risk.

- [ ] **Step 4: Validate and merge mapper payloads fail closed**

Extend `LegalAggregatePayload` and snapshot validation. Reject null items, blank IDs, invalid statuses, null nested required collections, negative lengths, text longer than configured bounded maxima, or original length shorter than stored text.

Aggregate with:

```csharp
var enrichments = results
    .SelectMany(result => result.EvidenceEnrichments ?? [])
    .GroupBy(item => item.EnrichmentId, StringComparer.Ordinal)
    .Select(MergeEnrichmentGroup)
    .OrderBy(item => item.Rank)
    .ThenBy(item => item.EnrichmentId, StringComparer.Ordinal)
    .ToArray();
```

`MergeEnrichmentGroup` is order-independent, unions limitations ordinally, preserves original/canonical snapshots, and returns `Conflict` on identity disagreement. Attach the same merged collection to both branches of `AggregateLegalResults` and to `MergeQueryStrategies` audits.

- [ ] **Step 5: Run propagation/mapper tests and verify GREEN**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~LegalCompliancePluginTests|FullyQualifiedName~SemanticKernelLegalAgentTests|FullyQualifiedName~ToolExecutionResultMapperTests"
```

Expected: all selected tests pass, including malformed fail-closed and reversed input order.

- [ ] **Step 6: Commit propagation and aggregation**

```powershell
git add backend/Orchestration.Application/Agents/Legal backend/Orchestration.Infrastructure/Agents backend/Orchestration.Tests/Agents
git commit -m "feat: aggregate legal evidence enrichments"
```

### Task 7: Persist enrichment and audit context

**Files:**
- Modify: `backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs`
- Modify: `backend/Orchestration.Tests/AnalysisSessions/AnalysisOrchestratorContextTests.cs`

- [ ] **Step 1: Write context JSON compatibility and separation tests**

Create a Planner result with one original snippet, verified bounded document/article, truncation metadata, limitation, and audit. Assert:

```csharp
root.GetProperty("compliance")
    .GetProperty("evidenceEnrichments")[0]
    .GetProperty("original")
    .GetProperty("snippet")
    .GetString().Should().Be("snippet original");

root.GetProperty("compliance")
    .GetProperty("evidenceEnrichments")[0]
    .GetProperty("article")
    .GetProperty("text")
    .GetString().Should().Be("texto canónico acotado");
```

Assert query strategy contains stage statuses/limitation codes without canonical text, and a legacy result with null enrichments serializes `evidenceEnrichments` as null or an absent-compatible value accepted by frontend types.

- [ ] **Step 2: Run context tests and verify RED**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter FullyQualifiedName~AnalysisOrchestratorContextTests
```

Expected: FAIL because enrichment and its audit are not projected.

- [ ] **Step 3: Add explicit persistence projections**

Inside `compliance`, add:

```csharp
evidenceEnrichments = plannerResult.LegalResult.EvidenceEnrichments?.Select(item => new
{
    enrichmentId = item.EnrichmentId,
    documentId = item.DocumentId,
    chunkId = item.ChunkId,
    rank = item.Rank,
    score = item.Score,
    original = item.Original,
    document = item.Document,
    article = item.Article,
    status = item.Status,
    limitations = item.Limitations
})
```

Extend `ProjectLegalQueryStrategy` with audit-only fields: candidate/rank/query indices, document/article selection/attempt/cache/status/length/truncation, final status, and limitation codes. Do not project regulatory text into query strategy.

- [ ] **Step 4: Run context tests and verify GREEN**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter FullyQualifiedName~AnalysisOrchestratorContextTests
```

Expected: context tests pass; original and canonical evidence remain separate; old/null data remains compatible.

- [ ] **Step 5: Commit persistence**

```powershell
git add backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs backend/Orchestration.Tests/AnalysisSessions/AnalysisOrchestratorContextTests.cs
git commit -m "feat: persist regulatory context verification"
```

### Task 8: Render verified regulatory context in Legal UI

**Files:**
- Modify: `frontend/src/types/domain.types.ts`
- Create: `frontend/src/utils/regulatoryEvidenceEnrichment.ts`
- Create: `frontend/tests/regulatoryEvidenceEnrichment.test.ts`
- Modify: `frontend/src/components/CompliancePanel.tsx`
- Create: `frontend/tests/compliancePanelEnrichment.test.mjs`

- [ ] **Step 1: Write formatter and component-source tests**

Formatter expectations:

```typescript
assert.equal(formatRegulatoryEnrichmentStatus("Verified"), "Verificado");
assert.equal(formatRegulatoryEnrichmentStatus("Partial"), "Parcial");
assert.equal(formatRegulatoryEnrichmentStatus("Conflict"), "Conflicto");
assert.equal(formatRegulatoryEnrichmentStatus("Unavailable"), "No disponible");
assert.equal(formatRegulatoryEnrichmentStatus("FutureStatus"), "FutureStatus");
```

The component-source test reads `CompliancePanel.tsx` and asserts it contains the heading, disclaimer, `<details>`, `<summary>`, original/canonical labels, truncation notice, and index-qualified limitation key expression. It also asserts the file does not use `dangerouslySetInnerHTML`.

- [ ] **Step 2: Run frontend tests and verify RED**

Run:

```powershell
node --experimental-strip-types --test frontend/tests/*.test.ts frontend/tests/*.test.mjs
```

Expected: FAIL because the formatter and section do not exist.

- [ ] **Step 3: Add nullable frontend contracts and formatter**

Define types matching persisted camelCase JSON. The top-level field is:

```typescript
evidenceEnrichments?: RegulatoryEvidenceEnrichmentContext[] | null;
```

Include original citation/snippet; document/article bounded text, original length, `isTruncated`; status; limitations. Implement the exact formatter table from Step 1 with unknown-value passthrough.

- [ ] **Step 4: Render a separate accessible verification section**

Render only when `(compliance.evidenceEnrichments?.length ?? 0) > 0`. Use:

```tsx
<section aria-labelledby="regulatory-context-verification-title">
  <h3 id="regulatory-context-verification-title">
    Verificación de contexto regulatorio
  </h3>
  <p>
    La verificación documental aporta contexto regulatorio, pero no determina
    aplicabilidad, incumplimiento ni asesoramiento legal.
  </p>
  {enrichments.map((item) => (
    <article key={item.enrichmentId}>
      <strong>{formatRegulatoryEnrichmentStatus(item.status)}</strong>
      <p><b>Fragmento original:</b> {item.original.snippet}</p>
      {(item.document || item.article) && (
        <details>
          <summary>Mostrar contexto canónico verificado</summary>
          {item.document?.text && <p>{item.document.text}</p>}
          {item.article?.text && <p>{item.article.text}</p>}
        </details>
      )}
      {(item.document?.isTruncated || item.article?.isTruncated) && (
        <p>El contexto mostrado fue truncado al límite seguro configurado.</p>
      )}
      <ul>
        {item.limitations.map((limitation, index) => (
          <li key={`${item.enrichmentId}-limitation-${index}`}>{limitation}</li>
        ))}
      </ul>
    </article>
  ))}
</section>
```

Render citation metadata and URLs with normal React text/attributes; keep canonical content collapsed initially; do not render raw HTML.

- [ ] **Step 5: Run frontend tests and production build**

Run:

```powershell
node --experimental-strip-types --test frontend/tests/*.test.ts frontend/tests/*.test.mjs
npm --prefix frontend run build
```

Expected: all Node tests pass; TypeScript and Vite production build pass.

- [ ] **Step 6: Commit frontend presentation**

```powershell
git add frontend/src frontend/tests
git commit -m "feat: show regulatory context verification"
```

### Task 9: Prove the end-to-end acceptance contract

**Files:**
- Modify: `backend/Orchestration.Tests/AnalysisSessions/ProductionLikeWorkflowE2ETests.cs`
- Modify: `docs/superpowers/specs/2026-07-21-cnv-hit-enrichment-design.md` only if implementation reveals a user-approved contract correction.

- [ ] **Step 1: Add one production-like direct/plan-driven proof**

Run a fake MCP search with two eligible hits, canonical success for one and timeout/missing for the other. Assert stored `contextJson` contains separate original/canonical data, partial limitation, audit stage state, bounded text, and unchanged legal policy:

```csharp
legalResult.HasComplianceRisk.Should().BeFalse();
legalResult.RiskLevel.Should().Be("NotEstablished");
legalResult.EvidenceAssessment!.Applicability.Should().Be("NotEstablished");
legalResult.RequiresHumanReview.Should().BeTrue();
```

Exercise plan-driven multi-call aggregation in reversed call order and assert identical enrichment output.

- [ ] **Step 2: Run focused acceptance tests**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~CnvRegulatoryHitEnricherTests|FullyQualifiedName~McpRegulatoryKnowledgeSourceTests|FullyQualifiedName~ToolExecutionResultMapperTests|FullyQualifiedName~AnalysisOrchestratorContextTests|FullyQualifiedName~ProductionLikeWorkflowE2ETests"
```

Expected: PASS for valid, missing, conflicting, timed-out, malformed, direct Legal, plan-driven, and persisted-context paths.

- [ ] **Step 3: Run full MCP verification**

Run:

```powershell
dotnet test tools/CnvRegulation.McpServer/tests/CnvRegulation.Application.Tests/CnvRegulation.Application.Tests.csproj --no-restore
dotnet test tools/CnvRegulation.McpServer/tests/CnvRegulation.McpServer.Tests/CnvRegulation.McpServer.Tests.csproj --no-restore
```

Expected: all non-PostgreSQL tests pass; existing opt-in PostgreSQL tests remain skipped; no database, corpus, embedding, migration, or ingestion operation runs.

- [ ] **Step 4: Run full orchestration and frontend verification**

Run:

```powershell
dotnet test backend/Orchestration.slnx --no-restore
node --experimental-strip-types --test frontend/tests/*.test.ts frontend/tests/*.test.mjs
npm --prefix frontend run build
```

Expected: full backend suite, all frontend Node tests, TypeScript, and Vite build pass. If locked binaries produce `MSB3026`/`MSB3027`, invoke the repo-local `aihitl-temp-output-verification` skill and rerun against isolated output paths.

- [ ] **Step 5: Run legal-language, bounded-data, and repository checks**

Run:

```powershell
rg -n -i "(enrichment|verificaci[oó]n).*(no hay riesgo|sin riesgo|cumple|incumplimiento confirmado|aplicabilidad establecida)" backend frontend tools
rg -n "Mock regulatory text for|GetDocumentOrDefault" tools/CnvRegulation.McpServer/src/CnvRegulation.Infrastructure/InMemory
rg -n "dangerouslySetInnerHTML" frontend/src/components/CompliancePanel.tsx
git diff --check origin/main...HEAD
git status --short
```

Expected: first three scans return no prohibited matches; `git diff --check` prints nothing; status shows only the intended final test change before commit.

- [ ] **Step 6: Commit integrated acceptance proof**

```powershell
git add backend/Orchestration.Tests/AnalysisSessions/ProductionLikeWorkflowE2ETests.cs
git commit -m "test: prove CNV hit enrichment workflow"
```

- [ ] **Step 7: Re-run clean final gate**

Run:

```powershell
git diff --check origin/main...HEAD
git status --short --branch
git log --oneline origin/main..HEAD
```

Expected: no whitespace errors; worktree clean; branch contains the design, plan, implementation, UI, and acceptance-proof commits only.
