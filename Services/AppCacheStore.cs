using System.IO;
using System.Text.Json;
using KLauncher.Models;

namespace KLauncher.Services;

/// <summary>
/// Disk-backed snapshot of the last successful application scan.
/// </summary>
public static class AppCacheStore
{
    private static readonly object Sync = new();
    private static readonly string FilePath = LauncherStorage.GetFilePath("app-cache.json");
    private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private sealed class CacheDocument
    {
        public DateTimeOffset SavedAtUtc { get; set; }
        public List<CachedApp> Apps { get; set; } = [];
    }

    private sealed class CachedApp
    {
        public string Name { get; set; } = string.Empty;
        public string ExePath { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
        public string LaunchTarget { get; set; } = string.Empty;
        public string LaunchArguments { get; set; } = string.Empty;
        public bool IsSpecialLogo { get; set; }
        public bool IsLauncher { get; set; }
        public bool IsUserAdded { get; set; }
        public string FallbackGlyph { get; set; } = "APP";
        public List<GameItem> Games { get; set; } = [];
        public List<QuickLink> QuickLinks { get; set; } = [];
    }

    public static bool TryLoad(out IReadOnlyList<AppModel> apps)
    {
        lock (Sync)
        {
            apps = [];
            try
            {
                if (!File.Exists(FilePath) ||
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(FilePath) > MaximumAge)
                {
                    return false;
                }

                var document = JsonSerializer.Deserialize<CacheDocument>(
                    File.ReadAllText(FilePath),
                    JsonOptions);
                if (document is null || document.Apps.Count == 0)
                    return false;

                apps = document.Apps.Select(ToAppModel).ToArray();
                return apps.Count > 0;
            }
            catch
            {
                apps = [];
                return false;
            }
        }
    }

    public static void Save(IEnumerable<AppModel> apps)
    {
        lock (Sync)
        {
            try
            {
                var document = new CacheDocument
                {
                    SavedAtUtc = DateTimeOffset.UtcNow,
                    Apps = apps.Select(ToCachedApp).ToList()
                };

                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var temporaryPath = FilePath + ".tmp";
                File.WriteAllText(
                    temporaryPath,
                    JsonSerializer.Serialize(document, JsonOptions));
                File.Move(temporaryPath, FilePath, overwrite: true);
            }
            catch
            {
                // Cache is an optimization; a storage failure must not break the launcher.
            }
        }
    }

    private static CachedApp ToCachedApp(AppModel app) => new()
    {
        Name = app.Name,
        ExePath = app.ExePath,
        IconPath = app.IconPath,
        LaunchTarget = app.LaunchTarget,
        LaunchArguments = app.LaunchArguments,
        IsSpecialLogo = app.IsSpecialLogo,
        IsLauncher = app.IsLauncher,
        IsUserAdded = app.IsUserAdded,
        FallbackGlyph = app.FallbackGlyph,
        Games = app.Games.ToList(),
        QuickLinks = app.QuickLinks.ToList()
    };

    private static AppModel ToAppModel(CachedApp app)
    {
        var model = new AppModel
        {
            Name = app.Name,
            ExePath = app.ExePath,
            IconPath = app.IconPath,
            LaunchTarget = app.LaunchTarget,
            LaunchArguments = app.LaunchArguments,
            IsSpecialLogo = app.IsSpecialLogo,
            IsLauncher = app.IsLauncher,
            IsUserAdded = app.IsUserAdded,
            FallbackGlyph = app.FallbackGlyph
        };

        foreach (var game in app.Games)
            model.Games.Add(game);
        foreach (var link in app.QuickLinks)
            model.QuickLinks.Add(link);

        return model;
    }
}
