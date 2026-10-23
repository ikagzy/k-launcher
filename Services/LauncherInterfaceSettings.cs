using System.IO;

namespace KLauncher.Services;

public enum LauncherInterfaceMode
{
    Grid,
    List,
    Overview
}

public static class LauncherInterfaceSettings
{
    private static readonly string SettingsPath =
        LauncherStorage.GetFilePath("interface.txt");

    public static LauncherInterfaceMode Load()
    {
        try
        {
            return File.ReadAllText(SettingsPath).Trim().ToLowerInvariant() switch
            {
                "list" or "launcher-list" => LauncherInterfaceMode.List,
                "overview" or "recent-apps" or "recentapps" => LauncherInterfaceMode.Overview,
                _ => LauncherInterfaceMode.Grid
            };
        }
        catch
        {
            return LauncherInterfaceMode.Grid;
        }
    }

    public static void Save(LauncherInterfaceMode mode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(
                SettingsPath,
                mode switch
                {
                    LauncherInterfaceMode.List => "list",
                    LauncherInterfaceMode.Overview => "overview",
                    _ => "grid"
                });
        }
        catch
        {
            // A missing preference must never stop the launcher.
        }
    }
}
