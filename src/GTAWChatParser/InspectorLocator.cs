using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GTAWChatParser;

internal static class InspectorLocator
{
    private const int AfInet = 2;
    private const uint Loopback = 0x0100007F;

    private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromMilliseconds(900)
    };

    public static async Task<int?> FindAsync(int preferred, CancellationToken cancellationToken)
    {
        var ports = CandidatePorts(preferred);
        var found = new List<int>();
        await Parallel.ForEachAsync(ports, new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = cancellationToken
        }, async (port, token) =>
        {
            if (!await HostsRootUiAsync(port, token).ConfigureAwait(false))
                return;

            lock (found)
                found.Add(port);
        }).ConfigureAwait(false);

        if (found.Count == 0)
            return null;

        if (found.Contains(preferred))
            return preferred;

        return found[0];
    }

    internal static int PortFromNetworkOrder(uint value)
    {
        return ((int)(value & 0xFF) << 8) | (int)((value >> 8) & 0xFF);
    }

    internal static bool ContainsRootUi(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var target in document.RootElement.EnumerateArray())
            {
                if (target.TryGetProperty("url", out var url) &&
                    string.Equals(url.GetString(), NuiChatClient.RootUiUrl, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static List<int> CandidatePorts(int preferred)
    {
        var ports = new HashSet<int> { 13172 };
        if (preferred is > 0 and <= 65535)
            ports.Add(preferred);

        var clientIds = GameProcess.ClientProcessIds();
        if (clientIds.Count == 0)
            return ports.OrderBy(port => port == preferred ? 0 : 1).ToList();

        foreach (var (pid, port, address) in ListeningPorts())
        {
            if (port is < 1 or > 65535)
                continue;
            if (address != 0 && address != Loopback)
                continue;
            if (clientIds.Contains(pid))
                ports.Add(port);
        }

        return ports.ToList();
    }

    private static async Task<bool> HostsRootUiAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(800));
            var json = await Http.GetStringAsync($"http://127.0.0.1:{port}/json", timeout.Token).ConfigureAwait(false);
            return ContainsRootUi(json);
        }
        catch (Exception exception) when (exception is HttpRequestException or SocketException or TaskCanceledException or JsonException or IOException)
        {
            return false;
        }
    }

    private static List<(int Pid, int Port, uint Address)> ListeningPorts()
    {
        var size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, true, AfInet, TcpTableClass.OwnerPidListener, 0);
        if (size <= 0)
            return [];

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var result = GetExtendedTcpTable(buffer, ref size, true, AfInet, TcpTableClass.OwnerPidListener, 0);
            if (result != 0)
                return [];

            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<TcpRow>();
            var rows = new List<(int, int, uint)>(count);
            var offset = 4;
            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<TcpRow>(buffer + offset);
                offset += rowSize;
                rows.Add((row.OwningPid, PortFromNetworkOrder(row.LocalPort), row.LocalAddr));
            }

            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private enum TcpTableClass
    {
        OwnerPidListener = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public int OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr table,
        ref int size,
        bool order,
        int ipVersion,
        TcpTableClass tableClass,
        uint reserved);
}
