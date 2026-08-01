namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

internal static class CnvRegulationMcpResponseContract
{
    internal const string InvalidStructuredContentWarning =
        "La herramienta MCP devolvió una respuesta vacía o no válida.";

    internal static CnvRegulationSearchResponse CreateInvalidSearchResponse(
        string query) =>
        new(query, [], [InvalidStructuredContentWarning]);

    internal static bool IsInvalidStructuredContent(
        CnvRegulationSearchResponse? response,
        string expectedQuery) =>
        response is null ||
        !string.Equals(response.Query, expectedQuery, StringComparison.Ordinal) ||
        response.Results is null ||
        response.Warnings is null ||
        response.Warnings.Any(static warning =>
            string.IsNullOrWhiteSpace(warning)) ||
        response.Warnings.Contains(
            InvalidStructuredContentWarning,
            StringComparer.Ordinal) ||
        response.Results.Any(IsInvalidResult);

    private static bool IsInvalidResult(CnvRegulationSearchResult? result) =>
        result is null ||
        string.IsNullOrWhiteSpace(result.DocumentId) ||
        string.IsNullOrWhiteSpace(result.Title) ||
        string.IsNullOrWhiteSpace(result.Source) ||
        string.IsNullOrWhiteSpace(result.Snippet) ||
        result.Citations is null ||
        result.Citations.Any(IsInvalidCitation);

    private static bool IsInvalidCitation(CnvRegulationCitation? citation) =>
        citation is null ||
        string.IsNullOrWhiteSpace(citation.Source) ||
        string.IsNullOrWhiteSpace(citation.Title);
}
