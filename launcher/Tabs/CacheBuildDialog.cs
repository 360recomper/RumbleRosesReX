namespace RRXXLauncher.Tabs;

/// <summary>A small modal window while the native renderer's shader cache is built (Play, the skip-launcher start,
/// Settings). Shows the tool's last output line; closes itself when the build ends.</summary>
sealed class CacheBuildDialog : Form
{
    readonly Label line = new()
    {
        Text = "Starting...", AutoSize = false, AutoEllipsis = true, UseMnemonic = false, Font = Theme.Font(9f), ForeColor = Theme.Muted,
        Size = new Size(Theme.S(440), Theme.S(22)), Margin = new Padding(0, Theme.S(2), 0, 0),
    };
    string? failed;

    CacheBuildDialog(bool preview = false)
    {
        Text = "Rumble Roses XX - native renderer";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ControlBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        Theme.DarkWindow(this);
        var col = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Theme.Bg,
            Padding = new Padding(Theme.S(24), Theme.S(18), Theme.S(24), Theme.S(18)), Location = Point.Empty,
        };
        col.Controls.Add(Ui.Head("Preparing the native renderer"));
        col.Controls[0].Margin = new Padding(0, 0, 0, Theme.S(6));
        col.Controls.Add(Ui.Text("Building the shader cache from your game files. This only happens once (and again after an update).", false, 440));
        col.Controls.Add(new PinkBar { Style = ProgressBarStyle.Marquee, Width = Theme.S(440), Height = Theme.S(14), Margin = new Padding(0, Theme.S(12), 0, Theme.S(6)) });
        col.Controls.Add(line);
        Controls.Add(col);
        ClientSize = col.GetPreferredSize(Size.Empty);
        if (preview) { line.Text = "317 containers (pass 2 of 3: compiling pixel shaders)"; return; }
        Shown += async (_, _) =>
        {
            var p = new Progress<string>(s => line.Text = s.Length > 90 ? s[..90] + "..." : s);
            try { failed = await Task.Run(() => NativeRenderer.PrepareCache(p)); }
            catch (Exception ex) { failed = ex.Message; NativeRenderer.UseEmulated(); }
            Close();
        };
    }

    /// <summary>--screenshot-all: the dialog as during a build, without building anything.</summary>
    internal static Form Preview() => new CacheBuildDialog(true);

    /// <summary>Builds the cache if it isn't current. Null = ready; else the reason (the toml is then back on Emulated).</summary>
    public static string? Run(IWin32Window? owner = null)
    {
        if (ShaderCache.Current) return null;
        using var d = new CacheBuildDialog();
        d.ShowDialog(owner);
        return d.failed;
    }
}
