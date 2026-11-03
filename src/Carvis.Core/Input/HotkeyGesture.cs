namespace Carvis.Core.Input;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A platform-independent shortcut such as "Alt+Space".</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    public static HotkeyGesture Default { get; } = new(HotkeyModifiers.Alt, "Space");

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = Default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var modifiers = HotkeyModifiers.None;
        string? key = null;

        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var modifier = raw.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Ctrl,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "meta" or "cmd" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };

            if (modifier != HotkeyModifiers.None)
                modifiers |= modifier;
            else if (key is null)
                key = raw;
            else
                return false; // more than one non-modifier key
        }

        if (key is null || modifiers == HotkeyModifiers.None)
            return false;

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public override string ToString()
    {
        var parts = Enum.GetValues<HotkeyModifiers>()
            .Where(m => m != HotkeyModifiers.None && Modifiers.HasFlag(m))
            .Select(m => m.ToString())
            .Append(Key);
        return string.Join('+', parts);
    }
}
