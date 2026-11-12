using System.Diagnostics;
using System.Text;

namespace RRXXLauncher;

/// <summary>The native renderer (rrxx_renderer in game\RumbleRosesReX.toml), RR-NR-11. USER 11-08: Native is the
/// default (no key = native), Emulated is the fallback. Only builds made with the native hooks have it; the launcher looks for its startup log line in rumble_roses_xx.exe. Ported from
/// UFC-native launcher/NativeRenderer.cs.</summary>
public static class NativeRenderer
{
    public const string RendererKey = "rrxx_renderer";

    /// <summary>rumble_roses_xx.exe exits with this when the native device can't be created
    /// (rumble_roses_xx_app.h kNativeDeviceFailedExitCode). Not 3: abort() and uncaught exceptions exit with 3, and a
    /// plain crash must not switch the player to Emulated.</summary>
    public const int DeviceFailedExitCode = 0x52520003;

    /// <summary>What the game is started with after <see cref="DeviceFailedExitCode"/> (this run only).</summary>
    public const string EmulatedArg = "--rrxx_renderer=emulated";

    // Logged only by builds with the native hooks (RRXX_NATIVE_HOOKS, rumble_roses_xx_app.h LatchRenderer:
    // "rrxx native: renderer on (experimental, plume D3D12)"). Scaffolding builds only say "not implemented".
    // Matched without the word "experimental", so the game may drop it from the log line (Native = default, 11-08).
    static readonly byte[] Marker = Encoding.ASCII.GetBytes("native: renderer on (");

    static string? stamp;
    static bool available;

    /// <summary>True when rumble_roses_xx.exe has the native renderer compiled in. Scans the exe once per size + date.</summary>
    public static bool Available
    {
        get
        {
            try
            {
                var fi = new FileInfo(Paths.GameExe);
                if (!fi.Exists) return false;
                var s = $"{fi.Length}:{fi.LastWriteTimeUtc.Ticks}";
                if (s != stamp) { available = ExeHasMarker(fi.FullName); stamp = s; }
                return available;
            }
            catch { return false; }
        }
    }

    public static bool ExeHasMarker(string exe)
    {
        using var f = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        var buf = new byte[4 << 20];
        int keep = 0, n;
        while ((n = f.Read(buf, keep, buf.Length - keep)) > 0)
        {
            int len = keep + n;
            if (buf.AsSpan(0, len).IndexOf(Marker) >= 0) return true;
            // Keep the tail so a marker split across two reads is still found.
            keep = Math.Min(Marker.Length - 1, len);
            Array.Copy(buf, len - keep, buf, 0, keep);
        }
        return false;
    }

    /// <summary>Renderer = native in the toml, or no rrxx_renderer key at all (Native is the default; existing installs
    /// without the key get Native too). Whether this exe has it is <see cref="Available"/>.</summary>
    public static bool Chosen => !new Toml().GetStr(RendererKey, "native").Equals("emulated", StringComparison.OrdinalIgnoreCase);

    /// <summary>The toml names no renderer (the launcher writes "native" before a native start, so the game, whose own
    /// cvar default may still be emulated, runs what the launcher shows).</summary>
    public static bool KeyMissing => new Toml().Get(RendererKey) == null;

    /// <summary>Renderer = Native in the toml and this exe has it.</summary>
    public static bool Selected => Chosen && Available;

    public static void SetChoice(bool native)
    {
        var t = new Toml();
        t.SetStr(RendererKey, native ? "native" : "emulated");
        t.Save();
        Log("renderer set to " + (native ? "native" : "emulated") + " in RumbleRosesReX.toml");
    }

    /// <summary>Makes sure the shader cache is there and current for a native run (builds it if not). On failure the
    /// renderer goes back to Emulated in the toml (a busy or locked cache folder: this run only) and the reason comes back;
    /// null = ready. Blocks: call off the UI thread.</summary>
    public static string? PrepareCache(IProgress<string>? progress = null)
    {
        var r = ShaderCache.EnsurePresent(Paths.Assets, progress);
        if (r.Ok) return null;
        if (!r.ThisRunOnly) UseEmulated();
        return r.Message;
    }

    /// <summary>rrxx_renderer = emulated in the toml. A file that can't be written only matters to later runs: this run
    /// gets the renderer on its command line (<see cref="LaunchEmulated"/>).</summary>
    public static void UseEmulated()
    {
        try { SetChoice(false); }
        catch (Exception ex) { Log("ERROR: could not write RumbleRosesReX.toml: " + ex.Message); }
    }

    /// <summary>What the player reads when a cache build failed and the game falls back.</summary>
    public static string FallbackText(string reason) =>
        "The native renderer's shader cache could not be built, so " +
        (Chosen ? "this run uses Emulated (Native is tried again next time).\n\n" : "the renderer is set back to Emulated.\n\n") +
        reason + "\n\nThe game still runs normally on Emulated. Details: launcher-native.log";

    /// <summary>What the player reads after <see cref="DeviceFailedExitCode"/> (asked as Yes/No: keep Emulated from now on?).</summary>
    public const string DeviceFailedText =
        "The native renderer could not start on this PC: the graphics card or its driver can't run it.\n\n" +
        "The game is starting again on the Emulated renderer.\n\n" +
        "Use Emulated from now on? (Yes = Settings go back to Emulated. No = try Native again next time.)";

    /// <summary>Starts a build that has native code on the emulated renderer, whatever the toml says.</summary>
    public static Process LaunchEmulated()
    {
        var psi = new ProcessStartInfo(Paths.GameExe) { WorkingDirectory = Paths.Game, UseShellExecute = false };
        psi.ArgumentList.Add(EmulatedArg);
        return Process.Start(psi) ?? throw new InvalidOperationException("rumble_roses_xx.exe did not start");
    }

    /// <summary>Starts the game as the toml says (no arguments).</summary>
    public static Process Launch() =>
        Process.Start(new ProcessStartInfo(Paths.GameExe) { WorkingDirectory = Paths.Game, UseShellExecute = false })
        ?? throw new InvalidOperationException("rumble_roses_xx.exe did not start");

    /// <summary>Waits for a native run. On <see cref="DeviceFailedExitCode"/> the game is started again on the emulated
    /// renderer; <paramref name="keepEmulated"/> (null = headless: toml untouched) decides whether the toml goes back to
    /// Emulated too. Returns the emulated process, or null when no restart was needed.</summary>
    public static Process? WaitAndFallBack(Process game, Func<bool>? keepEmulated)
    {
        try
        {
            game.WaitForExit();
            if (game.ExitCode != DeviceFailedExitCode) return null;
            Log("native device failed (exit code 0x52520003): restarting on the emulated renderer");
            if (keepEmulated != null && keepEmulated()) UseEmulated();
            return LaunchEmulated();
        }
        catch (Exception ex) { Log("ERROR: " + ex.Message); return null; }
    }

    public static string LogFile => Path.Combine(Paths.Root, "launcher-native.log");

    internal static void Log(string line)
    {
        try { File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}"); }
        catch { }
    }
}
