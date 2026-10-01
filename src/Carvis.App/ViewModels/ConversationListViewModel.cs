using System.Collections.ObjectModel;
using System.Globalization;
using Carvis.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carvis.App.ViewModels;

public sealed partial class ConversationItemViewModel(ConversationInfo info) : ViewModelBase
{
    public ConversationInfo Info { get; private set; } = info;
    public string Id => Info.Id;

    [ObservableProperty]
    private string _title = info.Title;

    [ObservableProperty]
    private bool _pinned = info.Pinned;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private bool _isCurrent;

    public string When => Describe(Info.UpdatedAt);

    private static string Describe(DateTimeOffset date)
    {
        var local = date.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        if (local.Date == today)
            return local.ToString("HH:mm");
        if (local.Date == today.AddDays(-1))
            return "Ayer";
        if (local.Date > today.AddDays(-7))
            return local.ToString("dddd", CultureInfo.GetCultureInfo("es-ES"));
        return local.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("es-ES"));
    }
}

/// <summary>The list of saved conversations shown in the history panel.</summary>
public sealed partial class ConversationListViewModel(IConversationStore store) : ViewModelBase
{
    public ObservableCollection<ConversationItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private string? _currentId;

    public event Action<ConversationItemViewModel>? OpenRequested;
    public event Action<string>? Deleted;

    public bool IsEmpty => Items.Count == 0;

    partial void OnSearchChanged(string value) => Refresh();

    partial void OnCurrentIdChanged(string? value)
    {
        foreach (var item in Items)
            item.IsCurrent = item.Id == value;
    }

    public void Refresh()
    {
        Items.Clear();
        foreach (var info in store.List(string.IsNullOrWhiteSpace(Search) ? null : Search))
            Items.Add(new ConversationItemViewModel(info) { IsCurrent = info.Id == CurrentId });
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void Open(ConversationItemViewModel item) => OpenRequested?.Invoke(item);

    /// <summary>The user wants the conversation as a Markdown file.</summary>
    public event Action<ConversationItemViewModel>? ExportRequested;

    [RelayCommand]
    private void Export(ConversationItemViewModel item) => ExportRequested?.Invoke(item);

    [RelayCommand]
    private void TogglePin(ConversationItemViewModel item)
    {
        store.SetPinned(item.Id, !item.Pinned);
        Refresh();
    }

    [RelayCommand]
    private void StartRename(ConversationItemViewModel item) => item.IsRenaming = true;

    [RelayCommand]
    private void FinishRename(ConversationItemViewModel item)
    {
        item.IsRenaming = false;
        if (!string.IsNullOrWhiteSpace(item.Title))
            store.Rename(item.Id, item.Title);
        Refresh();
    }

    [RelayCommand]
    private void Delete(ConversationItemViewModel item)
    {
        store.Delete(item.Id);
        Items.Remove(item);
        OnPropertyChanged(nameof(IsEmpty));
        Deleted?.Invoke(item.Id);
    }
}
