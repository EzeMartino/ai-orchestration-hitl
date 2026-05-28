using Orchestration.Application.Agents.Data.FinancialAnalysis;
using UglyToad.PdfPig;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public Task<IReadOnlyList<StructuredFinancialMetricsExtractedPage>> ExtractTextAsync(
        Stream pdf,
        int maxPages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        cancellationToken.ThrowIfCancellationRequested();

        using var nonDisposingPdf = new NonDisposingStream(pdf);
        using var document = PdfDocument.Open(nonDisposingPdf);
        var pages = new List<StructuredFinancialMetricsExtractedPage>();
        var remainingPages = Math.Max(0, maxPages);

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (pages.Count >= remainingPages)
            {
                break;
            }

            pages.Add(new StructuredFinancialMetricsExtractedPage(
                PageNumber: page.Number,
                Text: page.Text ?? string.Empty));
        }

        return Task.FromResult<IReadOnlyList<StructuredFinancialMetricsExtractedPage>>(pages);
    }

    private sealed class NonDisposingStream : Stream
    {
        private readonly Stream _inner;

        public NonDisposingStream(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
