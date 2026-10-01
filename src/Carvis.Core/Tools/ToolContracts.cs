namespace Carvis.Core.Tools;

// Phase 3: actions through tool calling (open programs, move/rename files, run scripts).
// Every action that changes something goes through IToolConfirmation first.

public sealed record ToolCall(string Name, IReadOnlyDictionary<string, object?> Arguments);

public sealed record ToolResult(bool Success, string Output);

public interface ITool
{
    /// <summary>Name the model uses to call the tool, e.g. "open_program".</summary>
    string Name { get; }

    string Description { get; }

    /// <summary>JSON Schema of the arguments, sent to Ollama as the tool definition.</summary>
    string ParametersJsonSchema { get; }

    bool RequiresConfirmation { get; }

    /// <summary>Human-readable summary shown in the confirmation dialog.</summary>
    string Describe(ToolCall call);

    Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default);
}

/// <summary>Asks the user in the UI before running an action.</summary>
public interface IToolConfirmation
{
    Task<bool> ConfirmAsync(ITool tool, ToolCall call, CancellationToken cancellationToken = default);
}
