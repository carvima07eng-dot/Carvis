using System.Text.RegularExpressions;

namespace Carvis.Core.Tools.Scripts;

/// <summary>
/// Looks for dangerous patterns in a PowerShell script before the user is asked about it.
/// Blocking findings stop the script; the rest are shown in red with an explanation.
/// It is a second line of defence: the user still sees and approves every script.
/// </summary>
public static class ScriptSafety
{
    private sealed record Rule(Regex Pattern, bool Blocks, string Explanation);

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Only as a command (start of a line or after ; | { ( ), so "del" in a Spanish sentence doesn't count.
    private const string Delete = @"(?:^|[;|{}(\r\n])\s*(?:cmd(?:\.exe)?\s*/c\s*['""]?)?(remove-item|rm|del|erase|rd|rmdir|ri)\b";
    private const string Download = @"(invoke-webrequest|\biwr\b|invoke-restmethod|\birm\b|\bcurl\b|\bwget\b|net\.webclient|downloadstring|downloadfile|downloaddata|start-bitstransfer|bitsadmin)";
    private const string Execute = @"(invoke-expression|\biex\b|start-process|\bsaps\b|\bstart\b\s|&\s*\(|&\s*\$|\.\s*\$|\.exe\b|\.msi\b|\.bat\b|\.cmd\b|\.ps1\b|\bpowershell(\.exe)?\b|\bpwsh\b|\bcmd(\.exe)?\s*/c)";
    private const string SystemPlaces = @"([a-z]:\\?\s*(['""\s;|]|$)|[a-z]:\\(windows|program files|programdata|users)\\?\s*(['""\s;|]|$)|\$env:(windir|systemroot|systemdrive|programfiles|userprofile|programdata)\b\\?\s*(['""\s;|]|$)|\\system32\b|~\\?\s*(['""\s;|]|$))";

    private static readonly Rule[] Rules =
    [
        new(new(@"\b(format-volume|clear-disk|initialize-disk|remove-partition|diskpart)\b|\bformat(\.com)?\s+[a-z]:", Options), true,
            "Formatea o borra un disco entero: se perderían todos sus datos."),
        new(new(@"\b(vssadmin|wbadmin)\b.*\bdelete\b|\bcipher\b.*/w|\bbcdedit\b", Options), true,
            "Borra copias de seguridad o toca el arranque de Windows: puede dejar el PC sin poder arrancar o sin forma de recuperar archivos."),
        new(new(@"\b(set-mppreference|add-mppreference)\b|\bwindefend\b|disableantispyware|disablerealtimemonitoring|uninstall-windowsfeature\b.*defender", Options), true,
            "Desactiva o debilita Microsoft Defender: el PC se queda sin antivirus."),
        new(new(@"set-netfirewallprofile\b.*-enabled\s+(\$?false|0)|netsh\s+(adv)?firewall\b.*\b(off|disable)", Options), true,
            "Apaga el cortafuegos de Windows."),
        new(new(@"-e(nc|ncodedcommand)?\s+[a-z0-9+/=]{16,}|frombase64string", Options), true,
            "Lleva código escondido (codificado en Base64): no se puede saber qué hace antes de ejecutarlo."),
        // Directly ("Remove-Item C:\ -Recurse") or through a pipe ("Get-ChildItem C:\Windows | Remove-Item").
        new(new(Delete + @"[^;\n|]*" + SystemPlaces + @"|\b(get-childitem|gci|dir|ls)\b[^;\n|]*" + SystemPlaces + @"[^;\n]*\|\s*(remove-item|rm|del|erase|ri)\b", Options), true,
            "Borra una carpeta del sistema, un disco entero o toda tu carpeta de usuario."),
        new(new(@"\breg(\.exe)?\s+delete\b|" + Delete + @"[^;\n|]*\b(hklm|hkcu|hkcr|hku|registry)::?", Options), true,
            "Borra claves del registro de Windows: puede romper programas o el propio Windows."),
        new(new(Delete + @"[^;\n|]*(-r(ecurse)?\b|/s\b)", Options), false,
            "Borra carpetas con todo su contenido (borrado recursivo) y sin pasar por la papelera."),
        new(new(@"\b(set-itemproperty|new-itemproperty|remove-itemproperty|new-item|set-item)\b[^;\n|]*\b(hklm|hkcu|hkcr|hku|registry)::?|\breg(\.exe)?\s+(add|import)\b", Options), false,
            "Cambia el registro de Windows."),
        new(new(@"set-executionpolicy\b.*\b(unrestricted|bypass)\b|-executionpolicy\s+(unrestricted|bypass)", Options), false,
            "Permite ejecutar cualquier script sin comprobar su firma."),
        new(new(@"\b(new-localuser|add-localgroupmember|net\s+user\b.*/add|net\s+localgroup\b.*administra)", Options), false,
            "Crea usuarios o da permisos de administrador."),
        new(new(@"\b(stop-computer|restart-computer)\b|\bshutdown(\.exe)?\s+/[srp]", Options), false,
            "Apaga o reinicia el ordenador: se perdería lo que no esté guardado."),
    ];

    private static readonly Regex DownloadPattern = new(Download, Options);
    private static readonly Regex ExecutePattern = new(Execute, Options);

    public static IReadOnlyList<ToolWarning> Analyze(string script)
    {
        // Backticks escape characters in PowerShell ("Remo`ve-Item"); they only get in the way here.
        var text = Regex.Replace(script.Replace("`", string.Empty), @"[ \t]+", " ");
        var findings = new List<ToolWarning>();

        if (DownloadPattern.IsMatch(text) && ExecutePattern.IsMatch(text))
            findings.Add(new ToolWarning("Descarga algo de Internet y lo ejecuta: así se instalan muchos virus.", Blocks: true));

        foreach (var rule in Rules)
        {
            if (rule.Pattern.IsMatch(text) && findings.All(f => f.Text != rule.Explanation))
                findings.Add(new ToolWarning(rule.Explanation, rule.Blocks));
        }

        // A recursive delete that is already blocked doesn't need the softer warning too.
        if (findings.Any(f => f.Blocks && f.Text.StartsWith("Borra una carpeta del sistema", StringComparison.Ordinal)))
            findings.RemoveAll(f => !f.Blocks && f.Text.StartsWith("Borra carpetas", StringComparison.Ordinal));
        return findings.OrderByDescending(f => f.Blocks).ToList();
    }
}
