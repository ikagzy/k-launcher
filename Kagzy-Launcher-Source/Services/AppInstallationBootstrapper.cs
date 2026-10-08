using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace KLauncher.Services;

public static class AppInstallationBootstrapper
{
    private const string MutexName = @"Local\KLauncher.InstallationBootstrap";

    private static readonly string InstallationDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "K-Launcher");

    private static readonly string InstalledExecutablePath =
        Path.Combine(InstallationDirectory, "KLauncher.exe");

    public static bool InstallAndRelaunchIfNeeded(string[] arguments)
    {
        var currentExecutablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not locate the running K-Launcher executable.");

        if (PathsEqual(currentExecutablePath, InstalledExecutablePath))
            return false;

        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
                throw new TimeoutException("K-Launcher could not acquire its installation lock.");

            if (IsInstalledExecutableRunning())
            {
                if (!File.Exists(InstalledExecutablePath) ||
                    !FilesEqual(currentExecutablePath, InstalledExecutablePath))
                {
                    throw new InvalidOperationException(
                        "Close the currently running K-Launcher before installing this update, then run this EXE again.");
                }

                return true;
            }

            Directory.CreateDirectory(InstallationDirectory);
            if (!File.Exists(InstalledExecutablePath) ||
                !FilesEqual(currentExecutablePath, InstalledExecutablePath))
            {
                InstallExecutable(currentExecutablePath);
            }

            StartInstalledExecutable(arguments);
            return true;
        }
        finally
        {
            if (ownsMutex)
                mutex.ReleaseMutex();
        }
    }

    private static void InstallExecutable(string sourcePath)
    {
        var stagedPath = Path.Combine(
            InstallationDirectory,
            $"KLauncher.{Guid.NewGuid():N}.installing");
        try
        {
            File.Copy(sourcePath, stagedPath, overwrite: false);
            File.Move(stagedPath, InstalledExecutablePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(stagedPath))
                File.Delete(stagedPath);
        }
    }

    private static void StartInstalledExecutable(string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = InstalledExecutablePath,
            UseShellExecute = true,
            WorkingDirectory = InstallationDirectory
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("Windows did not start the installed K-Launcher.");

        process.Dispose();
    }

    private static bool IsInstalledExecutableRunning()
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(InstalledExecutablePath)))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule is { FileName: var path } &&
                        PathsEqual(path, InstalledExecutablePath))
                    {
                        return true;
                    }
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    throw new InvalidOperationException(
                        "Could not check whether K-Launcher is already running; installation was stopped to protect the existing app.",
                        ex);
                }
                catch (InvalidOperationException)
                {
                    // The process exited while its executable path was being read.
                }
            }
        }

        return false;
    }

    private static bool FilesEqual(string firstPath, string secondPath)
    {
        using var first = File.OpenRead(firstPath);
        using var second = File.OpenRead(secondPath);
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(first),
            SHA256.HashData(second));
    }

    private static bool PathsEqual(string firstPath, string secondPath) =>
        Path.GetFullPath(firstPath).Equals(
            Path.GetFullPath(secondPath),
            StringComparison.OrdinalIgnoreCase);
}
