namespace RRXXLauncher;

/// <summary>All paths are relative to the folder holding the launcher exe.</summary>
public static class Paths
{
    public static readonly string Root =
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    public static string Game => Path.Combine(Root, "game");
    public static string Assets => Path.Combine(Game, "assets");
    public static string GameExe => Path.Combine(Game, "rumble_roses_xx.exe");
    public static string Toml => Path.Combine(Game, "RumbleRosesReX.toml");
    public static string Mods => Path.Combine(Root, "mods");
    public static string CostumeMods => Path.Combine(Mods, "costumes");
    public static string Saves => Path.Combine(Root, "saves");
    public static string Backups => Path.Combine(Root, "save-backups");
    public static string Title => Path.Combine(Saves, "0000000000000000", "4B4E07D1");
    public static string Dlc => Path.Combine(Title, "00000002");
    public static string DlcHeaders => Path.Combine(Title, "Headers", "00000002");
    public static string Config => Path.Combine(Root, "launcher.json");

    public static bool IsInstalled => File.Exists(GameExe) && File.Exists(Path.Combine(Assets, "default.xex"));
}
