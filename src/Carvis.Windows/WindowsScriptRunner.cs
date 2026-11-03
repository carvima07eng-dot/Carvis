using System.Runtime.Versioning;
using Carvis.Core.Platform;

namespace Carvis.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsScriptRunner : IScriptRunner
{
    public bool IsAvailable => true;

    public async Task<ScriptResult> RunPowerShellAsync(string script, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var result = await PowerShellRunner.RunAsync(script, timeout, cancellationToken);
        return new ScriptResult(result.ExitCode, result.Output, result.Error, result.TimedOut);
    }
}
