using System.Runtime.InteropServices;
using RRXXLauncher.Tabs;

namespace RRXXLauncher;

static class Program
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        int si = Array.IndexOf(args, "--screenshot-all");
        int? simPct = si >= 0 && si + 2 < args.Length && int.TryParse(args[si + 2], out var sp) ? sp : null;
        Theme.Init(simPct);
        if (si >= 0 && si + 1 < args.Length) { Environment.Exit(Shots.Run(args[si + 1], simPct ?? 100)); return; }
        bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
        if (args.Length == 2 && args[0] is "--install-pack" or "--uninstall-pack") { Environment.Exit(PackCli(args[0], args[1])); return; }
        if (args.Length == 2 && args[0] == "--bug-report") { Console.Error.WriteLine(BugReport.Create(args[1])); Environment.Exit(0); return; }
        if (args.Length == 1 && args[0] == "--apply-pending") { Environment.Exit(PendingCli()); return; }
        if (args.Length > 0 && args[0] is "--update" or "--repair") { Environment.Exit(Updater.Cli(args)); return; }
        if (args.Length > 0 && args[0] == "--shader-cache") { Environment.Exit(ShaderCacheCli(args)); return; }
        if (args.Length == 1 && args[0] == "--play")
        {
            // Headless Play (tests): as the Play button, no window and no message boxes; result in launcher-native.log.
            PlayTab.Headless = true;
            if (PlayTab.Launch(m => NativeRenderer.Log("--play: could not start the game: " + m))) { PlayTab.AfterGame(); Environment.Exit(0); }
            Environment.Exit(1);
            return;
        }
        var pendingMsgs = ModPack.FinishPending();
        SavesTab.AutoApply();
        if (pendingMsgs.Count > 0 && !(Paths.IsInstalled && Config.Load().SkipLauncher && !shift))
            DarkBox.Show(string.Join(Environment.NewLine + Environment.NewLine, pendingMsgs), "RRXX Launcher - mod pack save edit");
        bool forceUi = shift || args.Contains("--settings") || args.Contains("--updated");
        if (!forceUi && Paths.IsInstalled && Config.Load().SkipLauncher)
        {
            if (PlayTab.Launch(m => DarkBox.Show("Could not start the game: " + m, "RRXX Launcher"))) { PlayTab.AfterGame(); return; }
        }
        int ti = Array.IndexOf(args, "--tab");
        Application.Run(new MainForm(args.Contains("--settings"), ti >= 0 && ti + 1 < args.Length ? args[ti + 1] : null, args.Contains("--updated")));
        PlayTab.AfterGame();
    }

    /// <summary>Headless shader cache build (tests, scripts): --shader-cache [--force]. Builds only when missing or
    /// stale (--force: always). Log in launcher-native.log; exit 0 = cache current, 1 = failed, 2 = no native renderer here.</summary>
    static int ShaderCacheCli(string[] args)
    {
        NativeRenderer.Log("--shader-cache " + string.Join(' ', args.Skip(1)));
        if (!NativeRenderer.Available) { NativeRenderer.Log("--shader-cache: this build has no native renderer, nothing to build"); return 2; }
        var r = args.Contains("--force") ? ShaderCache.Build(Paths.Assets) : ShaderCache.EnsurePresent(Paths.Assets);
        NativeRenderer.Log("--shader-cache: " + (r.Ok ? "OK " : "FAILED ") + r.Message.Replace('\n', ' '));
        return r.Ok ? 0 : 1;
    }

    /// <summary>Headless retry of pending save edits (tests): result in modpacks\cli-log.txt, exit 0 = no edit left pending.</summary>
    static int PendingCli()
    {
        var log = new List<string> { $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} --apply-pending" };
        log.AddRange(ModPack.FinishPending());
        int left = ModPack.Installed().Count(r => r.PendingSave != null);
        log.Add(left == 0 ? "OK" : $"PENDING: {left}");
        Directory.CreateDirectory(ModPack.Dir);
        File.AppendAllLines(Path.Combine(ModPack.Dir, "cli-log.txt"), log);
        return left == 0 ? 0 : 1;
    }

    /// <summary>Headless install/uninstall (scripts, tests): result in modpacks\cli-log.txt, exit code 0 = ok.</summary>
    static int PackCli(string cmd, string arg)
    {
        var log = new List<string> { $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {cmd} {arg}" };
        int rc = 0;
        try
        {
            if (cmd == "--install-pack") log.AddRange(ModPack.Install(Path.GetFullPath(arg), (_, _) => { }, CancellationToken.None));
            else
            {
                var rec = ModPack.Installed().FirstOrDefault(r => r.Id == arg) ?? throw new InvalidOperationException("not installed: " + arg);
                log.AddRange(ModPack.Uninstall(rec, (_, _) => { }));
            }
            log.Add("OK");
        }
        catch (Exception ex) { log.Add("ERROR: " + ex.Message); rc = 1; }
        Directory.CreateDirectory(ModPack.Dir);
        File.AppendAllLines(Path.Combine(ModPack.Dir, "cli-log.txt"), log);
        return rc;
    }
}

