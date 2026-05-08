using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;

namespace CnvRegulation.Infrastructure.InMemory;

internal static class MockRegulationData
{
    public const string MockWarning = "Mock data only. Do not use for real regulatory decisions.";
    public const string Disclaimer = "This is an automated regulatory review aid, not legal advice.";

    public static IReadOnlyList<RegulationDocument> Documents =>
    [
        new()
        {
            Id = "cnv-nt-2013",
            Source = "CNV",
            DocumentType = "Normas CNV",
            Title = "Normas CNV N.T. 2013",
            Url = "https://www.cnv.gov.ar/",
            Status = "mock",
            Text = "Mock regulatory text for CNV N.T. 2013. This placeholder does not represent official CNV text."
        },
        new()
        {
            Id = "rg-622-2013",
            Source = "CNV",
            DocumentType = "Resolucion General",
            ResolutionNumber = "622/2013",
            Title = "Resolucion General CNV 622/2013",
            Url = "https://www.cnv.gov.ar/",
            Status = "mock",
            Text = "Mock regulatory text for Resolucion General CNV 622/2013. This placeholder does not represent official CNV text."
        },
        new()
        {
            Id = "rg-990-2024",
            Source = "CNV",
            DocumentType = "Resolucion General",
            ResolutionNumber = "990/2024",
            Title = "Resolucion General CNV 990/2024",
            Url = "https://www.cnv.gov.ar/",
            Status = "mock",
            Text = "Mock regulatory text for Resolucion General CNV 990/2024. This placeholder does not represent official CNV text."
        },
        new()
        {
            Id = "rg-1000-2024",
            Source = "CNV",
            DocumentType = "Resolucion General",
            ResolutionNumber = "1000/2024",
            Title = "Resolucion General CNV 1000/2024",
            Url = "https://www.cnv.gov.ar/",
            Status = "mock",
            Text = "Mock regulatory text for Resolucion General CNV 1000/2024. This placeholder does not represent official CNV text."
        },
        new()
        {
            Id = "rg-1082-2025",
            Source = "CNV",
            DocumentType = "Resolucion General",
            ResolutionNumber = "1082/2025",
            Title = "Resolucion General CNV 1082/2025",
            Url = "https://www.cnv.gov.ar/",
            Status = "mock",
            Text = "Mock regulatory text for Resolucion General CNV 1082/2025. This placeholder does not represent official CNV text."
        }
    ];

    public static IReadOnlyList<RegulationSearchResult> SearchResults =>
    [
        new()
        {
            DocumentId = "cnv-nt-2013",
            ChunkId = "mock-chunk-001",
            Title = "Normas CNV N.T. 2013",
            Chapter = "Titulo mock",
            Section = "Seccion mock",
            Article = "Articulo mock",
            Source = "CNV",
            Url = "https://www.cnv.gov.ar/",
            Snippet = "Mock regulatory snippet about ALyC obligations. This placeholder is not official CNV text.",
            Score = 0.82,
            Citations =
            [
                CreateCitation(
                    "Normas CNV N.T. 2013",
                    "Normas CNV",
                    null,
                    "Titulo mock",
                    "Seccion mock",
                    "Articulo mock",
                    "Mock regulatory text for ALyC obligations. Placeholder only.")
            ]
        },
        new()
        {
            DocumentId = "rg-990-2024",
            ChunkId = "mock-chunk-002",
            Title = "Resolucion General CNV 990/2024",
            Chapter = "Titulo mock",
            Section = "Seccion mock",
            Article = "Articulo mock",
            Source = "CNV",
            Url = "https://www.cnv.gov.ar/",
            Snippet = "Mock regulatory snippet about market conduct controls. This placeholder is not official CNV text.",
            Score = 0.74,
            Citations =
            [
                CreateCitation(
                    "Resolucion General CNV 990/2024",
                    "Resolucion General",
                    "990/2024",
                    "Titulo mock",
                    "Seccion mock",
                    "Articulo mock",
                    "Mock regulatory text for market conduct controls. Placeholder only.")
            ]
        },
        new()
        {
            DocumentId = "rg-1082-2025",
            ChunkId = "mock-chunk-003",
            Title = "Resolucion General CNV 1082/2025",
            Chapter = "Titulo mock",
            Section = "Seccion mock",
            Article = "Articulo mock",
            Source = "CNV",
            Url = "https://www.cnv.gov.ar/",
            Snippet = "Mock regulatory snippet about recent compliance updates. This placeholder is not official CNV text.",
            Score = 0.68,
            Citations =
            [
                CreateCitation(
                    "Resolucion General CNV 1082/2025",
                    "Resolucion General",
                    "1082/2025",
                    "Titulo mock",
                    "Seccion mock",
                    "Articulo mock",
                    "Mock regulatory text for recent compliance updates. Placeholder only.")
            ]
        }
    ];

    public static IReadOnlyList<RecentResolutionItem> RecentResolutions
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            return
            [
                new()
                {
                    DocumentId = "rg-1082-2025",
                    Title = "Resolucion General CNV 1082/2025",
                    Source = "boletin_oficial",
                    ResolutionNumber = "1082/2025",
                    PublicationDate = today.AddDays(-5),
                    Url = "https://www.cnv.gov.ar/",
                    Summary = "Mock recent resolution summary. This placeholder is not official CNV text."
                },
                new()
                {
                    DocumentId = "rg-1000-2024",
                    Title = "Resolucion General CNV 1000/2024",
                    Source = "cnv",
                    ResolutionNumber = "1000/2024",
                    PublicationDate = today.AddDays(-12),
                    Url = "https://www.cnv.gov.ar/",
                    Summary = "Mock recent resolution summary. This placeholder is not official CNV text."
                },
                new()
                {
                    DocumentId = "rg-990-2024",
                    Title = "Resolucion General CNV 990/2024",
                    Source = "boletin_oficial",
                    ResolutionNumber = "990/2024",
                    PublicationDate = today.AddDays(-22),
                    Url = "https://www.cnv.gov.ar/",
                    Summary = "Mock recent resolution summary. This placeholder is not official CNV text."
                }
            ];
        }
    }

    public static RegulationDocument GetDocumentOrDefault(string documentId)
    {
        var document = Documents.FirstOrDefault(document =>
            string.Equals(document.Id, documentId, StringComparison.OrdinalIgnoreCase));

        return document ?? new RegulationDocument
        {
            Id = documentId,
            Source = "CNV",
            DocumentType = "Unknown mock document",
            Title = $"Unknown mock document {documentId}",
            Url = "https://www.cnv.gov.ar/",
            Status = "mock-not-found",
            Text = "Mock regulatory text for an unknown document identifier. This placeholder does not represent official CNV text."
        };
    }

    public static RegulationCitation CreateDocumentCitation(RegulationDocument document) =>
        new()
        {
            Source = document.Source,
            DocumentType = document.DocumentType,
            ResolutionNumber = document.ResolutionNumber,
            Title = document.Title,
            PublicationDate = document.PublicationDate,
            Url = document.Url,
            QuotedText = document.Text
        };

    public static RegulationCitation CreateCitation(
        string title,
        string documentType,
        string? resolutionNumber,
        string? chapter,
        string? section,
        string? article,
        string quotedText) =>
        new()
        {
            Source = "CNV",
            DocumentType = documentType,
            ResolutionNumber = resolutionNumber,
            Title = title,
            Chapter = chapter,
            Section = section,
            Article = article,
            Url = "https://www.cnv.gov.ar/",
            QuotedText = quotedText
        };
}
