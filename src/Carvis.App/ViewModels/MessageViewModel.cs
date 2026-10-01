using Carvis.Core.Chat;
using CommunityToolkit.Mvvm.ComponentModel;

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
    private string _copyLabel = "Copiar";

    [ObservableProperty]
    private bool _isError;

    /// <summary>Waiting for the first token.</summary>
    public bool IsWaiting => IsStreaming && Content.Length == 0;

    public bool CanCopy => !IsUser && !IsStreaming && Content.Length > 0;

    public void Append(string text) => Content += text;

    public async Task ShowCopiedAsync()
    {
        CopyLabel = "Copiado";
        await Task.Delay(1500);
        CopyLabel = "Copiar";
    }
}
