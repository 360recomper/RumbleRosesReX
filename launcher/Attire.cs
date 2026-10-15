using System.Text.Json;
using System.Text.Json.Serialization;

namespace RRXXLauncher;

/// <summary>
/// Attire picker data (ATT-PICK). Which model each created-wrestler slot (EDIT n) wears is the game setting
/// lambda_model_overrides ("1=466, 2=464, ..."; EDIT n = costume id). The mod pack ships attire\attire.json
/// (slot names, pack defaults, outfit labels) and attire\&lt;id&gt;.jpg previews; costume mods from the Mods tab are
/// offered too. Only costumes whose folder is really in the DLC folder are listed.
/// </summary>
public static class Attire
{
    public const string Key = "lambda_model_overrides";
    public static string Dir => Path.Combine(Paths.Root, "attire");
    public static string BackupDir => Path.Combine(Paths.Root, "attire-backups");

    public class CatalogDiva { public int Edit { get; set; } public string Name { get; set; } = ""; public int Default { get; set; } }
    public class CatalogCostume { public int Id { get; set; } public int Edit { get; set; } public string Label { get; set; } = ""; }
    public class Catalog
    {
        public List<CatalogDiva> Divas { get; set; } = new();
        public List<CatalogCostume> Costumes { get; set; } = new();
    }

    /// <summary>One outfit the picker can offer. Edit = 0: not tied to a slot (a costume mod).</summary>
    public record Outfit(int Id, int Edit, string Label, string? Preview, bool FromMod);
    public record Slot(int Edit, string Name, int? Default);

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static Catalog LoadCatalog()
    {
        var p = Path.Combine(Dir, "attire.json");
        try { if (File.Exists(p)) return JsonSerializer.Deserialize<Catalog>(File.ReadAllText(p), Json) ?? new(); }
        catch { }
        return new();
    }

    /// <summary>Costume ids that have a folder in the DLC folder right now (pack slots and Mods-tab slots).</summary>
    public static HashSet<int> InstalledIds()
    {
        var ids = new HashSet<int>();
        if (!Directory.Exists(Paths.Dlc)) return ids;
        foreach (var d in Directory.GetDirectories(Paths.Dlc, Costumes.Prefix + "*"))
        {
            var cos = Path.Combine(d, "cos.dat");
            if (File.Exists(cos) && int.TryParse(File.ReadAllText(cos).Trim(), out var id) && Directory.Exists(Path.Combine(d, "e" + id)))
                ids.Add(id);
        }
        return ids;
    }

    /// <summary>The 30 slots: names from the pack catalog, else "Created wrestler n".</summary>
    public static List<Slot> Slots(Catalog cat)
    {
        var res = new List<Slot>();
        for (int n = 1; n <= 30; n++)
        {
            var d = cat.Divas.FirstOrDefault(x => x.Edit == n);
            res.Add(new Slot(n, d?.Name is { Length: > 0 } nm ? nm : $"Created wrestler {n}", d?.Default));
        }
        return res;
    }

    /// <summary>Every installed outfit: pack outfits (with their slot) first, then costume mods (Edit 0).</summary>
    public static List<Outfit> Outfits(Catalog cat, Config cfg)
    {
        var have = InstalledIds();
        var res = new List<Outfit>();
        foreach (var c in cat.Costumes.Where(c => have.Contains(c.Id)))
            res.Add(new Outfit(c.Id, c.Edit, c.Label, Img(Path.Combine(Dir, c.Id + ".jpg")), false));
        var known = res.Select(o => o.Id).ToHashSet();
        foreach (var (name, st) in cfg.Costumes.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (st.Id == 0 || !have.Contains(st.Id) || !known.Add(st.Id)) continue;
            var folder = Path.Combine(Paths.CostumeMods, name);
            var prev = new[] { "preview.jpg", "preview.png" }.Select(f => Path.Combine(folder, f)).FirstOrDefault(File.Exists);
            res.Add(new Outfit(st.Id, 0, name, prev, true));
        }
        // anything else in the DLC folder under our prefix (e.g. a pack without a catalog)
        foreach (var id in have.Where(i => !known.Contains(i)).OrderBy(i => i))
            res.Add(new Outfit(id, 0, $"Costume {id}", Img(Path.Combine(Dir, id + ".jpg")), false));
        return res;
    }

    static string? Img(string p) => File.Exists(p) ? p : null;

    // ---------------------------------------------------------------- the TOML value

    /// <summary>Slot -> costume id from lambda_model_overrides (entries like "3=480" or "5=481+head"; the id is the leading number).</summary>
    public static Dictionary<int, int> Current(Toml t)
    {
        var res = new Dictionary<int, int>();
        foreach (var (slot, rest) in Entries(t.Get(Key) ?? ""))
        {
            var digits = new string(rest.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var id)) res[slot] = id;
        }
        return res;
    }

    static List<(int slot, string rest)> Entries(string v)
    {
        var res = new List<(int, string)>();
        foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq > 0 && int.TryParse(part[..eq].Trim(), out var slot)) res.Add((slot, part[(eq + 1)..].Trim()));
        }
        return res;
    }

    /// <summary>Writes the picks into the TOML (backup first). Other slots and every other line stay as they are.</summary>
    public static string Apply(Dictionary<int, int> picks)
    {
        var t = new Toml();
        var entries = Entries(t.Get(Key) ?? "");
        foreach (var (slot, id) in picks)
        {
            int i = entries.FindIndex(e => e.slot == slot);
            if (i >= 0)
            {
                var old = entries[i].rest;
                var digits = new string(old.TakeWhile(char.IsDigit).ToArray());
                if (digits != id.ToString()) entries[i] = (slot, id.ToString());   // a new model: drop "+head"-style flags
            }
            else entries.Add((slot, id.ToString()));
        }
        var backup = Backup();
        t.Set(Key, "\"" + string.Join(", ", entries.OrderBy(e => e.slot).Select(e => $"{e.slot}={e.rest}")) + "\"");
        t.Save();
        return backup;
    }

    /// <summary>Copies the TOML to attire-backups\ (the 10 newest are kept).</summary>
    static string Backup()
    {
        if (!File.Exists(Paths.Toml)) return "";
        Directory.CreateDirectory(BackupDir);
        var dst = Path.Combine(BackupDir, $"RumbleRosesReX-{DateTime.Now:yyyyMMdd-HHmmss}.toml");
        File.Copy(Paths.Toml, dst, true);
        foreach (var old in Directory.GetFiles(BackupDir, "RumbleRosesReX-*.toml").OrderByDescending(f => f).Skip(10))
            File.Delete(old);
        return dst;
    }
}
