using System.IO;

namespace KLauncher.Services;

/// <summary>
/// Keeps mutable launcher data beside the installed executable so the app does
/// not need to rebuild it from the system on every start. A legacy fallback is
/// retained for protected install locations.
/// </summary>
public static class LauncherStorage
{
    private static readonly object Sync = new();
    private static readonly string LegacyDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "K-Launcher");

    private static readonly string[] MigratedFiles =
    [
        "custom-apps.json",
        "hidden-apps.json",
        "quick-links.json",
        "quick-links-removed.json",
        "scanner.log",
        "app-cache.json"
    ];

    private static string? _dataDirectory;

    public static string DataDirectory
    {
        get
        {
            lock (Sync)
                return _dataDirectory ??= ResolveDataDirectory();
        }
    }

    public static string GetFilePath(string fileName) =>
        Path.Combine(DataDirectory, fileName);

    public static void Initialize() => _ = DataDirectory;

    private static string ResolveDataDirectory()
    {
        // Next to the EXE itself. AppContext.BaseDirectory would be the temp
        // folder a single-file build unpacks into, which changes per build.
        var executableDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var preferredDirectory = Path.Combine(executableDirectory, "Data");
        try
        {
            Directory.CreateDirectory(preferredDirectory);
            MigrateLegacyFiles(preferredDirectory);
            MigrateFromUnpackFolders(preferredDirectory);
            return preferredDirectory;
        }
        catch (UnauthorizedAccessException)
        {
            Directory.CreateDirectory(LegacyDirectory);
            return LegacyDirectory;
        }
        catch (IOException)
        {
            Directory.CreateDirectory(LegacyDirectory);
            return LegacyDirectory;
        }
    }

    // Earlier single-file releases saved settings in %TEMP%\.net\KLauncher\<build>\Data.
    // Bring the newest copy of each file over once, when it is still missing here.
    private static void MigrateFromUnpackFolders(string destinationDirectory)
    {
        try
        {
            var unpackRoot = Path.Combine(Path.GetTempPath(), ".net", "KLauncher");
            if (!Directory.Exists(unpackRoot))
                return;

            var newestByName = Directory.EnumerateDirectories(unpackRoot)
                .Select(build => Path.Combine(build, "Data"))
                .Where(Directory.Exists)
                .Where(folder => !Path.GetFullPath(folder).Equals(Path.GetFullPath(destinationDirectory), StringComparison.OrdinalIgnoreCase))
                .SelectMany(Directory.EnumerateFiles)
                .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(File.GetLastWriteTimeUtc).First());

            foreach (var source in newestByName)
            {
                var destination = Path.Combine(destinationDirectory, Path.GetFileName(source));
                if (File.Exists(destination))
                    continue;
                try { File.Copy(source, destination); }
                catch { }
            }
        }
        catch
        {
            // Optional migration; never block startup.
        }
    }

    private static void MigrateLegacyFiles(string destinationDirectory)
    {
        if (!Directory.Exists(LegacyDirectory) ||
            Path.GetFullPath(LegacyDirectory).Equals(
                Path.GetFullPath(destinationDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var fileName in MigratedFiles)
        {
            var sourcePath = Path.Combine(LegacyDirectory, fileName);
            var destinationPath = Path.Combine(destinationDirectory, fileName);
            if (!File.Exists(sourcePath) || File.Exists(destinationPath))
                continue;

            try
            {
                File.Copy(sourcePath, destinationPath);
            }
            catch
            {
                // One failed optional migration must not prevent startup.
            }
        }
    }
}
