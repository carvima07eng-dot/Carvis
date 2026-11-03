using Carvis.Core.Configuration;

namespace Carvis.Tests.Configuration;

public class SettingsValidatorTests
{
    [Fact]
    public void Validate_AcceptsTheDefaults()
    {
        Assert.Empty(SettingsValidator.Validate(new CarvisSettings()));
    }

    [Fact]
    public void Validate_FixesImpossibleValuesAndExplainsThem()
    {
        var settings = new CarvisSettings();
        settings.Ollama.BaseUrl = "localhost sin http";
        settings.Ollama.ContextLength = 10;
        settings.Ollama.KeepAlive = "mucho";
        settings.Hotkey.ToggleWindow = "Espacio";
        settings.Window.Backdrop = "Cristal";

        var problems = SettingsValidator.Validate(settings);

        Assert.Equal(5, problems.Count);
        Assert.Equal("http://localhost:11434", settings.Ollama.BaseUrl);
        Assert.Equal(16384, settings.Ollama.ContextLength);
        Assert.Equal("-1", settings.Ollama.KeepAlive);
        Assert.Equal("Alt+Space", settings.Hotkey.ToggleWindow);
        Assert.Equal("Solid", settings.Window.Backdrop);
    }

    [Theory]
    [InlineData("30m", true)]
    [InlineData("1.5h", true)]
    [InlineData("-1", true)]
    [InlineData("0", true)]
    [InlineData("", false)]
    [InlineData("10x", false)]
    public void IsValidKeepAlive(string value, bool valid)
    {
        Assert.Equal(valid, SettingsValidator.IsValidKeepAlive(value));
    }
}
