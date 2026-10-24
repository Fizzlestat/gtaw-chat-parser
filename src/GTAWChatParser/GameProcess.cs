using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GTAWChatParser;

public readonly record struct GamePresence(bool ClientRunning, bool GameRunning);

public static class GameProcess
{
    public static bool IsClientRunning() => Query().ClientRunning;

    public static bool IsGameRunning() => Query().GameRunning;

    public static GamePresence Query()
    {
        var presence = new GamePresence(false, false);
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return presence;
        }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var name = process.ProcessName;
                    var client = IsClientProcessName(name);
                    var game = IsGameProcessName(name);
                    if (client || game)
                        presence = new GamePresence(presence.ClientRunning || client, presence.GameRunning || game);
                }
                catch
                {
                }
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        return presence;
    }

    public static bool IsFiveMFocused()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
            return false;

        _ = GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0)
            return false;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var name = process.ProcessName;
            return IsGameProcessName(name) || name.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsClientProcessName(string name)
    {
        return name.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("GTA5", StringComparison.OrdinalIgnoreCase);
    }

    internal static HashSet<int> ClientProcessIds()
    {
        var ids = new HashSet<int>();
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return ids;
        }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (IsClientProcessName(process.ProcessName))
                        ids.Add(process.Id);
                }
                catch
                {
                }
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        return ids;
    }

    private static bool IsGameProcessName(string name)
    {
        if (name.Contains("GTAProcess", StringComparison.OrdinalIgnoreCase))
            return true;

        return name.StartsWith("GTA5", StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
