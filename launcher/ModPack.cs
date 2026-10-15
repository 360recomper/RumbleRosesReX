using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RRXXLauncher;

/// <summary>
/// .rrxxpack = a patch pack: every file is rebuilt from the player's own stock game files (from their ISO, in
/// game\assets) plus the new bytes in the pack. Layout: "RRXXPACK" u32 version, u32 header length, header JSON,
/// data. Ops per file: (literal length, stock file, stock offset, copy length) records; literals = one compressed
/// stream. Every blob, stock file and output carries a SHA-1. Install state for Uninstall lives in modpacks\.
/// </summary>
public static class ModPack
{
    public const string Magic = "RRXXPACK";
    public const int Version = 1;
    const int OpSize = 20;

    public class Blob { public long Off { get; set; } public long Len { get; set; } public string Sha1 { get; set; } = ""; }
    public class StockFile { public string Path { get; set; } = ""; public long Size { get; set; } public string Sha1 { get; set; } = ""; }
    public class PackFile
    {
        public string Path { get; set; } = "";
        public long Size { get; set; }
        public string Sha1 { get; set; } = "";
        public Blob Ops { get; set; } = new();
        public Blob Lit { get; set; } = new();
        public string Codec { get; set; } = "deflate";
    }
    public class Region { public long Offset { get; set; } public int Length { get; set; } }
    public class SaveEdit
    {
        public long SavegameSize { get; set; }
        public List<Region> Regions { get; set; } = new();
        public Blob Data { get; set; } = new();
    }
    public class Header
    {
        public int Format { get; set; }
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string PackVersion { get; set; } = "";
        public string Description { get; set; } = "";
        public string XexSha1 { get; set; } = "";
        public List<StockFile> Stock { get; set; } = new();
        public List<PackFile> Files { get; set; } = new();
        public Dictionary<string, string> Toml { get; set; } = new();
        public SaveEdit? Save { get; set; }
        public List<int> DlcIds { get; set; } = new();
    }

