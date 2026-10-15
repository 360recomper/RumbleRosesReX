namespace RRXXLauncher.Tabs;

public class ModsTab : ILauncherTab
{
    readonly Panel root = new() { Dock = DockStyle.Fill };
    readonly CheckBox master = new() { Text = "Mods enabled (turns off the replacement-files overlay and all costumes)", AutoSize = true };
    readonly CheckedListBox list = new() { Width = 540, Height = 200, CheckOnClick = true };
    readonly Label info = Ui.Text("");
    List<Costumes.ModFolder> shown = new();
    Config cfg = new();

    public string Title => "Mods";
    public Control View => root;

    public ModsTab()
    {
        var col = Ui.Column();
        col.Controls.Add(master);
        col.Controls.Add(Ui.Text("Costumes", true));
        col.Controls.Add(Ui.Text("Costume folders from mods\\costumes\\ (each needs a cos.dat). Every one gets its own free slot and shows up under Others > Locker Room."));
        col.Controls.Add(list);
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        row.Controls.Add(Ui.Btn("Apply", (_, _) => Apply(), 100));
        row.Controls.Add(Ui.Btn("Open mods folder", (_, _) => Ui.OpenFolder(Paths.Mods)));
        row.Controls.Add(Ui.Btn("Open costumes folder", (_, _) => Ui.OpenFolder(Paths.CostumeMods)));
        col.Controls.Add(row);
        col.Controls.Add(info);
        root.Controls.Add(col);
    }

    public void OnShown()
    {
        cfg = Config.Load();
        master.Checked = new Toml().GetBool("mods_enabled", true);
        shown = Costumes.Scan();
        list.Items.Clear();
        foreach (var m in shown)
        {
            var on = !cfg.Costumes.TryGetValue(m.Name, out var st) || st.Enabled;
            var slot = st is { Id: not 0 } ? $"  (costume {st.Id})" : "";
            list.Items.Add(m.Name + slot, on);
        }
        info.Text = shown.Count == 0 ? "No costume mods found." : "";
    }

    void Apply()
    {
        try
        {
            for (int i = 0; i < shown.Count; i++)
            {
                if (!cfg.Costumes.TryGetValue(shown[i].Name, out var st))
                    cfg.Costumes[shown[i].Name] = st = new CostumeState();
                st.Enabled = list.GetItemChecked(i);
            }
            var t = new Toml();
            t.Set("mods_enabled", master.Checked);
            t.Save();
            var log = Costumes.Apply(cfg, master.Checked);
            OnShown();
            info.Text = log.Count == 0 ? "Applied." : string.Join("\n", log);
        }
        catch (Exception ex) { info.Text = "Error: " + ex.Message; }
    }
}
