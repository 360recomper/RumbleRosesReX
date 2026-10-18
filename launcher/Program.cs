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
        bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
        if (args.Length == 2 && args[0] is "--install-pack" or "--uninstall-pack") { Environment.Exit(PackCli(args[0], args[1])); return; }
        if (args.Length == 1 && args[0] == "--apply-pending") { Environment.Exit(PendingCli()); return; }
        if (args.Length > 0 && args[0] == "--update") { Environment.Exit(Updater.Cli(args)); return; }
        if (args.Length > 0 && args[0] is "--dlss5-setup" or "--dlss5-install-addon") { Environment.Exit(Dlss5.Cli(args)); return; }
        var pendingMsgs = ModPack.FinishPending();
        if (pendingMsgs.Count > 0 && !(Paths.IsInstalled && Config.Load().SkipLauncher && !shift))
            MessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, pendingMsgs), "RRXX Launcher - mod pack save edit");
        bool forceUi = shift || args.Contains("--settings") || args.Contains("--updated");
        if (!forceUi && Paths.IsInstalled && Config.Load().SkipLauncher)
        {
            if (PlayTab.Launch(m => MessageBox.Show("Could not start the game: " + m, "RRXX Launcher"))) { PlayTab.AfterGame(); return; }
        }
        int ti = Array.IndexOf(args, "--tab");
        Application.Run(new MainForm(args.Contains("--settings"), ti >= 0 && ti + 1 < args.Length ? args[ti + 1] : null, args.Contains("--updated")));
        PlayTab.AfterGame();
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
    readonly TabControl tc = new() { Dock = DockStyle.Fill };

    /// <param name="startTab">--tab &lt;title&gt; opens that tab (e.g. "Attire").</param>
    /// <param name="updated">--updated: restarted by the updater after it replaced the launcher.</param>
    public MainForm(bool startOnSettings, string? startTab = null, bool updated = false)
    {
        Text = "Rumble Roses XX Recomp - Launcher";
        ClientSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 440);

        var setup = new SetupTab();
        var play = new PlayTab();
        // Add new tabs (Mod packs, attire picker...) here.
        tabs.AddRange(new ILauncherTab[] { play, new SettingsTab(), new ModsTab(), new ModPacksTab(), new AttireTab(), new SavesTab(), new Dlss5Tab(), setup });
        play.UpdateFound += r => Text = $"Rumble Roses XX Recomp - Launcher  (update available: v{r.Version}, see Play)";
        play.Updated += () => Text = "Rumble Roses XX Recomp - Launcher";
        foreach (var t in tabs)
        {
            var page = new TabPage(t.Title);
            t.View.Dock = DockStyle.Fill;
            page.Controls.Add(t.View);
            tc.TabPages.Add(page);
        }
        tc.SelectedIndexChanged += (_, _) => tabs[tc.SelectedIndex].OnShown();
        Controls.Add(tc);
        setup.InstallChanged += () => tabs[tc.SelectedIndex].OnShown();

        Shown += (_, _) =>
        {
            if (!Paths.IsInstalled) tc.SelectedIndex = tabs.IndexOf(setup);
            else if (startOnSettings) tc.SelectedIndex = 1;
            else if (tabs.FindIndex(t => t.Title.Equals(startTab, StringComparison.OrdinalIgnoreCase)) is >= 0 and var i) tc.SelectedIndex = i;
            tabs[tc.SelectedIndex].OnShown();
            if (updated) MessageBox.Show($"Updated to v{AppVersion.Installed}. The replaced files are in the update-backups folder.", "RRXX Launcher");
            else if (Paths.IsInstalled && Config.Load().CheckForUpdates) _ = play.CheckForUpdates(false);
        };
    }
}
