namespace RRXXLauncher.Tabs;

public class SetupTab : ILauncherTab
{
    readonly Panel root = new() { Dock = DockStyle.Fill };
    readonly Label status = Ui.Text("");
    readonly ProgressBar bar = new() { Width = 540, Height = 18, Maximum = 1000 };
    readonly Label current = Ui.Text("");
    readonly Button isoBtn, dlcBtn, cancelBtn;
    CancellationTokenSource? cts;

    public string Title => "Setup";
    public Control View => root;
    public event Action? InstallChanged;

    public SetupTab()
    {
        var col = Ui.Column();
        col.Controls.Add(Ui.Text("Game files", true));
        col.Controls.Add(Ui.Text("Choose your own Rumble Roses XX disc image (.iso). The launcher checks the version and copies the game files (about 3.6 GB) into game\\assets."));
        isoBtn = Ui.Btn("Choose game ISO...", async (_, _) => await RunIso());
        col.Controls.Add(isoBtn);
        col.Controls.Add(Ui.Text("DLC (optional)", true));
        col.Controls.Add(Ui.Text("Choose the folder holding your Xbox 360 DLC packages. They are unpacked into your saves folder; official DLC costumes then appear under Others > Locker Room."));
        dlcBtn = Ui.Btn("Choose DLC folder...", async (_, _) => await RunDlc());
        col.Controls.Add(dlcBtn);
        cancelBtn = Ui.Btn("Cancel", (_, _) => cts?.Cancel(), 90);
        cancelBtn.Enabled = false;
        col.Controls.Add(bar);
        col.Controls.Add(current);
        col.Controls.Add(cancelBtn);
        col.Controls.Add(status);
        root.Controls.Add(col);
    }

    public void OnShown() =>
        status.Text = Paths.IsInstalled ? "Game files: installed." : "Game files: not installed yet. Choose your ISO to begin.";

    async Task Run(Func<IProgress<(string, double)>, CancellationToken, string> work)
    {
        cts = new CancellationTokenSource();
        isoBtn.Enabled = dlcBtn.Enabled = false; cancelBtn.Enabled = true;
        var prog = new Progress<(string name, double frac)>(p =>
        {
            bar.Value = (int)Math.Clamp(p.frac * 1000, 0, 1000);
            current.Text = p.name;
        });
        try
        {
            var msg = await Task.Run(() => work(prog, cts.Token));
            status.Text = msg;
        }
        catch (OperationCanceledException) { status.Text = "Cancelled. Run it again to continue where it stopped."; }
        catch (Exception ex) { status.Text = "Error: " + ex.Message; }
        finally
        {
            isoBtn.Enabled = dlcBtn.Enabled = true; cancelBtn.Enabled = false;
            bar.Value = 0; current.Text = "";
            InstallChanged?.Invoke();
        }
    }

    Task RunIso()
    {
        using var dlg = new OpenFileDialog { Title = "Rumble Roses XX disc image", Filter = "Disc image (*.iso)|*.iso|All files|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return Task.CompletedTask;
        var iso = dlg.FileName;
        return Run((p, ct) =>
        {
            Setup.ImportIso(iso, p, ct);
            return "Game files installed. You can press Play.";
        });
    }

    Task RunDlc()
    {
        using var dlg = new FolderBrowserDialog { Description = "Folder with your Xbox 360 DLC packages" };
        if (dlg.ShowDialog() != DialogResult.OK) return Task.CompletedTask;
        var dir = dlg.SelectedPath;
        return Run((p, ct) => string.Join("\n", Setup.ImportDlc(dir, p, ct)));
    }
}
