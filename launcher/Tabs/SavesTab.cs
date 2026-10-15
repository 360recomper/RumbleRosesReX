namespace RRXXLauncher.Tabs;

public class SavesTab : ILauncherTab
{
    readonly Panel root = new() { Dock = DockStyle.Fill };
    readonly Label info = Ui.Text("");

    public string Title => "Saves";
    public Control View => root;

    public SavesTab()
    {
        var col = Ui.Column();
        col.Controls.Add(Ui.Text("Your saves, installed DLC and shader cache live in the saves folder next to the launcher."));
        col.Controls.Add(Ui.Btn("Open saves folder", (_, _) => Ui.OpenFolder(Paths.Saves)));
        col.Controls.Add(Ui.Btn("Back up saves now", (_, _) => Backup()));
        col.Controls.Add(Ui.Btn("Open backups folder", (_, _) => Ui.OpenFolder(Paths.Backups)));
        col.Controls.Add(info);
        root.Controls.Add(col);
    }

    public void OnShown() => info.Text = "";

    void Backup()
    {
        try
        {
            if (!Directory.Exists(Paths.Saves)) { info.Text = "No saves yet."; return; }
            var dest = Path.Combine(Paths.Backups, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            int n = Copy(Paths.Saves, dest);
            info.Text = $"Backed up {n} files to save-backups\\{Path.GetFileName(dest)}";
        }
        catch (Exception ex) { info.Text = "Error: " + ex.Message; }
    }

    /// <summary>Copies a folder, skipping the shader cache (large and rebuilt automatically).</summary>
    static int Copy(string src, string dst, bool top = true)
    {
        Directory.CreateDirectory(dst);
        int n = 0;
        foreach (var f in Directory.GetFiles(src)) { File.Copy(f, Path.Combine(dst, Path.GetFileName(f))); n++; }
        foreach (var d in Directory.GetDirectories(src))
        {
            if (top && Path.GetFileName(d).Equals("cache", StringComparison.OrdinalIgnoreCase)) continue;
            n += Copy(d, Path.Combine(dst, Path.GetFileName(d)), false);
        }
        return n;
    }
}
