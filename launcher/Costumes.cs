using System.Text;

namespace RRXXLauncher;

/// <summary>
/// Costume mods: each folder in mods\costumes\ (cos.dat, DLcos&lt;n&gt;.pac, e&lt;n&gt;\) is installed as
/// saves\...\00000002\RRXXMOD0&lt;id&gt; under a free costume id (port of RRXX-tools\scripts\new_costume_slot.py).
/// </summary>
public static class Costumes
{
    /// <summary>Costume ids of the official DLC packs; never handed out, so a later DLC import can't clash.</summary>
    static readonly HashSet<int> OfficialDlc = new(
        Enumerable.Range(400, 8).Concat(Enumerable.Range(412, 36)).Concat(new[] { 460, 461, 462, 463 })
            .Concat(Enumerable.Range(468, 12)).Concat(Enumerable.Range(500, 5)).Concat(Enumerable.Range(507, 13))
            .Concat(Enumerable.Range(530, 5)).Concat(new[] { 540, 541, 542, 544, 546, 548, 549 }));

    const int FirstFree = 448;
    public const string Prefix = "RRXXMOD0";

    public record ModFolder(string Name, string Path, int OldId);

    public static List<ModFolder> Scan()
    {
        var res = new List<ModFolder>();
        if (!Directory.Exists(Paths.CostumeMods)) return res;
        foreach (var d in Directory.GetDirectories(Paths.CostumeMods).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var cos = Path.Combine(d, "cos.dat");
            if (File.Exists(cos) && int.TryParse(File.ReadAllText(cos).Trim(), out var old))
                res.Add(new ModFolder(Path.GetFileName(d), d, old));
        }
        return res;
    }

