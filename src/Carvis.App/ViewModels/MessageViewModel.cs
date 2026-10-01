using System.Collections.ObjectModel;
using Carvis.Core.Chat;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carvis.App.ViewModels;

public sealed partial class MessageViewModel(ChatRole role, string content = "") : ChatItemViewModel
{
    public ChatRole Role { get; } = role;
    public bool IsUser => Role == ChatRole.User;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    private string _content = content;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    private bool _isStreaming;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private string _copyLabel = "Copiar";

    /// <summary>Reasoning of the model, shown folded (only with thinking on).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThinking))]
    private string _thinking = string.Empty;

    [ObservableProperty]
    private bool _showThinking;

    /// <summary>Set on the last answer: offers "Regenerar".</summary>
    [ObservableProperty]
    private bool _canRegenerate;

    /// <summary>Set on the last user message: offers "Editar".</summary>
    [ObservableProperty]
    private bool _canEdit;

    /// <summary>Waiting for the first token.</summary>
    public bool IsWaiting => IsStreaming && Content.Length == 0;

    public bool CanCopy => !IsUser && !IsStreaming && Content.Length > 0;
    public bool HasThinking => !string.IsNullOrWhiteSpace(Thinking);

    /// <summary>Documents the answer could cite as [n].</summary>
    public ObservableCollection<SourceReference> Sources { get; } = [];

    /// <summary>Files the user attached to this message.</summary>
    public IReadOnlyList<string> Attachments { get; init; } = [];

    public bool HasAttachments => Attachments.Count > 0;
    public string AttachmentsText => string.Join("  ", Attachments.Select(a => "📎 " + Path.GetFileName(a)));

    public void Append(string text) => Content += text;

    public async Task ShowCopiedAsync()
    {
        CopyLabel = "Copiado";
        await Task.Delay(1500);
        CopyLabel = "Copiar";
    }

    [RelayCommand]
    private void ToggleThinking() => ShowThinking = !ShowThinking;
}

/// <summary>A tool call from a reopened conversation: just what was done, without buttons.</summary>
public sealed class HistoryToolViewModel(string toolName, string result) : ChatItemViewModel
{
    public string Text { get; } = $"{toolName}: {FirstLine(result)}";

    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length > 140 ? line[..140] + "…" : line;
    }
}
