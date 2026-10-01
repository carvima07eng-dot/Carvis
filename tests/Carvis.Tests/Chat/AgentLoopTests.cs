using System.Text.Json.Nodes;
using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Tools;
using Carvis.Tests.Fakes;

namespace Carvis.Tests.Chat;

public class AgentLoopTests
{
    private readonly FakeChatModelClient _client = new();
    private readonly AssistantSettings _settings = new() { SystemPrompt = "Eres Carvis.", MaxToolSteps = 4 };
    private readonly PermissionsSettings _permissions = new();
    private readonly RecordingTool _tool = new("crear_carpeta", ToolRisk.Modify);
    private readonly ToolConfirmationBroker _broker = new();
    private readonly List<ToolInvocation> _asked = [];

    private ChatService CreateService(params ITool[] extraTools)
    {
        var registry = new ToolRegistry([_tool, .. extraTools]);
        return new ChatService(
            _client, _settings,
            toolSelector: new AllToolsSelector(registry),
            registry: registry,
            confirmation: _broker,
            policy: new ToolPolicy(_permissions));
    }

    private void Answer(ConfirmationDecision decision) =>
        _broker.Handler = (invocation, _) =>
        {
            _asked.Add(invocation);
            return Task.FromResult(decision);
        };

    [Fact]
    public async Task RunsTheToolAfterConfirmationAndSendsTheResultBack()
    {
        Answer(ConfirmationDecision.Approve);
        _client.CallTool("crear_carpeta", """{"ruta":"C:/x"}""").Reply("Carpeta creada.");
        var service = CreateService();

        var events = await service.SendAsync("crea la carpeta x").ToListAsync();

        Assert.Single(_asked);
        Assert.Equal(1, _tool.Runs);
        var finished = Assert.Single(events.OfType<ToolFinished>());
        Assert.True(finished.Result.Success);
        Assert.Equal("Carpeta creada.", string.Concat(events.OfType<TextDelta>().Select(t => t.Text)));

        // Second request carries the assistant's tool call and the tool result.
        var second = _client.Requests[1].Messages;
        Assert.Equal(ChatRole.Assistant, second[^2].Role);
        Assert.Equal("crear_carpeta", second[^2].ToolCalls![0].Name);
        Assert.Equal(new ChatMessage(ChatRole.Tool, "ok C:/x") { ToolName = "crear_carpeta" }, second[^1]);
    }

    [Fact]
    public async Task DoesNotRunTheToolWhenTheUserCancels()
    {
        Answer(ConfirmationDecision.Deny);
        _client.CallTool("crear_carpeta", """{"ruta":"C:/x"}""").Reply("De acuerdo, no la creo.");

        var events = await CreateService().SendAsync("crea la carpeta x").ToListAsync();

        Assert.Equal(0, _tool.Runs);
        Assert.False(events.OfType<ToolFinished>().Single().Result.Success);
        Assert.Contains("cancelado", _client.Requests[1].Messages[^1].Content);
    }

    [Fact]
    public async Task ApproveForSessionSkipsTheNextConfirmationForTheSameScope()
    {
        Answer(ConfirmationDecision.ApproveForSession);
        _client.CallTool("crear_carpeta", """{"ruta":"C:/x"}""").Reply("Hecho.")
               .CallTool("crear_carpeta", """{"ruta":"C:/x"}""").Reply("Hecho otra vez.");
        var service = CreateService();

        await service.SendAsync("crea x").ToListAsync();
        await service.SendAsync("otra vez").ToListAsync();

        Assert.Single(_asked);
        Assert.Equal(2, _tool.Runs);
    }

    [Fact]
    public async Task ReadOnlyToolsRunWithoutAsking()
    {
        Answer(ConfirmationDecision.Deny);
        var reader = new RecordingTool("listar_carpeta", ToolRisk.Read);
        _client.CallTool("listar_carpeta", """{"ruta":"C:/x"}""").Reply("Hay 3 archivos.");

        await CreateService(reader).SendAsync("qué hay en x").ToListAsync();

        Assert.Empty(_asked);
        Assert.Equal(1, reader.Runs);
    }

