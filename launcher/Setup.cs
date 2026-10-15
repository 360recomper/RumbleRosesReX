namespace RRXXLauncher;

/// <summary>First-run work: ISO -> game\assets, DLC packages -> saves\...\00000002 + headers.</summary>
public static class Setup
{
    public static void ImportIso(string iso, IProgress<(string, double)> progress, CancellationToken ct)
    {
        using var xgd = new Xgd(iso);
        var xex = xgd.Find("default.xex") ?? throw new InvalidDataException("default.xex not found: this is not a game disc.");
        progress.Report(("Checking game version...", 0));
        var sha = xgd.Sha1(xex);
        if (sha != Xgd.XexSha1)
            throw new InvalidDataException(
                "Wrong game version. The recomp is built for one specific release of Rumble Roses XX; this disc's default.xex is different.");
        xgd.ExtractAll(Paths.Assets, progress, ct);
        // a half-copied install must never look finished: default.xex is verified again last
        var dest = Path.Combine(Paths.Assets, "default.xex");
        if (!File.Exists(dest)) throw new InvalidDataException("default.xex missing after copy.");
    }

    /// <summary>Scans a folder (recursively) for DLC packages; returns (imported, skipped, failed) messages.</summary>
    public static List<string> ImportDlc(string folder, IProgress<(string, double)> progress, CancellationToken ct)
    {
        var log = new List<string>();
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
        int ok = 0, i = 0;
        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report((Path.GetFileName(path), (double)i++ / Math.Max(1, files.Length)));
            try
            {
                using var pkg = new Stfs(path);
                if (pkg.ContentType != 2) { log.Add($"skipped (not DLC): {Path.GetFileName(path)}"); continue; }
                var name = Path.GetFileName(path);
                pkg.Extract(Path.Combine(Paths.Dlc, name));
                Costumes.WriteHeader(name, null);
                ok++;
            }
            catch (InvalidDataException ex) when (ex.Message == "not an STFS package") { /* unrelated file */ }
            catch (Exception ex) { log.Add($"failed: {Path.GetFileName(path)} ({ex.Message})"); }
        }
        log.Insert(0, $"{ok} DLC package(s) imported.");
        return log;
    }
}
