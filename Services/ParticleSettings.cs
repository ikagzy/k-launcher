using System.IO;

namespace KLauncher.Services;

/// <summary>
/// On/off switch for the ambient background particles. Off removes the
/// particle shapes and the per-frame callback, so they cost nothing.
/// </summary>
public static class ParticleSettings
{
    private static readonly string SettingsPath =
        LauncherStorage.GetFilePath("particles.txt");

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
