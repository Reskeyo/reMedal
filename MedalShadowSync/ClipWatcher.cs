using System.Collections.Concurrent;

namespace MedalShadowSync;

public sealed class ClipWatcher : IDisposable
{
    private readonly MedalDbInjector _injector;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _cts;
    private AppConfig? _config;

    public bool IsRunning => _watcher is not null;

    public ClipWatcher(MedalDbInjector injector, Action<string> log)
    {
        _injector = injector;
        _log = log;
    }

    public void Start(AppConfig config)
    {
        Stop();

        if (string.IsNullOrWhiteSpace(config.WatchFolder) || !Directory.Exists(config.WatchFolder))
        {
            _log($"Watch folder does not exist: {config.WatchFolder}");
            return;
        }

        _config = config;
        _cts = new CancellationTokenSource();

        _watcher = new FileSystemWatcher(config.WatchFolder, "*.mp4")
        {
            IncludeSubdirectories = config.IncludeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
            InternalBufferSize = 65536,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnRenamedEvent;
        _watcher.Error += (_, e) => _log($"Watcher error: {e.GetException().Message}");

        _log($"Watching: {config.WatchFolder} (subfolders: {(config.IncludeSubdirectories ? "yes" : "no")})");
    }

    public void Stop()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        if (_cts is not null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) => EnqueueFile(e.FullPath);
    private void OnRenamedEvent(object sender, RenamedEventArgs e) => EnqueueFile(e.FullPath);

    public void EnqueueFile(string filePath)
    {
        if (!filePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            return;

        var name = Path.GetFileName(filePath);
        if (name.StartsWith("~$") || name.EndsWith(".tmp.mp4", StringComparison.OrdinalIgnoreCase))
            return;

        if (!_inFlight.TryAdd(filePath, 0))
            return;

        var ct = _cts?.Token ?? CancellationToken.None;
        _ = Task.Run(() => ProcessFileAsync(filePath, ct), ct);
    }

    private async Task ProcessFileAsync(string filePath, CancellationToken ct)
    {
        try
        {
            _log($"Detected new clip: {Path.GetFileName(filePath)} — waiting for write to finish...");
            var ready = await WaitForFileReadyAsync(filePath, TimeSpan.FromMinutes(5), ct);
            if (!ready)
            {
                _log($"Skipped (file disappeared or stayed locked): {Path.GetFileName(filePath)}");
                return;
            }

            var cfg = _config ?? AppConfig.Load();
            var dbPath = MedalEnvironment.FindMedalDb(cfg.MedalDbPath);
            if (dbPath is null)
            {
                _log("Error: Medal SQLite database not found in %AppData%\\Medal.");
                return;
            }

            var ffmpeg = MedalEnvironment.FindFfmpeg();
            if (ffmpeg is null)
            {
                _log("Error: FFmpeg not found (neither in Medal recorder folder nor in PATH).");
                return;
            }

            var clipFolder = MedalEnvironment.FindMedalClipFolder(cfg.MedalClipFolder);
            var res = await _injector.ImportClipAsync(dbPath, clipFolder, ffmpeg, filePath, cfg.MaxBackups, ct);
            _log(res.Message);
        }
        catch (OperationCanceledException)
        {
            // Stopped cleanly
        }
        catch (Exception ex)
        {
            _log($"Import failed for {Path.GetFileName(filePath)}: {ex.Message}");
        }
        finally
        {
            _inFlight.TryRemove(filePath, out _);
        }
    }

    public static async Task<bool> WaitForFileReadyAsync(string filePath, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        long lastSize = -1;
        int stableChecks = 0;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
                return false;

            try
            {
                // Exclusive open succeeds only after ShadowPlay releases its write handle
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                var len = fs.Length;
                if (len > 0 && len == lastSize)
                {
                    stableChecks++;
                    if (stableChecks >= 2)
                        return true;
                }
                else
                {
                    lastSize = len;
                    stableChecks = 0;
                }
            }
            catch (IOException)
            {
                stableChecks = 0;
            }

            await Task.Delay(750, ct);
        }

        return false;
    }

    public void Dispose() => Stop();
}
