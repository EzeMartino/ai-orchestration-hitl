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
        List<RegulatoryFinding> Findings,
        List<LegalEvidenceReference> EvidenceReferences,
        List<string> Warnings
    );

    public async Task<RegulatoryReviewResult> ReviewAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["SessionId"] = report.SessionId
        });

        _logger.LogInformation("Starting regulatory review for report: {ReportName}", report.ReportName);

        var financialAnalysis = await TryLoadFinancialAnalysisAsync(report.SessionId, cancellationToken);
        var derivedQueries = _queryStrategy.BuildQueries(financialAnalysis);

        string sourceStr = (financialAnalysis != null && financialAnalysis.RiskSignals != null && financialAnalysis.RiskSignals.Count > 0)
            ? "financial_analysis"
            : "fallback";

        var audit = new LegalQueryStrategyAudit(
            Source: sourceStr,
            Queries: derivedQueries.Select(dq => new LegalCnvQueryAudit(
                Query: dq.Query,
                RegulationArea: dq.RegulationArea,
                Reason: dq.Reason,
                RelatedFinancialSignals: dq.RelatedFinancialSignals
            )).ToList()
        );

        // Publish Activity Feed event if derived from financial analysis and publisher is available
        if (_activityPublisher != null)
        {
            try
            {
                await _activityPublisher.PublishAsync(
                    new ActivityEvent(
                        report.SessionId,
                        "legal_cnv_queries_derived",
                        "LegalAgent",
                        "LegalAgent derived CNV search queries from financial analysis risk signals.",
                        DateTimeOffset.UtcNow
                    ),
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish activity feed event for derived queries.");
            }
        }

        var outcome = await SearchFindingsAsync(
            report,
            derivedQueries,
            cancellationToken
        );

        var warnings = new List<string>(outcome.Warnings);
        if (sourceStr == "fallback")
        {
            warnings.Add("No specific financial risk signals were available; using a general financial reporting query.");
        }

        var findings = outcome.Findings;
        var hasRisk = findings.Count > 0;

        var summary = hasRisk
            ? "CNV regulatory evidence was found for the submitted financial anomaly. Human legal review is required."
            : "No CNV regulatory evidence with citations was found for the submitted financial anomaly.";

        // AI Review Execution
        LegalAnalysisReviewResult legalReviewResult;
        if (financialAnalysis == null)
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
                    activityMsg = "LegalAgent AI review completed using LLM.";
                }
                else if (legalReviewResult.FailureReason == "financial_analysis_missing")
                {
                    activityMsg = "LegalAgent AI review was not executed.";
                }
                else
                {
                    activityMsg = "LegalAgent AI review completed using deterministic fallback.";
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish activity feed event for AI review.");
            }
        }

        return new RegulatoryReviewResult(
            HasComplianceRisk: hasRisk,
            RiskLevel: hasRisk ? "Medium" : "Low",
            Summary: summary,
            SourceEngine: "MCP CNV Regulation Server",
            Findings: findings,
            Warnings: warnings.Distinct().ToList(),
            QueryStrategy: audit,
            LegalReview: legalReviewResult
        );
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
        FinancialReportContext report,
        IReadOnlyList<LegalCnvQuery> queries,
        CancellationToken cancellationToken)
    {
        var allFindings = new List<RegulatoryFinding>();
        var allEvidence = new List<LegalEvidenceReference>();
        var warnings = new List<string>();
        bool hasUncitedEvidence = false;

        foreach (var queryInfo in queries)
        {
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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CNV MCP search failed for query: {Query}", queryInfo.Query);
                warnings.Add($"Search failed for query '{queryInfo.Query}': {ex.Message}");
            }
        }

        if (hasUncitedEvidence)
        {
            warnings.Add("Some CNV/Infoleg search results were ignored as strong evidence because they did not include citations.");
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
            warnings.Add("Automated regulatory retrieval only. Human legal review is required before making operational decisions.");
        }
        else
        {
            warnings.Add("No cited CNV regulatory evidence was found by the MCP search strategy.");
        }

        return new CnvSearchReviewResult(
            Findings: uniqueFindings,
            EvidenceReferences: uniqueEvidence,
            Warnings: warnings.Distinct().ToList()
        );
    }
}
