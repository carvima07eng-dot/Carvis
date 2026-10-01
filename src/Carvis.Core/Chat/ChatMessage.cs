using Carvis.Core.Tools;

namespace Carvis.Core.Chat;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool,
}

/// <summary>A document fragment offered to the model, cited in answers as [Number].</summary>
public sealed record SourceReference(int Number, string Path, int? Page = null, string? Section = null)
{
    public string Label => System.IO.Path.GetFileName(Path) + (Page is { } page ? $", pág. {page}" : Section is { Length: > 0 } section ? $" · {Shorten(section)}" : string.Empty);

    private static string Shorten(string text) => text.Length <= 32 ? text : text[..32] + "…";
}

/// <summary>What the user sends: text plus optional attached files and images.</summary>
public sealed record ChatInput(string Text)
{
    public IReadOnlyList<string> Attachments { get; init; } = [];
    public IReadOnlyList<byte[]> Images { get; init; } = [];
}

public sealed record ChatMessage(ChatRole Role, string Content)
{
    /// <summary>Tools the assistant asked to run in this message.</summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    /// <summary>For tool results: which tool produced this content.</summary>
    public string? ToolName { get; init; }

    /// <summary>PNG/JPEG images attached to the message (vision models).</summary>
    public IReadOnlyList<byte[]>? Images { get; init; }

    /// <summary>Context text that comes from documents or the web: data, not instructions.</summary>
    public bool IsExternal { get; init; }

    /// <summary>Numbered sources the model can cite as [n].</summary>
    public IReadOnlyList<SourceReference>? Sources { get; init; }

    public bool Equals(ChatMessage? other) =>
        other is not null && Role == other.Role && Content == other.Content && ToolName == other.ToolName
        && SameCalls(ToolCalls, other.ToolCalls) && (Images?.Count ?? 0) == (other.Images?.Count ?? 0);

    public override int GetHashCode() => HashCode.Combine(Role, Content, ToolName);

    private static bool SameCalls(IReadOnlyList<ToolCall>? a, IReadOnlyList<ToolCall>? b) =>
        (a?.Count ?? 0) == (b?.Count ?? 0) && (a is null || a.Zip(b!).All(p => p.First.Equals(p.Second)));
}
