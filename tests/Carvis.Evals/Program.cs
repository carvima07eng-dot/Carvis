using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Carvis.Core;
using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

// Measures how well the model picks tools and fills their arguments, against a real Ollama.
// Usage: dotnet run --project tests/Carvis.Evals [-- --model qwen3:8b] [--repeat 2] [--filter texto] [--report informe.md]
// Nothing is executed: only the model's first decision is checked. The report has the % per category.

var model = Arg("--model");
var repeat = int.TryParse(Arg("--repeat"), out var r) ? Math.Max(1, r) : 1;
var filter = Arg("--filter");
var reportPath = Arg("--report") ?? "eval-report.md";

var settings = new SettingsStore(Path.Combine(FindRepoRoot(), "src", "Carvis.App", "appsettings.json"), new AppPaths().UserSettingsFile).Load();
if (model is not null)
    settings.Ollama.ChatModel = model;

// The screen tool is only offered when a capture exists; nothing is captured here.
var services = new ServiceCollection().AddLogging()
    .AddSingleton<Carvis.Core.Vision.IScreenCapture, PretendScreenCapture>()
    .AddCarvisCore(settings).BuildServiceProvider();
var client = services.GetRequiredService<IChatModelClient>();
var selector = services.GetRequiredService<IToolSelector>();
var providers = services.GetServices<IChatContextProvider>().ToList();
// Tools switched off by the user's settings (Internet, PowerShell…) can't be picked: those cases are reported apart.
var disabled = services.GetRequiredService<IToolRegistry>().All
    .Where(t => t is IConditionalTool { IsEnabled: false })
    .ToDictionary(t => t.Name, t => ((IConditionalTool)t).DisabledReason);

var cases = JsonSerializer.Deserialize<List<EvalCase>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "cases.json")),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
if (filter is not null)
    cases = cases.Where(c => c.Message.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

Console.WriteLine($"Modelo: {settings.Ollama.ChatModel} · {cases.Count} casos × {repeat}\n");
int passed = 0, total = 0, selectionMisses = 0;
var timer = Stopwatch.StartNew();
var results = new List<EvalResult>();

foreach (var evalCase in cases)
{
    for (var i = 0; i < repeat; i++)
    {
        total++;
        var tools = await selector.SelectAsync(evalCase.Message, []);
        var expected = evalCase.Tool?.Split('|').Where(t => t != "null").ToArray() ?? [];
        if (expected.Length > 0 && !tools.Any(t => expected.Contains(t.Name)))
            selectionMisses++;

        var system = settings.Assistant.SystemPrompt;
        foreach (var provider in providers)
            foreach (var message in await provider.GetContextAsync(evalCase.Message))
                system += "\n\n" + message.Content;

        var request = new ModelRequest([new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, evalCase.Message)])
        {
            Tools = tools.Count > 0 ? tools : null,
            Temperature = settings.Ollama.Temperature,
        };

        ToolCall? call = null;
        var text = "";
        var started = Stopwatch.StartNew();
        await foreach (var chunk in client.StreamAsync(request))
        {
            call ??= chunk.ToolCalls?.FirstOrDefault();
            text += chunk.Text;
        }

        var (ok, reason) = Check(evalCase, call);
        if (!ok && expected.Length > 0 && expected.All(disabled.ContainsKey))
            reason = $"no disponible con tus ajustes: {disabled[expected[0]]}";
        if (ok)
            passed++;
        results.Add(new EvalResult(evalCase, ok, reason, started.Elapsed));
        Console.WriteLine($"{(ok ? "OK " : "MAL")} {started.Elapsed.TotalSeconds,5:0.0}s  {evalCase.Message}");
        if (!ok)
            Console.WriteLine($"      {reason} · herramientas ofrecidas: {string.Join(", ", tools.Select(t => t.Name))}");
    }
}

Console.WriteLine($"\nResultado: {passed}/{total} ({100.0 * passed / Math.Max(1, total):0}%) en {timer.Elapsed.TotalSeconds:0}s");
Console.WriteLine($"El selector no ofreció la herramienta correcta en {selectionMisses} casos.\n");

var byCategory = results.GroupBy(r => r.Case.Category ?? "Sin categoría").OrderBy(g => g.Key).ToList();
foreach (var group in byCategory)
    Console.WriteLine($"{group.Key,-26} {group.Count(r => r.Ok),3}/{group.Count(),-3} {Percent(group.Count(r => r.Ok), group.Count()),4}");

File.WriteAllText(reportPath, Report(settings.Ollama.ChatModel, results, byCategory, timer.Elapsed));
Console.WriteLine($"\nInforme: {Path.GetFullPath(reportPath)}");
return passed == total ? 0 : 1;

static string Percent(int ok, int count) => $"{100.0 * ok / Math.Max(1, count):0}%";

static string Report(string model, List<EvalResult> results, List<IGrouping<string, EvalResult>> byCategory, TimeSpan elapsed)
{
    var ok = results.Count(r => r.Ok);
    var text = new System.Text.StringBuilder()
        .AppendLine($"# Evaluación de Carvis · {model}")
        .AppendLine()
        .AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm} · {results.Count} casos · {elapsed.TotalSeconds:0} s · **{ok}/{results.Count} ({Percent(ok, results.Count)})**")
        .AppendLine()
        .AppendLine("| Categoría | Aciertos | % | Tiempo medio |")
        .AppendLine("|---|---|---|---|");
    foreach (var group in byCategory)
        text.AppendLine($"| {group.Key} | {group.Count(r => r.Ok)}/{group.Count()} | {Percent(group.Count(r => r.Ok), group.Count())} | {group.Average(r => r.Elapsed.TotalSeconds):0.0} s |");

    var failures = results.Where(r => !r.Ok).ToList();
    if (failures.Count > 0)
    {
        text.AppendLine().AppendLine("## Fallos").AppendLine();
        foreach (var failure in failures)
            text.AppendLine($"- **{failure.Case.Category}** · «{failure.Case.Message}»: {failure.Reason}");
    }
    return text.ToString();
}

static (bool, string) Check(EvalCase evalCase, ToolCall? call)
{
    if (evalCase.Tool is null)
        return call is null ? (true, "") : (false, $"no debía usar herramientas y usó {call.Name}");
    var expected = evalCase.Tool.Split('|');
    // "null" among the options: answering without a tool is fine too (e.g. the context already had it).
    if (call is null)
        return expected.Contains("null") ? (true, "") : (false, "no ha usado ninguna herramienta");

    if (!expected.Contains(call.Name))
        return (false, $"esperaba {evalCase.Tool} y usó {call.Name} {call.Arguments.ToJsonString()}");

    foreach (var (key, value) in evalCase.Args ?? [])
    {
        var actual = call.Arguments[key]?.ToJsonString() ?? "";
        var options = value!.GetValue<string>().Split('|');
        if (!options.Any(o => actual.Contains(o, StringComparison.OrdinalIgnoreCase)))
            return (false, $"argumento «{key}» = {actual}, esperaba que contuviera «{value}»");
    }
    return (true, "");
}

string? Arg(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "Carvis.sln")))
            return dir.FullName;
    return Directory.GetCurrentDirectory();
}

internal sealed record EvalCase(string Message, string? Tool, JsonObject? Args, string? Category = null);

internal sealed record EvalResult(EvalCase Case, bool Ok, string Reason, TimeSpan Elapsed);

internal sealed class PretendScreenCapture : Carvis.Core.Vision.IScreenCapture
{
    public bool IsAvailable => true;
    public Task<byte[]?> CaptureAsync(Carvis.Core.Vision.CaptureArea area, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
}
