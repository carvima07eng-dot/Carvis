namespace Carvis.Core.Tools;

/// <summary>
/// Connects the chat service with whatever UI answers confirmations, without either knowing the
/// other. With nobody listening every action is denied.
/// </summary>
public sealed class ToolConfirmationBroker : IToolConfirmation
{
    public Func<ToolInvocation, CancellationToken, Task<ConfirmationDecision>>? Handler { get; set; }

    public Task<ConfirmationDecision> ConfirmAsync(ToolInvocation invocation, CancellationToken cancellationToken = default) =>
        Handler?.Invoke(invocation, cancellationToken) ?? Task.FromResult(ConfirmationDecision.Deny);
}
