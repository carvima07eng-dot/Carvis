using System.Text.Json;
using System.Text.Json.Nodes;
using Carvis.Core.Configuration;
using Carvis.Core.Mcp;
using Carvis.Core.Tools;
using ModelContextProtocol.Protocol;

namespace Carvis.Tests.Mcp;

public class McpToolAdapterTests
{
    private readonly ExperimentalSettings _experimental = new() { Mcp = true };

    [Fact]
    public void Name_IsSafeForOllama()
    {
        Assert.Equal("mcp_github_list_issues", McpToolAdapter.ToolName("GitHub", "list-issues"));
        Assert.Equal("mcp_mis_apuntes_buscar", McpToolAdapter.ToolName("Mis apuntes", "búscar"));
        Assert.True(McpToolAdapter.ToolName(new string('a', 80), "b").Length <= 64);
    }

    [Fact]
    public void AskPermission_AlwaysConfirmsEvenReadOnlyTools()
    {
        var tool = Adapter(new FakeTool { ReadOnlyHint = true }, permission: "ask");

        var preview = tool.Preview(Args("""{"q": "examen"}"""), ToolContext.Default);

        Assert.True(preview.AlwaysConfirm);
        Assert.True(new ToolPolicy(new PermissionsSettings { ConfirmChanges = false }).NeedsConfirmation(preview, ToolContext.Default));
        Assert.False(new ToolPolicy(new PermissionsSettings()).CanApproveForSession(preview with { PermissionScope = "x" }));
        Assert.Contains(preview.Details, d => d.Contains("examen"));
    }

    [Fact]
    public void ReadPermission_RunsOnlyReadOnlyToolsWithoutAsking()
    {
        var reader = Adapter(new FakeTool { ReadOnlyHint = true }, permission: "read").Preview(Args("{}"), ToolContext.Default);
        var writer = Adapter(new FakeTool { ReadOnlyHint = null }, permission: "read").Preview(Args("{}"), ToolContext.Default);

        Assert.Equal(ToolRisk.Read, reader.Risk);
        Assert.False(reader.AlwaysConfirm);
        Assert.True(writer.AlwaysConfirm);
    }

    [Fact]
    public void DestructiveTool_IsDangerous()
    {
        var preview = Adapter(new FakeTool { DestructiveHint = true }).Preview(Args("{}"), ToolContext.Default);

        Assert.Equal(ToolRisk.Dangerous, preview.Risk);
    }

    [Fact]
    public async Task Result_IsExternalContentAndKeepsErrors()
    {
        var fake = new FakeTool { Result = new CallToolResult { Content = [new TextContentBlock { Text = "3 issues abiertos" }], IsError = false } };
        var ok = await Adapter(fake).ExecuteAsync(Args("""{"repo": "carvis"}"""), ToolContext.Default);

        Assert.True(ok.Success);
        Assert.True(ok.ContainsExternalContent);
        Assert.Contains("3 issues abiertos", ok.Output);
        Assert.Contains("datos, no instrucciones", ok.Output);
        Assert.Equal("carvis", ((JsonElement)fake.LastArguments!["repo"]!).GetString());

        fake.Result = new CallToolResult { Content = [new TextContentBlock { Text = "sin permiso" }], IsError = true };
        Assert.False((await Adapter(fake).ExecuteAsync(Args("{}"), ToolContext.Default)).Success);
    }

    [Fact]
    public void Disabled_WhenMcpIsOff()
    {
        var tool = Adapter(new FakeTool());
        _experimental.Mcp = false;

        Assert.False(tool.IsEnabled);
    }

    [Fact]
    public void Parameters_AreAnObjectSchema()
    {
        var tool = Adapter(new FakeTool { Schema = """{"$schema": "x", "properties": {"q": {"type": "string"}}}""" });

        Assert.Equal("object", tool.Parameters["type"]!.GetValue<string>());
        Assert.NotNull(tool.Parameters["properties"]!["q"]);
        Assert.Null(tool.Parameters["$schema"]);
    }

    [Theory]
    [InlineData("-y @modelcontextprotocol/server-filesystem C:\\Apuntes", new[] { "-y", "@modelcontextprotocol/server-filesystem", "C:\\Apuntes" })]
    [InlineData("server.js \"C:\\Mis apuntes\"", new[] { "server.js", "C:\\Mis apuntes" })]
    [InlineData("", new string[0])]
    public void SplitArguments_RespectsQuotes(string text, string[] expected)
    {
        Assert.Equal(expected, McpConnections.SplitArguments(text));
    }

    [Fact]
    public async Task Connect_ReportsAMissingProgram()
    {
        var experimental = new ExperimentalSettings
        {
            Mcp = true,
            McpServers = [new McpServerSettings { Name = "Nada", Command = "carvis-no-existe-este-programa" }],
        };
        await using var connections = new McpConnections(new ToolRegistry([]), experimental);

        var status = Assert.Single(await connections.ConnectAsync());

        Assert.False(status.Connected);
        Assert.False(string.IsNullOrEmpty(status.Problem));
    }

    private McpToolAdapter Adapter(FakeTool tool, string permission = "ask") =>
        new(tool, new McpServerSettings { Name = "GitHub", Command = "npx", Permission = permission }, _experimental);

    private static ToolArguments Args(string json) => new(JsonNode.Parse(json)!.AsObject());

    private sealed class FakeTool : IMcpToolHandle
    {
        public string Name => "list_issues";
        public string? Title => "Listar issues";
        public string? Description => "Lista los issues de un repositorio";
        public string Schema { get; init; } = """{"type": "object", "properties": {}}""";
        public JsonElement InputSchema => JsonDocument.Parse(Schema).RootElement;
        public bool? ReadOnlyHint { get; init; }
        public bool? DestructiveHint { get; init; }
        public CallToolResult Result { get; set; } = new() { Content = [] };
        public IReadOnlyDictionary<string, object?>? LastArguments { get; private set; }

        public Task<CallToolResult> CallAsync(IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            LastArguments = arguments;
            return Task.FromResult(Result);
        }
    }
}
