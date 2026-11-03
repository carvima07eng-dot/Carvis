using Carvis.Core.Input;
using Microsoft.Extensions.Logging;
using SharpHook;
using SharpHook.Data;

namespace Carvis.App.Services;

/// <summary>System-wide shortcuts based on a low-level keyboard hook (SharpHook).</summary>
public sealed class GlobalHotkeyService(ILogger<GlobalHotkeyService> logger) : IDisposable
{
    public const string WindowBinding = "window";

    private readonly object _lock = new();
    private readonly Dictionary<string, Binding> _bindings = [];
    private SimpleGlobalHook? _hook;

    private sealed class Binding(HotkeyModifiers modifiers, KeyCode key)
    {
        public HotkeyModifiers Modifiers { get; } = modifiers;
        public KeyCode Key { get; } = key;
        public bool IsDown { get; set; }
    }

    /// <summary>The window shortcut. Raised on the hook thread; marshal to the UI thread before touching controls.</summary>
    public event EventHandler? Pressed;

    /// <summary>Any other shortcut, by the name it was registered with (hook thread).</summary>
    public event Action<string>? BindingPressed;

    /// <summary>Raised if the hook stops unexpectedly. The argument is a message for the user.</summary>
    public event EventHandler<string>? Failed;

    public bool TryStart(HotkeyGesture gesture, out string? error)
    {
        if (!TrySet(WindowBinding, gesture, out error))
            return false;

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
        return true;
    }

    /// <summary>Changes the window shortcut without restarting the hook.</summary>
    public bool TryChange(HotkeyGesture gesture, out string? error) =>
        _hook is null ? TryStart(gesture, out error) : TrySet(WindowBinding, gesture, out error);

    /// <summary>Adds, changes or (with null) removes a named shortcut.</summary>
    public bool TrySet(string name, HotkeyGesture? gesture, out string? error)
    {
        error = null;
        lock (_lock)
        {
            if (gesture is null)
            {
                _bindings.Remove(name);
                return true;
            }
            if (!TryMapKey(gesture.Key, out var key))
            {
                error = $"La tecla «{gesture.Key}» del atajo no es válida.";
                return false;
            }
            var clash = _bindings.FirstOrDefault(b => b.Key != name && b.Value.Key == key && b.Value.Modifiers == gesture.Modifiers);
            if (clash.Value is not null)
            {
                error = $"El atajo {gesture} ya se usa para otra cosa.";
                return false;
            }
            _bindings[name] = new Binding(gesture.Modifiers, key);
        }
        return true;
    }

    public void Dispose()
    {
        if (_hook is { IsDisposed: false })
            _hook.Dispose();
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        string? fired = null;
        lock (_lock)
        {
            foreach (var (name, binding) in _bindings)
            {
                if (e.Data.KeyCode != binding.Key || !ModifiersMatch(e.RawEvent.Mask, binding.Modifiers))
                    continue;

                // Keeps e.g. Windows from opening the window menu on Alt+Space.
                e.SuppressEvent = true;
                if (binding.IsDown)
                    return; // auto-repeat while the key is held
                binding.IsDown = true;
                fired = name;
                break;
            }
        }

        if (fired == WindowBinding)
            Pressed?.Invoke(this, EventArgs.Empty);
        else if (fired is not null)
            BindingPressed?.Invoke(fired);
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        lock (_lock)
        {
            foreach (var binding in _bindings.Values)
            {
                if (e.Data.KeyCode == binding.Key && binding.IsDown)
                {
                    binding.IsDown = false;
                    e.SuppressEvent = true;
                }
            }
        }
    }

    private static bool ModifiersMatch(EventMask mask, HotkeyModifiers modifiers) =>
        mask.HasCtrl() == modifiers.HasFlag(HotkeyModifiers.Ctrl) &&
        mask.HasAlt() == modifiers.HasFlag(HotkeyModifiers.Alt) &&
        mask.HasShift() == modifiers.HasFlag(HotkeyModifiers.Shift) &&
        mask.HasMeta() == modifiers.HasFlag(HotkeyModifiers.Win);

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
