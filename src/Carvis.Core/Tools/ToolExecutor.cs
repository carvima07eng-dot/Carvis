using System.Runtime.CompilerServices;
using Carvis.Core.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carvis.Core.Tools;

/// <summary>One processed tool call: the event for the UI and, once finished, the message for the model.</summary>
public sealed record ToolStep(ChatEvent Event, ChatMessage? ResultMessage = null, bool ExternalContent = false);

/// <summary>
/// Runs tool calls the same way everywhere (chat answers, routines, scheduled tasks): validate,
/// ask when the policy says so, execute, write the journal. Calls a tool returns as follow-ups
/// (a routine's steps) are run right after it, each with its own confirmation.
/// </summary>
public sealed class ToolExecutor(
    IToolRegistry registry,
    IToolConfirmation? confirmation = null,
    ToolPolicy? policy = null,
    IActionJournal? journal = null,
    ILogger<ToolExecutor>? logger = null)
{
    private const int MaxFollowUps = 30;
    private readonly ILogger _logger = logger ?? NullLogger<ToolExecutor>.Instance;

    public async IAsyncEnumerable<ToolStep> RunAsync(
        IReadOnlyList<ToolCall> calls,
        bool externalContent,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var queue = new Queue<ToolCall>(calls);
        var followUps = 0;

        while (queue.Count > 0)
        {
            var call = queue.Dequeue();
            var context = new ToolContext { ExternalContentInTurn = externalContent };
            var invocation = Prepare(call, context, out var tool, out var arguments, out var error);
            yield return new ToolStep(new ToolStarted(invocation));

            ToolResult result;
            if (error is not null)
            {
                result = ToolResult.Fail(error);
            }
            else
            {
                var decision = invocation.NeedsConfirmation
                    ? confirmation is null ? ConfirmationDecision.Deny : await confirmation.ConfirmAsync(invocation, cancellationToken)
                    : ConfirmationDecision.Approve;

                if (decision == ConfirmationDecision.Deny)
                {
                    result = ToolResult.Fail("El usuario ha cancelado esta acción. No la repitas salvo que te lo pida.");
                }
                else
                {
                    if (decision == ConfirmationDecision.ApproveForSession)
                        policy?.ApproveForSession(invocation.Preview);
                    result = await ExecuteAsync(tool!, arguments!, invocation, context, cancellationToken);
                }
            }

            externalContent |= result.ContainsExternalContent;
            foreach (var followUp in result.FollowUpCalls ?? [])
            {
                if (++followUps <= MaxFollowUps)
                    queue.Enqueue(followUp);
            }

            yield return new ToolStep(new ToolFinished(invocation, result),
                new ChatMessage(ChatRole.Tool, result.Output) { ToolName = call.Name }, externalContent);
        }
    }

    public ToolInvocation Prepare(ToolCall call, ToolContext context, out ITool? tool, out ToolArguments? arguments, out string? error)
    {
        var id = call.Id ?? Guid.NewGuid().ToString("N")[..8];
        tool = registry.Find(call.Name);
        arguments = new ToolArguments(call.Arguments);
        error = null;

        if (tool is null || tool is IConditionalTool { IsEnabled: false })
        {
            error = tool is null
                ? $"La herramienta «{call.Name}» no existe. Usa solo las herramientas disponibles."
                : ((IConditionalTool)tool).DisabledReason;
            return new ToolInvocation(id, call.Name, new ToolPreview($"Herramienta no disponible: {call.Name}", ToolRisk.Read), false);
        }

        try
        {
            var preview = tool.Preview(arguments, context);
            if (preview.IsBlocked)
            {
                error = "Bloqueado por seguridad: " + string.Join(" ", preview.Warnings.Where(w => w.Blocks).Select(w => w.Text)) +
                        " No lo intentes de otra manera: explícale al usuario el riesgo y, si de verdad lo necesita, que lo haga él a mano.";
                return new ToolInvocation(id, tool.Name, preview, false)
                {
                    Category = tool.Category,
                    AfterExternalContent = context.ExternalContentInTurn,
                };
            }
            var confirm = policy?.NeedsConfirmation(preview, context) ?? (preview.AlwaysConfirm || preview.Risk >= ToolRisk.Modify);
            return new ToolInvocation(id, tool.Name, preview, confirm)
            {
                Category = tool.Category,
                AfterExternalContent = context.ExternalContentInTurn,
            };
        }
        catch (ToolArgumentException ex)
        {
            error = ex.Message;
            return new ToolInvocation(id, tool.Name, new ToolPreview($"{tool.Name}: argumentos no válidos", ToolRisk.Read), false);
        }
    }

    private async Task<ToolResult> ExecuteAsync(ITool tool, ToolArguments arguments, ToolInvocation invocation, ToolContext context, CancellationToken cancellationToken)
    {
        ToolResult result;
        try
        {
            result = await tool.ExecuteAsync(arguments, context, cancellationToken);
        }
        catch (ToolArgumentException ex)
        {
            result = ToolResult.Fail(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Tool {Tool} failed", tool.Name);
            result = ToolResult.Fail($"Error al ejecutar «{tool.Name}»: {ex.Message}");
        }

        // Everything that acts is written down, so the user can see later what Carvis did.
        if (result.JournalEntryId is null && invocation.Preview.Risk != ToolRisk.Read && journal is not null)
        {
            var entry = journal.Record(tool.Name, invocation.Preview.Summary, result.Success);
            result = result with { JournalEntryId = entry.Id };
        }

        _logger.LogInformation("Tool {Tool} -> {Success}", tool.Name, result.Success ? "ok" : "error");
        return result;
    }
}

/// <summary>A tool that can be switched off (e.g. Internet tools while Internet is not allowed).</summary>
public interface IConditionalTool
{
    bool IsEnabled { get; }
    string DisabledReason { get; }
}
