using System.Diagnostics;
using System.Text;
using SkiaSharp;

namespace Vakage.Core.Library;

/// <summary>Reads the text out of a file: a picture by OCR, a PDF by its text layer (or OCR for a scan).</summary>
public interface ITextReader
{
    /// <summary>Why it cannot read, or null when it can.</summary>
    string? Unavailable { get; }

    /// <summary>The text, empty when there is none; null when the file could not be read.</summary>
    string? Read(string path);
}

/// <summary>
/// Text through Eidokanon (<c>Eidokanon --extract file --stdout</c>), the
/// Tekhnexos toolset that reads PDFs and pictures. Found on PATH or in
/// <c>C:\TKHN\Eidokanon</c>; without it, nothing is turned into text.
/// </summary>
public sealed class EidokanonTextReader(string languages = "por+eng") : ITextReader
{
    private readonly string? _exe = Find();

    public string? Unavailable => _exe is null ? "Eidokanon is not installed, so no file can be turned into text." : null;

    private static string? Find()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                     .Append(@"C:\TKHN\Eidokanon"))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "Eidokanon.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry.
            }
        }
        return null;
    }

    public string? Read(string path)
    {
        if (_exe is null) return null;
        var start = new ProcessStartInfo(_exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("--extract");
        start.ArgumentList.Add(path);
        // A PDF comes back as Markdown (Eidokanon's document reading), which is
        // what is kept when the PDF itself is let go.
        start.ArgumentList.Add("--lang");
        start.ArgumentList.Add(languages);
        start.ArgumentList.Add("--stdout");
        using var process = Process.Start(start);
        if (process is null) return null;
        var stdout = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return null;
        }
        return process.ExitCode == 0 ? stdout.Result.Trim() : null;
    }
}

/// <summary>The small pictures kept in place of irrelevant images and videos.</summary>
public static class Thumbnails
{
    public const int MaxSide = 240;
    public const int Quality = 60;

    /// <summary>A small JPEG of a picture; false when the picture cannot be decoded (HEIC, say).</summary>
    public static bool FromImage(string source, string target)
    {
        using var original = SKBitmap.Decode(source);
        if (original is null) return false;
        var scale = Math.Min(1.0, (double)MaxSide / Math.Max(original.Width, original.Height));
        var info = new SKImageInfo(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale)));
        using var small = original.Resize(info, SKFilterQuality.Medium);
        if (small is null) return false;
        using var image = SKImage.FromBitmap(small);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, Quality);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var file = File.Create(target);
        data.SaveTo(file);
        return true;
    }

    /// <summary>A small JPEG of a video's first second, by ffmpeg; false without ffmpeg or on failure.</summary>
    public static bool FromVideo(string source, string target)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var start = new ProcessStartInfo("ffmpeg")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y", "-ss", "1", "-i", source, "-frames:v", "1",
                         "-vf", $"scale='min({MaxSide},iw)':-2", "-q:v", "6", target })
                start.ArgumentList.Add(a);
            using var p = Process.Start(start);
            if (p is null) return false;
            _ = p.StandardError.ReadToEndAsync();
            _ = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(TimeSpan.FromMinutes(1))) { p.Kill(true); return false; }
            return p.ExitCode == 0 && File.Exists(target) && new FileInfo(target).Length > 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // no ffmpeg on PATH
        }
    }

    /// <summary>Words of two letters or more: how much text a picture carries.</summary>
    public static int Words(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Count(char.IsLetter) >= 2);
}
