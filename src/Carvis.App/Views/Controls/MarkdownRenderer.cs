using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace Carvis.App.Views.Controls;

/// <summary>Turns Markdown into Avalonia controls. Covers what LLM answers usually contain.</summary>
internal static class MarkdownRenderer
{
    private const double BodySize = 14;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .Build();

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");
    private static readonly IBrush TextBrush = Brush.Parse("#E5E7EB");
    private static readonly IBrush MutedBrush = Brush.Parse("#9CA3AF");
    private static readonly IBrush AccentBrush = Brush.Parse("#22D3EE");
    private static readonly IBrush CodeBlockBackground = Brush.Parse("#0B1220");
    private static readonly IBrush InlineCodeBrush = Brush.Parse("#F0ABFC");
    private static readonly IBrush RuleBrush = Brush.Parse("#2A3A4F");

    public static Control Render(string markdown)
    {
        var panel = Stack(8);
        foreach (var block in Markdig.Markdown.Parse(markdown, Pipeline))
            panel.Children.Add(RenderBlock(block));
        return panel;
    }

    private static Control RenderBlock(Block block) => block switch
    {
        HeadingBlock heading => Text(heading.Inline, heading.Level switch { 1 => 20, 2 => 17, _ => 15 }, FontWeight.Bold),
        ParagraphBlock paragraph => Text(paragraph.Inline),
        CodeBlock code => CodeBox(code),
        ListBlock list => List(list),
        QuoteBlock quote => Quote(quote),
        ThematicBreakBlock => new Border { Height = 1, Background = RuleBrush, Margin = new Thickness(0, 4) },
        Table table => TableGrid(table),
        LeafBlock leaf => Plain(leaf.Lines.ToString()),
        ContainerBlock container => Children(container, 8),
        _ => new Panel(),
    };

    private static SelectableTextBlock Text(ContainerInline? inline, double size = BodySize, FontWeight? weight = null)
    {
        var text = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = size,
            LineHeight = Math.Round(size * 1.5),
            FontWeight = weight ?? FontWeight.Normal,
            Foreground = TextBrush,
        };
        if (inline is not null)
            AddInlines(text.Inlines!, inline);
        return text;
    }

    private static SelectableTextBlock Plain(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = BodySize,
        Foreground = TextBrush,
    };

    private static void AddInlines(InlineCollection target, ContainerInline container)
    {
        foreach (var inline in container)
            target.Add(ToAvalonia(inline));
    }

    private static Avalonia.Controls.Documents.Inline ToAvalonia(MdInline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                return new Run(literal.Content.ToString());

            case EmphasisInline emphasis:
                Span span = (emphasis.DelimiterChar, emphasis.DelimiterCount) switch
                {
                    ('~', 2) => new Span { TextDecorations = TextDecorations.Strikethrough },
                    ('+', 2) => new Underline(),
                    (_, >= 2) => new Bold(),
                    _ => new Italic(),
                };
                AddInlines(span.Inlines, emphasis);
                return span;

            case CodeInline code:
                // Run backgrounds only cover part of the text in Avalonia 11, so inline code uses a colour.
                return new Run(code.Content) { FontFamily = Mono, FontSize = BodySize - 1, Foreground = InlineCodeBrush };

            case LinkInline { IsImage: true } image:
                return new Run($"[imagen: {image.Url}]") { Foreground = MutedBrush };

            case LinkInline link:
                var linkSpan = new Span { Foreground = AccentBrush, TextDecorations = TextDecorations.Underline };
                if (link.FirstChild is null)
                    linkSpan.Inlines.Add(new Run(link.Url));
                else
                    AddInlines(linkSpan.Inlines, link);
                return linkSpan;

            case AutolinkInline autolink:
                return new Run(autolink.Url) { Foreground = AccentBrush, TextDecorations = TextDecorations.Underline };

            case TaskList task:
                return new Run(task.Checked ? "☑ " : "☐ ");

            case LineBreakInline:
                // Chat answers expect single newlines to show up as such.
                return new LineBreak();

            case HtmlEntityInline entity:
                return new Run(entity.Transcoded.ToString());

            case HtmlInline html:
                return new Run(html.Tag);

            case ContainerInline other:
                var container = new Span();
                AddInlines(container.Inlines, other);
                return container;

            default:
                return new Run(inline.ToString());
        }
    }

    private static Control CodeBox(CodeBlock block)
    {
        var code = block.Lines.ToString().TrimEnd('\r', '\n');
        var language = (block as FencedCodeBlock)?.Info;

        var copy = new Button { Content = "Copiar", FontSize = 11, Padding = new Thickness(8, 2) };
        copy.Classes.Add("ghost");
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(copy)?.Clipboard is not { } clipboard)
                return;
            await clipboard.SetTextAsync(code);
            copy.Content = "Copiado";
            await Task.Delay(1500);
            copy.Content = "Copiar";
        };
        Grid.SetColumn(copy, 1);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(language) ? "código" : language,
            FontSize = 11,
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(copy);

        var body = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new SelectableTextBlock { Text = code, FontFamily = Mono, FontSize = 13, Foreground = TextBrush },
        };

        var content = Stack(4);
        content.Children.Add(header);
        content.Children.Add(body);
        return new Border
        {
            Background = CodeBlockBackground,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 10),
            Child = content,
        };
    }

    private static Control List(ListBlock list)
    {
        var panel = Stack(4);
        var number = list.IsOrdered && int.TryParse(list.OrderedStart, out var start) ? start : 1;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var marker = list.IsOrdered ? $"{number++}{list.OrderedDelimiter}" : "•";
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            row.Children.Add(new TextBlock
            {
                Text = marker,
                FontSize = BodySize,
                LineHeight = Math.Round(BodySize * 1.5),
                Foreground = MutedBrush,
                MinWidth = list.IsOrdered ? 20 : 12,
                Margin = new Thickness(0, 0, 6, 0),
            });

            var content = Children(item, 4);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            panel.Children.Add(row);
        }
        return panel;
    }

    private static Control Quote(QuoteBlock quote) => new Border
    {
        BorderBrush = RuleBrush,
        BorderThickness = new Thickness(3, 0, 0, 0),
        Padding = new Thickness(12, 2, 0, 2),
        Child = Children(quote, 6),
    };

    private static Control TableGrid(Table table)
    {
        var grid = new Grid();
        var rows = table.OfType<TableRow>().ToList();
        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        for (var c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var column = 0;
            foreach (var cell in rows[r].OfType<TableCell>())
            {
                var content = Children(cell, 4);
                if (rows[r].IsHeader)
                {
                    foreach (var text in content.Children.OfType<SelectableTextBlock>())
                        text.FontWeight = FontWeight.SemiBold;
                }

                var border = new Border
                {
                    Padding = new Thickness(8, 4),
                    BorderBrush = RuleBrush,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = content,
                };
                Grid.SetRow(border, r);
                Grid.SetColumn(border, column);
                Grid.SetColumnSpan(border, Math.Max(1, cell.ColumnSpan));
                grid.Children.Add(border);
                column += Math.Max(1, cell.ColumnSpan);
            }
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = grid,
        };
    }

    private static StackPanel Children(ContainerBlock container, double spacing)
    {
        var panel = Stack(spacing);
        foreach (var child in container)
            panel.Children.Add(RenderBlock(child));
        return panel;
    }

    private static StackPanel Stack(double spacing) => new() { Spacing = spacing };
}
