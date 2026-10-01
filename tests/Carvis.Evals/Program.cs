using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Carvis.Core;
using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

// Measures how well the model picks tools and fills their arguments, against a real Ollama.
// Usage: dotnet run --project tests/Carvis.Evals [-- --model qwen3:8b] [--repeat 2] [--filter texto]
// Nothing is executed: only the model's first decision is checked.

var model = Arg("--model");
var repeat = int.TryParse(Arg("--repeat"), out var r) ? Math.Max(1, r) : 1;
var filter = Arg("--filter");

var settings = new SettingsStore(Path.Combine(FindRepoRoot(), "src", "Carvis.App", "appsettings.json"), new AppPaths().UserSettingsFile).Load();
if (model is not null)
    settings.Ollama.ChatModel = model;

var services = new ServiceCollection().AddLogging().AddCarvisCore(settings).BuildServiceProvider();
var client = services.GetRequiredService<IChatModelClient>();
var selector = services.GetRequiredService<IToolSelector>();
var providers = services.GetServices<IChatContextProvider>().ToList();

var cases = JsonSerializer.Deserialize<List<EvalCase>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "cases.json")),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
if (filter is not null)
    cases = cases.Where(c => c.Message.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

Console.WriteLine($"Modelo: {settings.Ollama.ChatModel} · {cases.Count} casos × {repeat}\n");
int passed = 0, total = 0, selectionMisses = 0;
var timer = Stopwatch.StartNew();

foreach (var evalCase in cases)
{
    for (var i = 0; i < repeat; i++)
    {
        total++;
        var tools = await selector.SelectAsync(evalCase.Message, []);
        var expected = evalCase.Tool?.Split('|') ?? [];
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
        if (ok)
            passed++;
        Console.WriteLine($"{(ok ? "OK " : "MAL")} {started.Elapsed.TotalSeconds,5:0.0}s  {evalCase.Message}");
        if (!ok)
            Console.WriteLine($"      {reason} · herramientas ofrecidas: {string.Join(", ", tools.Select(t => t.Name))}");
    }
}

Console.WriteLine($"\nResultado: {passed}/{total} ({100.0 * passed / Math.Max(1, total):0}%) en {timer.Elapsed.TotalSeconds:0}s");
Console.WriteLine($"El selector no ofreció la herramienta correcta en {selectionMisses} casos.");
return passed == total ? 0 : 1;

static (bool, string) Check(EvalCase evalCase, ToolCall? call)
{
    if (evalCase.Tool is null)
        return call is null ? (true, "") : (false, $"no debía usar herramientas y usó {call.Name}");
    if (call is null)
        return (false, "no ha usado ninguna herramienta");

    var expected = evalCase.Tool.Split('|');
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

internal sealed record EvalCase(string Message, string? Tool, JsonObject? Args);
