namespace RRXXLauncher.Tabs;

/// <summary>Writes game\RumbleRosesReX.toml. gpu_backend is deliberately never written (the F4 menu owns it).</summary>
public class SettingsTab : ILauncherTab
{
    readonly Panel root = new() { Dock = DockStyle.Fill };
    readonly ComboBox scale = Combo("1 - 720p", "2", "3 - 4K-class", "4", "5", "6 - 8K");
    readonly ComboBox bloom = Combo("Off", "Reduced", "Original glow");
    readonly CheckBox blur = new() { Text = "Background blur (on = like the original)", AutoSize = true };
    readonly ComboBox aniso = Combo("Off", "4x", "16x");
    readonly CheckBox unlock = new() { Text = "Unlock all (costumes, items, characters)", AutoSize = true };
    readonly CheckBox money = new() { Text = "Infinite money", AutoSize = true };
    readonly Label saved = Ui.Text("");

    static readonly int[] AnisoValues = { 0, 3, 5 };

    public string Title => "Settings";
    public Control View => root;

    static ComboBox Combo(params string[] items)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        c.Items.AddRange(items);
        return c;
    }

    public SettingsTab()
    {
        var col = Ui.Column();
        void Row(string label, Control c)
        {
            col.Controls.Add(Ui.Text(label, true));
            col.Controls.Add(c);
        }
        Row("Render scale (higher = sharper, slower)", scale);
        Row("Bloom", bloom);
        Row("Anisotropic filtering", aniso);
        col.Controls.Add(blur);
        col.Controls.Add(Ui.Text("Cheats (off by default)", true));
        col.Controls.Add(unlock);
        col.Controls.Add(money);
        col.Controls.Add(Ui.Btn("Save settings", (_, _) => Save()));
        col.Controls.Add(saved);
        col.Controls.Add(Ui.Text("Graphics backend, sharpening and the Mods switch are in-game too: press F4.\nSettings are stored in game\\RumbleRosesReX.toml."));
        root.Controls.Add(col);
    }

    public void OnShown()
    {
        var t = new Toml();
        scale.SelectedIndex = Math.Clamp(t.GetInt("resolution_scale", 2), 1, 6) - 1;
        bloom.SelectedIndex = Math.Clamp(t.GetInt("bloom", 2), 0, 2);
        blur.Checked = t.GetBool("blur", true);
        int i = Array.IndexOf(AnisoValues, t.GetInt("anisotropic_override", 5));
        aniso.SelectedIndex = i < 0 ? 2 : i;
        unlock.Checked = t.GetBool("unlock_all", false);
        money.Checked = t.GetBool("infinite_money", false);
        saved.Text = "";
    }

    void Save()
    {
        var t = new Toml();
        t.Set("resolution_scale", scale.SelectedIndex + 1);
        t.Set("bloom", bloom.SelectedIndex);
        t.Set("blur", blur.Checked);
        t.Set("anisotropic_override", AnisoValues[aniso.SelectedIndex]);
        t.Set("unlock_all", unlock.Checked);
        t.Set("infinite_money", money.Checked);
        t.Save();
        saved.Text = "Saved.";
    }
}
