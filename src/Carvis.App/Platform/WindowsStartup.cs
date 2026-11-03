using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Carvis.App.Platform;

/// <summary>"Start with Windows" through the current user's Run key (no admin rights needed).</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Carvis";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {App.StartHiddenArgument}");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
