namespace RRXXLauncher.Tabs;

public class Dlss5Tab : ILauncherTab
{
    readonly Panel root = new() { Dock = DockStyle.Fill };
    readonly Label status = Ui.Text("", true);
    readonly FlowLayoutPanel addonRow = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
    readonly FlowLayoutPanel mainRow = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
    readonly Button setupBtn, playBtn, stopBtn, dlBtn, zipBtn, folderBtn;
    readonly ProgressBar bar = new() { Width = 540, Height = 14, Maximum = 1000, Visible = false };
    readonly TextBox output = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Width = 580, Height = 170,
        Font = new Font(FontFamily.GenericMonospace, 8.5f), BackColor = SystemColors.Window, Visible = false,
    };
    CancellationTokenSource? cts;
    bool busy;

    public string Title => "DLSS 5";
    public Control View => root;

    public Dlss5Tab()
    {
        var col = Ui.Column();
        col.Controls.Add(Ui.Text("Optional: NVIDIA DLSS 5 (neural rendering), 4K at 60 fps. Needs an NVIDIA GeForce RTX card " +
            "(RTX 40/50 recommended) and your own nvngx_dlssnr.dll. Same saves and mods; the normal Play button is not changed."));
        col.Controls.Add(status);

        dlBtn = Ui.AutoBtn("Download add-on", async (_, _) => await Busy(DownloadAddon));
        zipBtn = Ui.AutoBtn("Install from zip...", (_, _) => InstallZip());
        addonRow.Controls.AddRange(new Control[] { dlBtn, zipBtn, Ui.AutoBtn("Release page", (_, _) => Open(Updater.ReleasesPage)) });
        col.Controls.Add(addonRow);

        setupBtn = Ui.AutoBtn("Set up DLSS 5...", async (_, _) => await Busy(Setup));
        playBtn = Ui.AutoBtn("Play with DLSS 5", (_, _) => Play());
        stopBtn = Ui.AutoBtn("Stop", (_, _) => cts?.Cancel());
        mainRow.Controls.AddRange(new Control[] { setupBtn, playBtn, folderBtn = Ui.AutoBtn("Open dlss5 folder", (_, _) => Ui.OpenFolder(Dlss5.Dir)), stopBtn });
        col.Controls.Add(mainRow);
        col.Controls.Add(bar);
        col.Controls.Add(output);
        col.Controls.Add(Ui.Text("In game: F9 = DLSS 5 on/off, F10 = DLSS 5 settings, Home = ReShade menu. Details: dlss5\\README-DLSS5.txt. " +
            "To start with DLSS 5 from the Play tab, tick \"Start with DLSS 5\" there. Undo: delete the dlss5 folder and the two DLSS 5 .bat files."));
        root.Controls.Add(col);
    }

    public void OnShown() => Refresh();

    void Refresh()
    {
        bool scripts = Dlss5.ScriptsPresent, ready = Dlss5.IsSetUp;
        status.Text = !Paths.IsInstalled ? "Install the game first (Setup tab)."
            : !scripts ? "DLSS 5 add-on: not installed. Download it here, or install the add-on zip from the release page."
            : !ready ? "DLSS 5 add-on: installed, not set up yet. Press \"Set up DLSS 5...\" and pick your nvngx_dlssnr.dll."
            : "DLSS 5: set up and ready.";
        addonRow.Visible = !scripts || !ready;
        dlBtn.Enabled = zipBtn.Enabled = !busy;
        setupBtn.Enabled = !busy && scripts && Paths.IsInstalled;
        setupBtn.Text = ready ? "Set up again..." : "Set up DLSS 5...";
        playBtn.Enabled = !busy && ready && Paths.IsInstalled;
        stopBtn.Visible = busy;
        folderBtn.Visible = scripts;
    }

    async Task Busy(Func<CancellationToken, Task> work)
    {
        cts = new CancellationTokenSource();
        busy = true; Refresh();
        try { await work(cts.Token); }
        catch (OperationCanceledException) { Append(Environment.NewLine + "Stopped." + Environment.NewLine); }
        catch (Exception ex) { Append(Environment.NewLine + "Error: " + ex.Message + Environment.NewLine); }
        finally { busy = false; bar.Visible = false; Refresh(); }
    }

    void Append(string s)
    {
        if (output.InvokeRequired) { output.BeginInvoke(() => Append(s)); return; }
        output.Visible = true;
        output.AppendText(s.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
    }

    async Task DownloadAddon(CancellationToken ct)
    {
        output.Clear(); Append("Downloading the DLSS 5 add-on from the latest release..." + Environment.NewLine);
        bar.Visible = true; bar.Value = 0;
        var log = await Dlss5.DownloadAddon(new Progress<double>(f => bar.Value = (int)Math.Clamp(f * 1000, 0, 1000)), ct);
        foreach (var l in log) Append(l + Environment.NewLine);
    }

    void InstallZip()
    {
        using var dlg = new OpenFileDialog { Title = "DLSS 5 add-on zip (RRXX-DLSS5-Addon-....zip)", Filter = "DLSS 5 add-on|RRXX-DLSS5-Addon-*.zip|Zip files|*.zip" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        output.Clear();
        try { foreach (var l in Dlss5.InstallAddon(dlg.FileName)) Append(l + Environment.NewLine); }
        catch (Exception ex) { Append("Error: " + ex.Message + Environment.NewLine); }
        Refresh();
    }

    async Task Setup(CancellationToken ct)
    {
        if (Updater.GameRunning()) { MessageBox.Show("Close the game first.", "DLSS 5"); return; }
        using var dlg = new OpenFileDialog { Title = "Pick your nvngx_dlssnr.dll", Filter = "nvngx_dlssnr.dll|nvngx_dlssnr.dll|DLL files|*.dll" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        output.Clear();
        Append("Setting up DLSS 5 (downloads about 195 MB, each file checked)..." + Environment.NewLine);
        int rc = await Dlss5.RunSetup(dlg.FileName, null, Append,
            q => MessageBox.Show(q + Environment.NewLine + Environment.NewLine + "Only use files from a source you trust.", "DLSS 5 setup",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes, ct);
        Append(Environment.NewLine + (rc == 0 && Dlss5.IsSetUp ? "DLSS 5 is set up. Press \"Play with DLSS 5\"." : $"Setup did not finish (exit code {rc}).") + Environment.NewLine);
    }

    void Play()
    {
        try { PlayTab.LaunchDlss5(); Application.Exit(); }
        catch (Exception ex) { Append("Could not start: " + ex.Message + Environment.NewLine); }
    }

    static void Open(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
}
