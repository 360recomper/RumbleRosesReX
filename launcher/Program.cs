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
        ModPack.FinishPending();
        bool forceUi = shift || args.Contains("--settings");
        if (!forceUi && Paths.IsInstalled && Config.Load().SkipLauncher)
        {
            if (PlayTab.Launch(m => MessageBox.Show("Could not start the game: " + m, "RRXX Launcher"))) return;
        }
        Application.Run(new MainForm(args.Contains("--settings")));
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

    public MainForm(bool startOnSettings)
    {
        Text = "Rumble Roses XX Recomp - Launcher";
        ClientSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 440);

        var setup = new SetupTab();
        // Add new tabs (Mod packs, attire picker...) here.
        tabs.AddRange(new ILauncherTab[] { new PlayTab(), new SettingsTab(), new ModsTab(), new ModPacksTab(), new SavesTab(), setup });
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
            tabs[tc.SelectedIndex].OnShown();
        };
    }
}