class MainForm : Form
{
    readonly List<ILauncherTab> tabs = new();
    readonly NavHost tc = new();
    readonly bool preview;

    /// <summary>--screenshot-all: never take the focus from the user.</summary>
    protected override bool ShowWithoutActivation => preview;

    internal NavHost Nav => tc;
    internal IReadOnlyList<ILauncherTab> Tabs => tabs;

    /// <param name="startTab">--tab &lt;title&gt; opens that tab (e.g. "Attire").</param>
    /// <param name="updated">--updated: restarted by the updater after it replaced the launcher.</param>
    /// <param name="preview">--screenshot-all: no update check, no messages.</param>
    public MainForm(bool startOnSettings, string? startTab = null, bool updated = false, bool preview = false)
    {
        this.preview = preview;
        Text = "Rumble Roses XX Recomp - Launcher";
        AutoScaleMode = AutoScaleMode.None;   // sizes are scaled by Theme.S
        Theme.DarkWindow(this);
        ClientSize = new Size(Theme.S(940), Theme.S(640));
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = SizeFromClientSize(new Size(Theme.S(780), Theme.S(540)));

        var setup = new SetupTab();
        var play = new PlayTab();
        // Add new tabs (Mod packs, attire picker...) here; the sidebar icon comes from Glyph below.
        tabs.AddRange(new ILauncherTab[] { play, new SettingsTab(), new ModsTab(), new ModPacksTab(), new AttireTab(), new SavesTab(), setup });
        play.UpdateFound += r => Text = $"Rumble Roses XX Recomp - Launcher  (update available: v{r.Version}, see Play)";
        play.Updated += () => Text = "Rumble Roses XX Recomp - Launcher";
        foreach (var t in tabs) tc.Add(t.Title, Glyph(t.Title), t.View, t is not (PlayTab or AttireTab));
        tc.SelectedIndexChanged += (_, _) => tabs[tc.SelectedIndex].OnShown();
        Controls.Add(tc);
        Theme.DarkScroll(this);
        setup.InstallChanged += () => tabs[tc.SelectedIndex].OnShown();

        Shown += (_, _) =>
        {
            if (preview) return;
            int start = 0;
            if (!Paths.IsInstalled) start = tabs.IndexOf(setup);
            else if (startOnSettings) start = 1;
            else if (tabs.FindIndex(t => t.Title.Equals(startTab, StringComparison.OrdinalIgnoreCase)) is >= 0 and var i) start = i;
            tc.SelectedIndex = start;   // raises OnShown
            if (updated) DarkBox.Show($"Updated to v{AppVersion.Installed}. The replaced files are in the update-backups folder.", "RRXX Launcher");
            else if (Paths.IsInstalled && Config.Load().CheckForUpdates) _ = play.CheckForUpdates(false);
            // An older updater may have skipped program files (v1.4: no pipelines_seed.bin): fetch them once.
            if (Paths.IsInstalled && (updated || Config.Load().CheckForUpdates)) _ = play.RepairMissing();
        };
    }

    /// <summary>Sidebar icons (Segoe Fluent Icons / MDL2 Assets, both ship with Windows).</summary>
    static string Glyph(string title) => title switch
    {
        "Play" => "\uE768",
        "Settings" => "\uE713",
        "Mods" => "\uEA86",
        "Mod packs" => "\uE7B8",
        "Attire" => "\uE790",
        "Saves" => "\uE74E",
        "Setup" => "\uE896",
        _ => "\uE8A5",
    };

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Tab)) { tc.Next(1); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.Tab)) { tc.Next(-1); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
