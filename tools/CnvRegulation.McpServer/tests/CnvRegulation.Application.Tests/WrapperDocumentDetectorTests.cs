using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Diagnostics;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class WrapperDocumentDetectorTests
{
    [Fact]
    public void WrapperDetector_ShouldMarkWrapperCandidate()
    {
        var detector = new WrapperDocumentDetector();
        var document = new RegulationDocument
        {
            Id = "wrapper",
            Source = "Infoleg",
            DocumentType = "Resolucion General",
            Title = "InfoLEG",
            Url = "https://servicios.infoleg.gob.ar/infolegInternet/wrapper.htm",
            Status = "candidate",
            Text = "InfoLEG wrapper " + string.Join(' ', Enumerable.Range(0, 25).Select(index => $"https://example.test/{index}")),
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fileType"] = "HTML"
            }
        };

        var result = detector.Detect(document, chunkCount: 0);

        result.IsWrapperCandidate.Should().BeTrue();
        result.Searchable.Should().BeFalse();
        result.Reason.Should().NotBeEmpty();
    }
}
