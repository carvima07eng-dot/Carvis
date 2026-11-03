using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Indexing;
using Carvis.Core.Indexing.Readers;
using Carvis.Core.Storage;
using Carvis.Tests.Fakes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Carvis.Tests.Indexing;

public sealed class IndexingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("carvis-rag").FullName;
    private readonly string _docs;
    private readonly CarvisDatabase _db;
    private readonly FakeEmbeddings _embeddings = new();
    private readonly DocumentsSettings _settings = new();

    public IndexingTests()
    {
        _docs = Directory.CreateDirectory(Path.Combine(_dir, "Apuntes")).FullName;
        _db = new CarvisDatabase(Path.Combine(_dir, "carvis.db"));
        _settings.Folders.Add(_docs);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private static DocumentTextExtractor Extractor() => new(
        [new TextDocumentReader(), new PdfDocumentReader(), new DocxDocumentReader(), new XlsxDocumentReader(), new PptxDocumentReader()]);

    private DocumentIndexer Indexer() => new(new DocumentIndex(_db), Extractor(), _embeddings, _settings, new OllamaSettings());

    private string Write(string name, string text)
    {
        var path = Path.Combine(_docs, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Chunker_KeepsShortTextsWholeAndSplitsLongOnesWithOverlap()
    {
        var chunker = new TextChunker(200, 40);
        var longText = string.Join("\n\n", Enumerable.Range(1, 10).Select(i => $"Párrafo {i}. Esto es una frase de relleno para el párrafo número {i}."));

        var chunks = chunker.Split("a.txt", new DocumentText([new DocumentPart("corto", 3, "Intro"), new DocumentPart(longText, 4)]));

        Assert.Equal("corto", chunks[0].Text);
        Assert.Equal(3, chunks[0].Page);
        Assert.True(chunks.Count > 3);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 260, c.Text));
        Assert.All(chunks.Skip(1), c => Assert.Equal(4, c.Page));
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Index));
    }

    [Fact]
    public async Task Readers_ExtractPdfPagesWordHeadingsExcelSheetsAndSlides()
    {
        var pdf = Path.Combine(_docs, "tema.pdf");
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText("Las redes de ordenadores conectan equipos", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText("El protocolo TCP garantiza la entrega", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        File.WriteAllBytes(pdf, builder.Build());

        var docx = Path.Combine(_docs, "clases.docx");
        using (var word = WordprocessingDocument.Create(docx, WordprocessingDocumentType.Document))
        {
            var main = word.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(
                new W.Paragraph(new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Heading1" }), new W.Run(new W.Text("Clases abstractas"))),
                new W.Paragraph(new W.Run(new W.Text("Una clase abstracta no se puede instanciar."))),
                new W.Table(new W.TableRow(new W.TableCell(new W.Paragraph(new W.Run(new W.Text("A")))), new W.TableCell(new W.Paragraph(new W.Run(new W.Text("B"))))))));
        }

        var extractor = Extractor();
        var pdfText = await extractor.ReadStructuredAsync(pdf);
        var docxText = await extractor.ReadStructuredAsync(docx);

        Assert.Equal([1, 2], pdfText.Parts.Select(p => p.Page!.Value));
        Assert.Contains("TCP", pdfText.Parts[1].Text);
        Assert.Equal("Clases abstractas", docxText.Parts.Single().Section);
        Assert.Contains("A | B", docxText.FullText);
    }

    [Fact]
    public async Task Index_FindsByMeaningAndByKeywordAndCitesThePage()
    {
        Write("redes.md", "# Redes\nEl protocolo TCP garantiza la entrega ordenada de los paquetes.");
        Write("poo.md", "# POO\nLas clases abstractas no se pueden instanciar y sirven de plantilla.");
        Write("cocina.txt", "Receta de paella valenciana con pollo y conejo.");
        var indexer = Indexer();

        await indexer.IndexAsync();
        var results = await indexer.SearchAsync("¿qué es una clase abstracta?");

        Assert.Equal(3, indexer.Status.Summary.Documents);
        Assert.EndsWith("poo.md", results[0].Chunk.SourcePath);
        Assert.Equal("POO", results[0].Chunk.Section);
        Assert.True(_db.HasVectorSearch);
    }

    [Fact]
    public async Task Index_IsIncrementalAndForgetsDeletedFiles()
    {
        var a = Write("a.txt", "Primer documento sobre bases de datos relacionales.");
        Write("b.txt", "Segundo documento sobre sistemas operativos.");
        var indexer = Indexer();
        await indexer.IndexAsync();
        var callsAfterFirstRun = _embeddings.Calls;

        await indexer.IndexAsync();
        Assert.Equal(callsAfterFirstRun, _embeddings.Calls);

        File.Delete(a);
        File.WriteAllText(Path.Combine(_docs, "b.txt"), "Segundo documento, ahora sobre redes.");
        File.SetLastWriteTimeUtc(Path.Combine(_docs, "b.txt"), DateTime.UtcNow.AddMinutes(1));
        await indexer.IndexAsync();

        Assert.Equal(1, indexer.Status.Summary.Documents);
        Assert.Equal(callsAfterFirstRun + 1, _embeddings.Calls);
        Assert.Contains("redes", (await indexer.SearchAsync("redes"))[0].Chunk.Text);
    }

    [Fact]
    public async Task Index_SkipsExcludedFoldersAndHugeFiles()
    {
        Write("node_modules/lib.txt", "dependencia que no interesa");
        Write("bueno.txt", "documento que sí interesa");
        _settings.MaxFileSizeMb = 1;
        File.WriteAllBytes(Path.Combine(_docs, "enorme.txt"), new byte[2 * 1024 * 1024]);
        var indexer = Indexer();

        await indexer.IndexAsync();

        Assert.Equal(1, indexer.Status.Summary.Documents);
    }

    [Fact]
    public async Task Index_RecordsUnreadableFilesWithoutStopping()
    {
        File.WriteAllText(Path.Combine(_docs, "roto.pdf"), "esto no es un pdf");
        Write("bueno.txt", "texto legible");
        var indexer = Indexer();

        await indexer.IndexAsync();

        Assert.Equal(1, indexer.Status.Summary.Failed);
        Assert.Contains("roto.pdf", indexer.Status.LastError);
    }

    [Fact]
    public async Task RagProvider_AddsNumberedSourcesOnlyWhenRelevant()
    {
        Write("poo.md", "# POO\nLas clases abstractas no se pueden instanciar.");
        var indexer = Indexer();
        await indexer.IndexAsync();
        var provider = new RagContextProvider(indexer, _settings);

        Assert.Empty(await provider.GetContextAsync("hola"));
        var context = Assert.Single(await provider.GetContextAsync("¿qué dicen mis apuntes de las clases abstractas?"));
        Assert.True(context.IsExternal);
        Assert.Contains("[1] poo.md", context.Content);
        Assert.Equal("poo.md", Path.GetFileName(context.Sources![0].Path));
    }

    [Fact]
    public async Task ChatService_SendsAttachedFilesAndReportsTheirSources()
    {
        var file = Write("practica.txt", "La práctica 2 se entrega el 15 de noviembre.");
        var client = new FakeChatModelClient().Reply("El 15 de noviembre [1].").Reply("Sí, la 2.");
        var service = new ChatService(client, new AssistantSettings(), attachments: new AttachmentContextBuilder(Extractor(), _embeddings));

        var events = await service.SendAsync(new ChatInput("¿cuándo se entrega?") { Attachments = [file] }).ToListAsync();
        await service.SendAsync("¿qué práctica era?").ToListAsync();

        var sources = Assert.Single(events.OfType<SourcesAttached>()).Sources;
        Assert.Equal(file, sources[0].Path);
        Assert.Contains("15 de noviembre", client.Requests[0].Messages[0].Content);
        // Follow-up questions still see the attached file.
        Assert.Contains("15 de noviembre", client.Requests[1].Messages[0].Content);
    }

    [Fact]
    public void FtsQuery_DropsStopWordsAndUsesPrefixes()
    {
        Assert.Equal("\"clase\"* OR \"abstracta\"*", DocumentIndex.FtsQuery("¿Qué dicen mis apuntes de las clases abstractas?"));
        Assert.Equal(string.Empty, DocumentIndex.FtsQuery("de la"));
    }
}
