using System.Diagnostics;

namespace RRXXLauncher.Tabs;

public class PlayTab : ILauncherTab
{
    readonly Panel root = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    readonly Button play;
    readonly CheckBox skip = new DarkCheck { Text = "Skip the launcher next time (hold Shift while starting to bring it back)", AutoSize = true };
    readonly CheckBox checkUpd = new DarkCheck { Text = "Check for updates when the launcher opens", AutoSize = true };
    readonly Label info = Ui.Text("");
    readonly Label header = Ui.Text($"Rumble Roses XX Recomp  v{AppVersion.Installed}");
    readonly FlowLayoutPanel updRow = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0, Theme.S(8), 0, 0), Visible = false };
    readonly Label updText = Ui.Text("", true, 330);
    readonly Button updBtn;
    readonly ProgressBar updBar = new PinkBar { Width = Theme.S(540), Height = Theme.S(14), Maximum = 1000, Visible = false };
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
        play = new FlatButton { Kind = BtnKind.Play, Text = "Play", Width = Theme.S(320), Height = Theme.S(82), Margin = new Padding(-Theme.S(7), Theme.S(6), 0, Theme.S(8)) };
        play.Click += (_, _) => Launch();
        col.Controls.Add(play);
        skip.CheckedChanged += (_, _) =>
        {
            if (loading) return;
            try { var c = Config.Load(); c.SkipLauncher = skip.Checked; c.Save(); }
            catch (Exception ex) { info.Text = "Could not save the launcher settings: " + ex.Message; return; }
            if (skip.Checked) WriteSettingsShortcut();
        };
        checkUpd.CheckedChanged += (_, _) =>
        {
            if (loading) return;
            try { var c = Config.Load(); c.CheckForUpdates = checkUpd.Checked; c.Save(); }
            catch (Exception ex) { info.Text = "Could not save the launcher settings: " + ex.Message; }
        };
        col.Controls.Add(skip);
        col.Controls.Add(info);

        updBtn = Ui.Primary(Ui.AutoBtn("Update", async (_, _) => await RunUpdate()));
        updText.ForeColor = Theme.PinkText; updText.Margin = new Padding(0, Theme.S(13), Theme.S(10), 0);
        updRow.Controls.AddRange(new Control[] { updText, updBtn, Ui.AutoBtn("What's new", (_, _) => OpenUrl(latest?.Page ?? Updater.ReleasesPage)) });
        col.Controls.Add(updRow);
        col.Controls.Add(updBar);
        col.Controls.Add(updInfo);
        col.Controls.Add(Ui.Head("Updates"));
        col.Controls.Add(checkUpd);
        col.Controls.Add(Ui.AutoBtn("Check now", async (_, _) => await CheckForUpdates(true)));
        root.Controls.Add(col);
        root.Controls.Add(new PlayBanner());
    }

    /// <summary>--screenshot-all: show the update row and bar as during a download.</summary>
    internal void PreviewUpdate()
    {
        updText.Text = $"Update available: v9.9 (you have v{AppVersion.Installed})";
        updRow.Visible = true;
        updBar.Visible = true; updBar.Value = 420;
        updInfo.Text = "Downloading RumbleRosesXX-Recomp-v9.9.zip...";
    }

    public void OnShown()
    {
        loading = true;
        var c = Config.Load();
        skip.Checked = c.SkipLauncher;
        checkUpd.Checked = c.CheckForUpdates;
        loading = false;
        play.Enabled = Paths.IsInstalled && !updating;
        info.Text = Paths.IsInstalled ? "" : "Game files are not installed yet: open the Setup tab and choose your ISO.";
        UpdatePlayText();
    }

    void UpdatePlayText() => play.Text = NativeRenderer.Selected ? "Play (Native renderer)" : "Play";

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

    /// <summary>Start path: re-applies the installed release once when an older updater left program files out.</summary>
    public async Task RepairMissing()
    {
        if (updating || !Updater.NeedsRepair) return;
        updating = true; play.Enabled = false;
        try
        {
            updInfo.Text = "Fetching program files the previous update skipped...";
            var r = await Updater.RepairFromRelease();
            updInfo.Text = r == null ? "" : r.Replaced > 0 ? $"Added {r.Replaced} missing program files." : "";
        }
        catch (Exception ex) { updInfo.Text = "Could not fetch the missing program files: " + ex.Message; }
        finally { updating = false; play.Enabled = Paths.IsInstalled; }
    }

        async Task RunUpdate()
    {
        var rel = latest;
        if (rel?.Game == null) return;
        if (Updater.GameRunning()) { updInfo.Text = "Close the game first."; return; }
        if (DarkBox.Show($"Download v{rel.Version} ({rel.Game.Size / 1048576.0:0} MB) and replace the changed program files?\n\n" +
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
    /// <summary>Started on the native renderer: AfterGame waits for it and restarts on Emulated after the device-failed exit code.</summary>
    static bool StartedNative;

    /// <summary>--play (tests): no message boxes; the toml is never changed by the device-failed path; waits for the game.</summary>
    public static bool Headless;

    /// <summary>Runs after the launcher window is gone (or right after the skip-launcher start). A native run is waited
    /// for: the device-failed exit code (no native device) starts the game again on Emulated. Then, if a mod pack's save edit is still
    /// waiting, keeps the launcher alive (hidden) until the game closes, applies it and says so.</summary>
    public static void AfterGame()
    {
        if (Started == null) return;
        if (StartedNative)
        {
            Func<bool>? ask = Headless ? null : () =>
                DarkBox.Show(NativeRenderer.DeviceFailedText, "RRXX Launcher - native renderer",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
            if (NativeRenderer.WaitAndFallBack(Started, ask) is { } emulated) { Started = emulated; StartedNative = false; }
        }
        if (Headless) { try { Started.WaitForExit(); } catch { } }
        if (!ModPack.Installed().Any(r => r.PendingSave != null)) return;
        try
        {
            Started.WaitForExit();
            Thread.Sleep(1500);
        }
        catch { return; }
        var msgs = ModPack.FinishPending();
        if (msgs.Count > 0) DarkBox.Show(string.Join(Environment.NewLine + Environment.NewLine, msgs), "RRXX Launcher - mod pack save edit");
    }

    /// <summary>Starts the game the way Play does. Native (the default; Settings > Renderer, and this exe has it): the shader cache
    /// is built first when missing or stale; a failed build falls back to Emulated (toml too) with a message.</summary>
    public static bool Launch(Action<string>? fail = null, IWin32Window? owner = null)
    {
        if (!Paths.IsInstalled) { fail?.Invoke("Game files are not installed."); return false; }
        // Tomls from before v1.5 (an update keeps the player's toml) lack readback_resolve: photo shoots then save garbage.
        try { var t = new Toml(); if (t.Get("readback_resolve") == null) { t.SetStr("readback_resolve", "some"); t.Save(); } }
        catch (Exception ex) { NativeRenderer.Log("could not add readback_resolve to the toml: " + ex.Message); }
        try
        {
            if (NativeRenderer.Selected)
            {
                var failed = Headless ? NativeRenderer.PrepareCache() : CacheBuildDialog.Run(owner);
                if (failed != null)
                {
                    if (!Headless) DarkBox.Show(NativeRenderer.FallbackText(failed), "RRXX Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Started = NativeRenderer.LaunchEmulated();
                    StartedNative = false;
                    return true;
                }
                if (NativeRenderer.KeyMissing) NativeRenderer.SetChoice(true);
                Started = NativeRenderer.Launch();
                StartedNative = true;
                NativeRenderer.Log("game started on the native renderer");
                return true;
            }
            Started = NativeRenderer.Launch();
            StartedNative = false;
            return true;
        }
        catch (Exception ex) { fail?.Invoke(ex.Message); return false; }
    }

    void Launch()
    {
        if (Launch(m => info.Text = "Could not start the game: " + m, root.FindForm())) Application.Exit();
    }
}
