using Carvis.Core.Input;
using Microsoft.Extensions.Logging;
using SharpHook;
using SharpHook.Data;

namespace Carvis.App.Services;

/// <summary>System-wide shortcut based on a low-level keyboard hook (SharpHook).</summary>
public sealed class GlobalHotkeyService(ILogger<GlobalHotkeyService> logger) : IDisposable
{
    private SimpleGlobalHook? _hook;
    private HotkeyModifiers _modifiers;
    private KeyCode _key;
    private bool _isDown;

    /// <summary>Raised on the hook thread; marshal to the UI thread before touching controls.</summary>
    public event EventHandler? Pressed;

    /// <summary>Raised if the hook stops unexpectedly. The argument is a message for the user.</summary>
    public event EventHandler<string>? Failed;

    public bool TryStart(HotkeyGesture gesture, out string? error)
    {
        if (!TryMapKey(gesture.Key, out _key))
        {
            error = $"La tecla «{gesture.Key}» del atajo no es válida.";
            return false;
        }

        _modifiers = gesture.Modifiers;
        _hook = new SimpleGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;

        try
        {
            _hook.RunAsync(GlobalHookType.Keyboard, useBackgroundThread: true)
                .ContinueWith(OnHookFaulted, TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not start the global keyboard hook");
            error = "No he podido registrar el atajo de teclado global.";
            return false;
        }

        logger.LogInformation("Global hotkey {Gesture} registered", gesture);
        error = null;
        return true;
    }

    /// <summary>Changes the shortcut without restarting the hook.</summary>
    public bool TryChange(HotkeyGesture gesture, out string? error)
    {
        if (_hook is null)
            return TryStart(gesture, out error);
        if (!TryMapKey(gesture.Key, out var key))
        {
            error = $"La tecla «{gesture.Key}» del atajo no es válida.";
            return false;
        }
        _key = key;
        _modifiers = gesture.Modifiers;
        _isDown = false;
        logger.LogInformation("Global hotkey changed to {Gesture}", gesture);
        error = null;
        return true;
    }

    public void Dispose()
    {
        if (_hook is { IsDisposed: false })
            _hook.Dispose();
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode != _key || !ModifiersMatch(e.RawEvent.Mask))
            return;

        // Keeps e.g. Windows from opening the window menu on Alt+Space.
        e.SuppressEvent = true;

        if (_isDown)
            return; // auto-repeat while the key is held

        _isDown = true;
        Pressed?.Invoke(this, EventArgs.Empty);
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode != _key || !_isDown)
            return;

        _isDown = false;
        e.SuppressEvent = true;
    }

    private bool ModifiersMatch(EventMask mask) =>
        mask.HasCtrl() == _modifiers.HasFlag(HotkeyModifiers.Ctrl) &&
        mask.HasAlt() == _modifiers.HasFlag(HotkeyModifiers.Alt) &&
        mask.HasShift() == _modifiers.HasFlag(HotkeyModifiers.Shift) &&
        mask.HasMeta() == _modifiers.HasFlag(HotkeyModifiers.Win);

    private void OnHookFaulted(Task task)
    {
        logger.LogError(task.Exception, "Global keyboard hook stopped");
        Failed?.Invoke(this, "El atajo de teclado global ha dejado de funcionar. Puedes abrir Carvis desde la bandeja del sistema.");
    }

    private static bool TryMapKey(string key, out KeyCode code)
    {
        var name = key.ToLowerInvariant() switch
        {
            "esc" => "Escape",
            "return" => "Enter",
            _ => key,
        };
        return Enum.TryParse("Vc" + name, ignoreCase: true, out code) && code != KeyCode.VcUndefined;
    }
}
