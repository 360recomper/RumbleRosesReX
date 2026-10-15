namespace RRXXLauncher;

/// <summary>
/// Minimal key = value editor for RumbleRosesReX.toml. Only touches the keys it is asked to set and keeps
/// every other line (gpu_backend belongs to the F4 menu; the launcher never writes it).
/// </summary>
public class Toml
{
    readonly List<string> lines;
    public Toml() => lines = File.Exists(Paths.Toml) ? File.ReadAllLines(Paths.Toml).ToList() : new();

    int Find(string key) =>
        lines.FindIndex(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith('#') && t.StartsWith(key) && t[key.Length..].TrimStart().StartsWith('=');
        });

    public string? Get(string key)
    {
        int i = Find(key);
        if (i < 0) return null;
        var v = lines[i][(lines[i].IndexOf('=') + 1)..].Trim();
        int hash = v.IndexOf(" #", StringComparison.Ordinal);
        if (hash >= 0 && !v.StartsWith('"')) v = v[..hash].Trim();
        return v.Trim('"');
    }

    public int GetInt(string key, int def) => int.TryParse(Get(key), out var v) ? v : def;
    public bool GetBool(string key, bool def) => Get(key) is { } s ? s == "true" : def;

    public void Set(string key, string rawValue)
    {
        int i = Find(key);
        var line = $"{key} = {rawValue}";
        if (i >= 0) lines[i] = line; else lines.Add(line);
    }
    /// <summary>The whole line for a key (null = not set); SetLine puts it back exactly (null removes the key).</summary>
    public string? GetLine(string key) { int i = Find(key); return i >= 0 ? lines[i] : null; }
    public void SetLine(string key, string? line)
    {
        int i = Find(key);
        if (line == null) { if (i >= 0) lines.RemoveAt(i); }
        else if (i >= 0) lines[i] = line; else lines.Add(line);
    }
    public void Set(string key, int v) => Set(key, v.ToString());
    public void Set(string key, bool v) => Set(key, v ? "true" : "false");

    public void Save()
    {
        Directory.CreateDirectory(Paths.Game);
        File.WriteAllLines(Paths.Toml, lines);
    }
}