    /// <summary>Ids used by anything in the DLC folder (imported DLC or our own mod slots).</summary>
    static HashSet<int> UsedIds(string? exceptFolder = null)
    {
        var ids = new HashSet<int>(OfficialDlc);
        ids.UnionWith(ModPack.ReservedDlcIds());
        if (!Directory.Exists(Paths.Dlc)) return ids;
        foreach (var d in Directory.GetDirectories(Paths.Dlc))
        {
            if (Path.GetFileName(d) == exceptFolder) continue;
            var cos = Path.Combine(d, "cos.dat");
            if (File.Exists(cos) && int.TryParse(File.ReadAllText(cos).Trim(), out var id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>Syncs the DLC folder with the Mods tab: enabled mods are installed, the rest removed.</summary>
    public static List<string> Apply(Config cfg, bool masterOn)
    {
        var log = new List<string>();
        var mods = Scan();
        var names = mods.Select(m => m.Name).ToHashSet();

        // forget costumes whose folder is gone
        foreach (var gone in cfg.Costumes.Keys.Where(k => !names.Contains(k)).ToList())
        {
            Uninstall(cfg.Costumes[gone].Id);
            cfg.Costumes.Remove(gone);
            log.Add($"{gone}: folder removed, slot freed");
        }

        // ids already promised to other mods stay reserved
        foreach (var m in mods)
        {
            if (!cfg.Costumes.TryGetValue(m.Name, out var st))
                cfg.Costumes[m.Name] = st = new CostumeState { Id = 0 };
            bool want = masterOn && st.Enabled;
            if (!want)
            {
                if (st.Id != 0 && Uninstall(st.Id)) log.Add($"{m.Name}: turned off");
                continue;
            }
            var used = UsedIds();
            foreach (var other in cfg.Costumes.Where(kv => kv.Key != m.Name && kv.Value.Id != 0)) used.Add(other.Value.Id);
            if (st.Id == 0 || OfficialDlc.Contains(st.Id) || used.Contains(st.Id))
            {
                int id = FirstFree;
                while (used.Contains(id)) id++;
                st.Id = id;
            }
            if (Directory.Exists(InstalledPath(st.Id))) { log.Add($"{m.Name}: on (costume {st.Id})"); continue; }
            Install(m, st.Id);
            log.Add($"{m.Name}: installed as costume {st.Id}");
        }
        // orphans (config lost or edited by hand): any of our slot folders nobody owns any more
        var owned = cfg.Costumes.Values.Where(s => s.Id != 0 && masterOn && s.Enabled).Select(s => Prefix + s.Id).ToHashSet();
        owned.UnionWith(ModPack.ReservedDlcIds().Select(id => Prefix + id));   // mod pack costumes are not ours to clean up
        if (Directory.Exists(Paths.Dlc))
            foreach (var d in Directory.GetDirectories(Paths.Dlc, Prefix + "*"))
                if (!owned.Contains(Path.GetFileName(d)))
                {
                    Uninstall(int.TryParse(Path.GetFileName(d)[Prefix.Length..], out var oid) ? oid : 0);
                    log.Add($"removed unowned slot {Path.GetFileName(d)}");
                }
        cfg.Save();
        return log;
    }

    static string InstalledPath(int id) => Path.Combine(Paths.Dlc, Prefix + id);

    static bool Uninstall(int id)
    {
        bool any = false;
        var p = InstalledPath(id);
        if (Directory.Exists(p)) { Directory.Delete(p, true); any = true; }
        var h = Path.Combine(Paths.DlcHeaders, Prefix + id + ".header");
        if (File.Exists(h)) { File.Delete(h); any = true; }
        return any;
    }

    static void Install(ModFolder m, int newId)
    {
        var dst = InstalledPath(newId);
        if (Directory.Exists(dst)) Directory.Delete(dst, true);
        CopyDir(m.Path, dst);
        string o = m.OldId.ToString(), n = newId.ToString();
        File.Move(Path.Combine(dst, $"DLcos{o}.pac"), Path.Combine(dst, $"DLcos{n}.pac"));
        if (o != n)
        {
            Directory.Move(Path.Combine(dst, "e" + o), Path.Combine(dst, "e" + n));
            foreach (var suffix in new[] { ".def", "figure.txt" })
                File.Move(Path.Combine(dst, "e" + n, "e" + o + suffix), Path.Combine(dst, "e" + n, "e" + n + suffix));
        }
        File.WriteAllText(Path.Combine(dst, "cos.dat"), n);
        PatchRecord(Path.Combine(dst, $"DLcos{n}.pac"), newId);

        Directory.CreateDirectory(Paths.DlcHeaders);
        WriteHeader(Prefix + newId, null);
    }

    /// <summary>Entry 0 of the pac is the costume record: BE u32 id + UTF-16BE "dl_bat&lt;id&gt;" at +16.</summary>
    static void PatchRecord(string pac, int newId)
    {
        var d = File.ReadAllBytes(pac);
        uint count = BitConverter.ToUInt32(d, 4);
        for (int i = 0; i < count; i++)
        {
            uint no = BitConverter.ToUInt32(d, 8 + 12 * i), off = BitConverter.ToUInt32(d, 12 + 12 * i),
                 ln = BitConverter.ToUInt32(d, 16 + 12 * i);
            if (no != 0) continue;
            long b = 8 + 12 * count + off;
            if (b + ln > d.Length || ln < 16) throw new InvalidDataException("bad costume record in " + pac);
            d[b] = (byte)(newId >> 24); d[b + 1] = (byte)(newId >> 16); d[b + 2] = (byte)(newId >> 8); d[b + 3] = (byte)newId;
            Array.Clear(d, (int)b + 16, (int)ln - 16);
            var name = Encoding.BigEndianUnicode.GetBytes("dl_bat" + newId);
            Array.Copy(name, 0, d, b + 16, Math.Min(name.Length, ln - 16));
            File.WriteAllBytes(pac, d);
            return;
        }
        throw new InvalidDataException("no costume record (entry 0) in " + pac);
    }

    /// <summary>The game's content enumerator needs a 0x148-byte header per package (blank display name).</summary>
    public static void WriteHeader(string fileName, string? title)
    {
        Directory.CreateDirectory(Paths.DlcHeaders);
        var b = new byte[0x148];
        b[3] = 1; b[7] = 2;                       // device 1, content type 2 (DLC), big-endian
        if (title != null)
        {
            var t = Encoding.BigEndianUnicode.GetBytes(title);
            Array.Copy(t, 0, b, 8, Math.Min(t.Length, 254));
        }
        var fn = Encoding.ASCII.GetBytes(fileName);
        Array.Copy(fn, 0, b, 0x108, Math.Min(fn.Length, 42));
        b[0x140] = 0x4B; b[0x141] = 0x4E; b[0x142] = 0x07; b[0x143] = 0xD1;   // title id
        File.WriteAllBytes(Path.Combine(Paths.DlcHeaders, fileName + ".header"), b);
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
