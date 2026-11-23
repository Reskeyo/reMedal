using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MedalShadowSync;

public sealed record MediaInfo(
    double DurationSeconds,
    int Width,
    int Height,
    string CodecName,
    string ColorSpace,
    long BitRate,
    double FrameRate,
    string AudioCodec,
    int AudioSampleRate,
    long AudioBitRate);

public static partial class MediaProcessor
{
    public static async Task<MediaInfo> ProbeAsync(string ffmpegPath, string videoPath, CancellationToken ct)
    {
        var (_, stderr) = await RunFfmpegAsync(ffmpegPath, ["-hide_banner", "-i", videoPath], ct);

        double duration = 0;
        var durMatch = DurationRegex().Match(stderr);
        if (durMatch.Success)
        {
            duration = int.Parse(durMatch.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
                     + int.Parse(durMatch.Groups[2].Value, CultureInfo.InvariantCulture) * 60
                     + double.Parse(durMatch.Groups[3].Value, CultureInfo.InvariantCulture);
        }

        long bitRate = 0;
        var brMatch = BitrateRegex().Match(stderr);
        if (brMatch.Success)
            bitRate = long.Parse(brMatch.Groups[1].Value, CultureInfo.InvariantCulture) * 1000;

        int width = 1920, height = 1080;
        string codec = "h264", colorSpace = "yuv420p";
        double fps = 60;

        var vidMatch = VideoStreamRegex().Match(stderr);
        if (vidMatch.Success)
        {
            codec = vidMatch.Groups[1].Value.ToLowerInvariant();
            var line = vidMatch.Value;

            var pixMatch = PixFmtRegex().Match(line);
            if (pixMatch.Success)
                colorSpace = pixMatch.Groups[1].Value;

            var resMatch = ResolutionRegex().Match(line);
            if (resMatch.Success)
            {
                width = int.Parse(resMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                height = int.Parse(resMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            }

            var fpsMatch = FpsRegex().Match(line);
            if (fpsMatch.Success)
                fps = double.Parse(fpsMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        string audioCodec = "aac";
        int audioSampleRate = 48000;
        long audioBitRate = 192000;
        var audMatch = AudioStreamRegex().Match(stderr);
        if (audMatch.Success)
        {
            audioCodec = audMatch.Groups[1].Value.ToLowerInvariant();
            var srMatch = SampleRateRegex().Match(audMatch.Value);
            if (srMatch.Success)
                audioSampleRate = int.Parse(srMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var abrMatch = StreamBitrateRegex().Match(audMatch.Value);
            if (abrMatch.Success)
                audioBitRate = long.Parse(abrMatch.Groups[1].Value, CultureInfo.InvariantCulture) * 1000;
        }

        return new MediaInfo(duration, width, height, codec, colorSpace, bitRate, fps, audioCodec, audioSampleRate, audioBitRate);
    }

    public static async Task<string> GenerateThumbnailAsync(
        string ffmpegPath,
        string videoPath,
        string thumbnailsDir,
        double durationSeconds,
        CancellationToken ct)
    {
        Directory.CreateDirectory(thumbnailsDir);
        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        var thumbPath = Path.Combine(thumbnailsDir, baseName + ".jpg");

        var seekSec = durationSeconds > 2.0 ? "1" : "0";
        var (exitCode, stderr) = await RunFfmpegAsync(
            ffmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y", "-ss", seekSec, "-i", videoPath, "-vframes", "1", "-q:v", "3", thumbPath],
            ct);

        if ((exitCode != 0 || !File.Exists(thumbPath)) && seekSec != "0")
        {
            (exitCode, stderr) = await RunFfmpegAsync(
                ffmpegPath,
                ["-hide_banner", "-loglevel", "error", "-y", "-ss", "0", "-i", videoPath, "-vframes", "1", "-q:v", "3", thumbPath],
                ct);
        }

        if (exitCode != 0 || !File.Exists(thumbPath))
            throw new InvalidOperationException($"FFmpeg thumbnail failed (exit {exitCode}): {stderr.Trim()}");

        return thumbPath;
    }

    private static async Task<(int ExitCode, string StdErr)> RunFfmpegAsync(
        string ffmpegPath, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start FFmpeg at {ffmpegPath}");

        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        await stdoutTask;
        return (proc.ExitCode, await stderrTask);
    }

    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"bitrate:\s*(\d+)\s*kb/s")]
    private static partial Regex BitrateRegex();

    [GeneratedRegex(@"Stream #\d+:\d+.*Video:\s*([a-zA-Z0-9_]+).*")]
    private static partial Regex VideoStreamRegex();

    [GeneratedRegex(@",\s*(yuv[a-z0-9]+|nv12|rgb[a-z0-9]+|gbrp[a-z0-9]*)", RegexOptions.IgnoreCase)]
    private static partial Regex PixFmtRegex();

    [GeneratedRegex(@",\s*(\d{2,5})x(\d{2,5})(?:\s|,|\[)")]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*fps")]
    private static partial Regex FpsRegex();

    [GeneratedRegex(@"Stream #\d+:\d+.*Audio:\s*([a-zA-Z0-9_]+).*")]
    private static partial Regex AudioStreamRegex();

    [GeneratedRegex(@"(\d+)\s*Hz")]
    private static partial Regex SampleRateRegex();

    [GeneratedRegex(@"(\d+)\s*kb/s")]
    private static partial Regex StreamBitrateRegex();
}
