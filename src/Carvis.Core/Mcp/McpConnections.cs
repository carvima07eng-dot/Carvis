using System.Text;
using Carvis.Core.Configuration;
using Carvis.Core.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace Carvis.Core.Mcp;

/// <summary>One line per server for Ajustes → Experimental and the notices.</summary>
public sealed record McpServerStatus(string Name, bool Connected, int Tools, string? Problem);

/// <summary>
/// Connects to the MCP servers in Ajustes → Experimental (only when MCP is on) and registers their tools.
/// Servers are reached once at startup; changing the list needs a restart.
/// </summary>
public sealed class McpConnections(IToolRegistry registry, ExperimentalSettings experimental, ILoggerFactory? loggerFactory = null) : IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);
    private readonly ILogger _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<McpConnections>();
    private readonly List<McpClient> _clients = [];

    public IReadOnlyList<McpServerStatus> Status { get; private set; } = [];

    public async Task<IReadOnlyList<McpServerStatus>> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!experimental.Mcp)
            return Status = [];

        var status = new List<McpServerStatus>();
        foreach (var server in experimental.McpServers.Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Name)))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ConnectTimeout);
                var client = await McpClient.CreateAsync(CreateTransport(server), loggerFactory: loggerFactory, cancellationToken: timeout.Token);
                _clients.Add(client);

                var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
                foreach (var tool in tools)
                    registry.Add(new McpToolAdapter(new SdkToolHandle(client, tool), server, experimental));
                _logger.LogInformation("MCP server {Server} connected with {Count} tools", server.Name, tools.Count);
                status.Add(new McpServerStatus(server.Name, true, tools.Count, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "MCP server {Server} failed", server.Name);
                status.Add(new McpServerStatus(server.Name, false, 0, Explain(server, ex)));
            }
        }
        return Status = status;
    }

    public static IClientTransport CreateTransport(McpServerSettings server)
    {
        if (server.Transport == "http")
        {
            if (!Uri.TryCreate(server.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new ArgumentException($"La dirección «{server.Url}» no es válida.");
            return new HttpClientTransport(new HttpClientTransportOptions { Endpoint = uri, Name = server.Name });
        }

        if (string.IsNullOrWhiteSpace(server.Command))
            throw new ArgumentException("Falta el programa que arranca el servidor.");
        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = server.Name,
            Command = server.Command.Trim(),
            Arguments = SplitArguments(server.Arguments),
        });
    }

    /// <summary>Splits like a command line: spaces separate, quotes keep "C:\Mis apuntes" together.</summary>
    public static List<string> SplitArguments(string? text)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var c in text ?? string.Empty)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any)
                    result.Add(current.ToString());
                current.Clear();
                any = false;
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }
        if (any)
            result.Add(current.ToString());
        return result;
    }

    private static string Explain(McpServerSettings server, Exception ex) => ex switch
    {
        ArgumentException => ex.Message,
        OperationCanceledException => "No ha contestado en 20 segundos.",
        System.ComponentModel.Win32Exception => $"No encuentro el programa «{server.Command}». Comprueba que está instalado (por ejemplo Node.js para npx).",
        HttpRequestException => $"No puedo conectar con {server.Url}. ¿Está arrancado?",
        _ => "No se ha podido iniciar. Mira los registros para ver el motivo.",
    };

    // The container disposes synchronously at exit; servers get a few seconds to close.
    public void Dispose() => Task.Run(() => DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(3));

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            try
            {
                await client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Closing an MCP server failed");
            }
        }
        _clients.Clear();
    }
}
