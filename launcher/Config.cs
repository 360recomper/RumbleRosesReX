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
    /// <summary>Ask GitHub for a newer release when the launcher window opens (opt-out).</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Keep every wrestler's popularity at 100 so Super Star form is always unlocked (on by default, user 2026-10-24).</summary>
    public bool AutoSuperStar { get; set; } = true;
    public Dictionary<string, CostumeState> Costumes { get; set; } = new();

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static Config Load()
    {
        try { return JsonSerializer.Deserialize<Config>(File.ReadAllText(Paths.Config)) ?? new(); }
        catch { return new(); }
    }

    public void Save() => File.WriteAllText(Paths.Config, JsonSerializer.Serialize(this, Opts));
}
