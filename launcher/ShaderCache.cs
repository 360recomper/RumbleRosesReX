using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace RRXXLauncher;

/// <summary>The native renderer's shader cache (RR-NR-11), built on the player's PC from their own disc files by
/// rrxx_shader_cache.exe (native/tools/native/shader_extract): it reads default.xex + pac\_startup_usa.pac, runs
/// XenosRecomp.exe and rrxx_prelink.exe, and writes shader_cache.bin + shader_cache_linked.bin. The game reads them
/// from where the cvar rrxx_shader_cache points; relative = beside rumble_roses_xx.exe (guest_shaders.cpp), so
/// game\shader_cache.bin by default. No game shader ever ships. Ported from UFC-native launcher/ShaderCache.cs.
/// Only one build runs at a time in this launcher: a second caller waits for the first.</summary>
public static class ShaderCache
{
    public const string ToolName = "rrxx_shader_cache.exe";
    public const string CacheKey = "rrxx_shader_cache";

    /// <summary>Everything in game\ that makes the cache; an update that changes one changes the shaders.
    /// The DXC DLLs are XenosRecomp's and the prelinker's compiler (the game uses the same ones).</summary>
    public static readonly string[] Makers = { ToolName, "XenosRecomp.exe", "rrxx_prelink.exe", "dxcompiler.dll", "dxil.dll" };

    /// <summary>The player's files the tool reads (relative to game\assets); a changed one (repair, a mod writing
    /// into assets) changes the shaders.</summary>
    public static readonly string[] Sources = { "default.xex", Path.Combine("pac", "_startup_usa.pac") };

    // Bump when the stamp's meaning changes (every older stamp then reads as stale once).
    const string StampHeader = "rrxx shader cache stamp 1";

    // Takes seconds on the machines tried. This only stops a stuck tool from holding up Play forever.
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(15);

    static readonly object gate = new();
    static Process? running;

