using System.Text.Json;

namespace RRXXLauncher;

public class CostumeState
{
    public int Id { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>launcher.json next to the exe: launcher-only state (the game's own settings live in the TOML).</summary>
public class Config
{
    public bool SkipLauncher { get; set; }
    public Dictionary<string, CostumeState> Costumes { get; set; } = new();

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static Config Load()
    {
        try { return JsonSerializer.Deserialize<Config>(File.ReadAllText(Paths.Config)) ?? new(); }
        catch { return new(); }
    }

    public void Save() => File.WriteAllText(Paths.Config, JsonSerializer.Serialize(this, Opts));
}
