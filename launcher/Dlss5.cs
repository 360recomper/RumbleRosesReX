using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace RRXXLauncher;

/// <summary>
/// The optional DLSS 5 add-on (release zip RRXX-DLSS5-Addon-*.zip): "Setup DLSS 5.bat", "Play with DLSS 5.bat" and dlss5\
/// next to the launcher. Setup = dlss5\setup-dlss5.ps1 (downloads + hash-checks the parts, takes the player's
/// nvngx_dlssnr.dll); it is set up once dlss5\host64\nvngx_dlssnr.dll exists.
/// </summary>
public static class Dlss5
{
    public static string Dir => Path.Combine(Paths.Root, "dlss5");
    public static string SetupScript => Path.Combine(Dir, "setup-dlss5.ps1");
    public static string PlayBat => Path.Combine(Paths.Root, "Play with DLSS 5.bat");
    public static string NrDll => Path.Combine(Dir, "host64", "nvngx_dlssnr.dll");

    public static bool ScriptsPresent => File.Exists(SetupScript) && File.Exists(PlayBat);
    public static bool IsSetUp => ScriptsPresent && File.Exists(NrDll);
    /// <summary>setup-dlss5.ps1 prints this right before asking y/n about an unknown, unsigned nvngx_dlssnr.dll.</summary>
    const string PromptLine = "Unknown build and not signed by NVIDIA";

    /// <summary>Starts "Play with DLSS 5.bat" without a console window (it starts the game and exits).</summary>
    public static Process? Launch()
    {
        if (!IsSetUp) throw new InvalidOperationException("DLSS 5 is not set up yet (DLSS 5 tab).");
        var psi = new ProcessStartInfo("cmd.exe") { WorkingDirectory = Paths.Root, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add(PlayBat);
        return Process.Start(psi);
    }

    /// <summary>Unpacks the add-on zip next to the launcher: only the two .bat files and dlss5\ (scripts, templates,
    /// runtime). Files made by setup (host64, downloads, the DLSS 5 TOML) are not in the zip and stay.</summary>
    public static List<string> InstallAddon(string zipPath)
    {
        var log = new List<string>();
        using var zip = ZipFile.OpenRead(zipPath);
        var anchor = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith("dlss5/setup-dlss5.ps1", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Not the DLSS 5 add-on zip (no dlss5/setup-dlss5.ps1).");
        var prefix = anchor.FullName.Replace('\\', '/')[..^"dlss5/setup-dlss5.ps1".Length];
        foreach (var e in zip.Entries)
        {
            var full = e.FullName.Replace('\\', '/');
            if (full.EndsWith('/') || !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = full[prefix.Length..];
            bool ok = !rel.Contains("..") && (rel.StartsWith("dlss5/", StringComparison.OrdinalIgnoreCase)
                || rel.Equals("Setup DLSS 5.bat", StringComparison.OrdinalIgnoreCase) || rel.Equals("Play with DLSS 5.bat", StringComparison.OrdinalIgnoreCase));
            if (!ok) { log.Add("skipped " + rel); continue; }
            var dest = Path.Combine(Paths.Root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, true);
            log.Add(rel);
        }
        log.Add($"DLSS 5 add-on installed ({log.Count} files).");
        return log;
    }

    /// <summary>Downloads the add-on zip of the latest release (SHA-256 checked) and installs it.</summary>
    public static async Task<List<string>> DownloadAddon(IProgress<double>? progress, CancellationToken ct)
    {
        var rel = await Updater.FetchLatest(ct);
        var a = rel.Dlss5Addon ?? throw new InvalidDataException($"Release {rel.Tag} has no DLSS 5 add-on zip. Use the release page.");
        var zip = await Updater.Download(a, progress, ct);
        var log = InstallAddon(zip);
        Updater.CleanDownloads();
        log.Insert(0, $"{a.Name} from {rel.Tag}, SHA-256 ok");
        return log;
    }

    /// <summary>
    /// Runs dlss5\setup-dlss5.ps1 -DlssNrDll &lt;dll&gt; -NoPause hidden, streaming its output. The script asks y/n for an
    /// unknown, unsigned dll; <paramref name="askYes"/> answers it. Returns the script's exit code (0 = set up).
    /// </summary>
    public static async Task<int> RunSetup(string nrDll, string? localFiles, Action<string> output, Func<string, bool> askYes, CancellationToken ct)
    {
        if (!File.Exists(SetupScript)) throw new FileNotFoundException("dlss5\\setup-dlss5.ps1 is missing: install the DLSS 5 add-on first.");
        var psi = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = Dir, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
        };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", SetupScript, "-DlssNrDll", nrDll, "-NoPause" }) psi.ArgumentList.Add(a);
        if (localFiles != null) { psi.ArgumentList.Add("-LocalFiles"); psi.ArgumentList.Add(localFiles); }

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("powershell.exe did not start");
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        var err = Task.Run(async () =>
        {
            string? l;
            while ((l = await p.StandardError.ReadLineAsync()) != null) output(l + Environment.NewLine);
        });
        // With stdin redirected, PowerShell 5.1 does not print the Read-Host prompt but still reads the answer, so the
        // script's warning line just before its only y/n Read-Host is the trigger.
        string? line;
        while ((line = await p.StandardOutput.ReadLineAsync()) != null)
        {
            output(line + Environment.NewLine);
            if (line.Contains(PromptLine, StringComparison.Ordinal))
            {
                bool yes = askYes(line.Trim() + Environment.NewLine + "Use it anyway?");
                output("  Use it anyway? (y/n) " + (yes ? "y" : "n") + Environment.NewLine);
                await p.StandardInput.WriteLineAsync(yes ? "y" : "n");
                await p.StandardInput.FlushAsync();
            }
        }
        await err;
        await p.WaitForExitAsync();
        ct.ThrowIfCancellationRequested();
        return p.ExitCode;
    }

    /// <summary>Headless (tests): --dlss5-setup &lt;dll&gt; [--local-files &lt;dir&gt;] [--answer y|n] or --dlss5-install-addon &lt;zip|download&gt;.
    /// Output in dlss5-cli-log.txt next to the launcher; exit code = the script's / 0 ok.</summary>
    public static int Cli(string[] args)
    {
        string? Opt(string k) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var sb = new StringBuilder($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {string.Join(' ', args)}{Environment.NewLine}");
        int rc;
        try
        {
            if (Opt("--dlss5-install-addon") is { } z)
            {
                var log = z == "download" ? DownloadAddon(null, CancellationToken.None).GetAwaiter().GetResult() : InstallAddon(z);
                foreach (var l in log) sb.AppendLine(l);
                rc = 0;
            }
            else
            {
                var answer = Opt("--answer") ?? "n";
                rc = RunSetup(Opt("--dlss5-setup")!, Opt("--local-files"), s => { lock (sb) sb.Append(s); },
                    q => { lock (sb) sb.AppendLine($"[launcher answered '{answer}' to: {q}]"); return answer == "y"; },
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            sb.AppendLine($"exit {rc}, set up: {IsSetUp}");
        }
        catch (Exception ex) { sb.AppendLine("ERROR: " + ex.Message); rc = 1; }
        File.AppendAllText(Path.Combine(Paths.Root, "dlss5-cli-log.txt"), sb.ToString());
        return rc;
    }
}
