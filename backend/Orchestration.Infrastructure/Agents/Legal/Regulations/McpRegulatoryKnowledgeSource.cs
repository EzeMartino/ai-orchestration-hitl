using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Infrastructure.Agents.Legal.Regulations;

public sealed class McpRegulatoryKnowledgeSource : IRegulatoryKnowledgeSource
{
    private readonly ICnvRegulationMcpClient _client;
    private readonly CnvRegulationMcpOptions _options;
    private readonly ILogger<McpRegulatoryKnowledgeSource> _logger;

    private sealed record RegulatorySearchOutcome(
        List<RegulatoryFinding> Findings,
        List<string> Warnings
    );

    public McpRegulatoryKnowledgeSource(
        ICnvRegulationMcpClient client,
        IOptions<CnvRegulationMcpOptions> options,
        ILogger<McpRegulatoryKnowledgeSource> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RegulatoryReviewResult> ReviewAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["SessionId"] = report.SessionId
        });

        _logger.LogInformation("Starting regulatory review for report: {ReportName}", report.ReportName);

        var outcome = await SearchFindingsAsync(
            report,
            cancellationToken
        );

        var findings = outcome.Findings;
        var hasRisk = findings.Count > 0;

        var summary = hasRisk
            ? "CNV regulatory evidence was found for the submitted financial anomaly. Human legal review is required."
            : "No CNV regulatory evidence with citations was found for the submitted financial anomaly.";

        return new RegulatoryReviewResult(
            HasComplianceRisk: hasRisk,
            RiskLevel: hasRisk ? "Medium" : "Low",
            Summary: summary,
            SourceEngine: "MCP CNV Regulation Server",
            Findings: findings,
            Warnings: outcome.Warnings
        );
    }

    private static IReadOnlyList<string> BuildCandidateQueries(
    FinancialReportContext report)
    {
        return
        [
        "agentes",
        "fondos comunes",
        "custodia",
        "autorización",
        "registro",
        "régimen informativo"
        ];
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

    // Realiza búsquedas secuenciales. La latencia total es lineal acumulada en base al número de consultas.
    private async Task<RegulatorySearchOutcome> SearchFindingsAsync(
    FinancialReportContext report,
    CancellationToken cancellationToken)
    {
        var queries = BuildCandidateQueries(report);
        var warnings = new List<string>();

        foreach (var query in queries)
        {
            var request = new CnvRegulationSearchRequest(
                Query: query,
                Area: "Agentes",
                Limit: _options.DefaultLimit,
                RequiresReview: true
            );

            var response = await _client.SearchAsync(
                request,
                cancellationToken
            );

            warnings.AddRange(response.Warnings);

            var findings = response.Results
                .Where(result => result.Citations.Count > 0)
                .SelectMany(MapFindings)
                .ToList();

            if (findings.Count > 0)
            {
                warnings.Add(
                    "Automated regulatory retrieval only. Human legal review is required before making operational decisions."
                );

                return new RegulatorySearchOutcome(
                    findings,
                    warnings.Distinct().ToList()
                );
            }
        }

        warnings.Add(
            "No cited CNV regulatory evidence was found by the MCP search strategy."
        );

        return new RegulatorySearchOutcome(
            [],
            warnings.Distinct().ToList()
        );
    }
}
