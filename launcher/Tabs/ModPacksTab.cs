namespace RRXXLauncher.Tabs;

/// <summary>
/// Patch packs (.rrxxpack): checks the player's own ISO files, rebuilds the pack's files from them into mods\ and
/// saves\, sets the pack's game settings and save slots, and can undo all of it (ModPack.cs does the work).
/// </summary>
public class ModPacksTab : ILauncherTab
{
    readonly Panel root = new() { Dock = DockStyle.Fill, AutoScroll = true };
    readonly ListBox list = new() { Width = 540, Height = 110 };
    readonly Label status = Ui.Text("");
    readonly Label current = Ui.Text("");
    readonly ProgressBar bar = new() { Width = 540, Height = 18, Maximum = 1000 };
    readonly Button installBtn, removeBtn, cancelBtn;
    List<ModPack.Record> installed = new();
    CancellationTokenSource? cts;

    public string Title => "Mod packs";
    public Control View => root;

    public ModPacksTab()
    {
        var col = Ui.Column();
        col.Controls.Add(Ui.Text("Mod packs", true));
        col.Controls.Add(status);   // right under the title so a message is never pushed out of view
        col.Controls.Add(Ui.Text("A mod pack (.rrxxpack) holds only the changes: the launcher rebuilds its files from your own game files "
            + "(the ISO you chose in Setup), so it first checks that your ISO is the right version. Everything it changes is recorded and "
            + "can be removed again with Uninstall. Close the game before installing or removing a pack."));
        col.Controls.Add(Ui.Text("Installed", true));
        col.Controls.Add(list);
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        installBtn = Ui.Btn("Install a pack...", async (_, _) => await InstallPack());
        removeBtn = Ui.Btn("Uninstall selected", async (_, _) => await RemovePack());
        cancelBtn = Ui.Btn("Cancel", (_, _) => cts?.Cancel(), 90);
        cancelBtn.Enabled = false;
        row.Controls.Add(installBtn); row.Controls.Add(removeBtn); row.Controls.Add(cancelBtn);
        col.Controls.Add(row);
        col.Controls.Add(bar);
        col.Controls.Add(current);
        root.Controls.Add(col);
    }

    public void OnShown()
    {
        if (cts != null) return;
        var pend = ModPack.FinishPending();
        installed = ModPack.Installed();
        list.Items.Clear();
        foreach (var r in installed)
            list.Items.Add($"{r.Title}  (version {r.PackVersion}, installed {r.Installed})"
                + (r.PendingSave != null ? "  - one more step: play once and save in the game" : ""));
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        installBtn.Enabled = Paths.IsInstalled;
        removeBtn.Enabled = installed.Count > 0;
        if (!Paths.IsInstalled) status.Text = "Install the game first (Setup tab: choose your ISO).";
        else if (pend.Count > 0) status.Text = string.Join(" ", pend);
        else if (installed.Count == 0 && status.Text.Length == 0) status.Text = "No mod packs installed.";
    }

    static bool GameRunning() => System.Diagnostics.Process.GetProcessesByName("rumble_roses_xx").Length > 0;

    async Task Run(Func<Action<string, double>, CancellationToken, List<string>> work)
    {
        if (GameRunning()) { status.Text = "The game is running: close it first."; return; }
        cts = new CancellationTokenSource();
        installBtn.Enabled = removeBtn.Enabled = false; cancelBtn.Enabled = true;
        status.Text = "";
        var prog = new Progress<(string name, double frac)>(p =>
        {
            bar.Value = (int)Math.Clamp(p.frac * 1000, 0, 1000);
            current.Text = p.name;
        });
        IProgress<(string, double)> ip = prog;
        try
        {
            var log = await Task.Run(() => work((n, f) => ip.Report((n, f)), cts.Token));
            status.Text = string.Join("\n", log.Where(l => l.Length > 0));
        }
        catch (OperationCanceledException)
        {
            status.Text = "Cancelled. If files were already written, the pack shows as installed: Uninstall it to undo, then install again.";
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally
        {
            cts = null; cancelBtn.Enabled = false;
            bar.Value = 0; current.Text = "";
            OnShown();
        }
    }

    Task InstallPack()
    {
        using var dlg = new OpenFileDialog { Title = "Choose a mod pack", Filter = "Mod pack (*.rrxxpack)|*.rrxxpack|All files|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return Task.CompletedTask;
        var path = dlg.FileName;
        try
        {
            using var r = new ModPack.Reader(path);
            var h = r.Head;
            long size = h.Files.Sum(f => f.Size);
            var msg = $"{h.Title}\nversion {h.PackVersion}\n\n{h.Description}\n\n"
                + $"Writes {h.Files.Count} files ({size / 1e9:0.0} GB) into mods\\ and saves\\"
                + (h.Toml.Count > 0 ? ", sets " + string.Join(", ", h.Toml.Keys) : "")
                + (h.Save != null ? $" and fills {h.Save.Regions.Count} save slots (created wrestlers and movesets) in every profile" : "")
                + ".\nAll of it can be undone with Uninstall.\n\nInstall now?";
            if (MessageBox.Show(msg, "Install mod pack", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return Task.CompletedTask;
        }
        catch (Exception ex) { status.Text = ex.Message; return Task.CompletedTask; }
        return Run((p, ct) => ModPack.Install(path, p, ct));
    }

    Task RemovePack()
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= installed.Count) return Task.CompletedTask;
        var rec = installed[list.SelectedIndex];
        if (MessageBox.Show($"Uninstall \"{rec.Title}\"?\n\nIts files are removed, anything it replaced is put back, its game settings "
                + "are restored and its save slots get their old contents back (the rest of your save stays as it is).",
                "Uninstall mod pack", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return Task.CompletedTask;
        return Run((p, _) => ModPack.Uninstall(rec, p));
    }
}
