using Carvis.Core.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carvis.App.ViewModels;

public enum ToolCallState
{
    AwaitingConfirmation,
    Running,
    Succeeded,
    Failed,
    Denied,
    Undone,
}

/// <summary>A card in the chat for one action: what it does, its state and the buttons to approve or undo it.</summary>
public sealed partial class ToolCallViewModel : ChatItemViewModel
{
    private readonly Func<string, Task<string>> _undo;
    private TaskCompletionSource<ConfirmationDecision>? _confirmation;

    public ToolCallViewModel(ToolInvocation invocation, bool canApproveForSession, Func<string, Task<string>> undo)
    {
        Invocation = invocation;
        CanApproveForSession = canApproveForSession;
        _undo = undo;
        _state = invocation.NeedsConfirmation ? ToolCallState.AwaitingConfirmation : ToolCallState.Running;
    }

    public ToolInvocation Invocation { get; }
    public string Summary => Invocation.Preview.Summary;
    public IReadOnlyList<string> Details => Invocation.Preview.Details;
    public bool HasDetails => Details.Count > 0;
    public bool IsDangerous => Invocation.Preview.Risk == ToolRisk.Dangerous;
    public bool CanApproveForSession { get; }
    public string ApproveLabel => IsDangerous ? "Sí, hazlo" : "Aceptar";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending), nameof(StateText), nameof(StateIcon), nameof(CanUndo), nameof(IsFailed), nameof(IsDone))]
    private ToolCallState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutput))]
    private string _output = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    private string? _journalEntryId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    private bool _isUndoable;

    [ObservableProperty]
    private bool _showOutput;

    public bool IsPending => State == ToolCallState.AwaitingConfirmation;
    public bool IsFailed => State is ToolCallState.Failed;
    public bool IsDone => State is ToolCallState.Succeeded;
    public bool HasOutput => Output.Length > 0;
    public bool CanUndo => State == ToolCallState.Succeeded && IsUndoable && JournalEntryId is not null;

    public string StateText => State switch
    {
        ToolCallState.AwaitingConfirmation => IsDangerous ? "Necesito tu confirmación (acción delicada)" : "¿Lo hago?",
        ToolCallState.Running => "En curso…",
        ToolCallState.Succeeded => "Hecho",
        ToolCallState.Failed => "No se ha podido hacer",
        ToolCallState.Denied => "Cancelado",
        ToolCallState.Undone => "Deshecho",
        _ => string.Empty,
    };

    public string StateIcon => State switch
    {
        ToolCallState.AwaitingConfirmation => "?",
        ToolCallState.Running => "…",
        ToolCallState.Succeeded => "✓",
        ToolCallState.Failed => "✕",
        ToolCallState.Denied => "–",
        _ => "↶",
    };

    /// <summary>Waits until the user presses one of the buttons (or the answer is stopped).</summary>
    public Task<ConfirmationDecision> WaitForDecisionAsync(CancellationToken cancellationToken)
    {
        _confirmation = new TaskCompletionSource<ConfirmationDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => _confirmation.TrySetResult(ConfirmationDecision.Deny));
        return _confirmation.Task;
    }

    public void Complete(ToolResult result, bool undoable)
    {
        Output = result.Output;
        JournalEntryId = result.JournalEntryId;
        IsUndoable = undoable;
        if (State != ToolCallState.Denied)
            State = result.Success ? ToolCallState.Succeeded : ToolCallState.Failed;
    }

    [RelayCommand]
    public void Approve() => Decide(ConfirmationDecision.Approve);

    [RelayCommand]
    public void ApproveForSession() => Decide(ConfirmationDecision.ApproveForSession);

    [RelayCommand]
    public void Deny()
    {
        if (!IsPending)
            return;
        State = ToolCallState.Denied;
        _confirmation?.TrySetResult(ConfirmationDecision.Deny);
    }

    [RelayCommand]
    private void ToggleOutput() => ShowOutput = !ShowOutput;

    [RelayCommand]
    private async Task UndoAsync()
    {
        if (!CanUndo)
            return;
        try
        {
            Output = await _undo(JournalEntryId!);
            State = ToolCallState.Undone;
        }
        catch (ToolArgumentException ex)
        {
            Output = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Output = $"No he podido deshacerlo: {ex.Message}";
        }
        ShowOutput = true;
    }

    private void Decide(ConfirmationDecision decision)
    {
        if (!IsPending)
            return;
        State = ToolCallState.Running;
        _confirmation?.TrySetResult(decision);
    }
}