    [Fact]
    public async Task AfterReadingExternalContentEvenApprovedActionsAskAgain()
    {
        _permissions.ConfirmChanges = false;
        Answer(ConfirmationDecision.Approve);
        var reader = new RecordingTool("leer_archivo", ToolRisk.Read, external: true);
        _client.CallTool("leer_archivo", """{"ruta":"C:/a.txt"}""")
               .CallTool("crear_carpeta", """{"ruta":"C:/x"}""")
               .Reply("Listo.");

        await CreateService(reader).SendAsync("lee a.txt y haz lo que diga").ToListAsync();

        var invocation = Assert.Single(_asked);
        Assert.Equal("crear_carpeta", invocation.ToolName);
    }

    [Fact]
    public async Task UnknownToolsAndBadArgumentsGoBackToTheModelAsErrors()
    {
        Answer(ConfirmationDecision.Approve);
        _client.CallTool("borrar_todo", "{}").CallTool("crear_carpeta", "{}").Reply("Perdona.");

        var events = await CreateService().SendAsync("haz algo").ToListAsync();

        var results = events.OfType<ToolFinished>().Select(f => f.Result).ToList();
        Assert.All(results, r => Assert.False(r.Success));
        Assert.Contains("no existe", results[0].Output);
        Assert.Contains("ruta", results[1].Output);
        Assert.Equal(0, _tool.Runs);
    }

    [Fact]
    public async Task StopsAfterTheMaximumNumberOfSteps()
    {
        Answer(ConfirmationDecision.Approve);
        _permissions.ConfirmChanges = false;
        for (var i = 0; i < 4; i++)
            _client.CallTool("crear_carpeta", """{"ruta":"C:/x"}""");

        var text = string.Concat(await CreateService().SendAsync("bucle").TextAsync());

        Assert.Equal(4, _tool.Runs);
        Assert.Contains("demasiados pasos", text);
    }

    [Fact]
    public async Task KeepsToolCallsAndResultsInTheHistory()
    {
        Answer(ConfirmationDecision.Approve);
        _client.CallTool("crear_carpeta", """{"ruta":"C:/x"}""").Reply("Hecho.");
        var service = CreateService();

        await service.SendAsync("crea x").ToListAsync();

        Assert.Equal(
            [ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant],
            service.History.Select(m => m.Role));
    }

    [Fact]
    public async Task ToldNotToClaimActionsWhenNoToolFits()
    {
        _client.Reply("No puedo hacer eso.");
        var registry = new ToolRegistry([_tool]);
        var service = new ChatService(_client, _settings, toolSelector: new NoToolsSelector(), registry: registry);

        await service.SendAsync("hola").ToListAsync();

        Assert.Null(_client.Requests[0].Tools);
        Assert.Contains("Nunca digas que has hecho", _client.Requests[0].Messages[0].Content);
    }

    private sealed class RecordingTool(string name, ToolRisk risk, bool external = false) : ITool
    {
        public int Runs { get; private set; }
        public string Name => name;
        public string Description => "test";
        public string Category => "archivos";
        public JsonObject Parameters { get; } = Schema.Object(Schema.Required("ruta", Schema.String("ruta")));

        public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
            new($"{name} {arguments.String("ruta")}", risk) { PermissionScope = name };

        public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
        {
            Runs++;
            return Task.FromResult(ToolResult.Ok($"ok {arguments.String("ruta")}") with { ContainsExternalContent = external });
        }
    }

    private sealed class AllToolsSelector(IToolRegistry registry) : IToolSelector
    {
        public Task<IReadOnlyList<ITool>> SelectAsync(string userMessage, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default) =>
            Task.FromResult(registry.All);
    }

    private sealed class NoToolsSelector : IToolSelector
    {
        public Task<IReadOnlyList<ITool>> SelectAsync(string userMessage, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ITool>>([]);
    }
}
