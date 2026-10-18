using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRXXLauncher;

/// <summary>
/// Update check + in-place update from the GitHub release. Only program files are replaced (game\*.exe|dll, the launcher,
/// top-level docs, licenses\); never game\RumbleRosesReX.toml, game\assets, saves, mods or dlss5. Replaced files are moved
/// to update-backups\&lt;time&gt;-to-v&lt;tag&gt;\ first. The running launcher is renamed into the backup (Windows allows renaming
/// a running exe), so the caller restarts it afterwards.
/// </summary>
public static class Updater
{
    public const string Repo = "360recomper/RumbleRosesReX";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
    const string Api = "https://api.github.com/repos/" + Repo + "/releases/latest";
    public const string LauncherName = "RRXX Launcher.exe";
    public static string BackupRoot => Path.Combine(Paths.Root, "update-backups");
    static string DownloadDir => Path.Combine(Paths.Root, "update-download");

    public record Asset(string Name, long Size, string Url, string? Sha256);
    public record Release(string Tag, string Name, string Page, Asset? Game, Asset? Dlss5Addon)
    {
        public string Version => Tag.TrimStart('v', 'V');
        public bool IsNewer => AppVersion.Newer(Tag, AppVersion.Installed);
    }

    public class Result
    {
        public List<string> Log { get; } = new();
        public string? BackupDir { get; set; }
        /// <summary>Set when the launcher itself was replaced: start this exe and exit.</summary>
        public string? RestartExe { get; set; }
        public int Replaced { get; set; }
    }

    static readonly Regex GameZip = new(@"^RRXX-Recomp-[\w.\-]+\.zip$", RegexOptions.IgnoreCase);
    static readonly Regex AddonZip = new(@"^RRXX-DLSS5-Addon-[\w.\-]+\.zip$", RegexOptions.IgnoreCase);

    static HttpClient Http(TimeSpan timeout)
    {
        var h = new HttpClient { Timeout = timeout };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("RRXX-Launcher/" + AppVersion.Launcher);
        return h;
    }

    public static async Task<Release> FetchLatest(CancellationToken ct = default)
    {
        using var http = Http(TimeSpan.FromSeconds(15));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var doc = JsonDocument.Parse(await http.GetStringAsync(Api, ct));
        var r = doc.RootElement;
        Asset? game = null, addon = null;
        foreach (var a in r.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            string? digest = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            var sha = digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null;
            var asset = new Asset(name, a.GetProperty("size").GetInt64(), a.GetProperty("browser_download_url").GetString() ?? "", sha);
            if (GameZip.IsMatch(name)) game = asset;
            else if (AddonZip.IsMatch(name)) addon = asset;
        }
        return new Release(r.GetProperty("tag_name").GetString() ?? "", r.GetProperty("name").GetString() ?? "",
            r.GetProperty("html_url").GetString() ?? ReleasesPage, game, addon);
    }

