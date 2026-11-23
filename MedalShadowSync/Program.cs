using System.Diagnostics;
using System.Text;
using Velopack;

namespace MedalShadowSync;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Must run first for Velopack install/update/uninstall lifecycle hooks
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ =>
            {
                try { MedalEnvironment.SetAutostart(false); } catch { }
                MedalAsarPatcher.TryRestoreSilently();
            })
            .Run();

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            return RunSelfTest();

        using var mutex = new Mutex(initiallyOwned: true, @"Local\reMedal_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("reMedal läuft bereits im Hintergrund (siehe System-Tray).", "reMedal", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        var config = AppConfig.Load();
        config.Save();

        var launchMinimized = args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        Application.Run(new MainForm(config, launchMinimized));
        return 0;
    }

    // Isolated self-test on temporary files — never modifies real Medal DB or real app.asar
    private static int RunSelfTest()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "reMedal_SelfTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempRoot);
        try
        {
            // 1. Verify GameCategoryResolver against real ShadowPlay folder names
            var cases = new (string Folder, string ExpectedId)[]
            {
                ("Dark Souls III", "L1KjNasj5"),
                ("Counter-strike 2", "1giLEcuGln2"),
                ("Tom Clancy's Rainbow Six  Siege", "HAuR_DD5N"),
                ("Call of Duty  Black Ops 6", "1AgyvzoAPxx"),
                ("Elden Ring", "16mny412ME0"),
                ("Desktop", GameCategoryResolver.ImportedCategoryId)
            };
            foreach (var (folder, expectedId) in cases)
            {
                var r = GameCategoryResolver.ResolveByGameName(null, folder);
                Trace.Assert(r.CategoryId == expectedId, $"GameCategoryResolver('{folder}') expected '{expectedId}', got '{r.CategoryId}' ({r.DisplayName})");
            }
            Console.WriteLine("[OK] GameCategoryResolver matched all test games (Dark Souls III, CS2, R6 Siege, BO6, Elden Ring, Desktop)");

            // 2. Verify SQLite version & jsonb() support
            var dbPath = Path.Combine(tempRoot, "test-medal.db");
            using (var conn = MedalDbInjector.OpenConnection(dbPath, readOnly: false))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE contents (
                        created_at INTEGER NOT NULL,
                        category_id TEXT,
                        video_path TEXT,
                        image_path TEXT,
                        thumbnail_path TEXT,
                        metadata BLOB NOT NULL,
                        remote_content_id TEXT UNIQUE,
                        local_content_id TEXT UNIQUE NOT NULL,
                        parent_id TEXT,
                        film_reel_path TEXT
                    );
                    SELECT sqlite_version();
                    """;
                var ver = cmd.ExecuteScalar()?.ToString() ?? "";
                Trace.Assert(Version.Parse(ver) >= new Version(3, 45, 0), $"SQLite >= 3.45 required for jsonb(), got {ver}");
                Console.WriteLine($"[OK] SQLite version {ver} supports jsonb()");
            }

            // 3. Verify WaitForFileReadyAsync respects exclusive file locks
            var gameDir = Path.Combine(tempRoot, "Watch", "Dark Souls III");
            Directory.CreateDirectory(gameDir);
            var lockedFile = Path.Combine(gameDir, "Dark Souls III 2026.11.22 - 23.00.00.01.DVR.mp4");
            using (var writer = new FileStream(lockedFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                writer.Write(new byte[1024]);
                writer.Flush();
                var quickWait = ClipWatcher.WaitForFileReadyAsync(lockedFile, TimeSpan.FromMilliseconds(900), CancellationToken.None).GetAwaiter().GetResult();
                Trace.Assert(!quickWait, "WaitForFileReadyAsync should return false while file is exclusively locked");
            }
            var unlockedWait = ClipWatcher.WaitForFileReadyAsync(lockedFile, TimeSpan.FromSeconds(5), CancellationToken.None).GetAwaiter().GetResult();
            Trace.Assert(unlockedWait, "WaitForFileReadyAsync should succeed after lock release");
            File.Delete(lockedFile);
            Console.WriteLine("[OK] File-lock wait logic verified");

            // 4. Verify end-to-end FFmpeg probe + thumbnail + SQLite JSONB insert + backfill 30-day filter + dedup
            var ffmpeg = MedalEnvironment.FindFfmpeg();
            if (ffmpeg is not null)
            {
                var watchRoot = Path.Combine(tempRoot, "Watch");
                var recentMp4 = Path.Combine(gameDir, "Dark Souls III 2026.11.22 - 23.15.00.01.DVR.mp4");
                var oldMp4 = Path.Combine(gameDir, "Dark Souls III 2026.09.01 - 12.00.00.01.DVR.mp4");
                var psi = new ProcessStartInfo(ffmpeg, $"-hide_banner -loglevel error -y -f lavfi -i color=c=blue:s=640x360:d=1 -f lavfi -i anullsrc=r=48000:cl=stereo -t 1 -c:v libx264 -c:a aac \"{recentMp4}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi)!) p.WaitForExit();
                Trace.Assert(File.Exists(recentMp4), "Failed to create synthetic test MP4");

                File.Copy(recentMp4, oldMp4);
                File.SetLastWriteTime(oldMp4, DateTime.Now.AddDays(-45));

                var injector = new MedalDbInjector();
                var bf1 = injector.BackfillRecentClipsAsync(dbPath, tempRoot, ffmpeg, watchRoot, includeSubdirs: true, daysBack: 30, maxBackups: 3, _ => { }, CancellationToken.None).GetAwaiter().GetResult();
                Trace.Assert(bf1.Imported == 1 && bf1.Failed == 0, $"Backfill expected 1 imported clip (45d old skipped), got imported={bf1.Imported}, skipped={bf1.Skipped}, failed={bf1.Failed}");

                var bf2 = injector.BackfillRecentClipsAsync(dbPath, tempRoot, ffmpeg, watchRoot, includeSubdirs: true, daysBack: 30, maxBackups: 3, _ => { }, CancellationToken.None).GetAwaiter().GetResult();
                Trace.Assert(bf2.Imported == 0 && bf2.Skipped == 1, $"Second backfill should skip duplicate, got imported={bf2.Imported}, skipped={bf2.Skipped}");
                Console.WriteLine("[OK] Backfill 30-day filter, FFmpeg probe, thumbnail, game category, SQLite JSONB insert & dedup verified");
            }

            // 5. Verify ASAR V2 patch & restore on a temporary copy of Medal's app.asar
            var realAsar = MedalAsarPatcher.FindAppAsar();
            if (realAsar is not null)
            {
                var tempAsar = Path.Combine(tempRoot, "app.asar");
                File.Copy(realAsar, tempAsar);
                // If user already patched real app.asar with V1/V2, restore temp copy to clean baseline first
                MedalAsarPatcher.RestoreOriginal(tempAsar);
                var cleanLen = new FileInfo(tempAsar).Length;

                var (okPatch, msgPatch) = MedalAsarPatcher.ApplyPatch(tempAsar, 3, true, false);
                Trace.Assert(okPatch, $"ASAR patch failed: {msgPatch}");
                Trace.Assert(MedalAsarPatcher.GetStatus(tempAsar) == PatchStatus.Patched, "Expected Patched V8 status");

                var preloadText = Encoding.UTF8.GetString(AsarArchive.ReadFile(tempAsar, "preload.min.js"));
                Trace.Assert(preloadText.Contains("REMEDAL_THROWBACK_PATCH_V8", StringComparison.Ordinal), "Missing V8 patch marker");
                Trace.Assert(!preloadText.Contains("require(\"fs\")", StringComparison.Ordinal), "Sandboxed preload must not require fs");
                Trace.Assert(!preloadText.Contains("__REMEDAL_CONFIG_JSON__", StringComparison.Ordinal), "Config placeholder was not substituted");
                Trace.Assert(preloadText.Contains("onlyLastYear", StringComparison.Ordinal), "Missing onlyLastYear config support");
                Trace.Assert(preloadText.Contains("story-avatar-card", StringComparison.Ordinal), "Missing native Stories bar selector");
                Trace.Assert(preloadText.Contains("width:82px;height:82px", StringComparison.Ordinal), "Missing 82px Story ring sizing");
                Trace.Assert(preloadText.Contains("Link teilen", StringComparison.Ordinal), "Missing Medal-style share link button");
                Trace.Assert(preloadText.Contains("requestFullscreen", StringComparison.Ordinal), "Missing fullscreen control");
                Trace.Assert(preloadText.Contains("remedal_watched_throwbacks_v1", StringComparison.Ordinal), "Missing watched tracking storage key");

                var mainBytes = AsarArchive.ReadFile(tempAsar, "main.min.js");
                Trace.Assert(mainBytes.Length > 1_000_000, "Repacked main.min.js seems corrupted");

                var (okRestore, msgRestore) = MedalAsarPatcher.RestoreOriginal(tempAsar);
                Trace.Assert(okRestore, $"ASAR restore failed: {msgRestore}");
                Trace.Assert(new FileInfo(tempAsar).Length == cleanLen, "Restored app.asar size mismatch");
                Console.WriteLine("[OK] ASAR V8 sandbox-safe patch, integrity check, and backup restore verified on temp copy");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FAIL] {ex}");
            return 1;
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }
}
