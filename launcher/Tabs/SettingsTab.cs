namespace RRXXLauncher.Tabs;

/// <summary>Writes game\RumbleRosesReX.toml. gpu_backend is deliberately never written (the F4 menu owns it).</summary>
public class SettingsTab : ILauncherTab
{
    readonly Panel root = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    readonly ComboBox scale = Combo("1 - 720p", "2", "3 - 4K-class", "4", "5", "6 - 8K");
    readonly ComboBox upscale = Combo("Off (bilinear)", "FSR (sharper at low scale)", "CAS (sharpen only)");
    readonly ComboBox bloom = Combo("Off", "Reduced", "Original glow");
    readonly CheckBox blur = new DarkCheck { Text = "Background blur (on = like the original)", AutoSize = true };
    readonly ComboBox aniso = Combo("Off", "4x", "16x");
    readonly CheckBox unlock = new DarkCheck { Text = "Unlock all (costumes, items, characters)", AutoSize = true };
    readonly CheckBox money = new DarkCheck { Text = "Infinite money", AutoSize = true };
    readonly Label saved = Ui.Text("");
    // Native renderer (RR-NR-11): shown only when rumble_roses_xx.exe has it (NativeRenderer.Available).
    readonly FlowLayoutPanel nativeBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0), BackColor = Theme.Bg };
    readonly ComboBox renderer = Combo("Native (default)", "Emulated (fallback)");
    readonly Label rendererHint = Ui.Text("");
    readonly ComboBox gpu = new DarkCombo { Width = Theme.S(380) };
    readonly List<string> gpuValues = new();
    readonly Button rebuild;
    bool buildingCache;

    static readonly int[] AnisoValues = { 0, 3, 5 };
    static readonly string[] EffectValues = { "bilinear", "fsr", "cas" };
    string? loadedEffect;

    public string Title => "Settings";
    public Control View => root;

    static ComboBox Combo(params string[] items)
    {
        var c = new DarkCombo { Width = Theme.S(280) };
        c.Items.AddRange(items);
        return c;
    }

    public SettingsTab()
    {
        var col = Ui.Column();
        saved.ForeColor = Theme.PinkText;
        void Row(string label, Control c)
        {
            col.Controls.Add(Ui.Text(label, true));
            c.Margin = new Padding(0, Theme.S(2), 0, Theme.S(10));
            col.Controls.Add(c);
        }
        nativeBox.Controls.Add(Ui.Head("Renderer"));
        renderer.Margin = gpu.Margin = new Padding(0, Theme.S(2), 0, Theme.S(6));
        nativeBox.Controls.Add(renderer);
        nativeBox.Controls.Add(rendererHint);
        nativeBox.Controls.Add(Ui.Text("Graphics card (native renderer only)", true));
        nativeBox.Controls.Add(gpu);
        nativeBox.Controls.Add(Ui.Text("Automatic picks the dedicated (high-performance) graphics card on laptops that also have built-in graphics. Pick a card by name to override it."));
        rebuild = Ui.AutoBtn("Rebuild shader cache", async (_, _) => await BuildCache(true));
        nativeBox.Controls.Add(rebuild);
        col.Controls.Add(nativeBox);
        renderer.SelectedIndexChanged += async (_, _) =>
        {
            if (loading || buildingCache) return;
            bool native = renderer.SelectedIndex == 0;
            try { NativeRenderer.SetChoice(native); }
            catch (Exception ex) { saved.Text = "Could not save: " + ex.Message; return; }
            saved.Text = native ? "Saved: Native renderer." : "Saved: Emulated renderer.";
            if (native) await BuildCache(false);
        };
        gpu.SelectedIndexChanged += (_, _) =>
        {
            if (loading || gpu.SelectedIndex < 0) return;
            try { var t = new Toml(); t.SetStr(Gpu.TomlKey, gpuValues[gpu.SelectedIndex]); t.Save(); saved.Text = "Saved. Restart the game to use it."; }
            catch (Exception ex) { saved.Text = "Could not save: " + ex.Message; }
        };
        col.Controls.Add(Ui.Head("Graphics"));
        Row("Render scale (higher = sharper, slower; 1 = fastest, for laptops / older GPUs)", scale);
        Row("Upscaler", upscale);
        Row("Bloom", bloom);
        Row("Anisotropic filtering", aniso);
        col.Controls.Add(blur);
        var tip = DarkTip.Create();
        var low = Ui.AutoBtn("Low-end PC preset", (_, _) =>
        {
            scale.SelectedIndex = 0;
            upscale.SelectedIndex = 1;
            aniso.SelectedIndex = 1;
            Save();
            if (saved.Text == "Saved.") saved.Text = "Low-end preset applied: scale 1, FSR upscaler, 4x filtering. Restart the game to use it.";
        });
        tip.SetToolTip(low, "Render scale 1 + FSR upscaling + 4x filtering. Much lighter on the GPU; bloom and blur are left as they are.");
        col.Controls.Add(low);
        col.Controls.Add(Ui.Head("Cheats (off by default)"));
        col.Controls.Add(unlock);
        col.Controls.Add(money);
        // Cheats apply on tick: ticking then pressing Play used to lose them (BUG-MONEY).
        unlock.CheckedChanged += (_, _) => { if (!loading) Save(); };
        money.CheckedChanged += (_, _) => { if (!loading) Save(); };
        col.Controls.Add(Ui.Primary(Ui.Btn("Save settings", (_, _) => Save())));
        col.Controls.Add(saved);
        col.Controls.Add(Ui.Head("Bug report"));
        col.Controls.Add(Ui.Text("Something wrong? Collect the game and launcher logs (no saves) into one zip on your Desktop to attach to a bug report."));
        col.Controls.Add(Ui.Btn("Save logs for a bug report", (_, _) =>
        {
            try
            {
                var z = BugReport.Create();
                saved.Text = "Saved to your Desktop: " + Path.GetFileName(z);
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + z + "\"");
            }
            catch (Exception ex) { saved.Text = "Could not save logs: " + ex.Message; }
        }));
        col.Controls.Add(Ui.Text("Graphics backend, sharpening and the Mods switch are in-game too: press F4 (the graphics backend applies to the Emulated renderer only).\nSettings are stored in game\\RumbleRosesReX.toml."));
        root.Controls.Add(col);
    }

    bool loading;

    public void OnShown()
    {
        loading = true;
        var t = new Toml();
        scale.SelectedIndex = Math.Clamp(t.GetInt("resolution_scale", 2), 1, 6) - 1;
        bloom.SelectedIndex = Math.Clamp(t.GetInt("bloom", 2), 0, 2);
        loadedEffect = t.Get("present_effect")?.Trim('"');
        int e = Array.IndexOf(EffectValues, loadedEffect ?? "bilinear");
        upscale.SelectedIndex = e < 0 ? 0 : e;
        blur.Checked = t.GetBool("blur", true);
        int i = Array.IndexOf(AnisoValues, t.GetInt("anisotropic_override", 5));
        aniso.SelectedIndex = i < 0 ? 2 : i;
        unlock.Checked = t.GetBool("unlock_all", false);
        money.Checked = t.GetBool("infinite_money", false);
        saved.Text = "";
        bool native = NativeRenderer.Available;
        nativeBox.Visible = native;
        if (native)
        {
            renderer.SelectedIndex = NativeRenderer.Chosen ? 0 : 1;
            renderer.Enabled = rebuild.Enabled = !buildingCache;
            rendererHint.Text = !ShaderCache.ToolsPresent
                ? "The shader cache tools are missing from the game folder, so Native can't be used (re-install this release)."
                : "Native draws the game directly with Direct3D 12 and ignores the F4 graphics backend. It needs a shader cache built from your game files (once, and again after an update; Play does it automatically). If Native can't start on your PC, the game restarts on Emulated. Emulated = the older Xenos GPU emulation (uses the F4 graphics backend).";
            var savedGpu = t.GetStr(Gpu.TomlKey, "");
            gpu.Items.Clear(); gpuValues.Clear();
            gpu.Items.Add("Automatic (default)"); gpuValues.Add("");
            foreach (var n in Gpu.AdapterNames()) { gpu.Items.Add(n); gpuValues.Add(n); }
            if (savedGpu.Length > 0 && !gpuValues.Contains(savedGpu)) { gpu.Items.Add(savedGpu + " (not found)"); gpuValues.Add(savedGpu); }
            gpu.SelectedIndex = Math.Max(0, gpuValues.IndexOf(savedGpu));
        }
        loading = false;
    }

    /// <summary>Native was just picked (or Rebuild pressed). Without game files there's nothing to build from yet: Play
    /// builds the cache later. A failed build puts the dropdown back on Emulated.</summary>
    async Task BuildCache(bool force)
    {
        if (!force && ShaderCache.Current) { saved.Text = "Saved: Native renderer. The shader cache is ready."; return; }
        if (!Paths.IsInstalled) { saved.Text = "Saved. The shader cache gets built when you press Play after setting up the game files (Setup tab)."; return; }
        buildingCache = true;
        renderer.Enabled = rebuild.Enabled = false;
        saved.Text = "Building the native renderer's shader cache from your game files. This only happens once...";
        var p = new Progress<string>(s => saved.Text = "Building the shader cache: " + s);
        ShaderCache.Result r;
        try { r = await Task.Run(() => force ? ShaderCache.Build(Paths.Assets, p) : ShaderCache.EnsurePresent(Paths.Assets, p)); }
        catch (Exception ex) { r = new ShaderCache.Result(false, ex.Message); }
        buildingCache = false;
        string keep;
        if (r.Ok) keep = "Shader cache built.";
        else if (NativeRenderer.Chosen && !r.ThisRunOnly)
        {
            NativeRenderer.UseEmulated();
            DarkBox.Show(NativeRenderer.FallbackText(r.Message), "RRXX Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            keep = "Renderer set back to Emulated: the shader cache could not be built.";
        }
        else keep = "The shader cache could not be built: " + r.Message;
        OnShown();
        saved.Text = keep;
    }

    void Save()
    {
        try { SaveToml(); saved.Text = "Saved."; }
        catch (Exception ex) { saved.Text = "Could not save the settings: " + ex.Message; }
    }

    void SaveToml()
    {
        var t = new Toml();
        t.Set("resolution_scale", scale.SelectedIndex + 1);
        t.Set("bloom", bloom.SelectedIndex);
        t.SetStr("readback_resolve", "some"); // photo shoot needs resolves copied to guest memory (none = garbage photos)
        // Keep an unknown value (fsr2/fsr3 set in F4) unless the user picked something here.
        if (Array.IndexOf(EffectValues, loadedEffect ?? "bilinear") >= 0 || upscale.SelectedIndex != 0)
            t.Set("present_effect", "\"" + EffectValues[upscale.SelectedIndex] + "\"");
        loadedEffect = EffectValues[upscale.SelectedIndex];
        t.Set("blur", blur.Checked);
        t.Set("anisotropic_override", AnisoValues[aniso.SelectedIndex]);
        t.Set("unlock_all", unlock.Checked);
        t.Set("infinite_money", money.Checked);
        t.Save();
    }
}
