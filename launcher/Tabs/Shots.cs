using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RRXXLauncher.Tabs;

/// <summary>
/// Hidden option for UI checks: RRXX Launcher.exe --screenshot-all &lt;dir&gt; [100|150]. Opens the launcher window,
/// shows every page and dialog in turn and saves a PNG of each (screen capture of the window). The number simulates
/// that Windows display scale on any PC (Theme.Sim). Never starts the game and writes nothing but the PNGs.
/// Everything is rendered off-screen (-32000,-32000), never activated, no taskbar button; PNGs come from PrintWindow.
/// </summary>
static class Shots
{
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    static readonly Point Off = new(-32000, -32000);

    public static int Run(string dir, int pct)
    {
        Directory.CreateDirectory(dir);
        int rc = 0;
        var form = new MainForm(false, preview: true) { StartPosition = FormStartPosition.Manual, ShowInTaskbar = false, Location = Off };
        form.Shown += async (_, _) =>
        {
            try { await Go(form, dir, pct); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(dir, $"{pct}-error.txt"), ex.ToString()); rc = 1; }
            form.Close();
        };
        Application.Run(form);
        return rc;
    }

    static int n;

    static async Task Go(MainForm form, string dir, int pct)
    {
        string P(string name) => Path.Combine(dir, $"{pct}-{++n:00}-{name}.png");
        var nav = form.Nav;
        int attire = -1;
        // the first off-screen shot catches the sidebar unpainted (white): take one and throw it away
        await Settle(900);
        var warm = Path.Combine(dir, $"{pct}-warmup.png");
        Capture(form, warm);
        File.Delete(warm);
        await Settle();
        for (int i = 0; i < form.Tabs.Count; i++)
        {
            var t = form.Tabs[i];
            if (t is AttireTab) { attire = i; continue; }
            nav.SelectedIndex = i;
            await Settle();
            var slug = t.Title.ToLowerInvariant().Replace(' ', '-');
            Capture(form, P(slug));
            if (Scroller(t.View) is { } sc)
            {
                sc.AutoScrollPosition = new Point(0, sc.DisplayRectangle.Height);
                await Settle();
                Capture(form, P(slug + "-bottom"));
                sc.AutoScrollPosition = Point.Empty;
            }
            if (t is PlayTab play) { play.PreviewUpdate(); await Settle(); Capture(form, P("play-update")); }
            if (t is SetupTab setup) { setup.PreviewBusy(); await Settle(); Capture(form, P("setup-importing")); }
            // Open drop-down lists are popups Windows keeps on a monitor, so they are not shot off-screen; their
            // numbers stay reserved for settings-renderer-open / settings-gpu-open.png from the 11-08 on-screen run.
            if (t is SettingsTab) n += 2;
        }
        if (attire >= 0) { nav.SelectedIndex = attire; await Settle(900); Capture(form, P("attire")); }

        using (var d = CacheBuildDialog.Preview())
        {
            ShowOff(d);
            await Settle(900);
            Capture(d, P("dialog-shader-cache"));
            d.Close();
        }

        var boxes = new (string Name, string Text, string Caption, MessageBoxButtons B, MessageBoxIcon I)[]
        {
            ("box-native-failed", NativeRenderer.DeviceFailedText, "RRXX Launcher - native renderer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning),
            ("box-cache-fallback", NativeRenderer.FallbackText("rrxx_shader_cache.exe stopped with exit code 3: a shader container could not be read."), "RRXX Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning),
            ("box-update", "Download v9.9 (212 MB) and replace the changed program files?\n\nYour settings (game\\RumbleRosesReX.toml), saves, mods and game files are not touched. Replaced files are kept in update-backups.", "Update", MessageBoxButtons.OKCancel, MessageBoxIcon.Question),
            ("box-uninstall", "Uninstall \"Sample pack\"?\n\nIts files are removed, anything it replaced is put back, its game settings are restored and its save slots get their old contents back (the rest of your save stays as it is).", "Uninstall mod pack", MessageBoxButtons.YesNo, MessageBoxIcon.Question),
            ("box-saves", string.Join("\n", Enumerable.Range(1, 3).Select(k => $"game save: saves\\E000000{k}F00000\\4B4E07D1\\00000001\\Game Play Data\\savegame.txt\n    ok, 2026-11-0{k} 21:4{k}"))
                + "\nother copy (not read by the game): saves\\savegame.txt\n    empty, 2026-10-30 18:02", "RRXX Launcher - saves", MessageBoxButtons.OK, MessageBoxIcon.None),
            ("box-long", string.Join("\n\n", Enumerable.Range(1, 14).Select(k => $"Mod pack {k}: the save edit was applied to profile {k} (created wrestlers 1-30 and movesets 100-159).")),
                "RRXX Launcher - mod pack save edit", MessageBoxButtons.OK, MessageBoxIcon.Information),
        };
        foreach (var b in boxes)
        {
            using var f = DarkBox.Create(b.Text, b.Caption, b.B, b.I);
            ShowOff(f);
            await Settle();
            Capture(f, P(b.Name));
            f.Close();
        }
    }

    /// <summary>Shown off-screen without taking focus from whatever the user is doing.</summary>
    static void ShowOff(Form d)
    {
        d.ShowInTaskbar = false;
        d.TopMost = false;
        d.StartPosition = FormStartPosition.Manual;
        d.Location = Off;
        ShowWindow(d.Handle, 4);   // SW_SHOWNOACTIVATE
    }

    static async Task Settle(int ms = 600)
    {
        Application.DoEvents();
        await Task.Delay(ms);
        Application.DoEvents();
    }

    static IEnumerable<Control> All(Control c)
    {
        foreach (Control k in c.Controls) { yield return k; foreach (var x in All(k)) yield return x; }
    }

    /// <summary>The page's scrolling column when it has more content than fits.</summary>
    static ScrollableControl? Scroller(Control view) =>
        new[] { view }.Concat(All(view)).OfType<ScrollableControl>().FirstOrDefault(s => s.AutoScroll && s.Visible && s.VerticalScroll.Visible);

    static void Capture(Form f, string path)
    {
        Application.DoEvents();
        GetWindowRect(f.Handle, out var w);
        using var full = new Bitmap(w.R - w.L, w.B - w.T, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(full))
        {
            var hdc = g.GetHdc();
            try { PrintWindow(f.Handle, hdc, 2); }   // PW_RENDERFULLCONTENT: works for off-screen windows
            finally { g.ReleaseHdc(hdc); }
        }
        // crop the invisible resize borders to the visible frame (DWMWA_EXTENDED_FRAME_BOUNDS)
        var crop = new Rectangle(0, 0, full.Width, full.Height);
        if (DwmGetWindowAttribute(f.Handle, 9, out var rc, Marshal.SizeOf<RECT>()) == 0)
        {
            var vis = Rectangle.Intersect(crop, new Rectangle(rc.L - w.L, rc.T - w.T, rc.R - rc.L, rc.B - rc.T));
            if (vis.Width > 0 && vis.Height > 0) crop = vis;
        }
        using var bmp = full.Clone(crop, PixelFormat.Format32bppArgb);
        bmp.Save(path, ImageFormat.Png);
    }
}
