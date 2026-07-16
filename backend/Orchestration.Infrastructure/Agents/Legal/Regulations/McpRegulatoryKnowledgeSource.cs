using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Application.Persistence;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Application.Agents.Shared;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal.AiReview;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Infrastructure.Agents.Legal.Regulations;

public sealed class McpRegulatoryKnowledgeSource(
    ICnvRegulationMcpClient client,
    IOptions<CnvRegulationMcpOptions> options,
    ILogger<McpRegulatoryKnowledgeSource> logger,
    ILegalAnalysisReviewService legalAnalysisReviewService,
    IOrchestrationDbContext? dbContext = null,
    ILegalCnvQueryStrategy? queryStrategy = null,
    IActivityEventPublisher? activityPublisher = null) : IRegulatoryKnowledgeSource
{
    private readonly ICnvRegulationMcpClient _client = client;
    private readonly CnvRegulationMcpOptions _options = options.Value;
    private readonly ILogger<McpRegulatoryKnowledgeSource> _logger = logger;
    private readonly ILegalAnalysisReviewService _legalAnalysisReviewService = legalAnalysisReviewService;
    private readonly IOrchestrationDbContext? _dbContext = dbContext;
    private readonly ILegalCnvQueryStrategy _queryStrategy = queryStrategy ?? new FinancialAnalysisLegalCnvQueryStrategy();
    private readonly IActivityEventPublisher? _activityPublisher = activityPublisher;

    private sealed record CnvSearchReviewResult(
        IReadOnlyList<RegulatoryFinding> Findings,
        IReadOnlyList<LegalEvidenceReference> EvidenceReferences,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<LegalCnvQueryAudit> QueryAudits,
        int CompletedQueries,
        int FailedQueries
    );

    public Task<RegulatoryReviewResult> ReviewAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        return ReviewAsync(
            new RegulatoryReviewRequest(report, LegalReviewContext.Default),
            cancellationToken
        );
    }

    public async Task<RegulatoryReviewResult> ReviewAsync(
        RegulatoryReviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Report);
        ArgumentNullException.ThrowIfNull(request.Context);

        var report = request.Report;
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["SessionId"] = report.SessionId
        });

        _logger.LogInformation("Starting regulatory review for report: {ReportName}", report.ReportName);

        var financialAnalysis = report.FinancialAnalysis;
        if (financialAnalysis is null &&
            request.Context.ResolutionMode ==
                FinancialAnalysisResolutionMode.ProvidedOrPersisted)
        {
            financialAnalysis = await TryLoadFinancialAnalysisAsync(
                report.SessionId,
                cancellationToken
            );
        }

        var dataEvidence = request.Context.DataEvidence ??
            LegalDataEvidenceClassifier.Classify(
                LegalDataToolStatuses.Executed,
                financialAnalysis
            );
        var queryPlan = _queryStrategy.BuildPlan(
            financialAnalysis,
            dataEvidence
        );
        var derivedQueries = queryPlan.Queries;
        var usedContextualPlan =
            queryPlan.Source == LegalCnvQuerySources.Contextual &&
            queryPlan.FallbackReason is null;

        var outcome = await SearchFindingsAsync(
            derivedQueries,
            cancellationToken
        );

        var audit = new LegalQueryStrategyAudit(
            StrategyVersion: queryPlan.StrategyVersion,
            Source: queryPlan.Source,
            FallbackReason: queryPlan.FallbackReason,
            DataToolStatus: queryPlan.DataEvidence.DataToolStatus,
            FinancialAnalysisStatus: queryPlan.DataEvidence.FinancialAnalysisStatus,
            FailedStages: queryPlan.DataEvidence.FailedStages.ToList().AsReadOnly(),
            Queries: outcome.QueryAudits.ToList().AsReadOnly()
        );

        await PublishQueryPlanEventAsync(
            report.SessionId,
            usedContextualPlan,
            cancellationToken
        );

        var warnings = new List<string>(outcome.Warnings);
        if (!usedContextualPlan)
        {
            warnings.Add("No había señales de riesgo financiero específicas disponibles; utilizando una consulta general de información financiera.");
        }

        var findings = outcome.Findings;
        var allQueriesFailed =
            outcome.CompletedQueries == 0 &&
            (outcome.FailedQueries > 0 || derivedQueries.Count == 0);
        var hasRisk = findings.Count > 0;

        var summary = allQueriesFailed
            ? "No fue posible completar las consultas regulatorias a la CNV. Se requiere revisión legal humana."
            : hasRisk
                ? "Se encontró evidencia regulatoria de la CNV para la anomalía financiera enviada. Se requiere revisión legal humana."
                : "No se encontró evidencia regulatoria de la CNV con citas para la anomalía financiera enviada.";

        // AI Review Execution
        LegalAnalysisReviewResult legalReviewResult;
        if (allQueriesFailed)
        {
            legalReviewResult = LegalAnalysisReviewResults.NotRun("cnv_queries_failed");
        }
        else if (financialAnalysis == null)
        {
            legalReviewResult = LegalAnalysisReviewResults.NotRun("financial_analysis_missing");
        }
        else
        {
            var input = new LegalAnalysisReviewInput(
                SessionId: report.SessionId.ToString(),
                Company: financialAnalysis.Company,
                DocumentId: financialAnalysis.DocumentId,
                MetricsInputSource: financialAnalysis.MetricsInputSource,
                MetricsProvenance: financialAnalysis.MetricsProvenance,
                FinancialAiReview: financialAnalysis.AiReview,
                FinancialRiskSignals: financialAnalysis.RiskSignals ?? Array.Empty<FinancialRiskSignal>(),
                FinancialRiskEvidence: financialAnalysis.RiskEvidence ?? Array.Empty<RiskEvidenceItem>(),
                FinancialWarnings: financialAnalysis.Warnings ?? Array.Empty<string>(),
                FinancialLimitations: financialAnalysis.Limitations ?? Array.Empty<string>(),
                CnvEvidence: outcome.EvidenceReferences
            );

            legalReviewResult = await _legalAnalysisReviewService.ReviewAsync(input, cancellationToken);
        }

        // Publish Activity Feed event for AI Review
        if (_activityPublisher != null)
        {
            try
            {
                string activityMsg;
                if (legalReviewResult.UsedLlm)
                {
                    activityMsg = "Revisión de IA de LegalAgent completada usando LLM.";
                }
                else if (legalReviewResult.FailureReason is
                    "financial_analysis_missing" or "cnv_queries_failed")
                {
                    activityMsg = "La revisión de IA de LegalAgent no fue ejecutada.";
                }
                else
                {
                    activityMsg = "Revisión de IA de LegalAgent completada usando la alternativa determinista.";
                }

                await _activityPublisher.PublishAsync(
                    new ActivityEvent(
                        report.SessionId,
                        "legal_agent_ai_review_completed",
                        "LegalAgent",
                        activityMsg,
                        DateTimeOffset.UtcNow
                    ),
                    cancellationToken
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish activity feed event for AI review.");
            }
        }

        return new RegulatoryReviewResult(
            HasComplianceRisk: hasRisk,
            RiskLevel: allQueriesFailed ? "Unknown" : hasRisk ? "Medium" : "Low",
            Summary: summary,
            SourceEngine: "MCP CNV Regulation Server",
            Findings: findings,
            Warnings: warnings.Distinct().ToList(),
            QueryStrategy: audit,
            LegalReview: legalReviewResult,
            RequiresHumanReview:
                !usedContextualPlan ||
                outcome.FailedQueries > 0 ||
                derivedQueries.Count == 0
        );
    }

    private async Task PublishQueryPlanEventAsync(
        Guid sessionId,
        bool usedContextualPlan,
        CancellationToken cancellationToken)
    {
        if (_activityPublisher is null)
        {
            return;
        }

        var eventType = usedContextualPlan
            ? "legal_cnv_queries_derived"
            : "legal_cnv_query_fallback_used";
        var message = usedContextualPlan
            ? "LegalAgent derivó consultas de búsqueda CNV a partir de las señales de riesgo del análisis financiero."
            : "LegalAgent utilizó una estrategia general de búsqueda CNV por falta de señales financieras específicas.";

        try
        {
            await _activityPublisher.PublishAsync(
                new ActivityEvent(
                    sessionId,
                    eventType,
                    "LegalAgent",
                    message,
                    DateTimeOffset.UtcNow
                ),
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish activity feed event for CNV query plan.");
        }
    }

    private async Task<FinancialAnalysisContext?> TryLoadFinancialAnalysisAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty || _dbContext == null)
        {
            return null;
        }

        try
        {
            var session = await _dbContext.AnalysisSessions
                .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);

            if (session is null || string.IsNullOrWhiteSpace(session.ContextJson))
            {
                return null;
            }

            var root = JsonNode.Parse(session.ContextJson) as JsonObject;
            var node = root?["financialAnalysis"];
            if (node is null)
            {
                return null;
            }

            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };

            return node.Deserialize<FinancialAnalysisContext>(options);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load FinancialAnalysisContext for session {SessionId}", sessionId);
            return null;
        }
    }

    private static IEnumerable<RegulatoryFinding> MapFindings(
        CnvRegulationSearchResult result)
    {
        foreach (var citation in result.Citations)
        {
            var section = citation.Article
                ?? citation.Section
                ?? citation.Chapter
                ?? result.Article
                ?? result.Section
                ?? result.Chapter
                ?? "N/A";

            var source = BuildSource(citation);

            var finding = !string.IsNullOrWhiteSpace(citation.QuotedText)
                ? citation.QuotedText
                : result.Snippet;

            yield return new RegulatoryFinding(
                Regulation: citation.Title,
                Section: section,
                Finding: finding,
                Source: source
            );
        }
    }

    private static string BuildSource(CnvRegulationCitation citation)
    {
        var parts = new[]
        {
            citation.Source,
            citation.DocumentType,
            citation.ResolutionNumber,
            citation.Url
        };

        return string.Join(" | ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private async Task<CnvSearchReviewResult> SearchFindingsAsync(
        IReadOnlyList<LegalCnvQuery> queries,
        CancellationToken cancellationToken)
    {
        var allFindings = new List<RegulatoryFinding>();
        var allEvidence = new List<LegalEvidenceReference>();
        var warnings = new List<string>();
        var queryAudits = new List<LegalCnvQueryAudit>(queries.Count);
        var completedQueries = 0;
        var failedQueries = 0;
        bool hasUncitedEvidence = false;

        for (var queryIndex = 0; queryIndex < queries.Count; queryIndex++)
        {
            var queryInfo = queries[queryIndex];
            var request = new CnvRegulationSearchRequest(
                Query: queryInfo.Query,
                Area: queryInfo.RegulationArea ?? "Agentes",
                Limit: _options.DefaultLimit,
                RequiresReview: true
            );

            try
            {
                var response = await _client.SearchAsync(
                    request,
                    cancellationToken
                );
                var resultCount = response.Results?.Count ?? 0;
                var citedEvidenceCount = 0;

                if (response.Warnings != null)
                {
                    warnings.AddRange(response.Warnings);
                }

                if (response.Results != null)
                {
                    foreach (var result in response.Results)
                    {
                        if (result.Citations == null || result.Citations.Count == 0)
                        {
                            hasUncitedEvidence = true;
                        }
                        else
                        {
                            citedEvidenceCount += result.Citations.Count;
                            var mapped = MapFindings(result);
                            allFindings.AddRange(mapped);

                            foreach (var citation in result.Citations)
                            {
                                var source = BuildSource(citation);
                                var title = citation.Title ?? result.Title;
                                var url = citation.Url ?? result.Url;
                                var citationStr = citation.Article
                                    ?? citation.Section
                                    ?? citation.Chapter
                                    ?? result.Article
                                    ?? result.Section
                                    ?? result.Chapter;

                                var snippet = !string.IsNullOrWhiteSpace(citation.QuotedText)
                                    ? citation.QuotedText
                                    : result.Snippet;

                                if (!string.IsNullOrWhiteSpace(citationStr))
                                {
                                    allEvidence.Add(new LegalEvidenceReference(
                                        Source: source,
                                        Title: title,
                                        Url: string.IsNullOrWhiteSpace(url) ? null : url,
                                        Citation: citationStr,
                                        Snippet: string.IsNullOrWhiteSpace(snippet) ? null : snippet,
                                        RegulationArea: queryInfo.RegulationArea ?? "Agentes",
                                        Score: result.Score
                                    ));
                                }
                            }
                        }
                    }
                }

                completedQueries++;
                queryAudits.Add(CreateQueryAudit(
                    queryIndex,
                    queries.Count,
                    queryInfo,
                    LegalCnvQueryExecutionStatuses.Succeeded,
                    resultCount,
                    citedEvidenceCount
                ));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedQueries++;
                queryAudits.Add(CreateQueryAudit(
                    queryIndex,
                    queries.Count,
                    queryInfo,
                    LegalCnvQueryExecutionStatuses.Failed,
                    0,
                    0
                ));
                _logger.LogWarning(
                    ex,
                    "CNV MCP search failed for query {QueryIndex} of {TotalQueries}.",
                    queryIndex + 1,
                    queries.Count
                );
                warnings.Add("La búsqueda en la CNV a través de MCP falló para una consulta.");
            }
        }

        if (hasUncitedEvidence)
        {
            warnings.Add("Algunos resultados de búsqueda de CNV/Infoleg se ignoraron como evidencia sólida debido a que no incluían citas.");
        }

        // Deduplicate findings
        var uniqueFindings = allFindings
            .GroupBy(f => $"{f.Regulation}||{f.Section}||{f.Finding}||{f.Source}")
            .Select(g => g.First())
            .ToList();

        // Deduplicate evidence references
        var uniqueEvidence = allEvidence
            .GroupBy(e => $"{e.Source}||{e.Title}||{e.Citation}||{e.Snippet}")
            .Select(g => g.First())
            .ToList();

        if (uniqueFindings.Count > 0)
        {
            warnings.Add("Recuperación regulatoria automatizada únicamente. Se requiere una revisión legal humana antes de tomar decisiones operativas.");
        }
        else
        {
            warnings.Add("No se encontró evidencia regulatoria citada de la CNV mediante la estrategia de búsqueda MCP.");
        }

        return new CnvSearchReviewResult(
            Findings: Array.AsReadOnly(uniqueFindings.ToArray()),
            EvidenceReferences: Array.AsReadOnly(uniqueEvidence.ToArray()),
            Warnings: Array.AsReadOnly(warnings.Distinct().ToArray()),
            QueryAudits: Array.AsReadOnly(queryAudits.ToArray()),
            CompletedQueries: completedQueries,
            FailedQueries: failedQueries
        );
    }

    private static LegalCnvQueryAudit CreateQueryAudit(
        int queryIndex,
        int totalQueries,
        LegalCnvQuery query,
        string executionStatus,
        int resultCount,
        int citedEvidenceCount)
    {
        return new LegalCnvQueryAudit(
            Index: queryIndex + 1,
            Total: totalQueries,
            Query: query.Query,
            RegulationArea: query.RegulationArea,
            Reason: query.Reason,
            RelatedFinancialSignals:
                query.RelatedFinancialSignals.ToList().AsReadOnly(),
            ExecutionStatus: executionStatus,
            ResultCount: resultCount,
            CitedEvidenceCount: citedEvidenceCount
        );
    }
}