    static ShaderCache()
    {
        // The launcher closing mid-build takes the tool with it (the tool's job object takes its children).
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { running?.Kill(true); } catch { }
        };
    }

    /// <summary>ThisRunOnly: the failure says nothing about this PC (folder busy or locked, tool exit 3), so the renderer
    /// choice stays as it is and only this run falls back.</summary>
    public sealed record Result(bool Ok, string Message, bool ThisRunOnly = false);

    public static string Tool => Path.Combine(Paths.Game, ToolName);

    /// <summary>The tools ship in this build (a native build without them can't make a cache).</summary>
    public static bool ToolsPresent => Makers.All(n => File.Exists(Path.Combine(Paths.Game, n)));

    public static string CacheFile
    {
        get
        {
            var set = new Toml().GetStr(CacheKey, "");
            return Path.GetFullPath(string.IsNullOrWhiteSpace(set) ? "shader_cache.bin" : set, Paths.Game);
        }
    }

    public static string LinkedFile
    {
        get
        {
            var c = CacheFile;
            return Path.Combine(Path.GetDirectoryName(c)!, Path.GetFileNameWithoutExtension(c) + "_linked" + Path.GetExtension(c));
        }
    }

    /// <summary>True when both files are there. Whether they belong together is the game's check (it refuses a
    /// linked file made from another shader_cache.bin).</summary>
    public static bool Present
    {
        get
        {
            try
            {
                var a = new FileInfo(CacheFile); var b = new FileInfo(LinkedFile);
                return a.Exists && a.Length > 0 && b.Exists && b.Length > 0;
            }
            catch { return false; }
        }
    }

    /// <summary>Beside the cache: what built it (tool hashes) and from what (source hashes), written after a good build.</summary>
    public static string StampFile => Path.ChangeExtension(CacheFile, ".stamp");

    static string Sha(string file)
    {
        try
        {
            using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Convert.ToHexString(SHA256.HashData(s));
        }
        catch { return "-"; }
    }

    /// <summary>The stamp for the files here now: one line per tool and per source, SHA-256 ("-" when missing).</summary>
    public static string ExpectedStamp(string gameData)
    {
        var sb = new StringBuilder(StampHeader).Append('\n');
        foreach (var n in Makers) sb.Append("tool ").Append(n).Append(' ').Append(Sha(Path.Combine(Paths.Game, n))).Append('\n');
        foreach (var n in Sources) sb.Append("source ").Append(n.Replace('\\', '/')).Append(' ').Append(Sha(Path.Combine(gameData, n))).Append('\n');
        return sb.ToString();
    }

    /// <summary>Why the cache can't be used as it is (null = current): missing, no stamp, other tools, other game files.</summary>
    public static string? StaleReason(string gameData)
    {
        if (!Present) return "not built yet";
        string have;
        try { have = File.ReadAllText(StampFile); }
        catch { return "no stamp (built before stamps or by hand)"; }
        var want = ExpectedStamp(gameData);
        if (have == want) return null;
        var h = have.Split('\n'); var w = want.Split('\n');
        var changed = w.Where(l => l.Length > 0 && !h.Contains(l)).Select(l => l.Split(' ') is { Length: >= 2 } p ? p[0] + " " + p[1] : l).ToList();
        if (changed.Count == 0 || !have.StartsWith(StampHeader + "\n", StringComparison.Ordinal)) return "stamp from an older launcher";
        return "changed since the last build: " + string.Join(", ", changed);
    }

    public static bool CurrentFor(string gameData) => StaleReason(gameData) == null;

    /// <summary>Present and built by the tools here from the game files here now.</summary>
    public static bool Current => CurrentFor(Paths.Assets);

    /// <summary>Builds the cache unless a current one is already there.</summary>
    public static Result EnsurePresent(string gameData, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        lock (gate)
        {
            var why = StaleReason(gameData);
            if (why == null) return new Result(true, "The shader cache is already there.");
            NativeRenderer.Log("shader cache: " + why + ", building");
            return Build(gameData, progress, ct);
        }
    }

    /// <summary>Runs the tool and waits. A failure is returned, not thrown; only cancelling throws. A failed run leaves
    /// an older cache in place (the tool moves new files in at the very end) but removes its stamp, so it is rebuilt.</summary>
    public static Result Build(string gameData, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        lock (gate)
        {
            NativeRenderer.Log($"shader cache build: \"{gameData}\" -> \"{CacheFile}\"");
            var r = Run(gameData, progress, ct);
            NativeRenderer.Log(r.Ok ? "shader cache build: done" : "shader cache build FAILED: " + r.Message);
            return r;
        }
    }

    static Result Run(string gameData, IProgress<string>? progress, CancellationToken ct)
    {
        var tool = Tool;
        if (!File.Exists(tool)) return new Result(false, $"{ToolName} is not in the game folder ({Paths.Game}).");
        // Taken before the run: a source that changes during the build must not get a stamp that says it's current.
        var stamp = ExpectedStamp(gameData);
        try { File.Delete(StampFile); } catch { }
        var psi = new ProcessStartInfo(tool)
        {
            WorkingDirectory = Paths.Game,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add(gameData);
        psi.ArgumentList.Add(Path.GetDirectoryName(CacheFile)!);

        var err = new List<string>();
        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            NativeRenderer.Log("  " + e.Data);
            progress?.Report(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            lock (err) err.Add(e.Data);
        };
        try { p.Start(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new Result(false, $"{ToolName} could not start: {ex.Message}");
        }
        running = p;
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try
        {
            var sw = Stopwatch.StartNew();
            while (!p.WaitForExit(200))
            {
                if (ct.IsCancellationRequested)
                {
                    Stop(p);
                    ct.ThrowIfCancellationRequested();
                }
                if (sw.Elapsed > Limit)
                {
                    Stop(p);
                    return new Result(false, $"{ToolName} was still running after {Limit.TotalMinutes:0} minutes and was stopped.");
                }
            }
            p.WaitForExit(); // the last output lines
        }
        finally { running = null; }

        string[] errLines;
        lock (err) errLines = err.ToArray();
        foreach (var l in errLines.Where(l => l.Contains(": error: ", StringComparison.Ordinal))) NativeRenderer.Log("  " + l);

        if (p.ExitCode != 0) return new Result(false, Explain(p.ExitCode, errLines, gameData), p.ExitCode == 3);
        if (!Present) return new Result(false, $"{ToolName} finished, but {Path.GetFileName(CacheFile)} and its linked file are not in {Path.GetDirectoryName(CacheFile)}.");
        // Without a stamp the next Play only builds it again.
        try { File.WriteAllText(StampFile, stamp); }
        catch (Exception ex) { NativeRenderer.Log("shader cache: could not write " + StampFile + ": " + ex.Message); }
        return new Result(true, "Shader cache built.");
    }

    static void Stop(Process p)
    {
        try { p.Kill(true); p.WaitForExit(5000); } catch { }
    }

    /// <summary>Exit codes: native/tools/native/shader_extract/main.cpp.</summary>
    static string Explain(int code, string[] err, string gameData)
    {
        var what = code switch
        {
            1 => $"{ToolName} rejected the launcher's arguments (launcher bug).",
            2 => $"The game files in {gameData} are missing or can't be used (re-import your ISO in the Setup tab).",
            3 => "The shader cache folder can't be written, or another shader cache build is running there.",
            4 => "XenosRecomp.exe, which translates the shaders, is missing or failed.",
            5 => "rrxx_prelink.exe, which links the shaders, is missing or failed.",
            _ => $"{ToolName} stopped unexpectedly (exit code {code}).",
        };
        const string Prefix = "error: ";
        var line = err.FirstOrDefault(l => l.Contains(": " + Prefix, StringComparison.Ordinal));
        if (line == null) return what;
        return what + "\n" + line[(line.IndexOf(": " + Prefix, StringComparison.Ordinal) + 2 + Prefix.Length)..];
    }
}
