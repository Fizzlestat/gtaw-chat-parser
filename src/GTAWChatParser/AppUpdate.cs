using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace GTAWChatParser;

internal sealed record UpdateOffer(string Version, string DownloadUrl);

internal static class AppUpdate
{
    internal const string Repository = "Fizzlestat/gtaw-chat-parser";

    private static readonly HttpClient Http = CreateClient();

    internal static bool IsConfigured => TrySplitRepository(Repository, out _, out _);

    internal static Version Current => Choose(AssemblyVersion, ReadInstalled());

    internal static string DisplayVersion => FormatVersion(Current);

    private static Version AssemblyVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    internal static Version Choose(Version assembly, Version? installed) =>
        installed != null && installed > assembly ? installed : assembly;

    internal static bool IsNewer(string? tag, Version current) =>
        TryParseTag(tag, out var remote) && remote > current;

    internal static async Task<UpdateOffer?> FindAsync(CancellationToken cancellationToken)
    {
        if (!TrySplitRepository(Repository, out var owner, out var name))
            throw new InvalidOperationException("No GitHub repository is set for updates.");

        var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/releases/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("No published release was found.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Couldn't check for updates.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() : null;
        if (!TryParseTag(tag, out var remote) || remote <= Current)
            return null;

        var download = FindAssetUrl(root);
        if (download == null)
            throw new InvalidOperationException("The latest release does not include GTAWChatParser.exe.");

        return new UpdateOffer(FormatVersion(remote), download);
    }

    internal static async Task DownloadAsync(UpdateOffer offer, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(AppPaths.Directory);
        var partial = Path.Combine(AppPaths.Directory, "update.exe.partial");
        var destination = Path.Combine(AppPaths.Directory, "update.exe");
        if (File.Exists(partial))
            File.Delete(partial);

        using var request = new HttpRequestMessage(HttpMethod.Get, offer.DownloadUrl);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The update could not be downloaded.");

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            await input.CopyToAsync(output, cancellationToken);

        if (!IsExecutable(partial))
        {
            File.Delete(partial);
            throw new InvalidOperationException("The download was not the parser.");
        }

        File.Move(partial, destination, overwrite: true);
    }

    internal static void BeginInstall(string releaseVersion)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            throw new InvalidOperationException("The parser path could not be found.");

        Directory.CreateDirectory(AppPaths.Directory);
        var pending = Path.Combine(AppPaths.Directory, "installed.version.pending");
        File.WriteAllText(pending, NormalizeVersionText(releaseVersion), new UTF8Encoding(false));
        var scriptPath = Path.Combine(AppPaths.Directory, "update.cmd");
        var target = exe.Replace("%", "%%");
        var script = string.Join("\r\n",
            "@echo off",
            "chcp 65001 >nul",
            "set \"SOURCE=%LOCALAPPDATA%\\GTAWChatParser\\update.exe\"",
            "set \"PENDING=%LOCALAPPDATA%\\GTAWChatParser\\installed.version.pending\"",
            "set \"STAMP=%LOCALAPPDATA%\\GTAWChatParser\\installed.version\"",
            "set \"TARGET=" + target + "\"",
            "set TRIES=0",
            ":wait",
            "tasklist /FI \"IMAGENAME eq GTAWChatParser.exe\" | find /I \"GTAWChatParser.exe\" >nul",
            "if not errorlevel 1 (",
            "  ping 127.0.0.1 -n 2 >nul",
            "  goto wait",
            ")",
            ":copy",
            "set /a TRIES+=1",
            "if %TRIES% GEQ 30 exit /b 1",
            "copy /y \"%SOURCE%\" \"%TARGET%\" >nul",
            "if errorlevel 1 (",
            "  ping 127.0.0.1 -n 2 >nul",
            "  goto copy",
            ")",
            "del /q \"%SOURCE%\"",
            "if exist \"%PENDING%\" move /y \"%PENDING%\" \"%STAMP%\" >nul",
            "start \"\" \"%TARGET%\"",
            "del \"%~f0\"");
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/d /s /c \"\"" + scriptPath + "\"\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GTAWChatParser");
        return client;
    }

    private static bool TrySplitRepository(string repository, out string owner, out string name)
    {
        owner = "";
        name = "";
        var slash = repository.IndexOf('/');
        if (slash <= 0 || slash != repository.LastIndexOf('/'))
            return false;

        owner = repository[..slash].Trim();
        name = repository[(slash + 1)..].Trim();
        return owner.Length > 0 && name.Length > 0;
    }

    private static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];

        var dash = text.IndexOfAny(['-', '+']);
        if (dash >= 0)
            text = text[..dash];

        if (!text.Contains('.'))
            text += ".0";

        if (!Version.TryParse(text, out var parsed) || parsed == null)
            return false;

        version = new Version(
            parsed.Major,
            parsed.Minor,
            parsed.Build < 0 ? 0 : parsed.Build,
            parsed.Revision < 0 ? 0 : parsed.Revision);
        return true;
    }

    private static Version? ReadInstalled()
    {
        try
        {
            var path = Path.Combine(AppPaths.Directory, "installed.version");
            if (!File.Exists(path))
                return null;

            return TryParseTag(File.ReadAllText(path).Trim(), out var version) ? version : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string NormalizeVersionText(string releaseVersion)
    {
        var chars = releaseVersion.Where(character => char.IsDigit(character) || character == '.').ToArray();
        var text = new string(chars).Trim('.');
        return TryParseTag(text, out var version) ? FormatVersion(version) : text;
    }

    private static string? FindAssetUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            if (!string.Equals(name, "GTAWChatParser.exe", StringComparison.OrdinalIgnoreCase))
                continue;

            var url = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() : null;
            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }

        return null;
    }

    private static string FormatVersion(Version version)
    {
        if (version.Revision > 0)
            return version.ToString(4);
        if (version.Build >= 0)
            return version.ToString(3);
        return version.ToString(2);
    }

    private static bool IsExecutable(string path)
    {
        var info = new FileInfo(path);
        if (info.Length < 65536)
            return false;

        using var stream = File.OpenRead(path);
        var header = new byte[2];
        return stream.Read(header, 0, 2) == 2 && header[0] == (byte)'M' && header[1] == (byte)'Z';
    }
}
