using System.IO;

namespace KLauncher.Services;

/// <summary>
/// On/off switch for the main window fading out when it loses focus or is
/// minimized. Off keeps the window fully opaque and minimizes it directly.
/// </summary>
public static class WindowFadeSettings
{
    private static readonly string SettingsPath =
        LauncherStorage.GetFilePath("window-fade.txt");

    public static bool Enabled { get; private set; } = Load();

    public static event Action? Changed;

    public static void Set(bool enabled)
    {
        if (enabled == Enabled)
            return;

        Enabled = enabled;
        Save(enabled);
        Changed?.Invoke();
    }

    private static bool Load()
    {
        try
        {
            return !File.ReadAllText(SettingsPath).Trim().Equals("off", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static void Save(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, enabled ? "on" : "off");
        }
        catch
        {
            // A missing preference must never stop the launcher.
        }
    }
}
