using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace MedalShadowSync;

public static class AppUpdater
{
    public const string RepoUrl = "https://github.com/Reskeyo/reMedal";

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0";

    public static async Task<(bool UpdateAvailable, string Message, UpdateManager? Manager, UpdateInfo? Info)> CheckAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
            if (!mgr.IsInstalled)
                return (false, "Lokal gestartet – Auto-Updates sind in der installierten Version (Setup.exe) aktiv.", null, null);

            var info = await mgr.CheckForUpdatesAsync();
            if (info is null)
                return (false, $"reMedal v{CurrentVersion} ist aktuell.", mgr, null);

            return (true, $"Update verfügbar: v{info.TargetFullRelease.Version}", mgr, info);
        }
        catch (Exception ex)
        {
            return (false, $"Update-Prüfung fehlgeschlagen: {ex.Message}", null, null);
        }
    }

    public static async Task DownloadAndRestartAsync(UpdateManager mgr, UpdateInfo info, string[]? restartArgs = null)
    {
        await mgr.DownloadUpdatesAsync(info);
        mgr.ApplyUpdatesAndRestart(info, restartArgs);
    }
}
