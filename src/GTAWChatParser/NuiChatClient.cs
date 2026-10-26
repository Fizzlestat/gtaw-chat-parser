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
        using JsonDocument response = await RequestAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = ChatPageScript.ExtractLines,
            ["contextId"] = _contextId,
            ["returnByValue"] = true
        }, cancellationToken).ConfigureAwait(false);

        string? payload = ReadEvaluateText(response.RootElement);
        if (string.IsNullOrWhiteSpace(payload))
            return [];

        CapturedLine[] lines = JsonSerializer.Deserialize<CapturedLine[]>(payload, LineJson) ?? [];
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

        if (evaluateResult.TryGetProperty("exceptionDetails", out JsonElement details))
        {
            string? reason = details.ValueKind == JsonValueKind.Object && details.TryGetProperty("text", out JsonElement text)
                ? text.GetString()
                : null;
            throw new IOException(string.IsNullOrWhiteSpace(reason) ? "The GTAW chat page rejected the read." : reason);
        }

        if (!evaluateResult.TryGetProperty("result", out JsonElement remote) || remote.ValueKind != JsonValueKind.Object)
            return null;

        if (remote.TryGetProperty("subtype", out JsonElement subtype) && subtype.ValueKind == JsonValueKind.String && subtype.GetString() == "error")
            throw new IOException("The GTAW chat page rejected the read.");

        if (!remote.TryGetProperty("value", out JsonElement value) || value.ValueKind != JsonValueKind.String)
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
        int port = _port();
        if (_socket is { State: WebSocketState.Open } && _contextId != 0 && _portInUse == port)
            return;

        Reset();
        _portInUse = port;

        string socketUrl = await FindRootSocketUrlAsync(port, cancellationToken).ConfigureAwait(false);
        ClientWebSocket socket = new ClientWebSocket();
        socket.Options.Proxy = null;
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

        using CancellationTokenSource connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(3));
        await socket.ConnectAsync(new Uri(socketUrl), connectTimeout.Token).ConfigureAwait(false);
        _socket = socket;

        using JsonDocument tree = await RequestAsync("Page.getFrameTree", new JsonObject(), cancellationToken).ConfigureAwait(false);
        string? frameId = FindClientFrameId(tree.RootElement);
        if (frameId == null)
            throw new IOException("GTAW chat is still loading.");

        using JsonDocument world = await RequestAsync("Page.createIsolatedWorld", new JsonObject
        {
            ["frameId"] = frameId,
            ["worldName"] = "gtaw-chat-parser",
            ["grantUniveralAccess"] = true
        }, cancellationToken).ConfigureAwait(false);

        if (!world.RootElement.TryGetProperty("executionContextId", out JsonElement contextId))
            throw new IOException("GTAW chat context is unavailable.");

        _contextId = contextId.GetInt32();
    }

    private async Task<string> FindRootSocketUrlAsync(int port, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        string json = await Http.GetStringAsync($"http://127.0.0.1:{port}/json", timeout.Token).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new IOException("FiveM did not list any inspectable pages.");

        foreach (JsonElement target in document.RootElement.EnumerateArray())
        {
            if (!target.TryGetProperty("url", out JsonElement urlProperty))
                continue;

            if (!string.Equals(urlProperty.GetString(), RootUiUrl, StringComparison.Ordinal))
                continue;

            if (target.TryGetProperty("webSocketDebuggerUrl", out JsonElement socketProperty) &&
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

        int id = ++_nextId;
        JsonObject message = new JsonObject
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        };
        byte[] bytes = Encoding.UTF8.GetBytes(message.ToJsonString());

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, timeout.Token).ConfigureAwait(false);

        while (true)
        {
            string response = await ReceiveTextAsync(timeout.Token).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(response);
            if (!document.RootElement.TryGetProperty("id", out JsonElement responseId) || responseId.GetInt32() != id)
                continue;

            if (document.RootElement.TryGetProperty("error", out JsonElement error))
            {
                string? text = error.TryGetProperty("message", out JsonElement errorMessage) ? errorMessage.GetString() : "Inspector request failed.";
                throw new IOException(text);
            }

            if (!document.RootElement.TryGetProperty("result", out JsonElement result))
                return JsonDocument.Parse("{}");

            return JsonDocument.Parse(result.GetRawText());
        }
    }

    private async Task<string> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        if (_socket == null)
            throw new IOException("The chat inspector is not connected.");

        using MemoryStream stream = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];
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

        if (element.TryGetProperty("frameTree", out JsonElement tree))
            return FindClientFrameId(tree);

        if (element.TryGetProperty("frame", out JsonElement frame) &&
            frame.TryGetProperty("url", out JsonElement url) &&
            string.Equals(url.GetString(), ClientFrameUrl, StringComparison.Ordinal) &&
            frame.TryGetProperty("id", out JsonElement id))
        {
            return id.GetString();
        }

        if (!element.TryGetProperty("childFrames", out JsonElement children) || children.ValueKind != JsonValueKind.Array)
            return null;

        foreach (JsonElement child in children.EnumerateArray())
        {
            string? found = FindClientFrameId(child);
            if (found != null)
                return found;
        }

        return null;
    }

    private void CloseSocket()
    {
        ClientWebSocket? socket = _socket;
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
