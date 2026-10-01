using System.Net;
using System.Text.Json.Nodes;
using Carvis.Core;
using Carvis.Core.Configuration;
using Carvis.Core.Tools;
using Carvis.Core.Tools.Internet;
using Carvis.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Carvis.Tests.Tools;

/// <summary>Every built-in tool, resolved the way the app does it.</summary>
public sealed class BuiltInToolsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("carvis-all").FullName;
    private readonly CarvisSettings _settings = new();
    private readonly ServiceProvider _services;

    public BuiltInToolsTests()
    {
        _services = new ServiceCollection()
            .AddCarvisCore(_settings, new AppPaths(Path.Combine(_dir, "data"), Path.Combine(_dir, "settings")))
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private IToolRegistry Registry => _services.GetRequiredService<IToolRegistry>();

    [Fact]
    public void AllToolsResolveWithValidNamesAndSchemas()
    {
        var tools = Registry.All;
        Assert.True(tools.Count >= 50, $"Only {tools.Count} tools");
        Assert.Equal(tools.Count, tools.Select(t => t.Name).Distinct().Count());
        foreach (var tool in tools)
        {
            Assert.Matches("^[a-z_]+$", tool.Name);
            Assert.Equal("object", tool.Parameters["type"]?.GetValue<string>());
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        }
    }

    [Theory]
    [InlineData("sube el volumen un poco", "volumen")]
    [InlineData("pausa la música", "multimedia")]
    [InlineData("¿cuánta batería me queda?", "info_sistema")]
    [InlineData("apaga el ordenador en 10 minutos", "energia")]
    [InlineData("recuérdame mañana a las 9 llamar al médico", "crear_recordatorio")]
    [InlineData("pon un temporizador de 10 minutos", "temporizador")]
    [InlineData("¿cuánto es el 15% de 80?", "calcular")]
    [InlineData("convierte 10 millas a km", "convertir_unidades")]
    [InlineData("¿qué hora es en Tokio?", "fecha_hora")]
    [InlineData("apunta que tengo que comprar pan", "crear_nota")]
    [InlineData("¿qué tareas tengo pendientes?", "listar_tareas")]
    [InlineData("activa el modo estudio", "ejecutar_rutina")]
    [InlineData("ponlo en modo oscuro", "tema_windows")]
    [InlineData("traduce lo que he copiado", "portapapeles")]
    [InlineData("abre los ajustes del bluetooth", "ajustes_windows")]
    public async Task SelectorOffersTheRightTool(string message, string expected)
    {
        var tools = await _services.GetRequiredService<IToolSelector>().SelectAsync(message, []);
        Assert.Contains(expected, tools.Select(t => t.Name));
    }

    [Fact]
    public async Task InternetToolsStayOffUntilAllowed()
    {
        var selector = _services.GetRequiredService<IToolSelector>();
        Assert.DoesNotContain("tiempo", (await selector.SelectAsync("¿qué tiempo hace en Alzira?", [])).Select(t => t.Name));

        var executor = new ToolExecutor(Registry);
        var finished = await FirstResultAsync(executor, new ToolCall("tiempo", new JsonObject { ["ciudad"] = "Alzira" }));
        Assert.False(finished.Success);
        Assert.Contains("Ajustes", finished.Output);

        _settings.Permissions.AllowInternet = true;
        Assert.Contains("tiempo", (await selector.SelectAsync("¿qué tiempo hace en Alzira?", [])).Select(t => t.Name));
        Assert.DoesNotContain("leer_resultados_web", Registry.All.Where(t => t is not IConditionalTool { IsEnabled: false }).Select(t => t.Name));
    }

    [Fact]
    public async Task PowerShellIsUnavailableOutsideWindowsAndAlwaysDangerous()
    {
        var tool = Registry.Find("ejecutar_powershell")!;
        Assert.Equal(OperatingSystem.IsWindows(), ((IConditionalTool)tool).IsEnabled);
        var preview = tool.Preview(new ToolArguments(new JsonObject { ["script"] = "Get-Date\nGet-Process", ["explicacion"] = "ver la hora" }), ToolContext.Default);
        Assert.Equal(ToolRisk.Dangerous, preview.Risk);
        Assert.Contains(preview.Details!, d => d.Contains("Get-Process"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task WeatherReadsOpenMeteo()
    {
        var handler = new StubHttpHandler((request, _) => request.RequestUri!.Host.StartsWith("geocoding")
            ? StubHttpHandler.Text("""{"results":[{"name":"Alzira","admin1":"Valencia","latitude":39.15,"longitude":-0.43}]}""")
            : StubHttpHandler.Text("""
                {"current":{"temperature_2m":24.1,"apparent_temperature":25,"relative_humidity_2m":60,"weather_code":1,"wind_speed_10m":9},
                 "daily":{"time":["2026-10-01"],"weather_code":[61],"temperature_2m_max":[26],"temperature_2m_min":[15],"precipitation_probability_max":[70]}}
                """));
        var tool = new WeatherTool(new InternetClient(handler), new PermissionsSettings { AllowInternet = true });

        var result = await tool.ExecuteAsync(new ToolArguments(new JsonObject { ["ciudad"] = "Alzira" }), ToolContext.Default);

        Assert.True(result.Success);
        Assert.True(result.ContainsExternalContent);
        Assert.Contains("Alzira, Valencia", result.Output);
        Assert.Contains("24.1 °C", result.Output);
        Assert.Contains("lluvia 70%", result.Output);
        Assert.Contains("latitude=39.15", handler.Requests[1].Request.RequestUri!.Query);
    }

    [Fact]
    public async Task CurrencyUsesTheEcbRate()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Text("""{"amount":100,"base":"EUR","date":"2026-09-30","rates":{"USD":117.5}}"""));
        var tool = new CurrencyTool(new InternetClient(handler), new PermissionsSettings { AllowInternet = true });

        var result = await tool.ExecuteAsync(new ToolArguments(new JsonObject { ["cantidad"] = 100, ["de"] = "euros", ["a"] = "usd" }), ToolContext.Default);

        Assert.Contains("117,5 USD", result.Output);
        Assert.Contains("from=EUR&to=USD", handler.Requests[0].Request.RequestUri!.Query);
    }

    private static async Task<ToolResult> FirstResultAsync(ToolExecutor executor, ToolCall call)
    {
        await foreach (var step in executor.RunAsync([call], false))
        {
            if (step.Event is Carvis.Core.Chat.ToolFinished finished)
                return finished.Result;
        }
        throw new InvalidOperationException("No result");
    }
}
