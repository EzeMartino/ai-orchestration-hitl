# PDF Financial Extraction Quality Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve PDF financial metric coverage without LLM extraction by adding deterministic extraction diagnostics, broader aliases/table parsing, and grouped warnings.

**Architecture:** Keep PDF ingestion as a preprocessing step into `StructuredFinancialMetricsInput`. Improve deterministic parsing in Application/Infrastructure, keep ratio computation in Python, and surface grouped warning summaries in the frontend. LLM review/correction is explicitly future scope and must not extract or mutate metrics in this phase.

**Tech Stack:** ASP.NET Core, C# records/services, PdfPig, xUnit/FluentAssertions, CSnakes Python tests, React/Vite/TypeScript.

---

### Task 1: Deterministic Parser Coverage

**Files:**
- Modify: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsTextParserTests.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsTextParser.cs`

- [ ] **Step 1: Write RED tests for Spanish and accounting aliases**

Add parser tests that verify `Ingresos`, `Ventas netas`, `Ganancia bruta`, `Resultado operativo`, `Pasivo corriente`, `Activo corriente`, `Deuda financiera total`, `Patrimonio neto`, `Deuda neta`, and `Flujo de caja libre` map to canonical metric names.

- [ ] **Step 2: Run parser tests and confirm failure**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "StructuredFinancialMetricsTextParserTests"
```

Expected: new alias tests fail because aliases are not recognized.

- [ ] **Step 3: Expand alias catalog and value parsing**

Update `StructuredFinancialMetricsTextParser` to use a richer alias table, sort aliases by longest first, and parse common accounting forms including `1.234,56`, `1,234.56`, `(1.234)`, and percentage values where needed.

- [ ] **Step 4: Run parser tests and confirm pass**

Run the same parser test command. Expected: pass.

### Task 2: PDF Text Row Quality

**Files:**
- Modify: `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/Pdf/PdfPigTextExtractor.cs`
- Modify: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/StructuredFinancialMetricsTextParserTests.cs`
- Modify: `backend/Orchestration.Application/Agents/Data/FinancialAnalysis/Input/StructuredFinancialMetricsTextParser.cs`

- [ ] **Step 1: Write RED tests for real PDF table shapes**

Add parser tests for rows where text extraction keeps columns but uses Spanish labels and rows like `Ventas netas 2021A 10.000 2022A 12.000`, plus header rows like `Concepto 2021A 2022A`.

- [ ] **Step 2: Run parser tests and confirm failure**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "StructuredFinancialMetricsTextParserTests"
```

Expected: new table-shape tests fail.

- [ ] **Step 3: Improve row parsing**

Parse period/value pairs when period tokens and values appear interleaved, and keep existing header-based parsing for `Metric 2024A 2025E` tables.

- [ ] **Step 4: Improve PdfPig row text**

Build page text from PdfPig words grouped by vertical position, sorted left-to-right, with fallback to `page.Text` when word grouping is empty.

- [ ] **Step 5: Run parser/PDF extractor tests**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "StructuredFinancialMetricsTextParserTests|StructuredFinancialMetricsPdfExtractorTests"
```

Expected: pass.

### Task 3: Ratio Direct Fallback

**Files:**
- Modify: `python-agents/tests/test_financial_analysis.py`
- Modify: `python-agents/data_agent/financial_analysis.py`

- [ ] **Step 1: Write RED tests for direct reported ratios**

Add tests showing a metric named `gross_margin` in period `2024A` is returned as a ratio when `gross_profit` or `revenue` is missing.

- [ ] **Step 2: Run Python tests and confirm failure**

Run:

```powershell
python -m unittest python-agents/tests/test_financial_analysis.py
```

Expected: direct ratio test fails because ratio computation currently requires numerator/denominator inputs.

- [ ] **Step 3: Implement direct ratio fallback**

In `_compute_ratio`, check for a direct metric matching the requested ratio before deriving from numerator/denominator. Return `source: "reported"` and preserve source confidence.

- [ ] **Step 4: Run Python tests and confirm pass**

Run the same unittest command. Expected: pass.

### Task 4: Grouped Warning Presentation

**Files:**
- Create: `frontend/src/utils/financialWarnings.ts`
- Modify: `frontend/src/components/FinancialRiskEvidencePanel.tsx`
- Modify: `frontend/src/types/domain.types.ts` only if a type helper is needed
- Modify: `frontend/src/App.css`

- [ ] **Step 1: Write a small RED helper check**

Before implementation, use a temporary Node check or TypeScript helper test to confirm warning strings such as `Missing numerator input for gross_margin in 2025A.` are grouped by period and ratio.

- [ ] **Step 2: Implement warning grouping helper**

Create `groupFinancialWarnings(warnings: string[])` that returns grouped missing-input summaries and an `ungrouped` list for non-matching warnings.

- [ ] **Step 3: Render grouped warnings**

Replace the raw warnings list in `FinancialRiskEvidencePanel` with concise grouped summaries and details. Keep all original warning text accessible in expandable details.

- [ ] **Step 4: Build frontend**

Run:

```powershell
cd frontend
npm run build
```

Expected: TypeScript and Vite build pass.

### Task 5: Verification

**Files:**
- No new files.

- [ ] **Step 1: Run targeted backend tests**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter "StructuredFinancialMetricsTextParserTests|StructuredFinancialMetricsPdfExtractorTests|FinancialAnalysisPluginTests"
```

Expected: pass.

- [ ] **Step 2: Run Python tests**

Run:

```powershell
python -m unittest python-agents/tests/test_financial_analysis.py
```

Expected: pass.

- [ ] **Step 3: Run frontend build**

Run:

```powershell
cd frontend
npm run build
```

Expected: pass.

- [ ] **Step 4: Run full backend tests if targeted checks pass**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj
```

Expected: pass or report unrelated pre-existing failures separately.