    /// <summary>What an install changed, so Uninstall can put back exactly what was there before.</summary>
    public class Record
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string PackVersion { get; set; } = "";
        public string Installed { get; set; } = "";
        public List<FileChange> Files { get; set; } = new();
        public Dictionary<string, string?> TomlBefore { get; set; } = new();
        /// <summary>SHA-1 of the TOML right after install: unchanged at uninstall = the old file comes back byte for byte.</summary>
        public string? TomlAfterSha1 { get; set; }
        public List<int> DlcIds { get; set; } = new();
        /// <summary>Save edits not applied yet (no save existed at install time).</summary>
        public SaveEdit? PendingSave { get; set; }
        public string? PackPath { get; set; }
        public List<SaveChange> Saves { get; set; } = new();
    }
    public class FileChange { public string Path { get; set; } = ""; public string? Backup { get; set; } }
    public class SaveChange { public string Path { get; set; } = ""; public string Before { get; set; } = ""; }

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Dir => Path.Combine(Paths.Root, "modpacks");
    static string RecordPath(string id) => Path.Combine(Dir, id + ".json");
    static string BackupDir(string id) => Path.Combine(Dir, "backups", id);

    public static List<Record> Installed()
    {
        var res = new List<Record>();
        if (!Directory.Exists(Dir)) return res;
        foreach (var f in Directory.GetFiles(Dir, "*.json"))
            try { if (JsonSerializer.Deserialize<Record>(File.ReadAllText(f), Json) is { } r) res.Add(r); } catch { }
        return res;
    }

    /// <summary>DLC costume ids owned by installed packs (the Mods tab must not reuse or clean them up).</summary>
    public static HashSet<int> ReservedDlcIds() => Installed().SelectMany(r => r.DlcIds).ToHashSet();

    // ---------------------------------------------------------------- reading

    public sealed class Reader : IDisposable
    {
        public readonly Header Head;
        public readonly string FilePath;
        readonly FileStream fs;
        readonly long dataStart;

        public Reader(string path)
        {
            FilePath = path;
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            var br = new BinaryReader(fs);
            if (fs.Length < 16 || Encoding.ASCII.GetString(br.ReadBytes(8)) != Magic)
                throw new InvalidDataException("This is not a mod pack (.rrxxpack) file.");
            int ver = br.ReadInt32();
            if (ver > Version) throw new InvalidDataException($"This pack needs a newer launcher (pack format {ver}).");
            int hl = br.ReadInt32();
            if (hl <= 0 || hl > fs.Length - 16) throw new InvalidDataException("The pack file is damaged (header).");
            Head = JsonSerializer.Deserialize<Header>(br.ReadBytes(hl), Json) ?? throw new InvalidDataException("Empty pack header.");
            dataStart = 16 + hl;
            if (string.IsNullOrWhiteSpace(Head.Id) || Head.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("The pack has no valid id.");
            foreach (var f in Head.Files) SafeRelative(f.Path);
        }

        public Stream Open(Blob b)
        {
            if (b.Off < 0 || b.Len < 0 || dataStart + b.Off + b.Len > fs.Length)
                throw new InvalidDataException("The pack file is damaged or incomplete (truncated download?).");
            return new SubStream(fs, dataStart + b.Off, b.Len);
        }

        public byte[] ReadAll(Blob b)
        {
            using var s = Open(b);
            var buf = new byte[b.Len];
            s.ReadExactly(buf);
            if (Sha1Hex(buf) != b.Sha1) throw new InvalidDataException("The pack file is damaged (checksum).");
            return buf;
        }

        /// <summary>Checks every blob checksum (a damaged download is caught before anything is written).</summary>
        public void VerifyBlobs(Action<string, double> progress, CancellationToken ct)
        {
            var blobs = Head.Files.SelectMany(f => new[] { (f.Path, f.Ops), (f.Path, f.Lit) }).ToList();
            if (Head.Save != null) blobs.Add(("save data", Head.Save.Data));
            long total = Math.Max(1, blobs.Sum(b => b.Item2.Len)), done = 0;
            var buf = new byte[1 << 20];
            foreach (var (name, b) in blobs)
            {
                using var s = Open(b);
                using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    h.AppendData(buf, 0, n); done += n;
                    progress("Checking the pack: " + Path.GetFileName(name), (double)done / total);
                }
                if (Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant() != b.Sha1)
                    throw new InvalidDataException($"The pack file is damaged ({name}). Download it again.");
            }
        }

        public void Dispose() => fs.Dispose();
    }

    // ---------------------------------------------------------------- install

    /// <summary>Wrong ISO version etc.: returns a list of problems (empty = ok). Hashes every stock file the pack uses.</summary>
    public static List<string> CheckStock(Header h, Action<string, double> progress, CancellationToken ct)
    {
        var bad = new List<string>();
        var xex = Path.Combine(Paths.Assets, "default.xex");
        if (!File.Exists(xex)) { bad.Add("The game is not installed yet (Setup tab: choose your ISO first)."); return bad; }
        if (!string.IsNullOrEmpty(h.XexSha1) && Sha1File(xex, ct) != h.XexSha1)
            bad.Add("default.xex is a different game version than this pack was made for.");
        long total = Math.Max(1, h.Stock.Sum(s => s.Size)), done = 0;
        foreach (var s in h.Stock)
        {
            var p = Path.Combine(Paths.Game, SafeRelative(s.Path));
            progress("Checking your game files: " + s.Path, (double)done / total);
            done += s.Size;
            if (!File.Exists(p)) { bad.Add($"missing: game\\{SafeRelative(s.Path)}"); continue; }
            if (new FileInfo(p).Length != s.Size || Sha1File(p, ct) != s.Sha1) bad.Add($"different version: game\\{SafeRelative(s.Path)}");
        }
        return bad;
    }

    public static List<string> Install(string packPath, Action<string, double> progress, CancellationToken ct)
    {
        var log = new List<string>();
        using var r = new Reader(packPath);
        var h = r.Head;
        if (Installed().Any(x => x.Id == h.Id))
            throw new InvalidOperationException($"\"{h.Title}\" is already installed. Uninstall it first to reinstall or update.");
        var bad = CheckStock(h, progress, ct);
        if (bad.Count > 0)
            throw new InvalidDataException("Your game files don't match the ISO version this pack was made for, so nothing was changed.\n"
                + "Re-import your Rumble Roses XX (USA) ISO in the Setup tab.\n\n" + string.Join("\n", bad.Take(8)));
        r.VerifyBlobs(progress, ct);

        var rec = new Record { Id = h.Id, Title = h.Title, PackVersion = h.PackVersion, DlcIds = h.DlcIds,
                               Installed = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
        var bk = BackupDir(h.Id);
        if (Directory.Exists(bk)) Directory.Delete(bk, true);
        // the record is written before the first change, so a crash half-way can still be uninstalled
        Directory.CreateDirectory(Dir);
        SaveRecord(rec);

        var stock = h.Stock.Select(s => Path.Combine(Paths.Game, SafeRelative(s.Path))).ToArray();
        long total = Math.Max(1, h.Files.Sum(f => f.Size)), done = 0;
        foreach (var f in h.Files)
        {
            ct.ThrowIfCancellationRequested();
            var dst = Path.Combine(Paths.Root, SafeRelative(f.Path));
            var tmp = dst + ".rrxxpack-tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            long before = done;
            Rebuild(r, h, f, stock, tmp, n => progress("Installing " + f.Path, (double)(before + n) / total));
            done += f.Size;
            string? backup = null;
            if (File.Exists(dst))
            {
                backup = Path.Combine(bk, SafeRelative(f.Path));
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Move(dst, backup, true);
            }
            File.Move(tmp, dst);
            rec.Files.Add(new FileChange { Path = f.Path, Backup = backup == null ? null : Path.GetRelativePath(Paths.Root, backup) });
            if (rec.Files.Count % 20 == 0) SaveRecord(rec);
        }
        SaveRecord(rec);
        log.Add($"{h.Files.Count} files installed.");

        if (h.Toml.Count > 0)
        {
            var t = new Toml();
            Directory.CreateDirectory(bk);
            if (File.Exists(Paths.Toml)) File.Copy(Paths.Toml, Path.Combine(bk, "toml-before.txt"), true);
            foreach (var (k, v) in h.Toml) { rec.TomlBefore[k] = t.GetLine(k); t.Set(k, v); }
            t.Save();
            rec.TomlAfterSha1 = Sha1Hex(File.ReadAllBytes(Paths.Toml));
            SaveRecord(rec);
            log.Add("Game settings: " + string.Join(", ", h.Toml.Keys));
        }

        if (h.Save != null)
        {
            rec.PendingSave = h.Save;
            // the pack file may be moved or deleted later: keep the save data next to the record
            var sd = Path.Combine(bk, "save-regions.bin");
            Directory.CreateDirectory(bk);
            File.WriteAllBytes(sd, r.ReadAll(h.Save.Data));
            rec.PackPath = Path.GetRelativePath(Paths.Root, sd);
            SaveRecord(rec);
            log.Add(ApplyPendingSave(rec));
        }
        return log;
    }

    /// <summary>Writes save edits that waited for the game to create its save. Safe to call any time.
    /// PendingSave is cleared only when at least one save was patched (now or earlier); otherwise it stays and the
    /// result says why (no save found / empty / wrong size / bad checksum), so the next launcher start retries.</summary>
    public static string ApplyPendingSave(Record rec)
    {
        if (rec.PendingSave is not { } se || rec.PackPath == null) return "";
        var saves = FindSaves();
        const string Todo = "One more step: start the game, save once in the game (any save), then quit. The launcher finishes the mod pack by itself after you quit.";
        if (saves.Count == 0)
        {
            LogDetail("no savegame.txt found anywhere under " + Paths.Saves);
            return Todo;
        }
        var data = File.ReadAllBytes(Path.Combine(Paths.Root, rec.PackPath));
        int n = 0, already = 0;
        var skipped = new List<string>();
        foreach (var p in saves)
        {
            var rel = Path.GetRelativePath(Paths.Root, p);
            if (rec.Saves.Any(s => s.Path == rel)) { already++; continue; }
            var d = File.ReadAllBytes(p);
            if (d.Length == 0) { skipped.Add($"{rel}: empty (0 bytes), the game has not written it yet"); continue; }
            if (d.Length != se.SavegameSize) { skipped.Add($"{rel}: {d.Length} bytes, expected {se.SavegameSize}"); continue; }
            if (ReadBE(d, 0) != d.Length || ReadBE(d, d.Length - 4) != SaveChecksum(d)) { skipped.Add($"{rel}: header or checksum is not a valid save"); continue; }
            var before = new MemoryStream();
            int pos = 0;
            foreach (var g in se.Regions)
            {
                before.Write(d, (int)g.Offset, g.Length);
                Buffer.BlockCopy(data, pos, d, (int)g.Offset, g.Length);
                pos += g.Length;
            }
            WriteBE(d, d.Length - 4, SaveChecksum(d));
            var bf = Path.Combine(BackupDir(rec.Id), $"save-before-{rec.Saves.Count}.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(bf)!);
            File.WriteAllBytes(bf, before.ToArray());
            File.WriteAllBytes(p, d);
            rec.Saves.Add(new SaveChange { Path = rel, Before = Path.GetRelativePath(Paths.Root, bf) });
            n++;
        }
        if (n == 0 && already == 0)
        {
            LogDetail("nothing usable: " + string.Join("; ", skipped));
            return Todo;
        }
        rec.PendingSave = null;
        SaveRecord(rec);
        if (skipped.Count > 0) LogDetail("skipped: " + string.Join("; ", skipped));
        return n > 0 ? "Done! The mod pack's names, looks, themes and movesets are now in your save." : "Your save already has the mod pack's changes.";
    }

    static void LogDetail(string t)
    {
        try { Directory.CreateDirectory(Dir); File.AppendAllText(Path.Combine(Dir, "save-log.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} (detail) {t}{Environment.NewLine}"); } catch { }
    }

    /// <summary>Called at launcher start: finishes save edits for packs installed before the first game run.
    /// Returns what happened (empty = nothing was pending); errors are returned and logged, never swallowed.</summary>
    public static List<string> FinishPending()
    {
        var msgs = new List<string>();
        try
        {
            foreach (var r in Installed().Where(r => r.PendingSave != null))
            {
                try { msgs.Add(ApplyPendingSave(r)); }
                catch (Exception ex) { msgs.Add("The mod pack could not finish editing your save: " + ex.Message); }
            }
        }
        catch (Exception ex) { msgs.Add("Could not check pending save edits: " + ex.Message); }
        if (msgs.Count > 0)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllLines(Path.Combine(Dir, "save-log.txt"), msgs.Select(m => $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {m}"));
            }
            catch { }
        }
        return msgs;
    }

    public static List<string> Uninstall(Record rec, Action<string, double> progress)
    {
        var log = new List<string>();
        int i = 0;
        foreach (var f in Enumerable.Reverse(rec.Files))
        {
            progress("Removing " + f.Path, (double)i++ / Math.Max(1, rec.Files.Count));
            var dst = Path.Combine(Paths.Root, SafeRelative(f.Path));
            if (File.Exists(dst)) File.Delete(dst);
            if (f.Backup != null && File.Exists(Path.Combine(Paths.Root, f.Backup)))
                File.Move(Path.Combine(Paths.Root, f.Backup), dst);
            else PruneEmptyDirs(Path.GetDirectoryName(dst)!);
        }
        log.Add($"{rec.Files.Count} files removed or restored.");
        if (rec.TomlBefore.Count > 0)
        {
            var old = Path.Combine(BackupDir(rec.Id), "toml-before.txt");
            if (File.Exists(Paths.Toml) && rec.TomlAfterSha1 == Sha1Hex(File.ReadAllBytes(Paths.Toml)) && File.Exists(old))
                File.Copy(old, Paths.Toml, true);           // settings untouched since install: exact old file
            else
            {
                var t = new Toml();                          // settings changed since: put back only the pack's keys
                foreach (var (k, line) in rec.TomlBefore) t.SetLine(k, line);
                t.Save();
            }
            log.Add("Game settings restored: " + string.Join(", ", rec.TomlBefore.Keys));
        }
        if (rec.Saves.Count > 0)
        {
            var regions = (rec.Saves.Count > 0 ? ReadSaveLayout(rec) : null) ?? new();
            int n = 0;
            foreach (var s in rec.Saves)
            {
                var p = Path.Combine(Paths.Root, s.Path);
                var bf = Path.Combine(Paths.Root, s.Before);
                if (!File.Exists(p) || !File.Exists(bf)) continue;
                var d = File.ReadAllBytes(p);
                var before = File.ReadAllBytes(bf);
                int pos = 0;
                foreach (var g in regions)
                {
                    if (g.Offset + g.Length > d.Length - 4) break;
                    Buffer.BlockCopy(before, pos, d, (int)g.Offset, g.Length);
                    pos += g.Length;
                }
                WriteBE(d, d.Length - 4, SaveChecksum(d));
                File.WriteAllBytes(p, d);
                n++;
            }
            // only the pack's save slots are put back; the rest of the save (progress, money) is kept
            log.Add($"Save: {n} profile(s) restored (the pack's diva slots only; your progress stays).");
        }
        var bk = BackupDir(rec.Id);
        if (Directory.Exists(bk)) Directory.Delete(bk, true);
        File.Delete(RecordPath(rec.Id));
        var bks = Path.Combine(Dir, "backups");
        if (Directory.Exists(bks) && !Directory.EnumerateFileSystemEntries(bks).Any()) Directory.Delete(bks);
        return log;
    }

    // ---------------------------------------------------------------- helpers

    static List<Region>? ReadSaveLayout(Record rec)
    {
        var p = Path.Combine(BackupDir(rec.Id), "save-layout.json");
        return File.Exists(p) ? JsonSerializer.Deserialize<List<Region>>(File.ReadAllText(p), Json) : null;
    }

    static void SaveRecord(Record rec)
    {
        Directory.CreateDirectory(Dir);
        if (rec.PendingSave != null)
        {
            // the region layout is needed for Uninstall after PendingSave is cleared
            Directory.CreateDirectory(BackupDir(rec.Id));
            File.WriteAllText(Path.Combine(BackupDir(rec.Id), "save-layout.json"), JsonSerializer.Serialize(rec.PendingSave.Regions, Json));
        }
        var tmp = RecordPath(rec.Id) + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(rec, Json));
        File.Move(tmp, RecordPath(rec.Id), true);
    }

    const int PackSource = 0x8000, SelfSource = 0xFFFF;

    /// <summary>Source of a copy: stock file i (&lt; 0x8000), pack file k installed earlier (0x8000 + k), or this file's own output.</summary>
    static void Rebuild(Reader r, Header h, PackFile f, string[] stock, string tmp, Action<long> progress)
    {
        var ops = r.ReadAll(f.Ops);
        if (ops.Length % OpSize != 0) throw new InvalidDataException("The pack file is damaged (ops).");
        using var litRaw = r.Open(f.Lit);
        using var litHash = new HashingStream(litRaw);
        using var lit = f.Codec switch
        {
            "deflate" => (Stream)new DeflateStream(litHash, CompressionMode.Decompress),
            "brotli" => new BrotliStream(litHash, CompressionMode.Decompress),
            "none" => litHash,
            _ => throw new InvalidDataException($"Unknown compression '{f.Codec}' (newer launcher needed)."),
        };
        var src = new Dictionary<int, FileStream>();
        int self = h.Files.IndexOf(f);
        using var outF = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20);
        using var outHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buf = new byte[1 << 20];
        long written = 0;
        void Pump(Stream s, long n)
        {
            while (n > 0)
            {
                int k = s.Read(buf, 0, (int)Math.Min(buf.Length, n));
                if (k <= 0) throw new InvalidDataException("The pack file is damaged (short data).");
                outF.Write(buf, 0, k); outHash.AppendData(buf, 0, k);
                n -= k; written += k;
                progress(written);
            }
        }
        try
        {
            for (int o = 0; o < ops.Length; o += OpSize)
            {
                long litLen = BitConverter.ToUInt32(ops, o);
                int sid = BitConverter.ToUInt16(ops, o + 4);
                long so = BitConverter.ToInt64(ops, o + 8);
                long cl = BitConverter.ToUInt32(ops, o + 16);
                Pump(lit, litLen);
                if (cl == 0) continue;
                string sp;
                if (sid == SelfSource) { outF.Flush(); sp = tmp; if (so + cl > written) throw new InvalidDataException("The pack file is damaged (self copy)."); }
                else if (sid >= PackSource && sid - PackSource < self) sp = Path.Combine(Paths.Root, SafeRelative(h.Files[sid - PackSource].Path));
                else if (sid < stock.Length) sp = stock[sid];
                else throw new InvalidDataException("The pack file is damaged (source index).");
                if (!src.TryGetValue(sid, out var s))
                    src[sid] = s = new FileStream(sp, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
                s.Position = so;
                Pump(s, cl);
            }
            if (lit.Read(buf, 0, 1) != 0) throw new InvalidDataException("The pack file is damaged (extra data).");
        }
        finally { foreach (var s in src.Values) s.Dispose(); }
        outF.Flush();
        if (written != f.Size || Convert.ToHexString(outHash.GetHashAndReset()).ToLowerInvariant() != f.Sha1
            || litHash.Hex() != f.Lit.Sha1)
        {
            outF.Dispose(); File.Delete(tmp);
            throw new InvalidDataException($"Rebuilt {f.Path} does not match the pack's checksum; nothing of it was installed.");
        }
    }

    /// <summary>Every savegame.txt anywhere under saves\ (any profile folder incl. 0000000000000000, any title/slot folder).</summary>
    public static List<string> FindSaves()
    {
        var res = new List<string>();
        if (!Directory.Exists(Paths.Saves)) return res;
        try
        {
            res.AddRange(Directory.EnumerateFiles(Paths.Saves, "savegame.txt", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }));
        }
        catch (IOException) { }
        res.Sort(StringComparer.OrdinalIgnoreCase);
        return res;
    }

    /// <summary>RRXX savegame checksum: signed sum of every byte but the last 4, stored big-endian in the last 4.</summary>
    public static uint SaveChecksum(byte[] d)
    {
        int sum = 0;
        for (int i = 0; i < d.Length - 4; i++) sum += (sbyte)d[i];
        return unchecked((uint)sum);
    }
    static uint ReadBE(byte[] d, int o) => (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3]);
    static void WriteBE(byte[] d, int o, uint v) { d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v; }

    static string SafeRelative(string rel)
    {
        var p = rel.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(p) || p.Split(Path.DirectorySeparatorChar).Any(s => s == ".." || s.Length == 0))
            throw new InvalidDataException($"Bad path in the pack: {rel}");
        return p;
    }

    static void PruneEmptyDirs(string dir)
    {
        var root = Path.GetFullPath(Paths.Root).TrimEnd(Path.DirectorySeparatorChar);
        while (dir.Length > root.Length && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()
               && Path.GetFileName(dir) is not ("mods" or "saves"))
        {
            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir)!;
        }
    }

    public static string Sha1Hex(byte[] b) => Convert.ToHexString(SHA1.HashData(b)).ToLowerInvariant();
    static string Sha1File(string p, CancellationToken ct)
    {
        using var s = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buf = new byte[1 << 20];
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0) { ct.ThrowIfCancellationRequested(); h.AppendData(buf, 0, n); }
        return Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Read-only window onto part of the pack file.</summary>
    sealed class SubStream : Stream
    {
        readonly Stream s; readonly long start, len; long pos;
        public SubStream(Stream s, long start, long len) { this.s = s; this.start = start; this.len = len; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (pos >= len) return 0;
            s.Position = start + pos;
            int n = s.Read(buffer, offset, (int)Math.Min(count, len - pos));
            pos += n;
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => len;
        public override long Position { get => pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    /// <summary>Passes reads through and hashes them (checks the compressed literal stream).</summary>
    sealed class HashingStream : Stream
    {
        readonly Stream s; readonly IncrementalHash h = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        public HashingStream(Stream s) => this.s = s;
        public string Hex()
        {
            var buf = new byte[1 << 16];
            while (Read(buf, 0, buf.Length) > 0) { } // the decompressor may stop before the end of the blob
            return Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant();
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = s.Read(buffer, offset, count);
            h.AppendData(buffer, offset, n);
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => s.Length;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        protected override void Dispose(bool d) { if (d) h.Dispose(); base.Dispose(d); }
    }
}
