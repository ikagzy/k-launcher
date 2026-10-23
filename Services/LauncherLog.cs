using System.IO;

namespace KLauncher.Services;

/// <summary>
/// Small error/diagnostic log next to the other settings (Data\k-launcher.log).
/// Writes only when something worth knowing happens: unhandled errors and the
/// window situations where Windows plays its error beep. Capped at 256 KB; the
/// previous file is kept as k-launcher.old.log.
/// </summary>
public static class LauncherLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => LauncherStorage.GetFilePath("k-launcher.log");

    public static void Write(string category, string message)
    {
        try
        {
            lock (Gate)
            {
                var path = FilePath;
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(path, LauncherStorage.GetFilePath("k-launcher.old.log"), overwrite: true);

                // No personal data: the Windows user name in paths is masked.
                var userName = Environment.UserName;
                if (!string.IsNullOrEmpty(userName))
                    message = message.Replace(userName, "<kullanıcı>", StringComparison.OrdinalIgnoreCase);

                File.AppendAllText(
                    path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} v4.1 [{category}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break the launcher.
        }
    }

    public static void Error(string where, Exception exception) =>
        Write("HATA", $"{where}: {exception}");
}
