using System.Text;

namespace RRXXLauncher;

/// <summary>Minimal STFS (LIVE/PIRS/CON) reader for DLC packages (port of dlc-staging/stfs_extract.py).</summary>
public class Stfs : IDisposable
{
    static readonly int[] BlocksPerLevel = { 170, 170 * 170, 170 * 170 * 170 };
    readonly FileStream f;
    readonly int shift, ftCount, ftBlock;
    readonly long baseOff;
    public uint ContentType { get; }
    public string Title { get; }

    static int I24(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16);
    static uint BE32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

    public Stfs(string path)
    {
        f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var h = new byte[0x1000];
            if (f.Read(h, 0, h.Length) < h.Length) throw new InvalidDataException("not an STFS package");
            var magic = Encoding.ASCII.GetString(h, 0, 4);
            if (magic is not ("LIVE" or "PIRS" or "CON ")) throw new InvalidDataException("not an STFS package");
            uint headerSize = BE32(h, 0x340);
            ContentType = BE32(h, 0x344);
            shift = (h[0x37B] & 1) != 0 ? 0 : 1;
            ftCount = BitConverter.ToUInt16(h, 0x37C);
            ftBlock = I24(h, 0x37E);
            Title = Encoding.BigEndianUnicode.GetString(h, 0x411, 0x80).Split('\0')[0];
            baseOff = (headerSize + 0xFFF) & ~0xFFFL;
        }
        catch { f.Dispose(); throw; }
    }

    long BlockOffset(int idx)
    {
        long block = idx;
        foreach (var lvl in BlocksPerLevel)
        {
            block += (long)((idx + lvl) / lvl) << shift;
            if (idx < lvl) break;
        }
        return baseOff + (block << 12);
    }

    byte[] ReadBlock(int idx)
    {
        var b = new byte[0x1000];
        f.Seek(BlockOffset(idx), SeekOrigin.Begin);
        f.ReadExactly(b);
        return b;
    }

    record Ent(string Name, bool Contig, bool IsDir, int Blocks, int Start, int Parent, long Size);

    List<Ent> Entries()
    {
        var res = new List<Ent>();
        for (int i = 0; i < ftCount; i++)
        {
            var blk = ReadBlock(ftBlock + i);
            for (int e = 0; e < 0x1000; e += 0x40)
            {
                int flags = blk[e + 0x28], n = flags & 0x3F;
                if (n == 0) continue;
                res.Add(new Ent(Encoding.ASCII.GetString(blk, e, n), (flags & 0x40) != 0, (flags & 0x80) != 0,
                    I24(blk, e + 0x29), I24(blk, e + 0x2F), (short)(blk[e + 0x32] << 8 | blk[e + 0x33]),
                    BE32(blk, e + 0x34)));
            }
        }
        return res;
    }

    /// <summary>Extract all files into dest; returns the number of files.</summary>
    public int Extract(string dest)
    {
        var ents = Entries();
        var root = Path.GetFullPath(dest);
        string Rel(int i)
        {
            var parts = new List<string>();
            for (int guard = 0; i >= 0; i = ents[i].Parent)
            {
                if (guard++ > 1000 || i >= ents.Count) throw new InvalidDataException("bad STFS directory");
                parts.Add(ents[i].Name);
            }
            parts.Reverse();
            return Path.Combine(parts.ToArray());
        }
        int count = 0;
        for (int i = 0; i < ents.Count; i++)
        {
            var e = ents[i];
            var p = Path.GetFullPath(Path.Combine(root, Rel(i)));
            if (!p.StartsWith(root + Path.DirectorySeparatorChar)) throw new InvalidDataException("unsafe path in package");
            if (e.IsDir) { Directory.CreateDirectory(p); continue; }
            if (!e.Contig) throw new InvalidDataException($"non-contiguous file {e.Name} not supported");
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            long remaining = e.Size;
            using var o = new FileStream(p, FileMode.Create, FileAccess.Write);
            for (int b = 0; b < e.Blocks && remaining > 0; b++)
            {
                var chunk = ReadBlock(e.Start + b);
                int n = (int)Math.Min(0x1000, remaining);
                o.Write(chunk, 0, n);
                remaining -= n;
            }
            if (remaining > 0) throw new InvalidDataException($"{e.Name}: short read");
            count++;
        }
        return count;
    }

    public void Dispose() => f.Dispose();
}
