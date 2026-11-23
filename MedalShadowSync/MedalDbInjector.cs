using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace MedalShadowSync;

public sealed record ImportResult(bool Imported, string Message, string? LocalContentId = null, string? CategoryId = null);

public sealed partial class MedalDbInjector
{
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public async Task<ImportResult> ImportClipAsync(
        string dbPath,
        string medalClipFolder,
        string ffmpegPath,
        string videoPath,
        int maxBackups,
        CancellationToken ct,
        bool skipBackup = false)
    {
        if (!File.Exists(videoPath))
            return new ImportResult(false, $"File not found: {videoPath}");

        var fullVideoPath = Path.GetFullPath(videoPath);
        var title = Path.GetFileNameWithoutExtension(fullVideoPath);

        if (await IsAlreadyImportedAsync(dbPath, fullVideoPath, ct))
            return new ImportResult(false, $"Already in Medal: {Path.GetFileName(fullVideoPath)}");

        var media = await MediaProcessor.ProbeAsync(ffmpegPath, fullVideoPath, ct);
        var thumbDir = Path.Combine(medalClipFolder, "Thumbnails");
        var thumbPath = await MediaProcessor.GenerateThumbnailAsync(ffmpegPath, fullVideoPath, thumbDir, media.DurationSeconds, ct);

        var videoFileInfo = new FileInfo(fullVideoPath);
        var thumbFileInfo = new FileInfo(thumbPath);
        var createdAtUnix = ResolveCreatedAtUnixSeconds(fullVideoPath, videoFileInfo);
        var localContentId = Guid.NewGuid().ToString();

        var metadataObj = new Dictionary<string, object?>
        {
            ["isImport"] = true,
            ["title"] = title,
            ["clipDuration"] = media.DurationSeconds,
            ["tags"] = Array.Empty<string>(),
            ["formatName"] = "mov,mp4,m4a,3gp,3g2,mj2",
            ["width"] = media.Width,
            ["height"] = media.Height,
            ["codecName"] = media.CodecName,
            ["colorSpace"] = media.ColorSpace,
            ["bitRate"] = media.BitRate,
            ["frameRate"] = media.FrameRate,
            ["audioStreams"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["index"] = 1,
                    ["title"] = "System sounds",
                    ["codecName"] = media.AudioCodec,
                    ["sampleRate"] = media.AudioSampleRate,
                    ["bitRate"] = media.AudioBitRate
                }
            },
            ["contentSize"] = videoFileInfo.Length,
            ["contentInode"] = MedalEnvironment.GetFileInode(fullVideoPath),
            ["thumbnailSize"] = thumbFileInfo.Length,
            ["thumbnailInode"] = MedalEnvironment.GetFileInode(thumbPath),
            ["userTags"] = Array.Empty<string>(),
            ["playerTags"] = Array.Empty<string>(),
            ["collections"] = Array.Empty<string>(),
            ["isFavorited"] = false,
            ["volumeId"] = MedalEnvironment.GetVolumeId(fullVideoPath)
        };

        var metadataJson = JsonSerializer.Serialize(metadataObj);

