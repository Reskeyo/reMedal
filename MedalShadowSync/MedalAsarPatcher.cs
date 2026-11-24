using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MedalShadowSync;

public enum PatchStatus
{
    NotFound,
    Unpatched,
    Patched,
    OutdatedPatch
}

public static class MedalAsarPatcher
{
    public const string PatchMarker = "/* REMEDAL_THROWBACK_PATCH_V18 */";
    private const string AnyPatchPrefix = "/* REMEDAL_THROWBACK_PATCH_";

    public static string? FindAppAsar()
    {
        var candidate = Path.Combine(MedalEnvironment.MedalLocalDir, "current", "resources", "app.asar");
        return File.Exists(candidate) ? candidate : null;
    }

    public static string GetBackupPath(string asarPath) => asarPath + ".remedal.bak";

    public static PatchStatus GetStatus(string? asarPath = null)
    {
        asarPath ??= FindAppAsar();
        if (asarPath is null || !File.Exists(asarPath))
            return PatchStatus.NotFound;

        try
        {
            var preload = Encoding.UTF8.GetString(AsarArchive.ReadFile(asarPath, "preload.min.js"));
            if (preload.Contains(PatchMarker, StringComparison.Ordinal))
                return PatchStatus.Patched;
            if (preload.Contains(AnyPatchPrefix, StringComparison.Ordinal))
                return PatchStatus.OutdatedPatch;
            return PatchStatus.Unpatched;
        }
        catch
        {
            return PatchStatus.NotFound;
        }
    }

    public static (bool Ok, string Message) ApplyPatch(
        string? asarPath = null,
        int throwbackWindowDays = 0,
        bool blockAdsEnabled = true,
        int maxYears = 0)
    {
        asarPath ??= FindAppAsar();
        if (asarPath is null || !File.Exists(asarPath))
            return (false, "Medal app.asar nicht gefunden.");

        var preloadBytes = AsarArchive.ReadFile(asarPath, "preload.min.js");
        var preloadText = Encoding.UTF8.GetString(preloadBytes);

        // Save backup only when app.asar is an unpatched original
        var backupPath = GetBackupPath(asarPath);
        if (!preloadText.Contains(AnyPatchPrefix, StringComparison.Ordinal))
        {
            File.Copy(asarPath, backupPath, overwrite: true);
        }
        else
        {
            // Strip any older reMedal patch block before applying the latest one
            var idx = preloadText.IndexOf(AnyPatchPrefix, StringComparison.Ordinal);
            if (idx >= 0)
                preloadText = preloadText[..idx].TrimEnd();
        }

        var cfgJson = JsonSerializer.Serialize(new
        {
            windowDays = throwbackWindowDays,
            blockAds = blockAdsEnabled,
            maxYears,
            iconDataUri = LoadAppIconDataUri()
        });
        var injectJs = LoadInjectScript().Replace("__REMEDAL_CONFIG_JSON__", cfgJson, StringComparison.Ordinal);
        var patchedBytes = Encoding.UTF8.GetBytes(preloadText + "\n" + injectJs);

        AsarArchive.ReplaceFile(asarPath, "preload.min.js", patchedBytes);
        return (true, "Rückblicke- & AdBlock-Patch (V18) erfolgreich in Medal installiert. Bitte Medal neu starten.");
    }

    public static (bool Ok, string Message) RestoreOriginal(string? asarPath = null)
    {
        asarPath ??= FindAppAsar();
        if (asarPath is null || !File.Exists(asarPath))
            return (false, "Medal app.asar nicht gefunden.");

        var backupPath = GetBackupPath(asarPath);
        if (File.Exists(backupPath))
        {
            File.Copy(backupPath, asarPath, overwrite: true);
            File.Delete(backupPath);
            return (true, "Original-app.asar aus Backup wiederhergestellt.");
        }

        // Fallback if no .bak exists: strip our appended snippet cleanly
        var preloadText = Encoding.UTF8.GetString(AsarArchive.ReadFile(asarPath, "preload.min.js"));
        var idx = preloadText.IndexOf(AnyPatchPrefix, StringComparison.Ordinal);
        if (idx < 0)
            return (true, "Medal ist bereits im Originalzustand.");

        var cleanBytes = Encoding.UTF8.GetBytes(preloadText[..idx].TrimEnd());
        AsarArchive.ReplaceFile(asarPath, "preload.min.js", cleanBytes);
        return (true, "reMedal-Patch sauber aus app.asar entfernt.");
    }

    public static void TryRestoreSilently()
    {
        try { RestoreOriginal(); } catch { /* Medal may be running during uninstall */ }
    }

    public static bool IsMedalRunning() => Process.GetProcessesByName("Medal").Length > 0;

    public static void KillMedal()
    {
        foreach (var p in Process.GetProcessesByName("Medal"))
        {
            try { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } catch { }
        }
    }

    public static void RestartMedal()
    {
        KillMedal();

        var medalExe = Path.Combine(MedalEnvironment.MedalLocalDir, "Medal.exe");
        if (!File.Exists(medalExe))
            medalExe = Path.Combine(MedalEnvironment.MedalLocalDir, "current", "Medal.exe");

        if (File.Exists(medalExe))
            Process.Start(new ProcessStartInfo(medalExe) { UseShellExecute = true });
    }

    private static string LoadAppIconDataUri()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(r => r.EndsWith(".remedal.ico", StringComparison.OrdinalIgnoreCase));
            if (resName is null) return "";
            using var stream = asm.GetManifestResourceStream(resName);
            if (stream is null) return "";
            using var icon = new Icon(stream, 48, 48);
            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return "";
        }
    }

    private static string LoadInjectScript()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames()
            .First(r => r.EndsWith("throwbacks_inject.js", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(resName)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
