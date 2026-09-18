using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;

namespace Gateway.IntegrationTests.Support;

/// <summary>
/// A minimal <c>graphql-transport-ws</c> client over the in-memory host's WebSocket — the same
/// protocol HotChocolate speaks to a browser, entered at the real <c>/graphql</c> endpoint
/// (CONVENTIONS.md "Testing"). Hand-rolled for the same reason <see cref="GraphQlClient"/> is:
/// one subscription field exists, and the protocol is four message types
/// (<c>connection_init</c>/<c>connection_ack</c>, <c>subscribe</c>, <c>next</c>/<c>error</c>/
/// <c>complete</c>), which is less machinery than a client library and its transport adapter.
/// </summary>
public sealed class GraphQlSubscriptionClient : IAsyncDisposable
{
    private const string SubProtocol = "graphql-transport-ws";

    private readonly WebSocket _socket;
    private int _nextId;

    private GraphQlSubscriptionClient(WebSocket socket)
    {
        _socket = socket;
    }

    public static async Task<GraphQlSubscriptionClient> ConnectAsync(TestServer server, CancellationToken cancellationToken)
    {
        var client = server.CreateWebSocketClient();
        client.SubProtocols.Add(SubProtocol);
        var uri = new UriBuilder(server.BaseAddress) { Scheme = "ws", Path = "/graphql" }.Uri;
        var socket = await client.ConnectAsync(uri, cancellationToken);

        var connection = new GraphQlSubscriptionClient(socket);
        await connection.SendAsync(new { type = "connection_init" }, cancellationToken);
        var ack = await connection.ReceiveAsync(cancellationToken);
        if (ack.Type != "connection_ack")
        {
            throw new InvalidOperationException($"Expected connection_ack, got '{ack.Type}'.");
        }

        return connection;
    }

    /// <summary>Sends a <c>subscribe</c> and returns its operation id; nothing is awaited from the server.</summary>
    public async Task<string> SubscribeAsync(string query, object? variables, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await SendAsync(new { id, type = "subscribe", payload = new { query, variables } }, cancellationToken);
        return id;
    }

    /// <summary>
    /// The next message the server sends, whatever it is. Callers assert on <see cref="SubscriptionMessage.Type"/>
    /// — a test that expects <c>next</c> must fail loudly on an <c>error</c>, not silently wait
    /// for a payload that will never come.
    /// </summary>
    public async Task<SubscriptionMessage> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(chunk, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException(
                    $"The server closed the socket ({result.CloseStatus}: {result.CloseStatusDescription}).");
            }

            buffer.Write(chunk, 0, result.Count);
        }
        while (!result.EndOfMessage);

        var document = JsonDocument.Parse(buffer.ToArray());
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString() ?? string.Empty;
        var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        var payload = root.TryGetProperty("payload", out var payloadElement) ? payloadElement.Clone() : (JsonElement?)null;
        return new SubscriptionMessage(type, id, payload);
    }

    /// <summary>
    /// The next message, or <see langword="null"/> if none arrives within <paramref name="timeout"/>
    /// — for the one kind of test that has to prove <em>silence</em> (a push that must not reach
    /// this subscriber). Prefer a positive follow-up message where possible; see the
    /// share-token-scoping test for the pattern.
    /// </summary>
    public async Task<SubscriptionMessage?> TryReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            return await ReceiveAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // The server may already have torn the socket down; nothing left to close.
            }
        }

        _socket.Dispose();
    }

    private Task SendAsync(object message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        return _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }
}

/// <summary>
/// One <c>graphql-transport-ws</c> server message. For <c>next</c>, <paramref name="Payload"/> is
/// the GraphQL execution result (<c>data</c>/<c>errors</c>); for <c>error</c>, it is the array
/// of GraphQL errors that failed the subscription at subscribe time.
/// </summary>
public sealed record SubscriptionMessage(string Type, string? Id, JsonElement? Payload)
{
    /// <summary>The <c>errorCode</c> extensions of an <c>error</c> message's errors, in order — the stable <c>Error.Code</c> a client branches on.</summary>
    public IReadOnlyList<string?> ErrorCodes => Type == "error" && Payload is { } payload
        ? payload.EnumerateArray()
            .Select(e => e.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("errorCode", out var code)
                ? code.GetString()
                : null)
            .ToList()
        : [];

    /// <summary>The <c>message</c> of every error, whichever message type carried them — <c>error</c> at subscribe time or <c>next</c> with an <c>errors</c> array.</summary>
    public IReadOnlyList<string> ErrorMessages
    {
        get
        {
            if (Payload is not { } payload)
            {
                return [];
            }

            var errors = Type == "error"
                ? payload
                : payload.TryGetProperty("errors", out var errorsElement) ? errorsElement : (JsonElement?)null;

            return errors is { ValueKind: JsonValueKind.Array } array
                ? array.EnumerateArray().Select(e => e.GetProperty("message").GetString() ?? string.Empty).ToList()
                : [];
        }
    }
}
