using Carvis.Core.Input;

namespace Carvis.Tests.Input;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Alt+Space", HotkeyModifiers.Alt, "Space")]
    [InlineData("ctrl + shift + J", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "J")]
    [InlineData("Win+F1", HotkeyModifiers.Win, "F1")]
    public void TryParse_ReadsModifiersAndKey(string text, HotkeyModifiers modifiers, string key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(new HotkeyGesture(modifiers, key), gesture);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Space")]
    [InlineData("Alt")]
    [InlineData("Alt+A+B")]
    public void TryParse_FallsBackToAltSpaceWhenInvalid(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(HotkeyGesture.Default, gesture);
    }

    [Fact]
    public void ToString_RoundTrips()
    {
        HotkeyGesture.TryParse("shift+ctrl+K", out var gesture);

        Assert.Equal("Ctrl+Shift+K", gesture.ToString());
    }
}
