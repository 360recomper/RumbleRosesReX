namespace RRXXLauncher;

/// <summary>Release version. Bump <see cref="Launcher"/> (and Version in RRXXLauncher.csproj) for every release.</summary>
public static class AppVersion
{
    /// <summary>The release this launcher ships with.</summary>
    public const string Launcher = "1.4";

    /// <summary>version.txt next to the launcher: written by the updater (it may keep a newer launcher than the release
    /// it installed). Without it the install is the launcher's own release.</summary>
    public static string File => Path.Combine(Paths.Root, "version.txt");

    public static string Installed
    {
        get
        {
            try { var t = System.IO.File.ReadAllText(File).Trim(); if (Parse(t) != null) return t.TrimStart('v', 'V'); }
            catch { }
            return Launcher;
        }
    }

    /// <summary>"v1.3" / "1.3.1" -> Version; null when not a version.</summary>
    public static Version? Parse(string? s)
    {
        s = s?.Trim().TrimStart('v', 'V');
        if (string.IsNullOrEmpty(s)) return null;
        if (!s.Contains('.')) s += ".0";
        return Version.TryParse(s, out var v) ? v : null;
    }

    /// <summary>a &gt; b, treating 1.3 == 1.3.0.</summary>
    public static bool Newer(string a, string b)
    {
        var va = Parse(a); var vb = Parse(b);
        if (va == null || vb == null) return false;
        static Version N(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        return N(va) > N(vb);
    }
}
