using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GTAWChatParser;

internal sealed class CapturedLine
{
    public CapturedLine()
    {
    }

    public CapturedLine(string plain, string colored)
    {
        Plain = plain;
        Colored = colored;
    }

    public string Plain { get; set; } = "";

    public string Colored { get; set; } = "";
}

internal sealed class NuiChatClient : IDisposable
{
    internal const string RootUiUrl = "nui://game/ui/root.html";
    private const string ClientFrameUrl = "https://cfx-nui-client/web/index.html";

    private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    private readonly Func<int> _port;
    private ClientWebSocket? _socket;
    private int _portInUse;
    private int _nextId;
    private int _contextId;

    public NuiChatClient(Func<int> port)
    {
        _port = port;
    }

    public async Task<IReadOnlyList<CapturedLine>> ReadLinesAsync(CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        using var response = await RequestAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = ChatPageScript.ExtractLines,
            ["contextId"] = _contextId,
            ["returnByValue"] = true
        }, cancellationToken).ConfigureAwait(false);

        var payload = ReadEvaluateText(response.RootElement);
        if (string.IsNullOrWhiteSpace(payload))
            return [];

        var lines = JsonSerializer.Deserialize<CapturedLine[]>(payload, LineJson) ?? [];
        return lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Plain))
            .Select(line => new CapturedLine(line.Plain.Trim(), string.IsNullOrWhiteSpace(line.Colored) ? line.Plain.Trim() : line.Colored.Trim()))
            .ToList();
    }

    private static readonly JsonSerializerOptions LineJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static string? ReadEvaluateText(JsonElement evaluateResult)
    {
        if (evaluateResult.ValueKind != JsonValueKind.Object)
            return null;

        if (evaluateResult.TryGetProperty("exceptionDetails", out var details))
        {
            var reason = details.ValueKind == JsonValueKind.Object && details.TryGetProperty("text", out var text)
                ? text.GetString()
                : null;
            throw new IOException(string.IsNullOrWhiteSpace(reason) ? "The GTAW chat page rejected the read." : reason);
        }

        if (!evaluateResult.TryGetProperty("result", out var remote) || remote.ValueKind != JsonValueKind.Object)
            return null;

        if (remote.TryGetProperty("subtype", out var subtype) && subtype.ValueKind == JsonValueKind.String && subtype.GetString() == "error")
            throw new IOException("The GTAW chat page rejected the read.");

        if (!remote.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        return value.GetString();
    }

    public void Reset()
    {
        CloseSocket();
        _contextId = 0;
        _nextId = 0;
    }

    public void Dispose() => Reset();

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        var port = _port();
        if (_socket is { State: WebSocketState.Open } && _contextId != 0 && _portInUse == port)
            return;

        Reset();
        _portInUse = port;

        var socketUrl = await FindRootSocketUrlAsync(port, cancellationToken).ConfigureAwait(false);
        var socket = new ClientWebSocket();
        socket.Options.Proxy = null;
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(3));
        await socket.ConnectAsync(new Uri(socketUrl), connectTimeout.Token).ConfigureAwait(false);
        _socket = socket;

        using var tree = await RequestAsync("Page.getFrameTree", new JsonObject(), cancellationToken).ConfigureAwait(false);
        var frameId = FindClientFrameId(tree.RootElement);
        if (frameId == null)
            throw new IOException("GTAW chat is still loading.");

        using var world = await RequestAsync("Page.createIsolatedWorld", new JsonObject
        {
            ["frameId"] = frameId,
            ["worldName"] = "gtaw-chat-parser",
            ["grantUniveralAccess"] = true
        }, cancellationToken).ConfigureAwait(false);

        if (!world.RootElement.TryGetProperty("executionContextId", out var contextId))
            throw new IOException("GTAW chat context is unavailable.");

        _contextId = contextId.GetInt32();
    }

    private async Task<string> FindRootSocketUrlAsync(int port, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var json = await Http.GetStringAsync($"http://127.0.0.1:{port}/json", timeout.Token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new IOException("FiveM did not list any inspectable pages.");

        foreach (var target in document.RootElement.EnumerateArray())
        {
            if (!target.TryGetProperty("url", out var urlProperty))
                continue;

            if (!string.Equals(urlProperty.GetString(), RootUiUrl, StringComparison.Ordinal))
                continue;

            if (target.TryGetProperty("webSocketDebuggerUrl", out var socketProperty) &&
                socketProperty.GetString() is { Length: > 0 } socketUrl)
            {
                return socketUrl;
            }
        }

        throw new IOException("FiveM root UI was not found.");
    }

    private async Task<JsonDocument> RequestAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        if (_socket is not { State: WebSocketState.Open })
            throw new IOException("The chat inspector is not connected.");

        var id = ++_nextId;
        var message = new JsonObject
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        };
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, timeout.Token).ConfigureAwait(false);

        while (true)
        {
            var response = await ReceiveTextAsync(timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(response);
            if (!document.RootElement.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id)
                continue;

            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var text = error.TryGetProperty("message", out var errorMessage) ? errorMessage.GetString() : "Inspector request failed.";
                throw new IOException(text);
            }

            if (!document.RootElement.TryGetProperty("result", out var result))
                return JsonDocument.Parse("{}");

            return JsonDocument.Parse(result.GetRawText());
        }
    }

    private async Task<string> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        if (_socket == null)
            throw new IOException("The chat inspector is not connected.");

        using var stream = new MemoryStream();
        var buffer = new byte[16 * 1024];
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new IOException("FiveM closed the chat inspector.");

            stream.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? FindClientFrameId(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (element.TryGetProperty("frameTree", out var tree))
            return FindClientFrameId(tree);

        if (element.TryGetProperty("frame", out var frame) &&
            frame.TryGetProperty("url", out var url) &&
            string.Equals(url.GetString(), ClientFrameUrl, StringComparison.Ordinal) &&
            frame.TryGetProperty("id", out var id))
        {
            return id.GetString();
        }

        if (!element.TryGetProperty("childFrames", out var children) || children.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var child in children.EnumerateArray())
        {
            var found = FindClientFrameId(child);
            if (found != null)
                return found;
        }

        return null;
    }

    private void CloseSocket()
    {
        var socket = _socket;
        _socket = null;
        if (socket == null)
            return;

        try
        {
            socket.Abort();
        }
        catch
        {
        }

        socket.Dispose();
    }
}
