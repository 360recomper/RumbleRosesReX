namespace RRXXLauncher.Tabs;

/// <summary>
/// A launcher tab. To add one (Mod packs, attire picker...), implement this and add it to the list in
/// MainForm.Tabs; nothing else needs to know about it.
/// </summary>
public interface ILauncherTab
{
    string Title { get; }
    Control View { get; }
    /// <summary>Called each time the tab becomes visible: re-read disk state here.</summary>
    void OnShown();
}
