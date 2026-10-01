using Carvis.Core.Tools;

namespace Carvis.Core.Chat;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool,
}

public sealed record ChatMessage(ChatRole Role, string Content)
{
    /// <summary>Tools the assistant asked to run in this message.</summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    /// <summary>For tool results: which tool produced this content.</summary>
    public string? ToolName { get; init; }

    /// <summary>PNG/JPEG images attached to the message (vision models).</summary>
    public IReadOnlyList<byte[]>? Images { get; init; }

    public bool Equals(ChatMessage? other) =>
        other is not null && Role == other.Role && Content == other.Content && ToolName == other.ToolName
        && SameCalls(ToolCalls, other.ToolCalls) && (Images?.Count ?? 0) == (other.Images?.Count ?? 0);

    public override int GetHashCode() => HashCode.Combine(Role, Content, ToolName);

    private static bool SameCalls(IReadOnlyList<ToolCall>? a, IReadOnlyList<ToolCall>? b) =>
        (a?.Count ?? 0) == (b?.Count ?? 0) && (a is null || a.Zip(b!).All(p => p.First.Equals(p.Second)));
}
