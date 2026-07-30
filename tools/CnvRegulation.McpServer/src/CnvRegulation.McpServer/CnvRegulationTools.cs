using System.ComponentModel;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using ModelContextProtocol.Server;

namespace CnvRegulation.McpServer;

/// <summary>
/// MCP tools exposed by the CNV regulation server.
/// </summary>
[McpServerToolType]
public static class CnvRegulationTools
{
    /// <summary>
    /// Searches mock CNV regulatory material.
    /// </summary>
    [McpServerTool(
        Name = "search_cnv_regulation",
        Title = "Search CNV Regulation",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Searches mock CNV regulatory material and returns traceable placeholder citations.")]
    public static Task<SearchRegulationResponse> SearchCnvRegulationAsync(
        IRegulationSearchService searchService,
        [Description("Natural language query or keywords to search for.")] string query,
        [Description("Optional regulatory area filter, such as Agentes.")] string? area = null,
        [Description("Maximum number of results to return.")] int limit = 5,
        [Description("Search mode: full_text, semantic, or hybrid.")] string searchMode = "full_text",
        [Description("Optional source filter, such as CNV or Infoleg.")] string? source = null,
        [Description("Optional document type filter.")] string? documentType = null,
        [Description("Optional resolution number filter, such as 622/2013.")] string? resolutionNumber = null,
        [Description("Optional status filter, such as candidate or mock.")] string? status = null,
        [Description("Optional requires-review filter.")] bool? requiresReview = null,
        [Description("Whether to include duplicate chunks.")] bool includeDuplicates = false,
        [Description("Whether to include non-searchable wrapper-like documents.")] bool includeNonSearchable = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchService);

        return searchService.SearchAsync(
            new SearchRegulationRequest
            {
                Query = query,
                Area = area,
                Limit = limit,
                SearchMode = searchMode,
                Source = source,
                DocumentType = documentType,
                ResolutionNumber = resolutionNumber,
                Status = status,
                RequiresReview = requiresReview,
                IncludeDuplicates = includeDuplicates,
                IncludeNonSearchable = includeNonSearchable
            },
            cancellationToken);
    }

    /// <summary>
    /// Retrieves a repository-backed CNV regulatory document by identifier.
    /// </summary>
    [McpServerTool(
        Name = "get_cnv_document",
        Title = "Get CNV Document",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Retrieves repository-backed CNV regulatory document metadata, content, citations, and retrieval warnings.")]
    public static Task<GetRegulationDocumentResponse> GetCnvDocumentAsync(
        IRegulationDocumentService documentService,
        [Description("Document identifier, for example cnv-nt-2013.")] string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentService);

        return documentService.GetDocumentAsync(
            new GetRegulationDocumentRequest
            {
                DocumentId = documentId
            },
            cancellationToken);
    }

    /// <summary>
    /// Retrieves a repository-backed CNV regulatory article by title/chapter/section/article.
    /// </summary>
    [McpServerTool(
        Name = "get_cnv_article",
        Title = "Get CNV Article",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Retrieves a structured repository-backed CNV regulatory article response with citation and confidence.")]
    public static Task<GetRegulationArticleResponse> GetCnvArticleAsync(
        IRegulationArticleService articleService,
        [Description("Article label, for example Articulo 4.")] string article,
        [Description("Optional title filter, for example Titulo VII.")] string? title = null,
        [Description("Optional chapter filter, for example Capitulo II.")] string? chapter = null,
        [Description("Optional section filter.")] string? section = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(articleService);

        return articleService.GetArticleAsync(
            new GetRegulationArticleRequest
            {
                Title = title,
                Chapter = chapter,
                Section = section,
                Article = article
            },
            cancellationToken);
    }

    /// <summary>
    /// Retrieves recent mock CNV resolutions.
    /// </summary>
    [McpServerTool(
        Name = "get_recent_cnv_resolutions",
        Title = "Get Recent CNV Resolutions",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Retrieves recent mock CNV resolution metadata from in-memory placeholder data.")]
    public static Task<GetRecentResolutionsResponse> GetRecentCnvResolutionsAsync(
        IRecentResolutionService recentResolutionService,
        [Description("Lookback window in days.")] int days = 30,
        [Description("Optional source filter, for example boletin_oficial.")] string? source = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recentResolutionService);

        return recentResolutionService.GetRecentAsync(
            new GetRecentResolutionsRequest
            {
                Days = days,
                Source = source
            },
            cancellationToken);
    }

    /// <summary>
    /// Performs an evidence-based CNV compliance analysis.
    /// </summary>
    [McpServerTool(
        Name = "analyze_text_against_cnv",
        Title = "Analyze Text Against CNV",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Runs an evidence-based regulatory review aid against supplied text and returns findings with citations.")]
    public static Task<AnalyzeTextAgainstCnvResponse> AnalyzeTextAgainstCnvAsync(
        IComplianceAnalysisService complianceAnalysisService,
        [Description("Text to analyze against CNV regulatory checks.")] string text,
        [Description("Optional regulatory area, such as Agentes.")] string? regulationArea = null,
        [Description("Whether to prefer full-text evidence strictly.")] bool strictMode = true,
        [Description("Whether to use hybrid search only as secondary exploratory evidence.")] bool useHybridSearch = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(complianceAnalysisService);

        return complianceAnalysisService.AnalyzeAsync(
            new AnalyzeTextAgainstCnvRequest
            {
                Text = text,
                RegulationArea = regulationArea,
                StrictMode = strictMode,
                UseHybridSearch = useHybridSearch
            },
            cancellationToken);
    }
}
