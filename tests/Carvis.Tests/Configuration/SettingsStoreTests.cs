using System.Text.Json.Nodes;
using Carvis.Core.Configuration;

namespace Carvis.Tests.Configuration;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("carvis-settings").FullName;
    private string Defaults => Path.Combine(_dir, "appsettings.json");
    private string User => Path.Combine(_dir, "user", "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_UsesCodeDefaultsWhenThereAreNoFiles()
    {
        var settings = new SettingsStore(Defaults, User).Load();

        Assert.Equal("qwen3:8b", settings.Ollama.ChatModel);
        Assert.Equal(16384, settings.Ollama.ContextLength);
    }

    [Fact]
    public void Load_OverlaysUserValuesOnTheDefaults()
    {
        File.WriteAllText(Defaults, """{ "Ollama": { "ChatModel": "qwen3:8b", "KeepAlive": "10m" } }""");
        Directory.CreateDirectory(Path.GetDirectoryName(User)!);
        File.WriteAllText(User, """{ "ollama": { "chatModel": "qwen3:14b" } }""");

        var settings = new SettingsStore(Defaults, User).Load();

        Assert.Equal("qwen3:14b", settings.Ollama.ChatModel);
        Assert.Equal("10m", settings.Ollama.KeepAlive);
    }

    [Fact]
    public void Save_WritesOnlyWhatDiffersFromTheDefaults()
    {
        File.WriteAllText(Defaults, """{ "Ollama": { "ChatModel": "qwen3:8b" } }""");
        var store = new SettingsStore(Defaults, User);
        var settings = store.Load();
        settings.Window.FontSize = 16;

        store.Save(settings);

        var saved = JsonNode.Parse(File.ReadAllText(User))!.AsObject();
        Assert.Single(saved);
        Assert.Equal(16, saved["Window"]!["FontSize"]!.GetValue<double>());
        Assert.Equal(16, store.Load().Window.FontSize);
    }

    [Fact]
    public void Load_IgnoresABrokenUserFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(User)!);
        File.WriteAllText(User, "{ esto no es json");
        var store = new SettingsStore(Defaults, User);

        var settings = store.Load();

        Assert.Equal("qwen3:8b", settings.Ollama.ChatModel);
        Assert.NotNull(store.LoadError);
    }

    [Fact]
    public void Load_ReplacesListsInsteadOfMergingThem()
    {
        File.WriteAllText(Defaults, """{ "Window": { "Backdrop": "Solid" } }""");
        Directory.CreateDirectory(Path.GetDirectoryName(User)!);
        File.WriteAllText(User, """{ "Window": { "Backdrop": "Mica" } }""");

        Assert.Equal("Mica", new SettingsStore(Defaults, User).Load().Window.Backdrop);
    }
}