    /// <summary>Downloads an asset into update-download\ and checks it against the release's SHA-256 digest.</summary>
    public static async Task<string> Download(Asset a, IProgress<double>? progress, CancellationToken ct)
    {
        if (a.Sha256 == null) throw new InvalidDataException($"{a.Name}: the release lists no SHA-256 digest, so it can't be checked. Download it from the release page instead.");
        Directory.CreateDirectory(DownloadDir);
        var path = Path.Combine(DownloadDir, a.Name);
        if (File.Exists(path) && Sha256(path) == a.Sha256) { progress?.Report(1); return path; }
        using var http = Http(TimeSpan.FromMinutes(30));
        using (var resp = await http.GetAsync(a.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? a.Size;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(path + ".part");
            var buf = new byte[1 << 16];
            long done = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        File.Move(path + ".part", path, true);
        var got = Sha256(path);
        if (got != a.Sha256) { File.Delete(path); throw new InvalidDataException($"{a.Name}: SHA-256 mismatch (got {got}, want {a.Sha256}). File deleted."); }
        return path;
    }

    public static string Sha256(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
    }

    static string Sha256(ZipArchiveEntry e)
    {
        using var s = e.Open();
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    /// <summary>The files an update may write (paths relative to the install, '/' separated).</summary>
    public static bool IsProgramFile(string rel) =>
        Regex.IsMatch(rel, @"^game/[^/]+\.(exe|dll)$", RegexOptions.IgnoreCase)
        || rel.Equals(LauncherName, StringComparison.OrdinalIgnoreCase)
        || (Regex.IsMatch(rel, @"^[^/]+\.(md|html|txt)$", RegexOptions.IgnoreCase) && !rel.Equals("version.txt", StringComparison.OrdinalIgnoreCase))
        || Regex.IsMatch(rel, @"^licenses/[^/]+$", RegexOptions.IgnoreCase);

    /// <summary>A game exe from this install (game\ or dlss5\) is running. Unreadable paths count as running.</summary>
    public static bool GameRunning()
    {
        var root = Path.GetFullPath(Paths.Root).TrimEnd('\\') + "\\";
        foreach (var p in Process.GetProcessesByName("rumble_roses_xx"))
        {
            try { if (p.MainModule?.FileName is { } f && !f.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; }
            catch { }
            return true;
        }
        return false;
    }

    static Version FileVer(string exe)
    {
        var v = FileVersionInfo.GetVersionInfo(exe);
        return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart, v.FilePrivatePart);
    }

    /// <summary>Replaces the changed program files of the install with the ones in a release game zip.</summary>
    public static Result Apply(string zipPath, string tag)
    {
        if (GameRunning()) throw new InvalidOperationException("Close the game first.");
        var res = new Result();
        using var zip = ZipFile.OpenRead(zipPath);
        // The zip has one top folder (RRXX-Recomp-<date>/); find it from the game exe.
        var anchor = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith("game/rumble_roses_xx.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Not a game zip (no game/rumble_roses_xx.exe).");
        var prefix = anchor.FullName.Replace('\\', '/')[..^"game/rumble_roses_xx.exe".Length];

        var running = Environment.ProcessPath ?? "";
        var plan = new List<(ZipArchiveEntry e, string rel, string dest)>();
        foreach (var e in zip.Entries)
        {
            var full = e.FullName.Replace('\\', '/');
            if (full.EndsWith('/') || !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = full[prefix.Length..];
            if (rel.Contains("..") || !IsProgramFile(rel)) continue;
            var dest = Path.Combine(Paths.Root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(dest) && Sha256(dest) == Sha256(e)) continue;
            if (rel.Equals(LauncherName, StringComparison.OrdinalIgnoreCase) && File.Exists(dest))
            {
                // Never swap in an older launcher (a newer one may already be installed by hand).
                var probe = Path.Combine(Path.GetTempPath(), $"rrxx-launcher-probe-{Environment.ProcessId}.exe");
                e.ExtractToFile(probe, true);
                Version vNew = FileVer(probe), vOld = FileVer(dest);
                File.Delete(probe);
                if (vNew < vOld) { res.Log.Add($"kept {rel} {vOld} (release has {vNew})"); continue; }
            }
            plan.Add((e, rel, dest));
        }

        if (plan.Count == 0)
        {
            res.Log.Add("All program files are already up to date.");
            File.WriteAllText(AppVersion.File, tag.TrimStart('v', 'V') + Environment.NewLine);
            return res;
        }

        var backup = Path.Combine(BackupRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-to-v{tag.TrimStart('v', 'V')}");
        Directory.CreateDirectory(backup);
        res.BackupDir = backup;
        var done = new List<(string dest, string? bak)>();
        try
        {
            foreach (var (e, rel, dest) in plan)
            {
                string? bak = null;
                if (File.Exists(dest))
                {
                    bak = Path.Combine(backup, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(bak)!);
                    File.Move(dest, bak);   // works for the running launcher too (rename, same volume)
                }
                done.Add((dest, bak));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                // Write through a stream so the file gets today's time: Play with DLSS 5.bat copies game\ with xcopy /d.
                using (var src = e.Open())
                using (var dst = File.Create(dest + ".new")) src.CopyTo(dst);
                File.Move(dest + ".new", dest);
                res.Log.Add((bak == null ? "added    " : "replaced ") + rel);
                if (rel.Equals(LauncherName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetFullPath(dest), Path.GetFullPath(running), StringComparison.OrdinalIgnoreCase))
                    res.RestartExe = dest;
            }
        }
        catch (Exception ex)
        {
            res.Log.Add("ERROR: " + ex.Message + " - restoring the old files");
            for (int i = done.Count - 1; i >= 0; i--)
            {
                var (dest, bak) = done[i];
                try { if (File.Exists(dest + ".new")) File.Delete(dest + ".new"); } catch { }
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                try { if (bak != null && File.Exists(bak)) File.Move(bak, dest); } catch { }
            }
            res.RestartExe = null;
            File.WriteAllLines(Path.Combine(backup, "update-log.txt"), res.Log);
            throw new IOException(ex.Message + " (old files restored)", ex);
        }
        res.Replaced = plan.Count;
        File.WriteAllText(AppVersion.File, tag.TrimStart('v', 'V') + Environment.NewLine);
        res.Log.Add($"Updated to v{tag.TrimStart('v', 'V')}: {plan.Count} files, old ones in update-backups\\{Path.GetFileName(backup)}");
        File.WriteAllLines(Path.Combine(backup, "update-log.txt"), res.Log);
        return res;
    }

    /// <summary>After a successful update: drop the downloaded zip.</summary>
    public static void CleanDownloads()
    {
        try { if (Directory.Exists(DownloadDir)) Directory.Delete(DownloadDir, true); } catch { }
    }

    /// <summary>
    /// Headless (scripts, tests): --update [--check-only] [--force] [--zip &lt;file&gt; --sha &lt;hex&gt; --tag &lt;vX&gt;].
    /// Without --zip it asks GitHub and downloads the game zip. Log in update-backups\cli-log.txt; exit 0 = ok.
    /// </summary>
    public static int Cli(string[] args)
    {
        string? Opt(string k) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var log = new List<string> { $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {string.Join(' ', args)} (installed v{AppVersion.Installed}, launcher v{AppVersion.Launcher})" };
        int rc = 0;
        try
        {
            string zipPath, tag;
            if (Opt("--zip") is { } z)
            {
                tag = Opt("--tag") ?? throw new ArgumentException("--tag missing");
                var want = (Opt("--sha") ?? throw new ArgumentException("--sha missing")).ToLowerInvariant();
                if (Sha256(z) != want) throw new InvalidDataException("SHA-256 mismatch");
                zipPath = z;
            }
            else
            {
                var rel = FetchLatest().GetAwaiter().GetResult();
                log.Add($"latest {rel.Tag} '{rel.Name}', game zip {rel.Game?.Name} {rel.Game?.Sha256}, add-on {rel.Dlss5Addon?.Name}");
                log.Add(rel.IsNewer ? $"Update available v{rel.Version}" : "Up to date");
                if (args.Contains("--check-only") || (!rel.IsNewer && !args.Contains("--force"))) goto end;
                var g = rel.Game ?? throw new InvalidDataException("release has no game zip");
                zipPath = Download(g, null, CancellationToken.None).GetAwaiter().GetResult();
                log.Add("downloaded + SHA-256 ok: " + zipPath);
                tag = rel.Tag;
            }
            var r = Apply(zipPath, tag);
            log.AddRange(r.Log);
            if (r.RestartExe != null) log.Add("launcher replaced: restart needed");
            if (!args.Contains("--keep-download")) CleanDownloads();
        }
        catch (Exception ex) { log.Add("ERROR: " + ex.Message); rc = 1; }
        end:
        log.Add(rc == 0 ? "OK" : "FAILED");
        Directory.CreateDirectory(BackupRoot);
        File.AppendAllLines(Path.Combine(BackupRoot, "cli-log.txt"), log);
        return rc;
    }
}
