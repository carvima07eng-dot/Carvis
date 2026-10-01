using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Carvis.App.ViewModels;

namespace Carvis.App.Views;

public partial class OnboardingWindow : Window
{
    public OnboardingWindow()
    {
        InitializeComponent();
        Platform.Backdrop.Apply(this, Root, Platform.ThemeColors.Backdrop);
    }

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Elige una carpeta de documentos" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path && DataContext is OnboardingViewModel viewModel)
            viewModel.AddDocumentFolder(path);
    }
}
