# Legal Retrieval and Compliance-Risk Separation Design

**Issue:** [#7 — Do not equate retrieved citations with compliance risk](https://github.com/EzeMartino/ai-orchestration-hitl/issues/7)

**Status:** Approved for implementation planning

## Problem

`McpRegulatoryKnowledgeSource` currently treats any cited MCP search result as a compliance risk. `Findings.Count > 0` sets `HasComplianceRisk=true` and `RiskLevel="Medium"`, even though retrieval proves only that text matched a query. The deterministic legal review also associates every cited reference with every financial signal and inherits financial severity without establishing regulatory relevance or applicability.

This conflates four distinct states: retrieval, relevance, evidence quality, and legal applicability. It can produce false-positive risk, misleading severity, and Planner approval decisions driven solely by search presence.

## Goals

- Represent retrieved evidence separately from assessed compliance risk.
- Evaluate relevance, applicability, and evidence quality with explicit deterministic rules.
- Label supported output as evidence found or a possible review area, never as a violation or legal advice.
- Require human approval when relevant evidence remains legally unassessed.
- Preserve retrieved citations for traceability while excluding irrelevant citations from legal review areas.
- Cover irrelevant citations, weak evidence, strong evidence, and empty results.

## Non-goals

- Determining whether a company or transaction is legally subject to a retrieved rule.
- Automating a legal conclusion, violation finding, or legal recommendation.
- Inferring jurisdiction, regulated-entity status, effective dates, exceptions, or materiality from search results.
- Replacing MCP search ranking or the financial-risk model.
- Assigning `Medium` or `High` legal severity when applicability is not established.

## Assessment Contract

Add a shared Application-layer record:

```csharp
public sealed record RegulatoryEvidenceAssessment(
    bool EvidenceFound,
    string Relevance,
    string Applicability,
    string EvidenceQuality,
    string Severity,
    bool RequiresHumanReview,
    IReadOnlyList<string> Reasons);
```

The allowed values are:

- `Relevance`: `None`, `Weak`, `Strong`.
- `Applicability`: `NotEstablished`.
- `EvidenceQuality`: `None`, `Weak`, `Strong`.
- `Severity`: `Info`, `Warning`.

`RegulatoryReviewResult`, `LegalCompliancePluginResult`, and `LegalAgentResult` gain a trailing optional `EvidenceAssessment` member. The optional position preserves existing test and mock construction while MCP-backed production results always provide it.

`LegalAnalysisReviewInput` also gains the assessment so deterministic and LLM-backed review use the same bounded criteria.

## Deterministic Classification Rules

Each MCP search result is evaluated independently, then aggregated conservatively.

### Retrieval

- `EvidenceFound=true` when MCP returns at least one result, including an uncited or irrelevant result.
- `EvidenceFound=false` only when all searches return no results.

### Relevance

Use the MCP result score only as retrieval relevance, never as legal applicability:

- `None`: score is absent or below `0.40`.
- `Weak`: score is at least `0.40` and below `0.75`.
- `Strong`: score is at least `0.75`.

The aggregate relevance is the strongest level observed.

### Evidence quality

- `None`: no citation exists.
- `Weak`: a citation exists but lacks one or more strong-quality fields.
- `Strong`: at least one citation has a non-empty source, title, normative locator (`Article`, `Section`, or `Chapter`), URL, and quoted text.

The aggregate quality is the strongest level observed. Quality describes traceability only; it does not establish applicability.

### Applicability and severity

Automated retrieval lacks the facts needed to establish applicability, so it is always `NotEstablished` in this workflow.

- `Severity=Info` when evidence is empty or retrieval relevance is `None`.
- `Severity=Warning` when relevance is `Weak` or `Strong`, regardless of evidence quality, because applicability remains unestablished.
- `RequiresHumanReview=true` when relevance is `Weak` or `Strong`.
- `RequiresHumanReview=false` for empty or irrelevant results.

`HasComplianceRisk` remains `false` and `RiskLevel` becomes `NotEstablished` for all MCP retrieval-only outcomes. A future workflow may set risk only after adding an explicit applicability input and separate approved policy; this issue does not create that path.

## Search and Review Data Flow

1. MCP queries return raw search results.
2. A focused infrastructure assessor class evaluates each result and produces the aggregate `RegulatoryEvidenceAssessment` with stable reasons.
3. All cited results remain mapped into top-level findings for retrieval traceability.
4. Only citations with `Weak` or `Strong` retrieval relevance enter `LegalAnalysisReviewInput.CnvEvidence`; irrelevant citations cannot create possible review areas.
5. Deterministic legal review uses the supplied assessment severity rather than copying financial-signal severity.
6. Semantic legal review receives the criteria in its JSON prompt. Parsed areas are normalized to the deterministic assessment severity, so an LLM cannot escalate `Warning` to `Medium` or `High`.
7. Summaries and warnings describe evidence found, limited evidence, possible review areas, and required human review. They do not state that no risk exists merely because search was empty.

## Human-Approval Behavior

Planner approval must no longer depend only on `LegalAgentResult.HasComplianceRisk`. Its rule becomes:

```csharp
var requiresHumanApproval =
    dataResult.HasAnomaly ||
    dataResult.RequiresHumanReview ||
    legalResult.HasComplianceRisk ||
    legalResult.EvidenceAssessment?.RequiresHumanReview == true;
```

Thus relevant but unassessed legal evidence pauses the workflow without being mislabeled as compliance risk. Empty and irrelevant retrieval do not independently trigger approval.

## Persisted and Frontend Contract

`AnalysisOrchestratorService` serializes the assessment under `compliance.evidenceAssessment`. Existing fields remain available:

- `riskDetected` remains `false` for retrieval-only outcomes.
- `riskLevel` is `NotEstablished`.
- `evidence` continues to contain cited retrieved material.
- `legalReview` contains only relevant citations and possible review areas.

The frontend type mirrors the assessment record. `CompliancePanel` renders a compact assessment block showing evidence state, relevance, applicability, quality, severity, and whether human review is required. Labels explicitly state that retrieval is not a compliance conclusion. The risk badge renders `NotEstablished` as `No determinado` rather than exposing the internal value.

## Language Safety

User-visible output must use cautious Spanish language:

- “evidencia regulatoria recuperada”;
- “posible área de revisión”;
- “aplicabilidad no determinada”;
- “requiere revisión legal humana”.

No summary, warning, review-area description, or activity message may declare a violation, breach, guilt, fraud, illegality, or provide legal advice. Existing Semantic Kernel forbidden-language fallback remains active after severity normalization.

The forbidden-language rule set adds Spanish definitive phrases such as `es ilegal`, `infringe la normativa`, `incumplimiento confirmado`, `violación legal confirmada`, `culpable`, and `cometió fraude`. Disclaimer phrases such as `no constituye asesoramiento legal` remain allowed and must not trigger fallback.

## Error Behavior

- MCP transport failures remain safe warnings without exception details.
- A failed query does not imply risk, relevance, or applicability.
- Mixed successful and failed queries aggregate only returned results and retain failure warnings.
- Missing scores classify relevance as `None`.
- Missing citations classify quality as `None`; cited but incomplete evidence classifies as `Weak`.
- Duplicate findings and references remain deduplicated before output.

## Testing Strategy

### Evidence assessment

- Irrelevant cited result: evidence found, relevance `None`, quality determined independently, severity `Info`, no risk, no legal review area.
- Weak evidence: relevant score with incomplete citation, quality `Weak`, severity `Warning`, no risk, human review required.
- Strong evidence: strong score and complete citation, quality `Strong`, severity `Warning`, no risk, human review required.
- Empty results: no evidence, `None` relevance/quality, `Info`, no risk, no review area.
- Missing score and uncited results follow conservative `None` classifications.

### Legal review safety

- Deterministic review uses assessment severity instead of financial severity.
- Semantic output attempting `Medium` or `High` is normalized to `Warning` while applicability is unestablished.
- Irrelevant citations are excluded from review areas.
- Existing invented-citation pruning and forbidden-language fallback remain covered.
- Spanish definitive legal claims trigger fallback, while advisory disclaimers do not.

### Workflow and serialization

- Relevant weak/strong evidence sets Planner human approval while `HasComplianceRisk=false`.
- Empty/irrelevant retrieval does not independently set approval.
- Plugin, agent, persisted context, and frontend types preserve the complete assessment.
- Production-like workflow assertions distinguish `riskDetected` from `requiresHumanReview`.

### Verification

- Run focused legal-source, legal-review, Planner, and context tests with temporary .NET output paths.
- Run the full backend test project.
- Run frontend tests/type checks and production build.
- Run `git diff --check` and inspect the final diff for unsafe legal language.

## Compatibility

- New record parameters are trailing and optional outside MCP-backed production results.
- Existing persisted contexts without `evidenceAssessment` remain readable because the frontend field is optional.
- Existing mock legal sources retain their explicitly modeled risk behavior; this change targets MCP retrieval-only inference.
- No database migration is required.

## Acceptance Mapping

- Retrieval distinct from risk: `EvidenceFound` is independent from `HasComplianceRisk`, which stays false without applicability.
- Evidence labeling: summaries, warnings, frontend labels, and review areas use retrieval/review language.
- Explicit severity criteria: score thresholds, citation completeness, and `NotEstablished` applicability determine `Info` or `Warning`.
- No violation/advice: language rules plus Semantic Kernel fallback prevent definitive legal claims.
- Human review: assessment flows into Planner approval independently from compliance risk.
- Required coverage: irrelevant, weak, strong, and empty cases are explicit test scenarios.