        await _dbLock.WaitAsync(ct);
        try
        {
            if (await IsAlreadyImportedAsync(dbPath, fullVideoPath, ct))
                return new ImportResult(false, $"Already in Medal: {Path.GetFileName(fullVideoPath)}");

            if (!skipBackup)
                CreateBackup(dbPath, maxBackups);

            await using var conn = OpenConnection(dbPath, readOnly: false);
            var resolved = GameCategoryResolver.Resolve(conn, fullVideoPath);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO contents (
                    created_at,
                    category_id,
                    video_path,
                    image_path,
                    thumbnail_path,
                    metadata,
                    remote_content_id,
                    local_content_id,
                    parent_id,
                    film_reel_path
                ) VALUES (
                    @created_at,
                    @category_id,
                    @video_path,
                    NULL,
                    @thumbnail_path,
                    jsonb(@metadata),
                    NULL,
                    @local_content_id,
                    NULL,
                    NULL
                )
                """;
            cmd.Parameters.AddWithValue("@created_at", createdAtUnix);
            cmd.Parameters.AddWithValue("@category_id", resolved.CategoryId);
            cmd.Parameters.AddWithValue("@video_path", fullVideoPath);
            cmd.Parameters.AddWithValue("@thumbnail_path", thumbPath);
            cmd.Parameters.AddWithValue("@metadata", metadataJson);
            cmd.Parameters.AddWithValue("@local_content_id", localContentId);

            await cmd.ExecuteNonQueryAsync(ct);
            return new ImportResult(true, $"Imported: {Path.GetFileName(fullVideoPath)} [{resolved.DisplayName}]", localContentId, resolved.CategoryId);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<(int Imported, int Skipped, int Failed)> BackfillRecentClipsAsync(
        string dbPath,
        string medalClipFolder,
        string ffmpegPath,
        string watchFolder,
        bool includeSubdirs,
        int daysBack,
        int maxBackups,
        Action<string> log,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(watchFolder) || !Directory.Exists(watchFolder))
        {
            log($"Nach-Import übersprungen – Ordner existiert nicht: {watchFolder}");
            return (0, 0, 0);
        }

        var cutoff = DateTime.Now.AddDays(-daysBack);
        var searchOption = includeSubdirs ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var candidates = new List<string>();
        foreach (var file in Directory.EnumerateFiles(watchFolder, "*.mp4", searchOption))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            if (name.StartsWith("~$", StringComparison.Ordinal) || name.EndsWith(".tmp.mp4", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                if (File.GetLastWriteTime(file) >= cutoff)
                    candidates.Add(Path.GetFullPath(file));
            }
            catch
            {
                // Ignore unreadable files
            }
        }

        if (candidates.Count == 0)
        {
            log($"Nach-Import ({daysBack} Tage): Keine passenden MP4-Clips gefunden.");
            return (0, 0, 0);
        }

        var existingPaths = await GetAllExistingVideoPathsAsync(dbPath, ct);
        var missing = new List<string>();
        int skipped = 0;

        foreach (var path in candidates)
        {
            if (existingPaths.Contains(path))
                skipped++;
            else
                missing.Add(path);
        }

        if (missing.Count == 0)
        {
            log($"Nach-Import ({daysBack} Tage): Alle {skipped} Clips sind bereits in Medal.");
            return (0, skipped, 0);
        }

        log($"Nach-Import ({daysBack} Tage): {missing.Count} neue(r) Clip(s) gefunden ({skipped} bereits vorhanden). Erstelle DB-Backup...");

        await _dbLock.WaitAsync(ct);
        try
        {
            CreateBackup(dbPath, maxBackups);
        }
        finally
        {
            _dbLock.Release();
        }

        int imported = 0;
        int failed = 0;

        for (int i = 0; i < missing.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var clipPath = missing[i];
            try
            {
                if (!IsFileReadyImmediate(clipPath))
                {
                    skipped++;
                    log($"Nach-Import übersprungen (Datei gesperrt/unvollständig): {Path.GetFileName(clipPath)}");
                    continue;
                }

                var res = await ImportClipAsync(dbPath, medalClipFolder, ffmpegPath, clipPath, maxBackups, ct, skipBackup: true);
                if (res.Imported)
                {
                    imported++;
                    log($"[{i + 1}/{missing.Count}] {res.Message}");
                }
                else
                {
                    skipped++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                log($"Nach-Import Fehler ({Path.GetFileName(clipPath)}): {ex.Message}");
            }
        }

        log($"Nach-Import abgeschlossen: {imported} importiert, {skipped} übersprungen, {failed} fehlgeschlagen.");
        return (imported, skipped, failed);
    }

    private static bool IsFileReadyImmediate(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            return fs.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static async Task<HashSet<string>> GetAllExistingVideoPathsAsync(string dbPath, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var conn = OpenConnection(dbPath, readOnly: true);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT video_path FROM contents WHERE video_path IS NOT NULL";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var p = r.GetString(0);
            if (!string.IsNullOrWhiteSpace(p))
                set.Add(p);
        }
        return set;
    }

    public static void CreateBackup(string dbPath, int maxBackups)
    {
        Directory.CreateDirectory(AppConfig.BackupsDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var backupPath = Path.Combine(AppConfig.BackupsDir, $"{Path.GetFileNameWithoutExtension(dbPath)}-{stamp}.db");

        using (var src = OpenConnection(dbPath, readOnly: true))
        using (var dst = OpenConnection(backupPath, readOnly: false))
        {
            src.BackupDatabase(dst);
        }

        var keep = Math.Max(1, maxBackups);
        var oldBackups = Directory.EnumerateFiles(AppConfig.BackupsDir, "*.db")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(keep)
            .ToList();

        foreach (var old in oldBackups)
        {
            try { File.Delete(old); } catch { /* best-effort rotation */ }
        }
    }

    private static async Task<bool> IsAlreadyImportedAsync(string dbPath, string fullVideoPath, CancellationToken ct)
    {
        await using var conn = OpenConnection(dbPath, readOnly: true);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM contents WHERE lower(video_path) = lower(@path) LIMIT 1";
        cmd.Parameters.AddWithValue("@path", fullVideoPath);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    private static long ResolveCreatedAtUnixSeconds(string filePath, FileInfo info)
    {
        var m = ShadowPlayDateRegex().Match(Path.GetFileNameWithoutExtension(filePath));
        if (m.Success &&
            int.TryParse(m.Groups["y"].Value, out var y) &&
            int.TryParse(m.Groups["mo"].Value, out var mo) &&
            int.TryParse(m.Groups["d"].Value, out var d) &&
            int.TryParse(m.Groups["h"].Value, out var h) &&
            int.TryParse(m.Groups["mi"].Value, out var mi) &&
            int.TryParse(m.Groups["s"].Value, out var s))
        {
            try
            {
                var dt = new DateTime(y, mo, d, h, mi, s, DateTimeKind.Local);
                return new DateTimeOffset(dt).ToUnixTimeSeconds();
            }
            catch
            {
                // Fall back to filesystem timestamp
            }
        }

        return new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
    }

    public static SqliteConnection OpenConnection(string dbPath, bool readOnly)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 5
        };
        var conn = new SqliteConnection(csb.ToString());
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    [GeneratedRegex(@"(?<y>\d{4})\.(?<mo>\d{2})\.(?<d>\d{2})\s*-\s*(?<h>\d{2})\.(?<mi>\d{2})\.(?<s>\d{2})")]
    private static partial Regex ShadowPlayDateRegex();
}
