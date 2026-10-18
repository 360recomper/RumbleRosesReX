using System.Diagnostics;

namespace RRXXLauncher.Tabs;

public class PlayTab : ILauncherTab
{
    readonly Panel root = new() { Dock = DockStyle.Fill };
    readonly Button play;
    readonly CheckBox skip = new() { Text = "Skip the launcher next time (hold Shift while starting to bring it back)", AutoSize = true };
    readonly CheckBox dlss = new() { Text = "Start with DLSS 5 (set up in the DLSS 5 tab)", AutoSize = true };
    readonly CheckBox checkUpd = new() { Text = "Check for updates when the launcher opens", AutoSize = true };
    readonly Label info = Ui.Text("");
    readonly Label header = Ui.Text($"Rumble Roses XX Recomp  v{AppVersion.Installed}", true);
    readonly FlowLayoutPanel updRow = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0), Visible = false };
    readonly Label updText = Ui.Text("", true, 330);
    readonly Button updBtn;
    readonly ProgressBar updBar = new() { Width = 540, Height = 14, Maximum = 1000, Visible = false };
    readonly Label updInfo = Ui.Text("");
    Updater.Release? latest;
    bool loading, updating;

    public string Title => "Play";
    public Control View => root;
    /// <summary>Raised (UI thread) when the check finds a newer release.</summary>
    public event Action<Updater.Release>? UpdateFound;
    /// <summary>Raised after an update that did not need a restart.</summary>
    public event Action? Updated;

    public PlayTab()
    {
        var col = Ui.Column();
        col.Controls.Add(header);
        play = new Button { Text = "Play", Width = 220, Height = 54, Font = new Font(SystemFonts.DefaultFont.FontFamily, 14, FontStyle.Bold), Margin = new Padding(0, 12, 0, 12) };
        play.Click += (_, _) => Launch();
        col.Controls.Add(play);
        skip.CheckedChanged += (_, _) =>
        {
            if (loading) return;
            var c = Config.Load(); c.SkipLauncher = skip.Checked; c.Save();
            if (skip.Checked) WriteSettingsShortcut();
        };
        dlss.CheckedChanged += (_, _) => { if (!loading) { var c = Config.Load(); c.PlayWithDlss5 = dlss.Checked; c.Save(); UpdatePlayText(); } };
        checkUpd.CheckedChanged += (_, _) => { if (!loading) { var c = Config.Load(); c.CheckForUpdates = checkUpd.Checked; c.Save(); } };
        col.Controls.Add(skip);
        col.Controls.Add(dlss);
        col.Controls.Add(info);

        updBtn = Ui.AutoBtn("Update", async (_, _) => await RunUpdate());
        updRow.Controls.AddRange(new Control[] { updText, updBtn, Ui.AutoBtn("What's new", (_, _) => OpenUrl(latest?.Page ?? Updater.ReleasesPage)) });
        col.Controls.Add(updRow);
        col.Controls.Add(updBar);
        col.Controls.Add(updInfo);
        col.Controls.Add(checkUpd);
        col.Controls.Add(Ui.AutoBtn("Check now", async (_, _) => await CheckForUpdates(true)));
        root.Controls.Add(col);
    }

    public void OnShown()
    {
        loading = true;
        var c = Config.Load();
        skip.Checked = c.SkipLauncher;
        checkUpd.Checked = c.CheckForUpdates;
        dlss.Checked = c.PlayWithDlss5;
        dlss.Visible = Dlss5.IsSetUp;
        loading = false;
        play.Enabled = Paths.IsInstalled && !updating;
        info.Text = Paths.IsInstalled ? "" : "Game files are not installed yet: open the Setup tab and choose your ISO.";
        UpdatePlayText();
    }

    void UpdatePlayText() => play.Text = UseDlss5 ? "Play (DLSS 5)" : "Play";

    static bool UseDlss5 => Config.Load().PlayWithDlss5 && Dlss5.IsSetUp;

    /// <summary>Asks GitHub for the latest release; shows the update row when it is newer. Quiet on errors unless asked.</summary>
    public async Task CheckForUpdates(bool manual)
    {
        if (updating) return;
        if (manual) updInfo.Text = "Checking...";
        try
        {
            latest = await Updater.FetchLatest();
            if (latest.IsNewer)
            {
                updText.Text = $"Update available: v{latest.Version} (you have v{AppVersion.Installed})";
                updRow.Visible = true;
                updInfo.Text = latest.Game == null ? "This release has no game zip; use \"What's new\"." : "";
                updBtn.Enabled = latest.Game != null;
                UpdateFound?.Invoke(latest);
            }
            else if (manual) updInfo.Text = $"Up to date (v{AppVersion.Installed}; latest release v{latest.Version}).";
        }
        catch (Exception ex) { if (manual) updInfo.Text = "Could not check for updates: " + ex.Message; }
    }

    async Task RunUpdate()
    {
        var rel = latest;
        if (rel?.Game == null) return;
        if (Updater.GameRunning()) { updInfo.Text = "Close the game first."; return; }
        if (MessageBox.Show($"Download v{rel.Version} ({rel.Game.Size / 1048576.0:0} MB) and replace the changed program files?\n\n" +
                "Your settings (game\\RumbleRosesReX.toml), saves, mods and game files are not touched. Replaced files are kept in update-backups.",
                "Update", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        updating = true; updBtn.Enabled = play.Enabled = false; updBar.Visible = true; updBar.Value = 0;
        try
        {
            updInfo.Text = $"Downloading {rel.Game.Name}...";
            var zip = await Updater.Download(rel.Game, new Progress<double>(f => updBar.Value = (int)Math.Clamp(f * 1000, 0, 1000)), CancellationToken.None);
            updInfo.Text = "SHA-256 ok. Replacing files...";
            var res = await Task.Run(() => Updater.Apply(zip, rel.Tag));
            Updater.CleanDownloads();
            updRow.Visible = false;
            if (res.RestartExe != null)
            {
                Process.Start(new ProcessStartInfo(res.RestartExe, "--updated") { WorkingDirectory = Paths.Root, UseShellExecute = false });
                Application.Exit();
                return;
            }
            updInfo.Text = res.Log.LastOrDefault() ?? $"Updated to v{rel.Version}.";
            header.Text = $"Rumble Roses XX Recomp  v{AppVersion.Installed}";
            Updated?.Invoke();
        }
        catch (Exception ex) { updInfo.Text = "Update failed: " + ex.Message; updBtn.Enabled = true; }
        finally { updating = false; updBar.Visible = false; play.Enabled = Paths.IsInstalled; }
    }

    static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

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
    static bool StartedViaBat;

    /// <summary>If a mod pack's save edit is still waiting, keeps the launcher alive (hidden) until the game closes,
    /// then applies it and says so. No-op when nothing is pending.</summary>
    public static void AfterGame()
    {
        if (Started == null || !ModPack.Installed().Any(r => r.PendingSave != null)) return;
        try
        {
            if (StartedViaBat)
            {
                // The .bat starts the game and exits: wait for the game process itself.
                Process? game = null;
                for (int i = 0; i < 60 && game == null; i++) { game = Process.GetProcessesByName("rumble_roses_xx").FirstOrDefault(); if (game == null) Thread.Sleep(500); }
                game?.WaitForExit();
            }
            else Started.WaitForExit();
            Thread.Sleep(1500);
        }
        catch { return; }
        var msgs = ModPack.FinishPending();
        if (msgs.Count > 0) MessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, msgs), "RRXX Launcher - mod pack save edit");
    }

    public static void LaunchDlss5()
    {
        if (!Paths.IsInstalled) throw new InvalidOperationException("Game files are not installed.");
        Started = Dlss5.Launch();
        StartedViaBat = true;
    }

    public static bool Launch(Action<string>? fail = null)
    {
        if (!Paths.IsInstalled) { fail?.Invoke("Game files are not installed."); return false; }
        try
        {
            if (UseDlss5) { LaunchDlss5(); return true; }
            Started = Process.Start(new ProcessStartInfo(Paths.GameExe) { WorkingDirectory = Paths.Game, UseShellExecute = false });
            StartedViaBat = false;
            return true;
        }
        catch (Exception ex) { fail?.Invoke(ex.Message); return false; }
    }

    void Launch()
    {
        if (Launch(m => info.Text = "Could not start the game: " + m)) Application.Exit();
    }
}
