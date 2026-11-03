using CommunityToolkit.Mvvm.ComponentModel;

namespace Carvis.App.ViewModels;

/// <summary>Anything shown in the conversation: messages and tool cards.</summary>
public abstract partial class ChatItemViewModel : ViewModelBase
{
    /// <summary>Just added in this session: it animates in once (not when scrolled back into view later).</summary>
    [ObservableProperty]
    private bool _isFresh;

    public T Fresh<T>() where T : ChatItemViewModel
    {
        IsFresh = true;
        Avalonia.Threading.DispatcherTimer.RunOnce(() => IsFresh = false, TimeSpan.FromMilliseconds(600));
        return (T)this;
    }
}
