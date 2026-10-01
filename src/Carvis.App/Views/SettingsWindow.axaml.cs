using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Carvis.App.ViewModels;

namespace Carvis.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is SettingsViewModel viewModel)
                await viewModel.LoadModelsAsync();
        };
    }

    private async void OnAddAllowedFolderClick(object? sender, RoutedEventArgs e)
    {
        if (await PickFolderAsync() is { } path && DataContext is SettingsViewModel viewModel)
            viewModel.AddAllowedFolder(path);
    }

    private async void OnAddDocumentFolderClick(object? sender, RoutedEventArgs e)
    {
        if (await PickFolderAsync() is { } path && DataContext is SettingsViewModel viewModel)
            viewModel.AddDocumentFolder(path);
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Elige una carpeta", AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
