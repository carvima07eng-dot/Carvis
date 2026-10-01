using System.Text.Json.Nodes;

namespace Carvis.Core.Tools;

/// <summary>How careful Carvis must be before running a tool.</summary>
public enum ToolRisk
{
    /// <summary>Only reads information. Never asks.</summary>
    Read,

    /// <summary>Acts, but harmlessly (open a program or a web page). Doesn't ask by default.</summary>
    Low,

    /// <summary>Changes files or settings in a reversible way. Asks unless allowed for the session.</summary>
    Modify,

    /// <summary>Deletes, overwrites, runs code or shuts the PC down. Always asks.</summary>
    Dangerous,
}

public sealed record ToolCall(string Name, JsonObject Arguments, string? Id = null)
{
    public bool Equals(ToolCall? other) =>
        other is not null && Name == other.Name && JsonNode.DeepEquals(Arguments, other.Arguments);

    public override int GetHashCode() => Name.GetHashCode();
}

/// <summary>What the tool is going to do, in words the user understands.</summary>
public sealed record ToolPreview(string Summary, ToolRisk Risk)
{
    /// <summary>Extra lines shown under the summary (files affected, script contents...).</summary>
    public IReadOnlyList<string> Details { get; init; } = [];

    /// <summary>Key used for "allow during this session" (e.g. tool + folder).</summary>
    public string? PermissionScope { get; init; }
}

public sealed record ToolResult(bool Success, string Output)
{
    /// <summary>Undo information recorded in the action journal, if the change can be reverted.</summary>
    public string? JournalEntryId { get; init; }

    /// <summary>The output contains text from outside (files, web pages): treat it as untrusted.</summary>
    public bool ContainsExternalContent { get; init; }

    /// <summary>More calls to run right after this one (the steps of a routine).</summary>
    public IReadOnlyList<ToolCall>? FollowUpCalls { get; init; }

    public static ToolResult Ok(string output) => new(true, output);
    public static ToolResult Fail(string output) => new(false, output);
}

public interface ITool
{
    /// <summary>Name the model uses to call the tool, e.g. "crear_carpeta".</summary>
    string Name { get; }

    /// <summary>Description for the model: what it does and when to use it.</summary>
    string Description { get; }

    /// <summary>Group used to pick the relevant tools for a message ("archivos", "programas"...).</summary>
    string Category { get; }

    /// <summary>JSON Schema of the arguments.</summary>
    JsonObject Parameters { get; }

    /// <summary>Validates the arguments and explains what will happen. Throws <see cref="ToolArgumentException"/>.</summary>
    ToolPreview Preview(ToolArguments arguments, ToolContext context);

    Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default);
}

/// <summary>The model sent wrong or missing arguments; the message goes back to it so it can retry.</summary>
public sealed class ToolArgumentException(string message) : Exception(message);

/// <summary>One tool call as the UI sees it.</summary>
public sealed record ToolInvocation(string Id, string ToolName, ToolPreview Preview, bool NeedsConfirmation);

public enum ConfirmationDecision
{
    Deny,
    Approve,

    /// <summary>Approve and don't ask again for the same scope during this session.</summary>
    ApproveForSession,
}

/// <summary>Asks the user in the UI before running an action.</summary>
public interface IToolConfirmation
{
    Task<ConfirmationDecision> ConfirmAsync(ToolInvocation invocation, CancellationToken cancellationToken = default);
}
