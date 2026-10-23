using System.IO;
using System.Text;

namespace KLauncher.Services;

/// <summary>
/// Small append-only log for batch installs so a stuck step can be diagnosed
/// afterwards: %LOCALAPPDATA%\K-Launcher\logs\toplu-kurulum.log
/// </summary>
public static class InstallLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "K-Launcher",
        "logs",
        "toplu-kurulum.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);

                File.AppendAllText(
                    FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never break an install.
        }
    }
}
