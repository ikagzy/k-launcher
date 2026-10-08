using System.IO;
using System.Runtime.InteropServices;

namespace KLauncher.Services;

public static class DesktopShortcutService
{
    private const uint ShellChangeAssociationChanged = 0x08000000;

    private static readonly string ShortcutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "KLauncher.lnk");

    private static readonly string InstalledIconsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "K-Launcher",
        "ThemeIcons");

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    public static void UpdateForTheme(string themeId)
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not locate the running K-Launcher executable.");
        var bundledIconPath = AssetPath.Get(Path.Combine("ThemeIcons", $"{themeId}.ico"));
        if (!File.Exists(bundledIconPath))
            throw new FileNotFoundException($"The icon for theme '{themeId}' was not found.", bundledIconPath);

        Directory.CreateDirectory(InstalledIconsDirectory);
        var installedIconPath = Path.Combine(InstalledIconsDirectory, $"{themeId}.ico");
        File.Copy(bundledIconPath, installedIconPath, overwrite: true);
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is unavailable; the desktop shortcut cannot be updated.");
        object? shellObject = null;
        object? shortcutObject = null;

        try
        {
            shellObject = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Could not initialize Windows Script Host.");
            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(ShortcutPath);
            dynamic shortcut = shortcutObject;
            shortcut.TargetPath = executablePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            shortcut.IconLocation = $"{installedIconPath},0";
            shortcut.Save();
        }
        finally
        {
            if (shortcutObject is not null && Marshal.IsComObject(shortcutObject))
                Marshal.FinalReleaseComObject(shortcutObject);
            if (shellObject is not null && Marshal.IsComObject(shellObject))
                Marshal.FinalReleaseComObject(shellObject);
        }

        SHChangeNotify(ShellChangeAssociationChanged, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
