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
    /// <summary>Play (and the skip-launcher start) use "Play with DLSS 5.bat" while DLSS 5 is set up.</summary>
    public bool PlayWithDlss5 { get; set; }
    public Dictionary<string, CostumeState> Costumes { get; set; } = new();

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static Config Load()
    {
        try { return JsonSerializer.Deserialize<Config>(File.ReadAllText(Paths.Config)) ?? new(); }
        catch { return new(); }
    }

    public void Save() => File.WriteAllText(Paths.Config, JsonSerializer.Serialize(this, Opts));
}
