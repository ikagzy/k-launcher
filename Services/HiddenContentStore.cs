using System.IO;
using System.Text.Json;

namespace KLauncher.Services;

public static class HiddenContentStore
{
    private sealed record HiddenContentSet(string Name, string LaunchTarget);
    private sealed record HiddenContentItem(
        string AppName,
        string AppLaunchTarget,
        string Kind,
        string Name,
        string Target);

    private static readonly string Folder = LauncherStorage.DataDirectory;
    private static readonly string FilePath = LauncherStorage.GetFilePath("hidden-content.json");
    private static readonly string ItemsFilePath = LauncherStorage.GetFilePath("hidden-content-items.json");
    private static readonly object Sync = new();

    public static bool IsHidden(string name, string launchTarget)
    {
        lock (Sync)
            return LoadUnsafe().Any(item => SameApp(item, name, launchTarget));
    }

    public static void SetHidden(string name, string launchTarget, bool hidden)
    {
        lock (Sync)
        {
            var items = LoadUnsafe();
            items.RemoveAll(item => SameApp(item, name, launchTarget));

            if (hidden)
                items.Add(new HiddenContentSet(name, launchTarget));

            SaveUnsafe(items);
        }
    }

    public static bool IsItemHidden(
        string appName,
        string appLaunchTarget,
        string kind,
        string name,
        string target)
    {
        lock (Sync)
        {
            return LoadItemsUnsafe().Any(item => SameItem(
                item,
                appName,
                appLaunchTarget,
                kind,
                name,
                target));
        }
    }

    public static bool HasHiddenItems(string appName, string appLaunchTarget)
    {
        lock (Sync)
        {
            return LoadItemsUnsafe().Any(item =>
                item.AppName.Equals(appName, StringComparison.OrdinalIgnoreCase) &&
                item.AppLaunchTarget.Equals(appLaunchTarget, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static void SetItemHidden(
        string appName,
        string appLaunchTarget,
        string kind,
        string name,
        string target,
        bool hidden)
    {
        lock (Sync)
        {
            var items = LoadItemsUnsafe();
            items.RemoveAll(item => SameItem(
                item,
                appName,
                appLaunchTarget,
                kind,
                name,
                target));

            if (hidden)
            {
                items.Add(new HiddenContentItem(
                    appName,
                    appLaunchTarget,
                    kind,
                    name,
                    target));
            }

            SaveItemsUnsafe(items);
        }
    }

    public static void ClearItems(string appName, string appLaunchTarget)
    {
        lock (Sync)
        {
            var items = LoadItemsUnsafe();
            items.RemoveAll(item =>
                item.AppName.Equals(appName, StringComparison.OrdinalIgnoreCase) &&
                item.AppLaunchTarget.Equals(appLaunchTarget, StringComparison.OrdinalIgnoreCase));
            SaveItemsUnsafe(items);
        }
    }

    private static bool SameApp(HiddenContentSet item, string name, string launchTarget) =>
        item.LaunchTarget.Equals(launchTarget, StringComparison.OrdinalIgnoreCase) &&
        item.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static bool SameItem(
        HiddenContentItem item,
        string appName,
        string appLaunchTarget,
        string kind,
        string name,
        string target) =>
        item.AppName.Equals(appName, StringComparison.OrdinalIgnoreCase) &&
        item.AppLaunchTarget.Equals(appLaunchTarget, StringComparison.OrdinalIgnoreCase) &&
        item.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) &&
        item.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
        item.Target.Equals(target, StringComparison.OrdinalIgnoreCase);

    private static List<HiddenContentSet> LoadUnsafe()
    {
        if (!File.Exists(FilePath))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<HiddenContentSet>>(
                       File.ReadAllText(FilePath)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void SaveUnsafe(List<HiddenContentSet> items)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(items, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        }
        catch
        {
            // Hiding content is optional and must not interrupt the launcher.
        }
    }

    private static List<HiddenContentItem> LoadItemsUnsafe()
    {
        if (!File.Exists(ItemsFilePath))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<HiddenContentItem>>(
                       File.ReadAllText(ItemsFilePath)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void SaveItemsUnsafe(List<HiddenContentItem> items)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(
                ItemsFilePath,
                JsonSerializer.Serialize(items, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        }
        catch
        {
            // Per-item hiding is optional and must not interrupt the launcher.
        }
    }
}
