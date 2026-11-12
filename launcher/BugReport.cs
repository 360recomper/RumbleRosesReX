using System.IO.Compression;
using System.Text.RegularExpressions;

namespace RRXXLauncher;

/// <summary>Zips the game logs, launcher log and toml to the Desktop; no saves, user name scrubbed from the text.</summary>
public static class BugReport
{
    public static string Create(string? outDir = null)
    {
        outDir ??= Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(outDir);
        var zipPath = Path.Combine(outDir, $"RRXX-bug-report-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var files = new List<(string src, string name)>();
        if (Directory.Exists(Paths.Game))
            foreach (var f in Directory.GetFiles(Paths.Game, "log*.txt")) files.Add((f, "game/" + Path.GetFileName(f)));
        files.Add((Paths.Toml, "game/RumbleRosesReX.toml"));
        files.Add((Path.Combine(ModPack.Dir, "cli-log.txt"), "launcher/cli-log.txt"));
        if (NativeRenderer.Available)
        {
            files.Add((NativeRenderer.LogFile, "launcher/launcher-native.log"));
            files.Add((ShaderCache.StampFile, "game/shader_cache.stamp"));
            files.Add((ShaderCache.CacheFile + ".failures.txt", "game/shader_cache.bin.failures.txt"));
        }
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var info = new List<string> { $"RRXX launcher {AppVersion.Installed}", $"{DateTime.Now:u}", Environment.OSVersion.ToString() };
        foreach (var (src, name) in files)
        {
            if (!File.Exists(src)) { info.Add("missing: " + name); continue; }
            // Share-read: the game may still hold its log open.
            using var fs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(entry.Open());
            w.Write(Scrub(sr.ReadToEnd()));
        }
        var e = zip.CreateEntry("info.txt");
        using (var w = new StreamWriter(e.Open())) w.Write(string.Join("\n", info));
        return zipPath;
    }

    /// <summary>Hides the Windows user name (profile paths) and the install dir's parent; keeps the install dir name.</summary>
    public static string Scrub(string text)
    {
        var s = text;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            s = s.Replace(profile, @"C:\Users\<user>", StringComparison.OrdinalIgnoreCase);
            s = s.Replace(profile.Replace('\\', '/'), "C:/Users/<user>", StringComparison.OrdinalIgnoreCase);
        }
        var user = Environment.UserName;
        if (user.Length >= 3) s = Regex.Replace(s, Regex.Escape(user), "<user>", RegexOptions.IgnoreCase);
        return s;
    }
}
