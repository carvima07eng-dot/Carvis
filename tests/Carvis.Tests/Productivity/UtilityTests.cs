using System.Text.Json.Nodes;
using Carvis.Core.Text;
using Carvis.Core.Tools;
using Carvis.Core.Tools.Utilities;
using Carvis.Tests.Fakes;

namespace Carvis.Tests.Productivity;

public sealed class UtilityTests
{
    [Theory]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("2^10", 1024)]
    [InlineData("-3 + 5", 2)]
    [InlineData("15% de 80", 12)]
    [InlineData("80 + 21%", 96.8)]
    [InlineData("100 - 10%", 90)]
    [InlineData("200 * 5%", 10)]
    [InlineData("10 mod 3", 1)]
    [InlineData("sqrt(16) + 1", 5)]
    [InlineData("3,5 * 2", 7)]
    [InlineData("1.234,5 + 0,5", 1235)]
    [InlineData("7 entre 2", 3.5)]
    [InlineData("2 ** 3", 8)]
    public void Evaluator_ComputesExpressions(string expression, double expected) =>
        Assert.Equal(expected, ExpressionEvaluator.Evaluate(expression), 9);

    [Theory]
    [InlineData("2 +")]
    [InlineData("(1 + 2")]
    [InlineData("hola")]
    [InlineData("1 / 0")]
    public void Evaluator_RejectsInvalidExpressions(string expression) =>
        Assert.ThrowsAny<Exception>(() => ExpressionEvaluator.Evaluate(expression));

    [Fact]
    public async Task CalculateTool_FormatsTheResultInSpanish()
    {
        var result = await new CalculateTool().ExecuteAsync(Args(new() { ["expresion"] = "1250 * 1,21" }), ToolContext.Default);
        Assert.True(result.Success);
        Assert.EndsWith("= 1.512,5", result.Output);
    }

    [Theory]
    [InlineData(10, "millas", "km", 16.09344)]
    [InlineData(100, "celsius", "fahrenheit", 212)]
    [InlineData(32, "°F", "c", 0)]
    [InlineData(1, "GB", "MB", 1024)]
    [InlineData(2, "horas", "minutos", 120)]
    [InlineData(5, "kilos", "libras", 11.0231131)]
    public void Units_Convert(double value, string from, string to, double expected) =>
        Assert.Equal(expected, ConvertUnitsTool.Convert(value, from, to), 5);

    [Fact]
    public async Task Units_RefuseIncompatibleDimensions()
    {
        var result = await new ConvertUnitsTool().ExecuteAsync(Args(new() { ["valor"] = 3, ["de"] = "km", ["a"] = "kg" }), ToolContext.Default);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task DateTool_CountsDaysAndNamesWeekdays()
    {
        var tool = new DateTimeTool(new ManualTime(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(2))));

        var until = await tool.ExecuteAsync(Args(new() { ["operacion"] = "dias_hasta", ["fecha"] = "2026-12-25" }), ToolContext.Default);
        Assert.Contains("Faltan 85 días", until.Output);

        var weekday = await tool.ExecuteAsync(Args(new() { ["operacion"] = "dia_semana", ["fecha"] = "2026-10-12" }), ToolContext.Default);
        Assert.Contains("lunes", weekday.Output);

        var between = await tool.ExecuteAsync(Args(new() { ["operacion"] = "dias_entre", ["fecha"] = "2026-01-01", ["fecha2"] = "2026-03-01" }), ToolContext.Default);
        Assert.Contains("59 días", between.Output);
    }

    private static ToolArguments Args(JsonObject json) => new(json);
}
