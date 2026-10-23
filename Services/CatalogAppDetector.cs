using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using KLauncher.Models;

namespace KLauncher.Services;

/// <summary>
/// Finds apps from the batch-install catalog that are installed on this PC,
/// however they were installed, so they show up in the launcher by themselves.
/// The Start Menu shortcut is the most reliable pointer to an app's main exe;
/// the uninstall entry's icon is the fallback.
/// </summary>
internal static class CatalogAppDetector
{
    private static readonly string[] ShortcutNoise =
        ["uninstall", "kaldır", "readme", "help", "website", "documentation", "release notes",
         "manual", "faq", "license", "changelog", "what's new", "safe mode", "debug"];

    private static readonly string[] ExeNoise =
        ["unins", "uninstall", "setup", "update", "updater", "crashpad", "crashreport", "helper", "elevate"];

    private sealed record Shortcut(string Name, string Normalized, string Path);
    private sealed record UninstallEntry(string Name, string? Icon);

    public static IEnumerable<AppModel> Detect(
        IReadOnlyCollection<AppModel> alreadyFound,
        Func<string, string, AppModel> make,
        CancellationToken token)
    {
        var knownNames = new HashSet<string>(alreadyFound.Select(app => Normalize(app.Name)));
        var knownExes = new HashSet<string>(
            alreadyFound.Select(app => app.ExePath).Where(path => !string.IsNullOrWhiteSpace(path)),
            StringComparer.OrdinalIgnoreCase);

        var shortcuts = LoadShortcuts();
        var uninstall = new Lazy<List<UninstallEntry>>(LoadUninstallEntries);
        var results = new List<AppModel>();

        object? shell = null;
        try
        {
            foreach (var package in WingetService.PopularPackages)
            {
                token.ThrowIfCancellationRequested();
                if (!package.Launchable)
                    continue;

                var normalized = Normalize(package.Name);
                if (normalized.Length < 2 || knownNames.Contains(normalized))
                    continue;

                var exe = FromShortcuts(package.Name, normalized, shortcuts, ref shell)
                          ?? FromWingetPortable(package.Id, normalized)
                          ?? FromUninstall(package.Name, uninstall.Value);
                if (exe is null || !knownExes.Add(exe))
                    continue;

                knownNames.Add(normalized);
                results.Add(make(package.Name, exe));
            }
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }

        return results;
    }

    private static string? FromShortcuts(string name, string normalized, List<Shortcut> shortcuts, ref object? shell)
    {
        // Exact name first ("Notepad++"), then the shortest one that starts
        // with it ("VLC media player" for VLC, "7-Zip File Manager" for 7-Zip).
        var candidates = shortcuts
            .Where(shortcut => shortcut.Normalized == normalized ||
                               (shortcut.Normalized.StartsWith(normalized, StringComparison.Ordinal) &&
                                shortcut.Name.Length > name.Length &&
                                !char.IsLetterOrDigit(shortcut.Name[name.Length])))
            .OrderBy(shortcut => shortcut.Normalized == normalized ? 0 : 1)
            .ThenBy(shortcut => shortcut.Normalized.Length);

        foreach (var shortcut in candidates)
        {
            var target = ResolveShortcut(shortcut.Path, ref shell);
            if (IsUsableExe(target))
                return Path.GetFullPath(target!);
        }

        return null;
    }

    // Portable packages (Rufus, CPU-Z ZIPs, …) have no shortcut and no
    // uninstall icon: winget unpacks them to WinGet\Packages\<Id>_<source>.
    private static readonly string[] WingetPackageRoots =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinGet", "Packages"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WinGet", "Packages")
    ];

    private static string? FromWingetPortable(string packageId, string normalizedName)
    {
        foreach (var root in WingetPackageRoots.Where(Directory.Exists))
        {
            try
            {
                foreach (var folder in Directory.EnumerateDirectories(root, packageId + "_*"))
                {
                    var best = Directory.EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories)
                        .Where(IsUsableExe)
                        .Select(path => (Path: path, Name: Normalize(Path.GetFileNameWithoutExtension(path)),
                                         Depth: path.Count(character => character == Path.DirectorySeparatorChar)))
                        .OrderBy(file => file.Name.Contains(normalizedName) || normalizedName.Contains(file.Name) ? 0 : 1)
                        .ThenBy(file => file.Depth)
                        .ThenByDescending(file => new FileInfo(file.Path).Length)
                        .Select(file => file.Path)
                        .FirstOrDefault();
                    if (best is not null)
                        return Path.GetFullPath(best);
                }
            }
            catch
            {
                // An unreadable folder must not stop the scan.
            }
        }

        return null;
    }

    private static string? FromUninstall(string name, List<UninstallEntry> entries)
    {
        var pattern = new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(name) + @"(?:\s.*)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var entry in entries)
        {
            if (!pattern.IsMatch(entry.Name))
                continue;

            if (IsUsableExe(entry.Icon))
                return Path.GetFullPath(entry.Icon!);
        }

        return null;
    }

    private static bool IsUsableExe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
            return false;

        var file = Path.GetFileNameWithoutExtension(path);
        return !ExeNoise.Any(noise => file.Contains(noise, StringComparison.OrdinalIgnoreCase));
    }

    private static List<Shortcut> LoadShortcuts()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        var shortcuts = new List<Shortcut>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (ShortcutNoise.Any(noise => name.Contains(noise, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    shortcuts.Add(new Shortcut(name, Normalize(name), path));
                }
            }
            catch
            {
                // A folder we cannot read must not stop the scan.
            }
        }

        return shortcuts;
    }

    private static string? ResolveShortcut(string path, ref object? shell)
    {
        object? shortcutObject = null;
        try
        {
            if (shell is null)
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null)
                    return null;
                shell = Activator.CreateInstance(shellType);
            }

            dynamic host = shell!;
            shortcutObject = host.CreateShortcut(path);
            dynamic shortcut = shortcutObject!;
            return shortcut.TargetPath as string;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (shortcutObject is not null && Marshal.IsComObject(shortcutObject))
                Marshal.FinalReleaseComObject(shortcutObject);
        }
    }

    private static List<UninstallEntry> LoadUninstallEntries()
    {
        var entries = new List<UninstallEntry>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var root = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (root is null)
                    continue;

                foreach (var subName in root.GetSubKeyNames())
                {
                    using var sub = root.OpenSubKey(subName);
                    var display = sub?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(display))
                        continue;
                    entries.Add(new UninstallEntry(display.Trim(), CleanIconPath(sub?.GetValue("DisplayIcon") as string)));
                }
            }
            catch
            {
                // Registry views can be missing or locked.
            }
        }

        return entries;
    }

    private static string? CleanIconPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var path = value.Trim().Trim('"');
        var comma = path.LastIndexOf(',');
        if (comma > 0 && path[(comma + 1)..].All(character => char.IsDigit(character) || character is '-' || char.IsWhiteSpace(character)))
            path = path[..comma];
        return Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
    }

    private static string Normalize(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
