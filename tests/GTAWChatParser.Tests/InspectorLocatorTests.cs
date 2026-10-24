using Xunit;

namespace GTAWChatParser.Tests;

public class InspectorLocatorTests
{
    [Fact]
    public void Network_port_bytes_become_the_local_port()
    {
        Assert.Equal(13172, InspectorLocator.PortFromNetworkOrder(0x7433));
        Assert.Equal(80, InspectorLocator.PortFromNetworkOrder(0x5000));
    }

    [Fact]
    public void Root_page_is_recognized_from_the_inspector_list()
    {
        const string json = """
            [
              { "url": "https://cfx-nui-game/ui/index.html" },
              { "url": "nui://game/ui/root.html", "webSocketDebuggerUrl": "ws://127.0.0.1:13172/devtools/page/1" }
            ]
            """;

        Assert.True(InspectorLocator.ContainsRootUi(json));
        Assert.False(InspectorLocator.ContainsRootUi("[]"));
        Assert.False(InspectorLocator.ContainsRootUi("not json"));
    }
}
