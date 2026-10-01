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

    /// <summary>No conversations at all (not just none matching the search).</summary>
    public bool HasNoConversations => IsEmpty && string.IsNullOrWhiteSpace(Search);

    public bool HasNoResults => IsEmpty && !string.IsNullOrWhiteSpace(Search);

    private CancellationTokenSource? _searching;

    // Typing searches after a short pause, and the query runs off the UI thread.
    partial void OnSearchChanged(string value) => _ = SearchAsync(value);

    private async Task SearchAsync(string text)
    {
        _searching?.Cancel();
        var cancellation = _searching = new CancellationTokenSource();
        try
        {
            await Task.Delay(150, cancellation.Token);
            var query = string.IsNullOrWhiteSpace(text) ? null : text;
            var results = await Task.Run(() => store.List(query), cancellation.Token);
            if (!cancellation.IsCancellationRequested)
                Show(results);
        }
        catch (OperationCanceledException)
        {
            // A newer search replaced this one.
        }
    }

    [RelayCommand]
    private void ClearSearch() => Search = string.Empty;

    partial void OnCurrentIdChanged(string? value)
    {
        foreach (var item in Items)
            item.IsCurrent = item.Id == value;
    }

    public void Refresh() => Show(store.List(string.IsNullOrWhiteSpace(Search) ? null : Search));

    private void Show(IEnumerable<ConversationInfo> conversations)
    {
        Items.Clear();
        foreach (var info in conversations)
            Items.Add(new ConversationItemViewModel(info) { IsCurrent = info.Id == CurrentId });
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoConversations));
        OnPropertyChanged(nameof(HasNoResults));
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
        OnPropertyChanged(nameof(HasNoConversations));
        OnPropertyChanged(nameof(HasNoResults));
        Deleted?.Invoke(item.Id);
    }
}
