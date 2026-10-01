using Carvis.Core.Chat;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Carvis.App.ViewModels;

public sealed partial class MessageViewModel(ChatRole role, string content = "") : ViewModelBase
{
    public ChatRole Role { get; } = role;
    public bool IsUser => Role == ChatRole.User;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    private string _content = content;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    private bool _isStreaming;

    [ObservableProperty]
    private bool _isError;

    /// <summary>Waiting for the first token.</summary>
    public bool IsWaiting => IsStreaming && Content.Length == 0;

    public void Append(string text) => Content += text;
}
