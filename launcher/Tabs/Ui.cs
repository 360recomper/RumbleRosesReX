namespace RRXXLauncher.Tabs;

static class Ui
{
    public static Button Btn(string text, EventHandler click, int w = 150)
    {
        var b = new Button { Text = text, Width = w, Height = 30, Margin = new Padding(0, 4, 8, 4) };
        b.Click += click;
        return b;
    }

    public static FlowLayoutPanel Column() => new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        Padding = new Padding(16), AutoScroll = true,
    };

    public static Label Text(string t, bool bold = false, int w = 560) => new()
    {
        Text = t, AutoSize = true, MaximumSize = new Size(w, 0), Margin = new Padding(0, 4, 0, 4),
        Font = bold ? new Font(SystemFonts.DefaultFont, FontStyle.Bold) : SystemFonts.DefaultFont,
    };

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }
}
