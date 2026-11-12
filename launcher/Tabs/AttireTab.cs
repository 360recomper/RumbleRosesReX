namespace RRXXLauncher.Tabs;

/// <summary>
/// Attire picker (ATT-PICK): one outfit per created-wrestler slot, shown as dark cards with front + back renders
/// (same look as the attire guide page). Every slot has 4 colours (the in-game colour choice); slots with more than 4
/// outfits let you pick which outfit each colour shows. Picks are written to lambda_model_overrides on Apply.
/// </summary>
public class AttireTab : ILauncherTab
{
    static readonly Color Bg = Theme.Bg, Panel = Theme.Surface, Line = Theme.Line, Ink = Theme.Ink, Muted = Theme.Muted,
        Accent = Theme.PinkText, Now = Theme.Purple, Render = Theme.Surface2;
    const int CardW = 300, PicH = 200;
    float k = 1;                                   // DPI scale: every pixel size below goes through S()
    int S(int px) => (int)Math.Round(px * k);

    readonly Panel root = new() { Dock = DockStyle.Fill, BackColor = Bg };
    readonly ListBox divas = new()
    {
        Dock = DockStyle.Left, Width = 210, BackColor = Panel, ForeColor = Ink, BorderStyle = BorderStyle.None,
        DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 30, IntegralHeight = false,
    };
    readonly FlowLayoutPanel cards = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Bg, Padding = new Padding(12) };
    readonly Label head = new() { Dock = DockStyle.Top, Height = 44, ForeColor = Ink, BackColor = Bg, Padding = new Padding(14, 8, 0, 0),
                                  Font = Theme.Display(15f) };
    readonly Label status = new() { AutoSize = true, ForeColor = Muted, Margin = new Padding(8, 10, 0, 0), MaximumSize = new Size(520, 0) };
    readonly CheckBox showAll = new DarkCheck { Text = "Show every installed costume for every slot", AutoSize = true, ForeColor = Muted,
                                        Margin = new Padding(0, 9, 12, 0) };
    readonly Dictionary<string, Image> imgCache = new();

    Attire.Catalog cat = new();
    List<Attire.Slot> slots = new();
    List<Attire.Outfit> outfits = new();
    Dictionary<int, int[]> current = new();        // what the TOML says now (4 colours per slot)
    readonly Dictionary<int, int[]> pending = new(); // picks not applied yet
    int colour;                                    // 0-3: the colour the next click assigns
    readonly FlowLayoutPanel colourBar = new() { Dock = DockStyle.Top, Height = 40, BackColor = Bg, Padding = new Padding(12, 4, 0, 0) };
    readonly Label colourNote = new() { AutoSize = true, ForeColor = Muted, Margin = new Padding(8, 8, 0, 0) };
    readonly List<Button> colourBtns = new();

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? app, string? idList);   // dark scrollbars (Windows 10 1809+)

    public string Title => "Attire";
    public Control View => root;

    public AttireTab()
    {
        bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, BackColor = Panel, Padding = new Padding(10, 6, 10, 0), WrapContents = false };
        bar.Controls.Add(Btn("Apply", (_, _) => Apply()));
        bar.Controls.Add(Btn("Default outfit", (_, _) => ResetSlot()));
        bar.Controls.Add(Btn("Reset all to default", (_, _) => ResetAll()));
        bar.Controls.Add(showAll);
        bar.Controls.Add(status);
        showAll.CheckedChanged += (_, _) => ShowCards();

        divas.DrawItem += DrawDiva;
        foreach (Control c in new Control[] { divas, cards }) c.HandleCreated += (_, _) => SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
        divas.SelectedIndexChanged += (_, _) => ShowCards();
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        for (int c = 0; c < Attire.Colours; c++)
        {
            int ci = c;
            var b = Btn($"Colour {c + 1}", (_, _) => { colour = ci; ShowCards(); });
            colourBtns.Add(b);
            colourBar.Controls.Add(b);
        }
        colourBar.Controls.Add(colourNote);
        right.Controls.Add(cards);
        right.Controls.Add(colourBar);
        right.Controls.Add(head);
        root.Controls.Add(right);
        root.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Line });
        root.Controls.Add(divas);
        root.Controls.Add(bar);
    }

    Button Btn(string text, EventHandler click, int w = 120)
    {
        var b = new FlatButton { Text = text, AutoSize = true, Padding = new Padding(S(10), 0, S(10), 0), MinimumSize = new Size(0, S(32)), Margin = new Padding(0, S(2), S(8), 0) };
        b.Click += click;
        return b;
    }

    readonly FlowLayoutPanel bar;

    public void OnShown()
    {
        k = Theme.Scale;
        divas.Width = S(220); divas.ItemHeight = S(30); bar.Height = S(46); head.Height = S(44); colourBar.Height = S(40);
        status.MaximumSize = new Size(S(520), 0);
        if (root.FindForm() is { } f && (f.ClientSize.Width < S(1000 + NavHost.SideW) || f.ClientSize.Height < S(700)))
        {
            f.ClientSize = new Size(Math.Max(f.ClientSize.Width, S(1000 + NavHost.SideW)), Math.Max(f.ClientSize.Height, S(700)));
            var wa = Screen.FromControl(f).WorkingArea;
            f.Location = new Point(Math.Max(wa.Left, wa.Left + (wa.Width - f.Width) / 2), Math.Max(wa.Top, wa.Top + (wa.Height - f.Height) / 2));
        }
        Reload();
    }

    void Reload()
    {
        int sel = Math.Max(0, divas.SelectedIndex);
        pending.Clear();
        cat = Attire.LoadCatalog();
        slots = Attire.Slots(cat);
        outfits = Attire.Outfits(cat, Config.Load());
        current = Attire.Current(new Toml());
        divas.BeginUpdate();
        divas.Items.Clear();
        foreach (var s in slots) divas.Items.Add(s);
        divas.EndUpdate();
        divas.SelectedIndex = Math.Min(sel, divas.Items.Count - 1);
        status.Text = !Paths.IsInstalled ? "Set up the game first (Setup tab)."
            : outfits.Count == 0 ? "No outfits installed yet: install the mod pack (Mod packs tab) or add costume mods (Mods tab)."
            : cat.Divas.Count == 0 ? "No mod pack attire list found; showing your costume mods." : "";
    }

    Attire.Slot? Selected => divas.SelectedItem as Attire.Slot;
    int[]? Wearing(int edit) => pending.TryGetValue(edit, out var p) ? p : current.TryGetValue(edit, out var c) ? c : null;

    /// <summary>The outfits offered for a slot without "show all": her own + costume mods.</summary>
    List<Attire.Outfit> Own(Attire.Slot s) => outfits.Where(o => o.Edit == s.Edit || (o.Edit == 0 && o.FromMod)).ToList();
    /// <summary>Only slots with more than 4 outfits have a choice to make; the others already show all theirs in the 4 colours.</summary>
    bool Editable(Attire.Slot s) => Own(s).Count > Attire.Colours;

    void DrawDiva(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var s = (Attire.Slot)divas.Items[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using var bg = new SolidBrush(sel ? Render : Panel);
        e.Graphics.FillRectangle(bg, e.Bounds);
        if (sel) using (var a = new SolidBrush(Accent)) e.Graphics.FillRectangle(a, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
        var font = divas.Font;
        var tag = pending.ContainsKey(s.Edit) ? "changed" : s.Edit.ToString();
        var small = Theme.Font(8.5f);
        var sz = TextRenderer.MeasureText(tag, small);
        var mid = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(e.Graphics, s.Name, font, new Rectangle(e.Bounds.X + S(12), e.Bounds.Y, e.Bounds.Width - sz.Width - S(24), e.Bounds.Height), Ink, mid);
        TextRenderer.DrawText(e.Graphics, tag, small, new Rectangle(e.Bounds.Right - sz.Width - S(8), e.Bounds.Y, sz.Width, e.Bounds.Height), pending.ContainsKey(s.Edit) ? Accent : Muted, mid);
    }

    void ShowCards()
    {
        cards.SuspendLayout();
        foreach (Control c in cards.Controls) c.Dispose();
        cards.Controls.Clear();
        if (Selected is not { } s) { cards.ResumeLayout(); return; }
        int[]? wear = Wearing(s.Edit);
        bool edit = Editable(s);
        colourBar.Visible = edit;
        for (int c = 0; c < colourBtns.Count; c++)
        {
            ((FlatButton)colourBtns[c]).Kind = c == colour ? BtnKind.Primary : BtnKind.Normal;
            colourBtns[c].Invalidate();
        }
        colourNote.Text = $"Click an outfit to set it as colour {colour + 1} (the 4 colours you cycle through in the game).";
        head.Text = $"{s.Name}   ·   created wrestler slot {s.Edit}";
        // her own outfits first, then Mods-tab costume mods; "show all" adds every other slot's outfits and unknown
        // costume folders (e.g. third-party body-only mods without a catalog entry or preview)
        var list = outfits.Where(o => o.Edit == s.Edit).Concat(outfits.Where(o => o.Edit == 0 && o.FromMod));
        if (showAll.Checked) list = list.Concat(outfits.Where(o => o.Edit != s.Edit && !(o.Edit == 0 && o.FromMod)));
        var shown = list.ToList();
        if (wear != null)
            foreach (var w in wear.Distinct().Reverse())
                if (!shown.Any(o => o.Id == w))
                    shown.Insert(0, outfits.FirstOrDefault(o => o.Id == w) ?? new Attire.Outfit(w, s.Edit, $"Costume {w} (not installed)", null, false));
        foreach (var o in shown) cards.Controls.Add(Card(s, o, wear, edit));
        if (!edit && !showAll.Checked)
            cards.Controls.Add(new Label { Text = "This slot has " + Own(s).Count + " outfit(s), all shown in its 4 colours (badges below). Tick \"Show every installed costume\" below to borrow another one, or add costume mods in the Mods tab.",
                                           ForeColor = Muted, AutoSize = true, MaximumSize = new Size(S(CardW), 0), Margin = new Padding(8, 8, 8, 8) });
        cards.ResumeLayout();
    }

    Control Card(Attire.Slot s, Attire.Outfit o, int[]? wear, bool edit)
    {
        bool isDefault = s.Default == o.Id;
        var cols = wear == null ? new List<int>() : Enumerable.Range(0, Attire.Colours).Where(c => wear[c] == o.Id).ToList();
        bool wearing = cols.Count > 0;
        bool changed = pending.ContainsKey(s.Edit) && wearing;
        var border = changed ? Accent : wearing ? Now : Line;
        var card = new Panel { Width = S(CardW + 4), Height = S(PicH + 96), BackColor = border, Margin = new Padding(S(8)), Padding = new Padding(S(2)), Cursor = Cursors.Hand };
        var inner = new Panel { Dock = DockStyle.Fill, BackColor = Panel };
        var pic = new PictureBox { Dock = DockStyle.Top, Height = S(PicH), BackColor = Render, SizeMode = PictureBoxSizeMode.Zoom, Image = Load(o.Preview) };
        var title = new Label { Text = o.Label, ForeColor = Ink, Font = Theme.Font(9.5f, true), Location = new Point(S(10), S(PicH + 6)),
                                Size = new Size(S(CardW - 20), S(36)), AutoEllipsis = true, UseMnemonic = false };
        var tags = new List<string> { $"costume {o.Id}" };
        if (o.FromMod) tags.Add("costume mod");
        else if (o.Edit != s.Edit && o.Edit != 0) tags.Add($"from {slots.FirstOrDefault(x => x.Edit == o.Edit)?.Name}");
        if (isDefault) tags.Add("default");
        var sub = new Label { Text = string.Join("  ·  ", tags), ForeColor = Muted, Font = Theme.Mono(8.5f), Location = new Point(S(10), S(PicH + 44)), AutoSize = true };
        inner.Controls.Add(sub);
        inner.Controls.Add(title);
        if (wearing)
            inner.Controls.Add(new Label { Text = (changed ? "PICKED (not applied)" : "WEARING") + "  ·  colour " + string.Join(", ", cols.Select(c => c + 1)), ForeColor = changed ? Accent : Now, Font = Theme.Font(8f, true),
                                           AutoSize = true, Location = new Point(S(10), S(PicH + 64)) });
        inner.Controls.Add(pic);
        card.Controls.Add(inner);
        void pick(object? _, EventArgs __) => Pick(s, o.Id);
        if (!edit) card.Cursor = Cursors.Default;
        foreach (Control c in new Control[] { card, inner, pic, title, sub }) c.Click += pick;
        return card;
    }

    Image Load(string? path)
    {
        var key = path ?? "";
        if (imgCache.TryGetValue(key, out var im)) return im;
        try
        {
            if (path != null)
            {
                using var fs = File.OpenRead(path);   // no file lock (the pack's uninstall must be able to delete it)
                using var tmp = Image.FromStream(fs);
                return imgCache[key] = new Bitmap(tmp);
            }
        }
        catch { }
        return imgCache[key] = Placeholder();
    }

    static Image Placeholder()
    {
        var b = new Bitmap(CardW, PicH);
        b.SetResolution(96, 96);   // drawn at 100%; the PictureBox zooms it with the card
        using var g = Graphics.FromImage(b);
        g.Clear(Render);
        var f = Theme.Font(10f);
        TextRenderer.DrawText(g, "No preview", f, new Rectangle(0, 0, CardW, PicH), Theme.Faint,
                              TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return b;
    }

    void Pick(Attire.Slot s, int id)
    {
        if (!Editable(s)) { status.ForeColor = Muted; status.Text = $"{s.Name} has {Own(s).Count} outfit(s): all are already in the 4 colours."; return; }
        SetColour(s, colour, id);
        Changed();
    }

    void SetColour(Attire.Slot s, int c, int id)
    {
        var ids = (int[])(Wearing(s.Edit) ?? Enumerable.Repeat(id, Attire.Colours).ToArray()).Clone();
        ids[c] = id;
        if (current.TryGetValue(s.Edit, out var cur) && cur.SequenceEqual(ids)) pending.Remove(s.Edit); else pending[s.Edit] = ids;
    }

    void ResetSlot()
    {
        if (Selected is not { } s) return;
        if (s.Default is not { } d) { status.Text = "No default outfit known for this slot (mod pack not installed?)."; return; }
        SetColour(s, 0, d);
        Changed();
    }

    void ResetAll()
    {
        int n = 0;
        foreach (var s in slots.Where(s => s.Default != null))
        {
            SetColour(s, 0, s.Default!.Value);
            n++;
        }
        if (n == 0) { status.Text = "No mod pack attire list found, nothing to reset to."; return; }
        Changed();
    }

    void Changed()
    {
        status.Text = pending.Count == 0 ? "" : $"{pending.Count} change(s) not applied yet: press Apply.";
        status.ForeColor = pending.Count == 0 ? Muted : Accent;
        divas.Invalidate();
        ShowCards();
    }

    void Apply()
    {
        if (pending.Count == 0) { status.Text = "Nothing to apply."; return; }
        try
        {
            var bk = Attire.Apply(pending);
            int n = pending.Count;
            Reload();
            status.ForeColor = Muted;
            status.Text = $"Saved {n} pick(s). Old settings backed up in {Path.GetFileName(Attire.BackupDir)}\\{Path.GetFileName(bk)}.";
        }
        catch (Exception ex) { status.ForeColor = Accent; status.Text = "Could not save: " + ex.Message; }
    }
}
