using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace RRXXLauncher.Tabs;

// LAUNCHER-UI: the themed controls. Drop-in subclasses of the stock WinForms controls (same events and properties), so
// the tabs' logic is unchanged; only painting differs.

public enum BtnKind { Normal, Primary, Play }

/// <summary>Flat rounded button with hover / pressed / focus states.</summary>
public class FlatButton : Button
{
    bool hover, down;
    public BtnKind Kind { get; set; }

    public FlatButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = Theme.Surface;
        ForeColor = Theme.Ink;
        Cursor = Cursors.Hand;
        Font = Theme.Semi;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.BackOf(this));
        float glow = Kind == BtnKind.Play ? Theme.S(7) : 0;
        var r = new RectangleF(glow + 0.5f, glow + 0.5f, Width - 2 * glow - 1.5f, Height - 2 * glow - 1.5f);
        float rad = Kind == BtnKind.Play ? r.Height / 2 : Theme.S(7);
        bool on = Enabled;

        if (Kind == BtnKind.Play)
        {
            if (on)
                for (int i = 1; i <= 6; i++)   // soft pink glow around the pill
                {
                    float gr = glow * i / 6f;
                    using var gp = Theme.Round(RectangleF.Inflate(r, gr, gr), rad + gr);
                    using var gb = new SolidBrush(Color.FromArgb((hover ? 30 : 18) - i * 2, Theme.Pink));
                    g.FillPath(gb, gp);
                }
            using var path = Theme.Round(r, rad);
            Color a = !on ? Theme.Surface2 : down ? Theme.PrimaryDown : hover ? Theme.PrimaryHot : Theme.Primary;
            Color b = !on ? Theme.Surface2 : down ? Theme.Mix(Theme.Violet, Color.Black, 0.2f) : hover ? Theme.Mix(Theme.Violet, Theme.Pink, 0.25f) : Theme.Violet;
            using (var br = new LinearGradientBrush(r, a, b, LinearGradientMode.Horizontal)) g.FillPath(br, path);
            if (on)
            {
                using var shine = new LinearGradientBrush(r, Color.FromArgb(60, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical);
                var top = new RectangleF(r.X, r.Y, r.Width, r.Height * 0.5f);
                using var tp = Theme.Round(top, rad);
                g.FillPath(shine, tp);
            }
            if (Focused && ShowFocusCues) using (var fp = new Pen(Color.FromArgb(200, Color.White), Theme.S(1.5f))) g.DrawPath(fp, path);
            DrawPlayText(g, r, on);
            return;
        }

        using (var path = Theme.Round(r, rad))
        {
            Color fill, border;
            if (Kind == BtnKind.Primary)
            {
                fill = !on ? Theme.Surface : down ? Theme.PrimaryDown : hover ? Theme.PrimaryHot : Theme.Primary;
                border = !on ? Theme.Line : fill;
            }
            else
            {
                fill = !on ? Theme.Mix(Theme.Surface, Theme.Bg, 0.5f) : down ? Theme.Mix(Theme.Surface2, Theme.Primary, 0.25f) : hover ? Theme.Surface2 : Theme.Surface;
                border = !on ? Theme.Mix(Theme.Line, Theme.Bg, 0.4f) : hover || down ? Theme.Pink : Theme.Line;
            }
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            using (var p = new Pen(border, Theme.S(1))) g.DrawPath(p, path);
            if (Focused && ShowFocusCues && on)
            {
                var fr = RectangleF.Inflate(r, -Theme.S(2.5f), -Theme.S(2.5f));
                using var fpath = Theme.Round(fr, rad - Theme.S(2));
                using var fp = new Pen(Color.FromArgb(170, Theme.Ink), Theme.S(1)) { DashStyle = DashStyle.Dot };
                g.DrawPath(fp, fpath);
            }
        }
        var fore = !on ? Theme.Faint : Kind == BtnKind.Primary ? Color.White : ForeColor;
        TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.HidePrefix);
    }

    /// <summary>"Play (Native renderer)" -> big PLAY with a play glyph, "Native renderer" small under it.</summary>
    void DrawPlayText(Graphics g, RectangleF r, bool on)
    {
        string main = Text, sub = "";
        int k = Text.IndexOf(" (", StringComparison.Ordinal);
        if (k > 0 && Text.EndsWith(')')) { main = Text[..k]; sub = Text[(k + 2)..^1]; }
        main = main.ToUpperInvariant();
        var fore = on ? Color.White : Theme.Faint;
        var big = Theme.Heavy(17f);
        var small = Theme.Font(8.5f, true);
        var icon = Theme.Icon(15f);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var sf = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
        var szMain = g.MeasureString(main, big, PointF.Empty, sf);
        var szIcon = g.MeasureString("\uE768", icon, PointF.Empty, sf);
        var szSub = sub.Length > 0 ? g.MeasureString(sub.ToUpperInvariant(), small, PointF.Empty, sf) : SizeF.Empty;
        float gap = Theme.S(10);
        float w = szIcon.Width + gap + Math.Max(szMain.Width, szSub.Width);
        float h = szMain.Height + (sub.Length > 0 ? szSub.Height * 0.9f : 0);
        float x = r.X + (r.Width - w) / 2, y = r.Y + (r.Height - h) / 2;
        using var b = new SolidBrush(fore);
        g.DrawString("\uE768", icon, b, x, r.Y + (r.Height - szIcon.Height) / 2, sf);
        x += szIcon.Width + gap;
        g.DrawString(main, big, b, x, y, sf);
        if (sub.Length > 0)
        {
            using var sb = new SolidBrush(on ? Color.FromArgb(225, Color.White) : Theme.Faint);
            g.DrawString(sub.ToUpperInvariant(), small, sb, x + Theme.S(1), y + szMain.Height * 0.92f, sf);
        }
    }
}

