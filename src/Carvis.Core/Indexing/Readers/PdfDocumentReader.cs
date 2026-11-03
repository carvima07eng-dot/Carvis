using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Carvis.Core.Indexing.Readers;

/// <summary>PDF text page by page (PdfPig). Pages without text are handed to OCR when available.</summary>
public sealed class PdfDocumentReader(IOcrEngine? ocr = null) : IDocumentReader
{
    private const int MaxOcrPages = 60;

    public bool CanRead(string path) => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<DocumentText> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var parts = new List<DocumentPart>();
        var pagesWithoutText = new List<(int Page, byte[] Image)>();

        using (var document = PdfDocument.Open(path))
        {
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = ContentOrderTextExtractor.GetText(page).Trim();
                if (text.Length > 20)
                {
                    parts.Add(new DocumentPart(text, page.Number));
                    continue;
                }

                // A scanned page is usually one big image.
                if (ocr is { IsAvailable: true } && pagesWithoutText.Count < MaxOcrPages)
                {
                    var image = page.GetImages().OrderByDescending(i => i.BoundingBox.Width * i.BoundingBox.Height).FirstOrDefault();
                    if (image is not null && (image.TryGetPng(out var png) ? png : image.RawBytes.ToArray()) is { Length: > 0 } bytes)
                        pagesWithoutText.Add((page.Number, bytes));
                }
            }
        }

        foreach (var (pageNumber, image) in pagesWithoutText)
        {
            var text = await ocr!.RecognizeAsync(image, cancellationToken);
            if (!string.IsNullOrWhiteSpace(text))
                parts.Add(new DocumentPart(text.Trim(), pageNumber, "texto reconocido (OCR)"));
        }

        return new DocumentText(parts.OrderBy(p => p.Page).ToList());
    }
}

/// <summary>Reads text from images (Windows.Media.Ocr on Windows).</summary>
public interface IOcrEngine
{
    bool IsAvailable { get; }
    Task<string> RecognizeAsync(byte[] image, CancellationToken cancellationToken = default);
}

public sealed class NoOcrEngine : IOcrEngine
{
    public bool IsAvailable => false;
    public Task<string> RecognizeAsync(byte[] image, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
}
