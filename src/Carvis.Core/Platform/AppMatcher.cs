using Carvis.Core.Tools;

namespace Carvis.Core.Platform;

/// <summary>Finds "the spoti" in a list of installed programs.</summary>
public static class AppMatcher
{
    // Spanish names for programs whose shortcut has an English name.
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["bloc de notas"] = ["notepad", "bloc de notas"],
        ["calculadora"] = ["calculator", "calculadora"],
        ["explorador"] = ["file explorer", "explorador de archivos"],
        ["explorador de archivos"] = ["file explorer", "explorador de archivos"],
        ["configuracion"] = ["settings", "configuracion"],
        ["ajustes"] = ["settings", "configuracion"],
        ["terminal"] = ["windows terminal", "terminal"],
        ["consola"] = ["windows terminal", "windows powershell", "command prompt"],
        ["simbolo del sistema"] = ["command prompt", "simbolo del sistema"],
        ["paint"] = ["paint"],
        ["recortes"] = ["snipping tool", "recortes"],
        ["word"] = ["word"],
        ["excel"] = ["excel"],
        ["powerpoint"] = ["powerpoint"],
        ["vscode"] = ["visual studio code"],
        ["vs code"] = ["visual studio code"],
        ["code"] = ["visual studio code"],
        ["tienda"] = ["microsoft store"],
        ["administrador de tareas"] = ["task manager", "administrador de tareas"],
        ["panel de control"] = ["control panel", "panel de control"],
        ["camara"] = ["camera", "camara"],
        ["fotos"] = ["photos", "fotos"],
        ["reloj"] = ["clock", "reloj", "alarms & clock"],
    };

    public static IReadOnlyList<(AppEntry App, double Score)> Rank(string query, IReadOnlyList<AppEntry> apps)
    {
        var q = ToolSelector.Normalize(query).Trim();
        var candidates = Aliases.TryGetValue(q, out var alias) ? alias.Append(q).ToArray() : [q];

        return apps
            .Select(app => (App: app, Score: candidates.Max(c => Score(c, ToolSelector.Normalize(app.Name)))))
            .Where(x => x.Score >= 0.5)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.App.Name.Length)
            .ToList();
    }

    public static double Score(string query, string name)
    {
        if (query.Length == 0)
            return 0;
        if (name == query)
            return 1;
        if (name.StartsWith(query))
            return 0.95;
        if (name.Split(' ', '-', '_').Any(word => word.StartsWith(query)))
            return 0.9;
        if (name.Contains(query))
            return 0.8;
        if (query.Contains(name) && name.Length >= 4)
            return 0.75;

        var distance = Levenshtein(query, name.Length > query.Length + 3 ? name[..(query.Length + 3)] : name);
        var similarity = 1 - (double)distance / Math.Max(query.Length, name.Length);
        return similarity * 0.85;
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            previous = current;
        }
        return previous[b.Length];
    }
}
