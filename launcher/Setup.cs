namespace RRXXLauncher;

/// <summary>First-run work: ISO -> game\assets, DLC packages -> saves\...\00000002 + headers.</summary>
public static class Setup
{
    /// <summary>repair = rewrite every file even when the size matches (fixes a modified stock pac).</summary>
    public static void ImportIso(string iso, IProgress<(string, double)> progress, CancellationToken ct, bool repair = false)
    {
        using var xgd = new Xgd(iso);
        var xex = xgd.Find("default.xex") ?? throw new InvalidDataException("default.xex not found: this is not a game disc.");
        progress.Report(("Checking game version...", 0));
        var sha = xgd.Sha1(xex);
        if (sha != Xgd.XexSha1)
            throw new InvalidDataException(
                "Wrong game version. The recomp is built for one specific release of Rumble Roses XX; this disc's default.xex is different.");
        Directory.CreateDirectory(Paths.Game);
        File.WriteAllText(Paths.ImportMarker, "ISO import not finished");
        xgd.ExtractAll(Paths.Assets, progress, ct, repair);
        // a half-copied install must never look finished: default.xex is verified again last
        var dest = Path.Combine(Paths.Assets, "default.xex");
        if (!File.Exists(dest)) throw new InvalidDataException("default.xex missing after copy.");
        File.Delete(Paths.ImportMarker);
    }

    /// <summary>The step after the disc files are in place, on builds with the native renderer: (re)builds its shader
    /// cache from the new files, whichever renderer is picked, so switching to Native later is instant. A failed build
    /// never fails the install; with no usable cache left, a Native choice goes back to Emulated. Returns the text to
    /// add to the install message ("" = nothing to say).</summary>
    public static string BuildShaderCacheAfterImport(IProgress<(string, double)> progress, CancellationToken ct)
    {
        if (!NativeRenderer.Available) return "";
        const string Busy = "Building the native renderer's shader cache...";
        progress.Report((Busy, 0));
        ShaderCache.Result r;
        // Cancel here stops only the cache build: the game files are already in, and Play builds the cache.
        try { r = ShaderCache.Build(Paths.Assets, new Progress<string>(s => progress.Report((Busy + " " + s, 0.5))), ct); }
        catch (OperationCanceledException) { return "\nThe shader cache build was cancelled; it is built when you press Play."; }
        if (r.Ok) return "";
        bool fellBack = !r.ThisRunOnly && !ShaderCache.Current && NativeRenderer.Chosen;
        if (fellBack) NativeRenderer.UseEmulated();
        return "\nThe native renderer's shader cache could not be built" + (fellBack ? ", so the renderer is set back to Emulated" : "") +
               ":\n" + r.Message + "\nDetails: launcher-native.log";
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
