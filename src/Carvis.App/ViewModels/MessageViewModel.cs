using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Carvis.Core.Chat;
using Carvis.Core.Tools.Scripts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carvis.App.ViewModels;

public sealed partial class MessageViewModel(ChatRole role, string content = "") : ChatItemViewModel
{
    private static readonly Regex CodeBlock = new(@"```[^\n]*\n(.*?)```", RegexOptions.Singleline);

    public ChatRole Role { get; } = role;
    public bool IsUser => Role == ChatRole.User;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    private string _content = content;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    [NotifyPropertyChangedFor(nameof(ShowExternalNote), nameof(CommandWarning), nameof(HasCommandWarning), nameof(IsCommandDangerous))]
    private bool _isStreaming;

    /// <summary>The answer was written after reading a web page, a document or the output of an action.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExternalNote), nameof(CommandWarning), nameof(HasCommandWarning), nameof(IsCommandDangerous))]
    private bool _usesExternalContent;

    public bool ShowExternalNote => UsesExternalContent && !IsStreaming && !HasCommandWarning;

    /// <summary>Commands in an answer based on outside text are never trusted silently.</summary>
    public string? CommandWarning
    {
        get
        {
            if (!UsesExternalContent || IsStreaming)
                return null;
            var code = CodeBlock.Matches(Content).Select(m => m.Groups[1].Value).ToList();
            if (code.Count == 0)
                return null;
            var risks = code.SelectMany(ScriptSafety.Analyze).Select(w => w.Text).Distinct().ToList();
            return risks.Count > 0
                ? "Cuidado: estos comandos salen de contenido externo y son peligrosos. " + string.Join(" ", risks) + " No los ejecutes."
                : "Estos comandos salen de contenido externo (una web o un documento). Revísalos antes de ejecutarlos: no los he comprobado.";
        }
    }

    public bool HasCommandWarning => CommandWarning is not null;
    public bool IsCommandDangerous => CommandWarning?.StartsWith("Cuidado", StringComparison.Ordinal) == true;

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

    /// <summary>Images sent with this message (only the count: they aren't kept).</summary>
    public int ImageCount { get; init; }

    public bool HasAttachments => Attachments.Count > 0 || ImageCount > 0;
    public string AttachmentsText => string.Join(" · ", Attachments.Select(Path.GetFileName)
        .Concat(ImageCount > 0 ? [ImageCount == 1 ? "1 imagen" : $"{ImageCount} imágenes"] : []));

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
    public string Text { get; } = FirstLine(result);
    public string IconKey { get; } = ToolIcons.For(toolName);

    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length > 140 ? line[..140] + "…" : line;
    }
}
