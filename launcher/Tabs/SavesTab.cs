namespace RRXXLauncher.Tabs;

public class SavesTab : ILauncherTab
{
    readonly Panel root = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    readonly Label info = Ui.Text("");
    readonly CheckBox autoSs = new DarkCheck { Text = "Keep Super Star unlocked (popularity 100) - on by default", AutoSize = true, Margin = new Padding(0, Theme.S(4), 0, Theme.S(4)) };

    public string Title => "Saves";
    public Control View => root;

    public SavesTab()
    {
        var col = Ui.Column();
        col.Controls.Add(Ui.Text("Your saves, installed DLC and shader cache live in the saves folder next to the launcher."));
        col.Controls.Add(Ui.Text("First time? Start the game, save once in the game, then quit. Mod pack names and Super Star apply only to a save the game has written. "
            + "Your save: saves\\<profile>\\4B4E07D1\\00000001\\Game Play Data\\savegame.txt (the 0000000000000000 folder is DLC). "
            + "To use an old save, close the game and replace that file; a save anywhere else is not read."));
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(Theme.S(620), 0),
                                        Margin = new Padding(0, Theme.S(8), 0, 0) };
        row.Controls.Add(Ui.AutoBtn("Open saves folder", (_, _) => Ui.OpenFolder(Paths.Saves)));
        row.Controls.Add(Ui.Primary(Ui.AutoBtn("Back up saves now", (_, _) => Backup())));
        row.Controls.Add(Ui.AutoBtn("Open backups folder", (_, _) => Ui.OpenFolder(Paths.Backups)));
        row.Controls.Add(Ui.AutoBtn("Check / fix my save", (_, _) => CheckSaves()));
        col.Controls.Add(row);
        col.Controls.Add(Ui.Head("Super Star"));
        col.Controls.Add(Ui.Text("Super Star form unlocks at 80% popularity. With the box ticked, the launcher keeps every wrestler at 100% "
            + "each time it starts (your saves are backed up first). In character select, press left/right on \"Normal\" for \"Superstar\"."));
        autoSs.Checked = Config.Load().AutoSuperStar;
        autoSs.CheckedChanged += (_, _) =>
        {
            try { var c = Config.Load(); c.AutoSuperStar = autoSs.Checked; c.Save(); }
            catch (Exception ex) { info.Text = "Could not save the launcher settings: " + ex.Message; return; }
            if (autoSs.Checked) info.Text = ApplySuperStar();
        };
        col.Controls.Add(autoSs);
        info.ForeColor = Theme.PinkText;
        col.Controls.Add(info);
        col.Controls.Add(new Panel { Width = 1, Height = Theme.S(40), Margin = new Padding(0) }); // scroll area ignores bottom padding
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

    const int SaveSize = 873264, Block = 660, Pop = 542;
    static readonly string GameDir = Path.Combine("4B4E07D1", "00000001", "Game Play Data");

    enum SaveState { Empty, WrongSize, BadHeader, BadChecksum, Ok }

