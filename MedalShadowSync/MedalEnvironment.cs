using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace MedalShadowSync;

public static class MedalEnvironment
{
    private const string AutostartKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutostartValueName = "reMedal";

    public static string MedalRoamingDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Medal");

    public static string MedalLocalDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Medal");

    public static string? FindMedalDb(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return overridePath;

        if (!Directory.Exists(MedalRoamingDir))
            return null;

        return Directory.EnumerateFiles(MedalRoamingDir, "medal-*.db")
            .Where(p => !Path.GetFileName(p).Equals("medal-guest.db", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public static string FindMedalClipFolder(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && Directory.Exists(overridePath))
            return overridePath;

        var settingsPath = Path.Combine(MedalRoamingDir, "store", "settings.json");
        if (File.Exists(settingsPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
                if (doc.RootElement.TryGetProperty("recorder", out var rec) &&
                    rec.TryGetProperty("clipFolder", out var folder))
                {
                    var val = folder.GetString();
                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }
            }
            catch
            {
                // Ignore and use fallback
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Medal");
    }

    public static string? FindFfmpeg()
    {
        if (Directory.Exists(MedalLocalDir))
        {
            var bundled = Directory.EnumerateDirectories(MedalLocalDir, "recorder-*")
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .SelectMany(d => Directory.EnumerateFiles(d, "ffmpeg*.exe"))
                .FirstOrDefault();

            if (bundled is not null)
                return bundled;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static string? FindShadowPlayFolder()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\NVIDIA Corporation\Global\ShadowPlay\NVSPCAPS");
            if (key?.GetValue("DefaultPathW") is byte[] bytes && bytes.Length >= 2)
            {
                var path = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    return path;
            }
        }
        catch
        {
            // Registry key missing or unreadable
        }

        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "NVIDIA");
        return Directory.Exists(fallback) ? fallback : null;
    }

    public static string FindDefaultWatchFolder()
    {
        if (Directory.Exists(@"D:\Clips"))
            return @"D:\Clips";

        return FindShadowPlayFolder()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "NVIDIA");
    }

    public static bool IsAutostartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutostartKeyPath);
            return key?.GetValue(AutostartValueName) is string val && !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public static void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(AutostartKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(AutostartKeyPath);

        if (enabled)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath))
                key.SetValue(AutostartValueName, $"\"{exePath}\" --minimized");
        }
        else
        {
            key.DeleteValue(AutostartValueName, throwOnMissingValue: false);
        }
    }

    public static string? GetVolumeId(string filePath)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(filePath));
        if (string.IsNullOrWhiteSpace(root))
            return null;

        return GetVolumeInformationW(root, null, 0, out var serial, out _, out _, null, 0)
            ? $"win32:{serial:x8}"
            : null;
    }

    public static string? GetFileInode(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (GetFileInformationByHandle(fs.SafeFileHandle.DangerousGetHandle(), out var info))
            {
                ulong inode = ((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow;
                return inode.ToString();
            }
        }
        catch
        {
            // Optional metadata field
        }
        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(
        string lpRootPathName,
        StringBuilder? lpVolumeNameBuffer,
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder? lpFileSystemNameBuffer,
        int nFileSystemNameSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        IntPtr hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }
}
