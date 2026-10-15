using System.Diagnostics;

namespace RRXXLauncher.Tabs;

public class PlayTab : ILauncherTab
{
    readonly Panel root = new() { Dock = DockStyle.Fill };
    readonly Button play;
    readonly CheckBox skip = new() { Text = "Skip the launcher next time (hold Shift while starting to bring it back)", AutoSize = true };
    readonly Label info = Ui.Text("");
    bool loading;

    public string Title => "Play";
    public Control View => root;

    public PlayTab()
    {
        var col = Ui.Column();
        col.Controls.Add(Ui.Text("Rumble Roses XX Recomp", true));
        play = new Button { Text = "Play", Width = 220, Height = 54, Font = new Font(SystemFonts.DefaultFont.FontFamily, 14, FontStyle.Bold), Margin = new Padding(0, 12, 0, 12) };
        play.Click += (_, _) => Launch();
        col.Controls.Add(play);
        skip.CheckedChanged += (_, _) =>
        {
            if (loading) return;
            var c = Config.Load(); c.SkipLauncher = skip.Checked; c.Save();
            if (skip.Checked) WriteSettingsShortcut();
        };
        col.Controls.Add(skip);
        col.Controls.Add(info);
        root.Controls.Add(col);
    }

    public void OnShown()
    {
        loading = true; skip.Checked = Config.Load().SkipLauncher; loading = false;
        play.Enabled = Paths.IsInstalled;
        info.Text = Paths.IsInstalled ? "" : "Game files are not installed yet: open the Setup tab and choose your ISO.";
    }

    /// <summary>A double-clickable way back in when the launcher is skipped.</summary>
    static void WriteSettingsShortcut()
    {
        try
        {
            File.WriteAllText(Path.Combine(Paths.Root, "Launcher Settings.bat"),
                "@echo off\r\nstart \"\" \"%~dp0RRXX Launcher.exe\" --settings\r\n");
        }
        catch { /* read-only folder: Shift still works */ }
    }

    static Process? Started;

    /// <summary>If a mod pack's save edit is still waiting, keeps the launcher alive (hidden) until the game closes,
    /// then applies it and says so. No-op when nothing is pending.</summary>
    public static void AfterGame()
    {
        if (Started == null || !ModPack.Installed().Any(r => r.PendingSave != null)) return;
        try { Started.WaitForExit(); Thread.Sleep(1500); } catch { return; }
        var msgs = ModPack.FinishPending();
        if (msgs.Count > 0) MessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, msgs), "RRXX Launcher - mod pack save edit");
    }

    public static bool Launch(Action<string>? fail = null)
    {
        if (!Paths.IsInstalled) { fail?.Invoke("Game files are not installed."); return false; }
        try
        {
            Started = Process.Start(new ProcessStartInfo(Paths.GameExe) { WorkingDirectory = Paths.Game, UseShellExecute = false });
            return true;
        }
        catch (Exception ex) { fail?.Invoke(ex.Message); return false; }
    }

    void Launch()
    {
        if (Launch(m => info.Text = "Could not start the game: " + m)) Application.Exit();
    }
}