/// <summary>Check box with a rounded pink box.</summary>
public class DarkCheck : CheckBox
{
    bool hover;
    int Box => Theme.S(18);
    int Gap => Theme.S(9);

    public DarkCheck()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Padding = Padding.Empty;
    }

    public override Size GetPreferredSize(Size proposed)
    {
        var t = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        return new Size(Box + Gap + t.Width + Theme.S(4), Math.Max(Box, t.Height) + Theme.S(8));
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var back = Theme.BackOf(this);
        g.Clear(back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new RectangleF(0.5f, (Height - Box) / 2f, Box - 1, Box - 1);
        using (var path = Theme.Round(box, Theme.S(5)))
        {
            if (Checked)
            {
                using var b = new SolidBrush(!Enabled ? Theme.Line : hover ? Theme.PrimaryHot : Theme.Primary);
                g.FillPath(b, path);
                using var pen = new Pen(Color.White, Theme.S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(pen, new[]
                {
                    new PointF(box.X + box.Width * 0.24f, box.Y + box.Height * 0.52f),
                    new PointF(box.X + box.Width * 0.43f, box.Y + box.Height * 0.70f),
                    new PointF(box.X + box.Width * 0.77f, box.Y + box.Height * 0.32f),
                });
            }
            else
            {
                using var b = new SolidBrush(Theme.Surface);
                g.FillPath(b, path);
                using var p = new Pen(!Enabled ? Theme.Line : hover ? Theme.Pink : Theme.Muted, Theme.S(1.4f));
                g.DrawPath(p, path);
            }
            if (Focused && ShowFocusCues)
            {
                using var fp = Theme.Round(RectangleF.Inflate(box, Theme.S(2.5f), Theme.S(2.5f)), Theme.S(7));
                using var pen = new Pen(Color.FromArgb(160, Theme.Pink), Theme.S(1));
                g.DrawPath(pen, fp);
            }
        }
        var tr = new Rectangle(Box + Gap, 0, Width - Box - Gap, Height);
        TextRenderer.DrawText(g, Text, Font, tr, Enabled ? ForeColor : Theme.Faint,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

/// <summary>Drop-down list: the closed box is painted here (rounded, chevron); the open list is owner-drawn.</summary>
public class DarkCombo : ComboBox
{
    bool hover;

    public DarkCombo()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.Surface;
        ForeColor = Theme.Ink;
        ItemHeight = Theme.S(24);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnDropDownClosed(EventArgs e) { Invalidate(); base.OnDropDownClosed(e); }
    protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(sel ? Theme.Primary : Theme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
        var r = new Rectangle(e.Bounds.X + Theme.S(8), e.Bounds.Y, e.Bounds.Width - Theme.S(10), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, r, sel ? Color.White : Theme.Ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PAINTSTRUCT { public IntPtr hdc; public int fErase; public int l, t, r, b; public int fRestore, fIncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved; }
    [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);

    protected override void WndProc(ref Message m)
    {
        const int WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014;
        if (m.Msg == WM_ERASEBKGND) { m.Result = 1; return; }
        if (m.Msg == WM_PAINT && m.WParam == IntPtr.Zero)
        {
            var hdc = BeginPaint(Handle, out var ps);
            try
            {
                using var bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
                using (var g = Graphics.FromImage(bmp)) PaintBox(g);
                using var screen = Graphics.FromHdc(hdc);
                screen.DrawImageUnscaled(bmp, 0, 0);
            }
            finally { EndPaint(Handle, ref ps); }
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    void PaintBox(Graphics g)
    {
        g.Clear(Theme.BackOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.Round(r, Theme.S(7));
        using (var b = new SolidBrush(!Enabled ? Theme.Mix(Theme.Surface, Theme.Bg, 0.5f) : hover || DroppedDown ? Theme.Surface2 : Theme.Surface)) g.FillPath(b, path);
        var border = !Enabled ? Theme.Line : DroppedDown || Focused ? Theme.Pink : hover ? Theme.Purple : Theme.Line;
        using (var p = new Pen(border, Theme.S(1))) g.DrawPath(p, path);
        int arrowW = Theme.S(30);
        var tr = new Rectangle(Theme.S(10), 0, Width - arrowW - Theme.S(10), Height);
        TextRenderer.DrawText(g, Text, Font, tr, Enabled ? ForeColor : Theme.Faint,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "\uE70D", Theme.Icon(8.5f), new Rectangle(Width - arrowW, 0, arrowW - Theme.S(4), Height),
            Enabled ? (DroppedDown || hover ? Theme.Pink : Theme.Muted) : Theme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>List box with a pink selection bar.</summary>
public class DarkList : ListBox
{
    public DarkList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Surface;
        ForeColor = Theme.Ink;
        ItemHeight = Theme.S(28);
        IntegralHeight = false;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        using (var bg = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(bg, e.Bounds);
        if (e.Index < 0 || e.Index >= Items.Count) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        if (sel)
        {
            using var b = new SolidBrush(Theme.Surface2); e.Graphics.FillRectangle(b, e.Bounds);
            using var a = new SolidBrush(Theme.Pink); e.Graphics.FillRectangle(a, e.Bounds.X, e.Bounds.Y, Theme.S(3), e.Bounds.Height);
        }
        var r = new Rectangle(e.Bounds.X + Theme.S(12), e.Bounds.Y, e.Bounds.Width - Theme.S(16), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, r, Theme.Ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Checked list box drawn in the theme (the base still handles clicks and check state).</summary>
public class DarkCheckedList : CheckedListBox
{
    public DarkCheckedList()
    {
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Surface;
        ForeColor = Theme.Ink;
        IntegralHeight = false;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new SolidBrush(Theme.Surface)) g.FillRectangle(bg, e.Bounds);
        if (e.Index < 0 || e.Index >= Items.Count) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        if (sel) using (var b = new SolidBrush(Theme.Surface2)) g.FillRectangle(b, e.Bounds);
        bool on = GetItemChecked(e.Index);
        int bs = Math.Min(Theme.S(15), e.Bounds.Height - 2);
        var box = new RectangleF(e.Bounds.X + Theme.S(8) + 0.5f, e.Bounds.Y + (e.Bounds.Height - bs) / 2f, bs - 1, bs - 1);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.Round(box, Theme.S(4)))
        {
            if (on)
            {
                using var b = new SolidBrush(Theme.Primary); g.FillPath(b, path);
                using var pen = new Pen(Color.White, Theme.S(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLines(pen, new[]
                {
                    new PointF(box.X + box.Width * 0.24f, box.Y + box.Height * 0.52f),
                    new PointF(box.X + box.Width * 0.43f, box.Y + box.Height * 0.70f),
                    new PointF(box.X + box.Width * 0.77f, box.Y + box.Height * 0.32f),
                });
            }
            else using (var p = new Pen(Theme.Muted, Theme.S(1.2f))) g.DrawPath(p, path);
        }
        g.SmoothingMode = SmoothingMode.None;
        var r = new Rectangle((int)box.Right + Theme.S(9), e.Bounds.Y, e.Bounds.Right - (int)box.Right - Theme.S(12), e.Bounds.Height);
        TextRenderer.DrawText(g, GetItemText(Items[e.Index]), Font, r, Theme.Ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Rounded pink-to-violet progress bar; Marquee style animates a moving segment.</summary>
public class PinkBar : ProgressBar
{
    readonly System.Windows.Forms.Timer anim = new() { Interval = 30 };
    float phase;

    public PinkBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        anim.Tick += (_, _) => { phase = (phase + 0.012f) % 1.4f; Invalidate(); };
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); anim.Enabled = Style == ProgressBarStyle.Marquee; }
    protected override void OnHandleDestroyed(EventArgs e) { anim.Enabled = false; base.OnHandleDestroyed(e); }
    protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // PBM_SETPOS, PBM_DELTAPOS, PBM_SETSTEP/STEPIT, PBM_SETRANGE32: the value changed, repaint.
        if (m.Msg is 0x0402 or 0x0403 or 0x0405 or 0x0406) Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.BackOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float h = Math.Min(Height - 1, Theme.S(8));
        var track = new RectangleF(0.5f, (Height - h) / 2f, Width - 1.5f, h);
        using (var tp = Theme.Round(track, h / 2))
        using (var tb = new SolidBrush(Theme.Surface2)) g.FillPath(tb, tp);
        RectangleF fill;
        if (Style == ProgressBarStyle.Marquee)
        {
            float seg = track.Width * 0.3f, x = track.X - seg + (track.Width + seg) * (phase / 1.4f);
            fill = RectangleF.Intersect(new RectangleF(x, track.Y, seg, h), track);
        }
        else
        {
            float f = Maximum > Minimum ? (Value - Minimum) / (float)(Maximum - Minimum) : 0;
            fill = new RectangleF(track.X, track.Y, Math.Max(f > 0 ? h : 0, track.Width * f), h);
        }
        if (fill.Width < 1) return;
        using var fp = Theme.Round(fill, h / 2);
        using var fb = new LinearGradientBrush(new RectangleF(track.X - 1, track.Y, track.Width + 2, h), Theme.Pink, Theme.Purple, LinearGradientMode.Horizontal);
        g.FillPath(fb, fp);
    }
}

/// <summary>Themed tooltip (owner-drawn).</summary>
public static class DarkTip
{
    public static ToolTip Create()
    {
        var t = new ToolTip { OwnerDraw = true, BackColor = Theme.Surface2, ForeColor = Theme.Ink };
        t.Popup += (_, e) =>
        {
            var sz = TextRenderer.MeasureText(t.GetToolTip(e.AssociatedControl), Theme.Body, new Size(Theme.S(360), 0), TextFormatFlags.WordBreak);
            e.ToolTipSize = new Size(sz.Width + Theme.S(20), sz.Height + Theme.S(14));
        };
        t.Draw += (_, e) =>
        {
            using (var b = new SolidBrush(Theme.Surface2)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.Pink)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            var r = Rectangle.Inflate(e.Bounds, -Theme.S(10), -Theme.S(7));
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, Theme.Body, r, Theme.Ink, TextFormatFlags.WordBreak | TextFormatFlags.Left);
        };
        return t;
    }
}
