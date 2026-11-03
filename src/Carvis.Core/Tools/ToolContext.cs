namespace Carvis.Core.Tools;

/// <summary>Information about the turn in which a tool runs.</summary>
public sealed record ToolContext
{
    /// <summary>Text from files, web pages or screenshots entered the conversation in this turn.</summary>
    public bool ExternalContentInTurn { get; init; }

    public static ToolContext Default { get; } = new();
}
