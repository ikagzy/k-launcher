using System.IO;
using System.Text.Json;
using KLauncher.Models;

namespace KLauncher.Services;

public static class CustomAppStore
{
    private sealed record Entry(string Name, string LaunchTarget);

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "K-Launcher");

    private static readonly string FilePath = Path.Combine(Folder, "custom-apps.json");
    private static readonly object Sync = new();

    public static IReadOnlyList<AppModel> Load()
    {
        lock (Sync)
        {
            try
            {
                if (!File.Exists(FilePath))
                    return [];

                var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? [];
                return entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Name)
                        && !string.IsNullOrWhiteSpace(entry.LaunchTarget)
                        && File.Exists(entry.LaunchTarget))
                    .Select(CreateApp)
                    .ToArray();
            }
            catch
            {
                return [];
            }
        }
    }

    public static bool Add(string name, string launchTarget)
    {
        name = name.Trim();
        launchTarget = Path.GetFullPath(launchTarget.Trim());
        if (name.Length == 0 || !File.Exists(launchTarget))
            return false;

        lock (Sync)
        {
            var entries = LoadEntriesUnsafe();
            if (entries.Any(entry => entry.LaunchTarget.Equals(launchTarget, StringComparison.OrdinalIgnoreCase)
                || entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return false;

            entries.Add(new Entry(name, launchTarget));
            SaveUnsafe(entries);
            return true;
        }
    }

    public static void Remove(AppModel app)
    {
        lock (Sync)
        {
            var entries = LoadEntriesUnsafe();
            entries.RemoveAll(entry => entry.LaunchTarget.Equals(app.LaunchTarget, StringComparison.OrdinalIgnoreCase)
                || entry.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase));
            SaveUnsafe(entries);
        }
    }

    private static AppModel CreateApp(Entry entry) => new()
    {
        Name = entry.Name,
        ExePath = entry.LaunchTarget,
        IconPath = entry.LaunchTarget,
        LaunchTarget = entry.LaunchTarget,
        IsLauncher = false,
        IsSpecialLogo = false,
        IsUserAdded = true,
        FallbackGlyph = "APP"
    };

    private static List<Entry> LoadEntriesUnsafe()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch
        {
            return [];
        }
    }

    private static void SaveUnsafe(List<Entry> entries)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
