namespace GTAWChatParser;

public sealed class CaptureService : IDisposable
{
    private static readonly TimeSpan ArchiveInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PortScanGap = TimeSpan.FromSeconds(3);

    private readonly AppConfig _config;
    private readonly object _gate = new();
    private readonly NuiChatClient _reader;
    private ChatSession? _session;
    private string? _lastSavedPath;
    private bool _disposed;
    private DateTime _nextPortScan = DateTime.MinValue;
    private string? _lastStatus;

    public CaptureService(AppConfig config)
    {
        _config = config;
        _reader = new NuiChatClient(() => _config.ResolvedDevtoolsPort);
    }

    public event Action<string>? StatusChanged;

    public event Action<IReadOnlyList<string>>? LinesAppended;

    public event Action<string, bool>? LogSaved;

    public string GetSnapshot()
    {
        lock (_gate)
            return _session?.ToFileText() ?? string.Empty;
    }

    public string? SaveNow()
    {
        string? text;
        DateTime startedAt;
        lock (_gate)
        {
            if (_session == null)
                return _lastSavedPath;

            text = _session.ToFileText();
            startedAt = _session.StartedAt;
        }

        var path = LogArchive.Save(_config.BackupDirectory, startedAt, text);
        lock (_gate)
        {
            _lastSavedPath = path;
            if (_session != null && _session.ToFileText() == text)
                _session.MarkClean();
        }

        return path;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        RecoverClosedGame();

        var previous = new List<string>();
        var baselineTaken = false;
        DateTime? missingSince = null;
        var lastArchive = DateTime.UtcNow;
        string? lastQuietMessage = null;
        var lastQuietAt = DateTime.MinValue;

        if (GameProcess.IsGameRunning() && TryResumeJournal())
        {
            lock (_gate)
            {
                if (_session != null)
                    previous.AddRange(_session.Lines);
            }

            baselineTaken = true;
            lastArchive = DateTime.UtcNow;
            try
            {
                var recovered = SaveNow();
                if (recovered != null)
                    LogSaved?.Invoke(recovered, false);
            }
            catch (Exception exception)
            {
                AppLog.Error("Could not archive the recovered session.", exception);
            }
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            GamePresence presence = default;
            try
            {
                presence = GameProcess.Query();
                if (!presence.ClientRunning && !presence.GameRunning)
                {
                    if (HasSession())
                    {
                        missingSince ??= DateTime.UtcNow;
                        if (DateTime.UtcNow - missingSince.Value >= CloseGrace)
                        {
                            FinalizeSession();
                            previous.Clear();
                            baselineTaken = false;
                            missingSince = null;
                            SetStatus("Log saved. Waiting for FiveM");
                        }
                        else
                        {
                            SetStatus("Game closed, saving the log...");
                        }
                    }
                    else
                    {
                        SetStatus("Waiting for FiveM");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var visible = await _reader.ReadLinesAsync(cancellationToken).ConfigureAwait(false);
                missingSince = null;
                if (visible.Count == 0)
                {
                    SetStatus(HasSession() ? $"Capturing · {LineCount()} lines" : "Waiting for GTAW chat");
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var overlap = ChatLog.FindOverlap(previous, visible);
                var fresh = visible.Skip(overlap).ToList();
                previous = visible.ToList();

                if (fresh.Count > 0)
                {
                    var capturedAt = DateTime.Now;
                    // A large first snapshot is chat that was already on screen. Small bursts and later lines are new.
                    var notify = baselineTaken || fresh.Count <= 3;
                    var formatted = fresh.Select(line => ChatLog.FormatLine(line, capturedAt)).Where(line => line.Length > 0).ToList();
                    var startedNew = false;
                    lock (_gate)
                    {
                        if (formatted.Count > 0 && _session == null)
                        {
                            _session = ChatSession.Start(fresh[0], capturedAt);
                            startedNew = true;
                        }

                        _session?.Add(formatted);
                    }

                    if (startedNew)
                    {
                        lastArchive = DateTime.UtcNow;
                        AppLog.Info($"Session started {capturedAt:yyyy-MM-dd HH:mm:ss}.");
                    }

                    baselineTaken = true;
                    PersistJournal();
                    if (startedNew)
                        AppLog.Info($"Captured {formatted.Count} lines from the open chat.");
                    if (notify && formatted.Count > 0)
                    {
                        try
                        {
                            LinesAppended?.Invoke(formatted);
                        }
                        catch (Exception exception)
                        {
                            AppLog.Error("Keyword notification failed.", exception);
                        }
                    }
                }

                SetStatus($"Capturing · {LineCount()} lines");
                if (IsDirty() && DateTime.UtcNow - lastArchive >= ArchiveInterval)
                {
                    var path = SaveNow();
                    lastArchive = DateTime.UtcNow;
                    if (path != null)
                    {
                        AppLog.Info($"Periodic save {path}");
                        LogSaved?.Invoke(path, false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _reader.Reset();
                if (!presence.GameRunning && HasSession())
                {
                    missingSince ??= DateTime.UtcNow;
                    if (DateTime.UtcNow - missingSince.Value >= CloseGrace)
                    {
                        FinalizeSession();
                        previous.Clear();
                        baselineTaken = false;
                        missingSince = null;
                        SetStatus("Log saved. Waiting for FiveM");
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    SetStatus("Game closed, saving the log...");
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var message = exception.Message;
                if (!string.Equals(message, lastQuietMessage, StringComparison.Ordinal) || DateTime.UtcNow - lastQuietAt > TimeSpan.FromSeconds(30))
                {
                    lastQuietMessage = message;
                    lastQuietAt = DateTime.UtcNow;
                    AppLog.Info("Waiting for GTAW chat: " + message);
                }

                var searching = (presence.ClientRunning || presence.GameRunning) && ShouldSearchPort(exception);
                SetStatus(HasSession()
                    ? $"Capturing · {LineCount()} lines"
                    : searching ? "Looking for GTAW chat" : "Waiting for GTAW chat");
                if (searching)
                    await SearchForInspectorAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _reader.Dispose();
    }

    private void RecoverClosedGame()
    {
        if (GameProcess.IsGameRunning() || !File.Exists(AppPaths.JournalPath))
            return;

        try
        {
            var session = ChatSession.TryLoad(File.ReadAllText(AppPaths.JournalPath));
            if (session is { Lines.Count: > 0 })
            {
                var path = LogArchive.Save(_config.BackupDirectory, session.StartedAt, session.ToFileText());
                lock (_gate)
                    _lastSavedPath = path;
                AppLog.Info($"Recovered session {path}");
                LogSaved?.Invoke(path, true);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not recover the previous session.", exception);
        }
        finally
        {
            DeleteJournal();
        }
    }

    private bool TryResumeJournal()
    {
        if (!File.Exists(AppPaths.JournalPath))
            return false;

        try
        {
            var session = ChatSession.TryLoad(File.ReadAllText(AppPaths.JournalPath));
            if (session == null || session.Lines.Count == 0)
            {
                DeleteJournal();
                return false;
            }

            lock (_gate)
                _session = session;
            AppLog.Info("Resumed the open FiveM session.");
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not resume the open session.", exception);
            DeleteJournal();
            return false;
        }
    }

    private void FinalizeSession()
    {
        string? path = null;
        try
        {
            path = SaveNow();
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not save the log when FiveM closed.", exception);
            return;
        }

        lock (_gate)
            _session = null;

        DeleteJournal();
        _reader.Reset();
        if (path != null)
        {
            AppLog.Info($"Game closed, saved {path}");
            LogSaved?.Invoke(path, true);
        }
    }

    private void PersistJournal()
    {
        string? text;
        lock (_gate)
            text = _session?.ToFileText();

        if (text == null)
            return;

        try
        {
            Directory.CreateDirectory(AppPaths.Directory);
            var temp = AppPaths.JournalPath + ".tmp";
            File.WriteAllText(temp, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, AppPaths.JournalPath, overwrite: true);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not update the session journal.", exception);
        }
    }

    private static void DeleteJournal()
    {
        try
        {
            if (File.Exists(AppPaths.JournalPath))
                File.Delete(AppPaths.JournalPath);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not remove the session journal.", exception);
        }
    }

    private bool HasSession()
    {
        lock (_gate)
            return _session != null;
    }

    private bool IsDirty()
    {
        lock (_gate)
            return _session is { IsDirty: true };
    }

    private int LineCount()
    {
        lock (_gate)
            return _session?.Lines.Count ?? 0;
    }

    private async Task SearchForInspectorAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow < _nextPortScan)
            return;

        _nextPortScan = DateTime.UtcNow.Add(PortScanGap);
        int found;
        try
        {
            var port = await InspectorLocator.FindAsync(_config.ResolvedDevtoolsPort, cancellationToken).ConfigureAwait(false);
            if (port == null)
                return;
            found = port.Value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Error("Inspector search failed.", exception);
            return;
        }

        if (found == _config.ResolvedDevtoolsPort)
            return;

        _config.DevtoolsPort = found;
        try
        {
            _config.Save();
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not store the inspector port.", exception);
        }

        _reader.Reset();
        AppLog.Info($"Using FiveM inspector port {found}.");
    }

    private static bool ShouldSearchPort(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("still loading", StringComparison.OrdinalIgnoreCase))
            return false;
        if (message.Contains("rejected the read", StringComparison.OrdinalIgnoreCase))
            return false;
        if (message.Contains("context is unavailable", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private void SetStatus(string status)
    {
        if (string.Equals(status, _lastStatus, StringComparison.Ordinal))
            return;

        _lastStatus = status;
        try
        {
            StatusChanged?.Invoke(status);
        }
        catch (Exception exception)
        {
            AppLog.Error("Status update failed.", exception);
        }
    }
}
