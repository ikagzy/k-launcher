using System.IO;
using System.Text.Json;

namespace KLauncher.Services;

public sealed record HiddenApp(string Name, string LaunchTarget);

public static class HiddenAppStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "K-Launcher");

    private static readonly string FilePath = Path.Combine(Folder, "hidden-apps.json");
    private static readonly object Sync = new();

    public static IReadOnlyList<HiddenApp> Load()
    {
        lock (Sync)
        {
            if (!File.Exists(FilePath))
                return [];

            return JsonSerializer.Deserialize<List<HiddenApp>>(File.ReadAllText(FilePath)) ?? [];
        }
    }

    public static IReadOnlyList<HiddenApp> Hide(string name, string launchTarget)
    {
        lock (Sync)
        {
            var apps = LoadUnsafe();
            if (!apps.Any(app => app.LaunchTarget.Equals(launchTarget, StringComparison.OrdinalIgnoreCase)))
                apps.Add(new HiddenApp(name, launchTarget));

            SaveUnsafe(apps);
            return apps;
        }
    }

    public static IReadOnlyList<HiddenApp> Restore(string launchTarget)
    {
        lock (Sync)
        {
            var apps = LoadUnsafe();
            apps.RemoveAll(app => app.LaunchTarget.Equals(launchTarget, StringComparison.OrdinalIgnoreCase));
            SaveUnsafe(apps);
            return apps;
        }
    }

    public static void RestoreAll()
    {
        lock (Sync)
            SaveUnsafe([]);
    }

    private static List<HiddenApp> LoadUnsafe()
    {
        if (!File.Exists(FilePath))
            return [];

        return JsonSerializer.Deserialize<List<HiddenApp>>(File.ReadAllText(FilePath)) ?? [];
    }

    private static void SaveUnsafe(List<HiddenApp> apps)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(apps, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
