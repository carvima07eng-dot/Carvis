using System.Diagnostics;
using System.Text;

namespace Carvis.Windows;

public sealed record PowerShellResult(int ExitCode, string Output, string Error, bool TimedOut)
{
    public bool Success => ExitCode == 0 && !TimedOut;
}

/// <summary>Runs Windows PowerShell scripts without profile, with UTF-8 output and a time limit.</summary>
public static class PowerShellRunner
{
    public static async Task<PowerShellResult> RunAsync(string script, TimeSpan timeout, CancellationToken cancellationToken = default, string? workingDirectory = null)
    {
        // -EncodedCommand avoids every quoting problem.
        var wrapped = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; $ProgressPreference = 'SilentlyContinue'; " + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped));

        var info = new ProcessStartInfo("powershell.exe", $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        using var process = new Process { StartInfo = info };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (error) error.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new PowerShellResult(-1, output.ToString().Trim(), error.ToString().Trim(), TimedOut: true);
        }

        process.WaitForExit();
        return new PowerShellResult(process.ExitCode, output.ToString().Trim(), error.ToString().Trim(), TimedOut: false);
    }

    /// <summary>Escapes text for a single-quoted PowerShell string.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
