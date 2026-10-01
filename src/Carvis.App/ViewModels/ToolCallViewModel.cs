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
        // Never "always allow" something risky or proposed after reading outside text.
        CanApproveForSession = canApproveForSession && !invocation.AfterExternalContent && invocation.Preview.Warnings.Count == 0;
        _undo = undo;
        _state = invocation.NeedsConfirmation ? ToolCallState.AwaitingConfirmation : ToolCallState.Running;
    }

    public ToolInvocation Invocation { get; }
    public string Summary => Invocation.Preview.Summary;
    public string IconKey => ToolIcons.For(Invocation.ToolName, Invocation.Category);

    /// <summary>Carvis proposes it after reading text from outside (a web page, a document, a capture).</summary>
    public bool AfterExternalContent => Invocation.AfterExternalContent && Invocation.Preview.Risk != ToolRisk.Read;
    public IReadOnlyList<string> Details => Invocation.Preview.Details;
    public bool HasDetails => Details.Count > 0;
    public bool IsDangerous => Invocation.Preview.Risk == ToolRisk.Dangerous || HasWarnings;

    /// <summary>Red lines explaining what is risky (e.g. a script that deletes folders).</summary>
    public IReadOnlyList<ToolWarning> Warnings => Invocation.Preview.Warnings;
    public bool HasWarnings => Warnings.Count > 0;
    public bool IsBlocked => Invocation.Preview.IsBlocked;
    public bool CanApproveForSession { get; }
    public string ApproveLabel => IsDangerous ? "Sí, hazlo" : "Aceptar";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending), nameof(StateText), nameof(StateIconKey), nameof(CanUndo), nameof(IsFailed), nameof(IsDone), nameof(IsRunning), nameof(ResultText), nameof(IsSettled))]
    private ToolCallState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutput), nameof(ResultText))]
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
    public bool IsRunning => State == ToolCallState.Running;
    public bool IsSettled => !IsPending && !IsRunning;
    public bool IsFailed => State is ToolCallState.Failed;
    public bool IsDone => State is ToolCallState.Succeeded;
    public bool HasOutput => Output.Length > 0;
    public bool CanUndo => State == ToolCallState.Succeeded && IsUndoable && JournalEntryId is not null;

    public string StateText => State switch
    {
        ToolCallState.AwaitingConfirmation => IsDangerous ? "Es una acción delicada: revísala antes de aceptar." : "¿Lo hago?",
        ToolCallState.Running => "Haciéndolo…",
        ToolCallState.Succeeded => "Hecho",
        ToolCallState.Failed => IsBlocked ? "Bloqueado por seguridad: no lo he ejecutado" : "No se ha podido hacer",
        ToolCallState.Denied => "Cancelado",
        ToolCallState.Undone => "Deshecho",
        _ => string.Empty,
    };

    /// <summary>The result in one line ("Volumen al 40 %"), or the state while there is none.</summary>
    public string ResultText
    {
        get
        {
            if (State is ToolCallState.AwaitingConfirmation or ToolCallState.Running or ToolCallState.Denied || Output.Length == 0 || IsBlocked)
                return StateText;
            var line = Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? StateText;
            if (State == ToolCallState.Undone)
                return "Deshecho";
            return line.Length > 120 ? line[..120] + "…" : line;
        }
    }

    public string StateIconKey => State switch
    {
        ToolCallState.Succeeded => "Success",
        ToolCallState.Failed => IsBlocked ? "ShieldError" : "Error",
        ToolCallState.Denied => "Dismiss",
        ToolCallState.Undone => "Undo",
        ToolCallState.AwaitingConfirmation when IsDangerous => "ShieldError",
        _ => "Question",
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
            Output = $"No he podido deshacerlo ({ex.Message}). Puede que el archivo se haya movido o esté abierto en otro programa.";
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