    static SaveState State(byte[] d)
    {
        if (d.Length == 0) return SaveState.Empty;
        if (d.Length != SaveSize) return SaveState.WrongSize;
        if (BE(d, 0) != d.Length) return SaveState.BadHeader;
        return BE(d, d.Length - 4) == ModPack.SaveChecksum(d) ? SaveState.Ok : SaveState.BadChecksum;
    }
    static uint BE(byte[] d, int o) => (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3]);
    static void SetChecksum(byte[] d)
    {
        uint c = ModPack.SaveChecksum(d);
        d[^4] = (byte)(c >> 24); d[^3] = (byte)(c >> 16); d[^2] = (byte)(c >> 8); d[^1] = (byte)c;
    }

    /// <summary>Lists every savegame.txt (game's own file or stray copy) and offers two fixes, each with a backup first:
    /// recompute a wrong checksum, and copy a valid stray save into an empty game save (research notes).</summary>
    void CheckSaves()
    {
        try
        {
            var files = ModPack.FindSaves(all: true);
            if (files.Count == 0) { info.Text = "No savegame.txt yet. Start the game, save once in the game, then quit."; return; }
            var lines = new List<string>();
            var own = new List<(string Path, SaveState S)>();
            var spare = new List<(string Path, DateTime T)>();
            foreach (var f in files)
            {
                var s = State(File.ReadAllBytes(f));
                bool isOwn = ModPack.IsProfileSave(f) && Path.GetDirectoryName(f)!.EndsWith(GameDir, StringComparison.OrdinalIgnoreCase);
                var rel = Path.GetRelativePath(Paths.Root, f);
                lines.Add($"{(isOwn ? "game save" : "other copy (not read by the game)")}: {rel}\n    {s}, {File.GetLastWriteTime(f):yyyy-MM-dd HH:mm}");
                if (isOwn) own.Add((f, s));
                else if (s == SaveState.Ok) spare.Add((f, File.GetLastWriteTime(f)));
            }
            if (own.Count == 0) lines.Add("No game save yet: start the game, save once in the game, then quit.");
            DarkBox.Show(string.Join("\n", lines), "RRXX Launcher - saves");

            int fixedN = 0;
            string? dest = null;
            string BackupOne(string f)
            {
                dest ??= Path.Combine(Paths.Backups, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-savefix");
                Directory.CreateDirectory(dest);
                var b = Path.Combine(dest, fixedN + "-savegame.txt");
                File.Copy(f, b);
                return b;
            }
            foreach (var (f, s) in own.Where(o => o.S == SaveState.BadChecksum))
            {
                if (DarkBox.Show($"{Path.GetRelativePath(Paths.Root, f)} has a wrong checksum (the game may reject it).\nRecompute it? A backup is made first. Close the game first.",
                        "Fix checksum", MessageBoxButtons.YesNo) != DialogResult.Yes) continue;
                var d = File.ReadAllBytes(f);
                BackupOne(f);
                SetChecksum(d);
                File.WriteAllBytes(f, d);
                fixedN++;
            }
            var newest = spare.OrderByDescending(x => x.T).FirstOrDefault();
            if (newest.Path != null)
                foreach (var (f, _) in own.Where(o => o.S == SaveState.Empty))
                {
                    if (DarkBox.Show($"The game save {Path.GetRelativePath(Paths.Root, f)} is empty, but a valid save sits where the game does not read it:\n"
                            + $"{Path.GetRelativePath(Paths.Root, newest.Path)} ({newest.T:yyyy-MM-dd HH:mm}).\nCopy it into the game save? Close the game first.",
                            "Use this save", MessageBoxButtons.YesNo) != DialogResult.Yes) continue;
                    BackupOne(f);
                    File.Copy(newest.Path, f, true);
                    fixedN++;
                }
            info.Text = fixedN > 0 ? $"Fixed {fixedN} save(s). Originals in save-backups\\{Path.GetFileName(dest)}. Mod pack save edits apply at the next launcher start."
                : "Save check done, nothing changed.";
        }
        catch (Exception ex) { info.Text = "Error: " + ex.Message; }
    }

    /// <summary>Sets popularity to 100 for blocks 0-19 and their Super Star mirror 30-49 (tools/superstar/superstar_unlock.py), fixes the checksum.</summary>

    /// <summary>Runs at launcher start while AutoSuperStar is on; quiet, never throws.</summary>
    public static void AutoApply()
    {
        try { if (Config.Load().AutoSuperStar) ApplySuperStar(); } catch { }
    }

    /// <summary>Sets popularity to 100 for blocks 0-19 and their Super Star mirror 30-49 (tools/superstar/superstar_unlock.py),
    /// fixes the checksum. Skips saves that are already at 100, not valid, or while the game runs. Backup per changed save.</summary>
    public static string ApplySuperStar()
    {
        try
        {
            if (System.Diagnostics.Process.GetProcessesByName("rumble_roses_xx").Length > 0) return "Close the game first.";
            var files = ModPack.FindSaves();
            if (files.Count == 0) return "No savegame.txt found. Start the game once and save.";
            string? dest = null;
            int done = 0, already = 0, skipped = 0;
            foreach (var f in files)
            {
                var d = File.ReadAllBytes(f);
                if (State(d) != SaveState.Ok) { skipped++; continue; }
                bool need = false;
                for (int b = 0; b < 50; b++)
                    if ((b < 20 || b >= 30) && d[b * Block + Pop] != 100) need = true;
                if (!need) { already++; continue; }
                dest ??= Path.Combine(Paths.Backups, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-superstar");
                Directory.CreateDirectory(dest);
                File.Copy(f, Path.Combine(dest, done + "-savegame.txt"));
                for (int b = 0; b < 50; b++)
                    if (b < 20 || b >= 30) d[b * Block + Pop] = 100;
                SetChecksum(d);
                ModPack.WriteAtomic(f, d); // a half-written save would be lost; the game reads it whole
                done++;
            }
            return $"Super Star unlocked in {done} save(s)" + (already > 0 ? $", {already} already unlocked" : "")
                + (skipped > 0 ? $", {skipped} skipped (empty or not a valid save)" : "")
                + (dest != null ? $". Originals in save-backups\\{Path.GetFileName(dest)}" : ".");
        }
        catch (Exception ex) { return "Error: " + ex.Message; }
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
