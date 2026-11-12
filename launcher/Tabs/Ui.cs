namespace RRXXLauncher.Tabs;

static class Ui
{
    /// <summary>Button with a minimum width; grows to fit its text (fixed widths clip at high DPI).</summary>
    public static FlatButton Btn(string text, EventHandler click, int w = 150)
    {
        var b = new FlatButton
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly, MinimumSize = new Size(Theme.S(w), Theme.S(36)),
            Padding = new Padding(Theme.S(12), 0, Theme.S(12), 0), Margin = new Padding(0, Theme.S(5), Theme.S(10), Theme.S(5)),
        };
        b.Click += click;
        return b;
    }

    /// <summary>A button that grows to fit its text.</summary>
    public static FlatButton AutoBtn(string text, EventHandler click) => Btn(text, click, 80);

    /// <summary>Marks a button as the page's main action (pink).</summary>
    public static FlatButton Primary(FlatButton b) { b.Kind = BtnKind.Primary; return b; }

    public static FlowLayoutPanel Column() => new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        Padding = new Padding(Theme.S(28), Theme.S(4), Theme.S(28), Theme.S(24)), AutoScroll = true, BackColor = Theme.Bg,
    };

    public static Label Text(string t, bool bold = false, int w = 600) => new()
    {
        Text = t, AutoSize = true, MaximumSize = new Size(Theme.S(w), 0), Margin = new Padding(0, Theme.S(4), 0, Theme.S(4)),
        Font = bold ? Theme.Semi : Theme.Body, ForeColor = bold ? Theme.Ink : Theme.Muted, UseMnemonic = false,
    };

    /// <summary>Section heading inside a page (pink, with space above).</summary>
    public static Label Head(string t) => new()
    {
        Text = t, AutoSize = true, Margin = new Padding(0, Theme.S(16), 0, Theme.S(4)), Font = Theme.Font(12f, true),
        ForeColor = Theme.PinkText, UseMnemonic = false,
    };

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }
}
