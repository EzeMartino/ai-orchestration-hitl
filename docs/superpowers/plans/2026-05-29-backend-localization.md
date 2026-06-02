# Backend Localization Spanish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Localize user-facing English strings to Spanish in the backend .NET orchestration, agent services, data workflows, CNV regulatory retrieved evidence, and risk threshold descriptions, and update all affected unit/integration tests to ensure they continue to pass.

**Architecture:** Replace hardcoded user-facing English strings with their Spanish equivalents while keeping all system IDs, codes, event types, and internal parameters intact in English. Update relevant xUnit/FluentAssertions tests to expect Spanish strings.

**Tech Stack:** .NET 10.0, xUnit, FluentAssertions

---

### Task 1: AnalysisOrchestratorService Localization

**Files:**
- Modify: `backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs:49-335`

- [ ] **Step 1: Localize strings in AnalysisOrchestratorService.cs**
  Modify [AnalysisOrchestratorService.cs](file:///c:/Repositories/ai-orchestration-hitl/backend/Orchestration.Application/AnalysisSessions/AnalysisOrchestratorService.cs) to translate the specified strings:
  - In `StartAnalysisAsync` (line ~49): Change `"Starting analysis session."` to `"Iniciando sesión de análisis."`
  - In `StartAnalysisAsync` (line ~67): Change `"Session moved to {session.Status}."` to `"La sesión cambió al estado: {session.Status}."`
  - In `StartAnalysisAsync` (line ~94): Change `"Execution paused. Waiting for human auditor approval."` to `"Ejecución pausada. Esperando la aprobación del auditor humano."`
  - In `StartAnalysisAsync` (line ~102): Change `"Session moved to {session.Status}."` to `"La sesión cambió al estado: {session.Status}."`
  - In `StartAnalysisAsync` (line ~123): Change `"Session moved to {session.Status}."` to `"La sesión cambió al estado: {session.Status}."`
  - In `StartAnalysisAsync` (line ~131): Change `"Analysis completed without requiring human approval."` to `"Análisis completado sin requerir aprobación humana."`
  - In `ApproveAsync` (line ~160): Change `"Approval received. Reason: {request.Reason ?? \"No reason provided.\"}"` to `"Aprobación recibida. Motivo: {request.Reason ?? \"No se proporcionó ningún motivo.\"}"`
  - In `ApproveAsync` (line ~173): Change `"Session moved to {session.Status}."` to `"La sesión cambió al estado: {session.Status}."`
  - In `ApproveAsync` (line ~181): Change `"Analysis completed after human approval."` to `"Análisis completado tras la aprobación humana."`
  - In `RejectAsync` (line ~210): Change `"Rejection received. Reason: {request.Reason ?? \"No reason provided.\"}"` to `"Rechazo recibido. Motivo: {request.Reason ?? \"No se proporcionó ningún motivo.\"}"`
  - In `RejectAsync` (line ~216): Change `"Rejected by human auditor."` to `"Rechazado por el auditor humano."`
  - In `RejectAsync` (line ~224): Change `"Session moved to {session.Status}."` to `"La sesión cambió al estado: {session.Status}."`
  - In `RejectAsync` (line ~232): Change `"Analysis was rejected by the human auditor."` to `"El análisis fue rechazado por el auditor humano."`
  - In `BuildAnalysisContext` (line ~334): Change `"Human approval is required before continuing the analysis."` to `"Se requiere aprobación humana antes de continuar con el análisis."`
  - In `BuildAnalysisContext` (line ~335): Change `"No human approval is required based on the current data analysis."` to `"No se requiere aprobación humana según el análisis de datos actual."`

- [ ] **Step 2: Compile the Orchestration project**
  Run: `dotnet build backend/Orchestration.slnx`
  Expected: Successful build without errors.

---

### Task 2: PlannerAgent Localization

**Files:**
- Modify: `backend/Orchestration.Application/Agents/Planner/PlannerAgent.cs:60-613`

- [ ] **Step 1: Localize strings in PlannerAgent.cs**
  Modify [PlannerAgent.cs](file:///c:/Repositories/ai-orchestration-hitl/backend/Orchestration.Application/Agents/Planner/PlannerAgent.cs) to translate the specified strings:
  - In `RunAsync` (line ~60): Change `"PlannerAgent initialized. Building execution plan."` to `"PlannerAgent (Agente Planificador) inicializado. Construyendo plan de ejecución."`
  - In `RunPlanDrivenModeAsync` (line ~173): Change `"Plan-driven execution failed; deterministic agent path was used."` to `"La ejecución basada en plan falló; se utilizó la ruta determinista del agente."`
  - In `RunDeterministicAgentsAsync` (line ~211): Change `"Delegating anomaly detection to DataAgent."` to `"Delegando detección de anomalías a DataAgent (Agente de Datos)."`
  - In `RunDeterministicAgentsAsync` (line ~224): Change `"Anomaly detection completed using {dataResult.Engine}."` to `"Detección de anomalías completada usando {dataResult.Engine}."`
  - In `RunDeterministicAgentsAsync` (line ~240): Change `"Delegating compliance review to LegalAgent."` to `"Delegando revisión de cumplimiento normativo a LegalAgent (Agente Legal)."`
  - In `RunDeterministicAgentsAsync` (line ~257): Change `"Compliance review completed using {legalResult.Engine}."` to `"Revisión de cumplimiento completada usando {legalResult.Engine}."`
  - In `CompletePlannerAsync` (line ~332): Change `"PlannerAgent determined that human approval is required before completing the workflow."` to `"PlannerAgent determinó que se requiere aprobación humana antes de completar el flujo de trabajo."`
  - In `CompletePlannerAsync` (line ~333): Change `"PlannerAgent determined that the workflow can be completed without human intervention."` to `"PlannerAgent determinó que el flujo de trabajo puede completarse sin intervención humana."`
  - In `BuildExecutionAuditAsync` (line ~380): Change `"Tool execution failed."` to `"La ejecución de la herramienta falló."`
  - In `CreateMissingExecutionResult` (line ~417): Change `"Tool execution result was not returned."` to `"No se devolvió el resultado de ejecución de la herramienta."`
  - In `PublishToolPlanAuditEventsAsync` (line ~443): Change `"PlannerAgent proposed {toolPlan.ProposedCalls.Count} tool calls."` to `"PlannerAgent propuso {toolPlan.ProposedCalls.Count} llamadas a herramientas."`
  - In `PublishToolPlanAuditEventsAsync` (line ~454): Change `"Tool plan validated: {toolPlan.ApprovedCalls.Count} approved, {toolPlan.RejectedCalls.Count} rejected."` to `"Plan de herramientas validado: {toolPlan.ApprovedCalls.Count} aprobadas, {toolPlan.RejectedCalls.Count} rechazadas."`
  - In `PublishToolPlanAuditEventsAsync` (line ~465): Change `"Rejected tool call '{rejectedCall.ToolName}': {rejectedCall.Reason}"` to `"Llamada a herramienta '{rejectedCall.ToolName}' rechazada: {rejectedCall.Reason}"`
  - In `PublishToolPlanAuditEventsAsync` (line ~478): Change `"Skipped approved tool call '{executionAudit.ToolName}': {executionAudit.Summary}"` to `"Llamada a herramienta aprobada '{executionAudit.ToolName}' omitida: {executionAudit.Summary}"`
  - In `PublishToolPlanAuditEventsAsync` (line ~489): Change `"Executed approved tool call '{executionAudit.ToolName}' using {executionAudit.Engine}."` to `"Llamada a herramienta aprobada '{executionAudit.ToolName}' ejecutada usando {executionAudit.Engine}."`
  - In `PublishToolPlanAuditEventsAsync` (line ~500): Change `"Approved tool call '{executionAudit.ToolName}' failed: {executionAudit.Error ?? executionAudit.Summary}"` to `"Llamada a herramienta aprobada '{executionAudit.ToolName}' falló: {executionAudit.Error ?? executionAudit.Summary}"`
  - In `GetReasoningEventMessage` (line ~536): Change `"LLM reasoning failed; deterministic fallback was used."` to `"El razonamiento del LLM falló; se utilizó la alternativa determinista de contingencia."`
  - In `GetReasoningEventMessage` (line ~541): Change `"Planner reasoning completed using {reasoningResult.Engine}."` to `"Razonamiento del Planificador completado usando {reasoningResult.Engine}."`
  - In `GetReasoningEventMessage` (line ~544): Change `"Planner reasoning completed using deterministic fallback."` to `"Razonamiento del Planificador completado usando la alternativa determinista."`
  - In `BuildInitialToolPlanProposalInput` (line ~608): Change `"Collect read-only financial anomaly and regulatory retrieval evidence before planner reasoning."` to `"Recopilar evidencia de anomalías financieras y recuperación regulatoria antes del razonamiento del planificador."`
  - In `BuildInitialToolPlanProposalInput` (line ~612): Change `"The LLM may propose tools only; workflow control remains deterministic."` to `"El LLM puede proponer herramientas únicamente; el control del flujo de trabajo sigue siendo determinista."`
  - In `BuildInitialToolPlanProposalInput` (line ~613): Change `"Human approval remains mandatory when risk exists."` to `"La aprobación humana sigue siendo obligatoria cuando existe riesgo."`

- [ ] **Step 2: Compile the Orchestration project**
  Run: `dotnet build backend/Orchestration.slnx`
  Expected: Successful build without errors.

---

### Task 3: DataAgentFinancialAnalysisWorkflow Localization & Tests

**Files:**
- Modify: `backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/DataAgentFinancialAnalysisWorkflow.cs:17-357`
- Modify: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/DataAgentFinancialAnalysisWorkflowTests.cs:100-242`

- [ ] **Step 1: Localize strings in DataAgentFinancialAnalysisWorkflow.cs**
  Modify [DataAgentFinancialAnalysisWorkflow.cs](file:///c:/Repositories/ai-orchestration-hitl/backend/Orchestration.Infrastructure/Agents/Data/FinancialAnalysis/DataAgentFinancialAnalysisWorkflow.cs) to translate the specified strings:
  - In `FinancialMetricsInputLimitation` (line ~17): Change `"Financial analysis can parse PDFs and run OCR when needed, but structured JSON or CSV uploads are recommended to preserve data fidelity. It does not make operational decisions."` to `"El análisis financiero puede procesar archivos PDF y ejecutar OCR de ser necesario, pero se recomiendan cargas estructuradas en JSON o CSV para preservar la fidelidad de los datos. No toma decisiones operativas."`
  - In `FixtureFallbackWarning` (line ~19): Change `"Fixture fallback metrics were used. This mode is intended for development/demo only."` to `"Se utilizaron métricas de prueba predefinidas (fixtures). Este modo está destinado únicamente a desarrollo y demostración."`
  - In `RequiredMetricsWarning` (line ~21): Change `"Structured financial metrics are required for this mode but were not attached to the session."` to `"Se requieren métricas financieras estructuradas para este modo, pero no se adjuntaron a la sesión."`
  - In `AnalyzeAsync` (line ~105): Change `"Structured financial metrics were required but not attached to this session."` to `"Se requerían métricas financieras estructuradas, pero no se adjuntaron a esta sesión."`
  - In `AnalyzeAsync` (line ~173): Change `"Financial analysis used fixture fallback metrics because no session metrics were attached."` to `"El análisis financiero utilizó métricas predefinidas de prueba (fixture) porque no se adjuntaron métricas a la sesión."`
  - In `NoMetricsResult` (line ~237): Change `"Structured financial metrics were not available. Human review recommended."` to `"Las métricas financieras estructuradas no estaban disponibles. Se recomienda una revisión humana."`
  - In `NoMetricsResult` (line ~245): Change `"No structured financial metrics were available for quantitative analysis."` to `"No había métricas financieras estructuradas disponibles para el análisis cuantitativo."`
  - In `NoMetricsResult` (line ~256): Change `"Structured financial metrics were not available."` to `"Las métricas financieras estructuradas no estaban disponibles."`
  - In `RequiredMetricsMissingResult` (line ~270): Change `"Structured financial metrics are required but were not attached to this session."` to `"Se requieren métricas financieras estructuradas, pero no se adjuntaron a esta sesión."`
  - In `RequiredMetricsMissingResult` (line ~278): Change `"Structured financial metrics are required for this mode but were not attached to the session."` to `"Se requieren métricas financieras estructuradas para este modo, pero no se adjuntaron a la sesión."`
  - In `RequiredMetricsMissingResult` (line ~292): Change `"No financial ratios or period comparisons were computed because no structured metrics were available."` to `"No se calcularon índices financieros ni comparaciones de períodos porque no había métricas estructuradas disponibles."`
  - In `BuildSummary` (line ~357): Change `"Financial risk signals detected. Human review recommended."` to `"Se detectaron señales de riesgo financiero. Se recomienda revisión humana."`

- [ ] **Step 2: Update assertions in DataAgentFinancialAnalysisWorkflowTests.cs**
  Modify [DataAgentFinancialAnalysisWorkflowTests.cs](file:///c:/Repositories/ai-orchestration-hitl/backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/DataAgentFinancialAnalysisWorkflowTests.cs) to match the Spanish translations:
  - In `AnalyzeAsync_Should_return_safe_result_when_metrics_are_unavailable` (line ~100): Change `"Structured financial metrics were not available. Human review recommended."` to `"Las métricas financieras estructuradas no estaban disponibles. Se recomienda una revisión humana."`
  - In `AnalyzeAsync_Should_return_safe_result_when_metrics_are_unavailable` (line ~104): Change `"Structured financial metrics were not available."` to `"Las métricas financieras estructuradas no estaban disponibles."`
  - In `AnalyzeAsync_Should_return_required_metrics_result_when_metrics_are_missing_and_required` (line ~138): Change `"Structured financial metrics are required but were not attached to this session."` to `"Se requieren métricas financieras estructuradas, pero no se adjuntaron a esta sesión."`
  - In `AnalyzeAsync_Should_return_required_metrics_result_when_metrics_are_missing_and_required` (line ~141-143): Change `"Structured financial metrics are required for this mode but were not attached to the session."` to `"Se requieren métricas financieras estructuradas para este modo, pero no se adjuntaron a la sesión."`
  - In `AnalyzeAsync_Should_return_required_metrics_result_when_metrics_are_missing_and_required` (line ~145-146): Change `"No financial ratios or period comparisons were computed because no structured metrics were available."` to `"No se calcularon índices financieros ni comparaciones de períodos porque no había métricas estructuradas disponibles."`
  - In `AnalyzeAsync_Should_store_fixture_metrics_input_source` (line ~241-243): Change `"Fixture fallback metrics were used. This mode is intended for development/demo only."` to `"Se utilizaron métricas de prueba predefinidas (fixtures). Este modo está destinado únicamente a desarrollo y demostración."`

- [ ] **Step 3: Run the Data Agent workflow tests**
  Run: `dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter DataAgentFinancialAnalysisWorkflowTests`
  Expected: PASS

---

### Task 4: McpRegulatoryKnowledgeSource Localization & Tests

**Files:**
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/McpRegulatoryKnowledgeSource.cs:84-376`
- Modify: `backend/Orchestration.Tests/Agents/Legal/McpRegulatoryKnowledgeSourceTests.cs:38-639`

- [ ] **Step 1: Localize strings in McpRegulatoryKnowledgeSource.cs**
  Modify [McpRegulatoryKnowledgeSource.cs](file:///c:/Repositories/ai-orchestration-hitl/backend/Orchestration.Infrastructure/Agents/Legal/Regulations/McpRegulatoryKnowledgeSource.cs) to translate the specified strings:
  - In `ReviewAsync` (line ~84): Change `"LegalAgent derived CNV search queries from financial analysis risk signals."` to `"LegalAgent derivó consultas de búsqueda CNV a partir de las señales de riesgo del análisis financiero."`
  - In `ReviewAsync` (line ~105): Change `"No specific financial risk signals were available; using a general financial reporting query."` to `"No había señales de riesgo financiero específicas disponibles; utilizando una consulta general de información financiera."`
  - In `ReviewAsync` (line ~112): Change `"CNV regulatory evidence was found for the submitted financial anomaly. Human legal review is required."` to `"Se encontró evidencia regulatoria de la CNV para la anomalía financiera enviada. Se requiere revisión legal humana."`
  - In `ReviewAsync` (line ~113): Change `"No CNV regulatory evidence with citations was found for the submitted financial anomaly."` to `"No se encontró evidencia regulatoria de la CNV con citas para la anomalía financiera enviada."`
  - In `ReviewAsync` (line ~148): Change `"LegalAgent AI review completed using LLM."` to `"Revisión de IA de LegalAgent completada usando LLM."`
  - In `ReviewAsync` (line ~151): Change `"LegalAgent AI review was not executed."` to `"La revisión de IA de LegalAgent no fue ejecutada."`
  - In `ReviewAsync` (line ~156): Change `"LegalAgent AI review completed using deterministic fallback."` to `"Revisión de IA de LegalAgent completada usando la alternativa determinista."`
  - In `SearchFindingsAsync` (line ~349): Change `"CNV MCP search failed for one query. See application logs for details."` to `"La búsqueda en la CNV a través de MCP falló para una consulta. Consulte los registros de la aplicación para más detalles."`
  - In `SearchFindingsAsync` (line ~355): Change `"Some CNV/Infoleg search results were ignored as strong evidence because they did not include citations."` to `"Algunos resultados de búsqueda de CNV/Infoleg se ignoraron como evidencia sólida debido a que no incluían citas."`
  - In `SearchFindingsAsync` (line ~372): Change `"Automated regulatory retrieval only. Human legal review is required before making operational decisions."` to `"Recuperación regulatoria automatizada únicamente. Se requiere una revisión legal humana antes de tomar decisiones operativas."`
  - In `SearchFindingsAsync` (line ~376): Change `"No cited CNV regulatory evidence was found by the MCP search strategy."` to `"No se encontró evidencia regulatoria citada de la CNV mediante la estrategia de búsqueda MCP."`

- [ ] **Step 2: Update assertions in McpRegulatoryKnowledgeSourceTests.cs**
  Modify [McpRegulatoryKnowledgeSourceTests.cs](file:///c:/Repositories/ai-orchestration-hitl/backend/Orchestration.Tests/Agents/Legal/McpRegulatoryKnowledgeSourceTests.cs) to match the Spanish translations:
  - In `ReviewAsync_Should_map_cited_mcp_results_to_regulatory_findings` (line ~37-39): Change `"Automated regulatory retrieval only. Human legal review is required before making operational decisions."` to `"Recuperación regulatoria automatizada únicamente. Se requiere una revisión legal humana antes de tomar decisiones operativas."`
  - In `ReviewAsync_Should_ignore_mcp_results_without_citations` (line ~80-82): Change `"No cited CNV regulatory evidence was found by the MCP search strategy."` to `"No se encontró evidencia regulatoria citada de la CNV mediante la estrategia de búsqueda MCP."`
  - In `ReviewAsync_Should_include_mcp_response_warnings` (line ~96-98): Change `"Automated regulatory retrieval only. Human legal review is required before making operational decisions."` to `"Recuperación regulatoria automatizada únicamente. Se requiere una revisión legal humana antes de tomar decisiones operativas."`
  - In `ReviewAsync_Should_use_safe_warning_when_mcp_search_throws` (line ~510): Change `"CNV MCP search failed for one query. See application logs for details."` to `"La búsqueda en la CNV a través de MCP falló para una consulta. Consulte los registros de la aplicación para más detalles."`
  - In `ReviewAsync_Should_warn_and_ignore_uncited_evidence_as_strong_support` (line ~610): Change `"Some CNV/Infoleg search results were ignored as strong evidence because they did not include citations."` to `"Algunos resultados de búsqueda de CNV/Infoleg se ignoraron como evidencia sólida debido a que no incluían citas."`
  - In `ReviewAsync_Should_still_work_with_fallback_when_financialAnalysis_missing` (line ~639): Change `"No specific financial risk signals were available; using a general financial reporting query."` to `"No había señales de riesgo financiero específicas disponibles; utilizando una consulta general de información financiera."`

- [ ] **Step 3: Run the Legal Agent regulatory tests**
  Run: `dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter McpRegulatoryKnowledgeSourceTests`
  Expected: PASS

---

### Task 5: InMemoryFinancialRiskThresholdProfileProvider Localization & Tests

**Files:**
- Modify: `backend/Orchestration.Application/FinancialAnalysis/Thresholds/InMemoryFinancialRiskThresholdProfileProvider.cs:10-123`
- Modify: `backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/FinancialRiskThresholdProfileProviderTests.cs:48`

- [ ] **Step 1: Localize strings in InMemoryFinancialRiskThresholdProfileProvider.cs**
  Modify [InMemoryFinancialRiskThresholdProfileProvider.cs](file:///c:/Repositories/ai-orchestration-hitl/backend/Orchestration.Application/FinancialAnalysis/Thresholds/InMemoryFinancialRiskThresholdProfileProvider.cs) to translate the specified strings:
  - In `DefaultProfile` (line ~10): Change `"Standard moderate thresholds for general corporate credit risk evaluation."` to `"Umbrales moderados estándar para la evaluación general de riesgo crediticio corporativo."`
  - In `DefaultProfile` / `OilAndGasProfile` / `StrictProfile` / `DemoProfile` `current_ratio` threshold descriptions:
    - Default (line ~13): Change `"Current ratio is below 1.2. Human review recommended."` to `"El índice de liquidez corriente es menor a 1.2. Se recomienda revisión humana."`
    - OilAndGas (line ~26): Change `"Current ratio is below 1.0. Human review recommended."` to `"El índice de liquidez corriente es menor a 1.0. Se recomienda revisión humana."`
    - Strict (line ~39): Change `"Current ratio is below 1.5. Human review recommended."` to `"El índice de liquidez corriente es menor a 1.5. Se recomienda revisión humana."`
    - Demo (line ~52): Change `"Current ratio is below 3.0. Human review recommended."` to `"El índice de liquidez corriente es menor a 3.0. Se recomienda revisión humana."`
  - In `DefaultProfile` / `OilAndGasProfile` / `StrictProfile` / `DemoProfile` `quick_ratio` threshold descriptions:
    - Default (line ~14): Change `"Quick ratio is below 1.0. Liquidity should be reviewed."` to `"La prueba del ácido (quick ratio) es menor a 1.0. Se debe revisar la liquidez."`
    - OilAndGas (line ~27): Change `"Quick ratio is below 0.8. Liquidity should be reviewed."` to `"La prueba del ácido (quick ratio) es menor a 0.8. Se debe revisar la liquidez."`
    - Strict (line ~40): Change `"Quick ratio is below 1.2. Liquidity should be reviewed."` to `"La prueba del ácido (quick ratio) es menor a 1.2. Se debe revisar la liquidez."`
    - Demo (line ~53): Change `"Quick ratio is below 2.5. Liquidity should be reviewed."` to `"La prueba del ácido (quick ratio) es menor a 2.5. Se debe revisar la liquidez."`
  - In `DefaultProfile` / `OilAndGasProfile` / `StrictProfile` / `DemoProfile` `net_debt_to_ebitda` threshold descriptions (lines ~15, 28, 41, 54):
    - Change `"Net debt to EBITDA is above the configured risk threshold."` to `"La relación deuda neta a EBITDA supera el umbral de riesgo configurado."`
  - In `DefaultProfile` / `OilAndGasProfile` / `StrictProfile` / `DemoProfile` `debt_to_equity` threshold descriptions (lines ~16, 29, 42, 55):
    - Change `"Debt to equity is above the configured risk threshold."` to `"La relación deuda/patrimonio neto supera el umbral de riesgo configurado."`
  - In `DefaultProfile` / `OilAndGasProfile` / `StrictProfile` / `DemoProfile` `interest_coverage` threshold descriptions (lines ~17, 30, 43, 56):
    - Change `"Interest coverage is below the configured risk threshold."` to `"La cobertura de intereses está por debajo del umbral de riesgo configurado."`
  - In `OilAndGasProfile` (line ~23): Change `"Industry-specific risk guidelines for energy and commodity extraction corporations."` to `"Pautas de riesgo específicas del sector para corporaciones de extracción de energía y materias primas."`
  - In `StrictProfile` (line ~36): Change `"Conservative risk settings enforcing highly safe liquidity and low leverage levels."` to `"Configuraciones de riesgo conservadoras que exigen niveles de apalancamiento bajos y alta liquidez."`
  - In `DemoProfile` (line ~49): Change `"Sensitive and aggressive thresholds tailored specifically for demonstrations and testing."` to `"Umbrales sensibles y agresivos adaptados específicamente para demostraciones y pruebas."`
  - In `ResolveProfile` (line ~123): Change `"Requested threshold profile '{profileName}' was not found. Fallen back to 'default'."` to `"No se encontró el perfil de umbral solicitado '{profileName}'. Se utilizó el perfil predeterminado ('default')."`

- [ ] **Step 2: Update assertions in FinancialRiskThresholdProfileProviderTests.cs**
  Modify [FinancialRiskThresholdProfileProviderTests.cs](file:///c:/Repositories/ai-orchestration-hitl/backend/Orchestration.Tests/Agents/Data/FinancialAnalysis/FinancialRiskThresholdProfileProviderTests.cs) to match the Spanish translations:
  - In `ResolveProfile_Should_fallback_to_default_with_warning_when_profile_is_unknown` (line ~48): Change `"Requested threshold profile '{requestedName}' was not found. Fallen back to 'default'."` to `"No se encontró el perfil de umbral solicitado '{requestedName}'. Se utilizó el perfil predeterminado ('default')."`

- [ ] **Step 3: Run the threshold provider tests**
  Run: `dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --filter FinancialRiskThresholdProfileProviderTests`
  Expected: PASS

---

### Task 6: Comprehensive Verification

**Files:**
- None (Test verification only)

- [ ] **Step 1: Run all tests in the slnx solution**
  Run: `dotnet test backend/Orchestration.slnx`
  Expected: All 441 backend unit and integration tests PASS.
