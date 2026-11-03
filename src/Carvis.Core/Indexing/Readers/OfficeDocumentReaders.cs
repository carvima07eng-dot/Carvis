using System.Text;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Carvis.Core.Indexing.Readers;

/// <summary>Word (.docx): paragraphs and tables, headings become sections.</summary>
public sealed class DocxDocumentReader : IDocumentReader
{
    public bool CanRead(string path) => Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase);

    public Task<DocumentText> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
            return Task.FromResult(new DocumentText([]));

        var parts = new List<DocumentPart>();
        var section = (string?)null;
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length > 0)
                parts.Add(new DocumentPart(current.ToString().Trim(), Section: section));
            current.Clear();
        }

        foreach (var element in body.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (element)
            {
                case W.Paragraph paragraph:
                    var text = paragraph.InnerText.Trim();
                    if (text.Length == 0)
                        continue;
                    var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
                    if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) || style.StartsWith("Ttulo", StringComparison.OrdinalIgnoreCase)
                        || style.StartsWith("Título", StringComparison.OrdinalIgnoreCase) || style.Equals("Title", StringComparison.OrdinalIgnoreCase))
                    {
                        Flush();
                        section = text;
                    }
                    current.Append(text).Append('\n');
                    break;

                case W.Table table:
                    foreach (var row in table.Elements<W.TableRow>())
                        current.Append(string.Join(" | ", row.Elements<W.TableCell>().Select(c => c.InnerText.Trim()))).Append('\n');
                    current.Append('\n');
                    break;
            }
        }
        Flush();
        return Task.FromResult(new DocumentText(parts));
    }
}

/// <summary>Excel (.xlsx): each sheet becomes a section with its rows as "a | b | c".</summary>
public sealed class XlsxDocumentReader : IDocumentReader
{
    private const int MaxRowsPerSheet = 5000;

    public bool CanRead(string path) => Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);

    public Task<DocumentText> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var workbook = document.WorkbookPart;
        if (workbook?.Workbook?.Sheets is null)
            return Task.FromResult(new DocumentText([]));

        var shared = workbook.SharedStringTablePart?.SharedStringTable?.Elements<S.SharedStringItem>().Select(i => i.InnerText).ToList() ?? [];
        var parts = new List<DocumentPart>();

        foreach (var sheet in workbook.Workbook.Sheets.Elements<S.Sheet>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sheet.Id?.Value is not { } id || workbook.GetPartById(id) is not WorksheetPart { Worksheet: { } worksheetRoot })
                continue;

            var text = new StringBuilder();
            foreach (var row in worksheetRoot.Descendants<S.Row>().Take(MaxRowsPerSheet))
            {
                var cells = row.Elements<S.Cell>().Select(c => CellText(c, shared)).ToList();
                if (cells.Any(c => c.Length > 0))
                    text.Append(string.Join(" | ", cells)).Append('\n');
            }
            if (text.Length > 0)
                parts.Add(new DocumentPart(text.ToString().Trim(), Section: $"Hoja {sheet.Name?.Value}"));
        }
        return Task.FromResult(new DocumentText(parts));
    }

    private static string CellText(S.Cell cell, IReadOnlyList<string> shared)
    {
        var value = cell.CellValue?.Text ?? cell.InnerText;
        if (cell.DataType?.Value == S.CellValues.SharedString && int.TryParse(value, out var index) && index < shared.Count)
            return shared[index];
        return value?.Trim() ?? string.Empty;
    }
}

/// <summary>PowerPoint (.pptx): the text of each slide, with the slide number as page.</summary>
public sealed class PptxDocumentReader : IDocumentReader
{
    public bool CanRead(string path) => Path.GetExtension(path).Equals(".pptx", StringComparison.OrdinalIgnoreCase);

    public Task<DocumentText> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        using var document = PresentationDocument.Open(path, false);
        var presentation = document.PresentationPart;
        var slideIds = presentation?.Presentation?.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>().ToList() ?? [];
        var parts = new List<DocumentPart>();

        for (var i = 0; i < slideIds.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (slideIds[i].RelationshipId?.Value is not { } id || presentation!.GetPartById(id) is not SlidePart { Slide: { } slideRoot } slide)
                continue;

            var paragraphs = slideRoot.Descendants<A.Paragraph>()
                .Select(p => string.Concat(p.Descendants<A.Text>().Select(t => t.Text)).Trim())
                .Where(t => t.Length > 0)
                .ToList();
            var notes = slide.NotesSlidePart?.NotesSlide?.Descendants<A.Text>().Select(t => t.Text).ToList() ?? [];
            if (notes.Count > 0)
                paragraphs.Add("Notas: " + string.Join(" ", notes));
            if (paragraphs.Count > 0)
                parts.Add(new DocumentPart(string.Join("\n", paragraphs), i + 1, paragraphs[0]));
        }
        return Task.FromResult(new DocumentText(parts));
    }
}

/// <summary>Images (.png, .jpg...) through OCR, when the platform offers it.</summary>
public sealed class ImageDocumentReader(IOcrEngine? ocr = null) : IDocumentReader
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };

    public bool CanRead(string path) => ocr is { IsAvailable: true } && Extensions.Contains(Path.GetExtension(path));

    public async Task<DocumentText> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var text = await ocr!.RecognizeAsync(await File.ReadAllBytesAsync(path, cancellationToken), cancellationToken);
        return new DocumentText([new DocumentPart(text.Trim(), Section: "texto reconocido (OCR)")]);
    }
}
