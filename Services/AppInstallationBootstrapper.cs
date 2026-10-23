using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace KLauncher.Services;

public static class AppInstallationBootstrapper
{
    private const string MutexName = @"Local\KLauncher.InstallationBootstrap";

    // Shown in the info window, so people know where the launcher put itself.
    public static readonly string InstallationDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "K-Launcher");

    private static readonly string InstalledExecutablePath =
        Path.Combine(InstallationDirectory, "KLauncher.exe");

    private static readonly string InstalledAssemblyPath =
        Path.Combine(InstallationDirectory, "KLauncher.dll");

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
                    !AreInstalledFilesCurrent(currentExecutablePath))
                {
                    throw new InvalidOperationException(
                        "Close the currently running K-Launcher before installing this update, then run this EXE again.");
                }

                StartInstalledExecutable(WithPortableCleanup(arguments, currentExecutablePath));
                return true;
            }

            Directory.CreateDirectory(InstallationDirectory);
            if (!File.Exists(InstalledExecutablePath) ||
                !AreInstalledFilesCurrent(currentExecutablePath))
            {
                InstallExecutable(currentExecutablePath);
            }

            StartInstalledExecutable(WithPortableCleanup(arguments, currentExecutablePath));
            return true;
        }
        finally
        {
            if (ownsMutex)
                mutex.ReleaseMutex();
        }
    }

    public const string RemovePortableCopyArgument = "--remove-portable-copy";

    // The portable release is a single self-contained EXE (no side-by-side
    // DLL). Once it has installed itself, the installed copy removes it and
    // only the desktop shortcut is left.
    private static bool IsPortableBuild(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath) ?? string.Empty;
        return !File.Exists(Path.Combine(directory, Path.GetFileNameWithoutExtension(executablePath) + ".dll"));
    }

    private static string[] WithPortableCleanup(string[] arguments, string currentExecutablePath) =>
        IsPortableBuild(currentExecutablePath)
            ? [.. arguments, RemovePortableCopyArgument, currentExecutablePath]
            : arguments;

    /// <summary>
    /// Runs in the installed copy: deletes the portable EXE it was started
    /// from and returns the remaining arguments. The file is deleted only when
    /// it is byte-for-byte the installed EXE, so the argument cannot be used
    /// to remove anything else.
    /// </summary>
    public static string[] RemovePortableCopy(string[] arguments)
    {
        var index = Array.IndexOf(arguments, RemovePortableCopyArgument);
        if (index < 0)
            return arguments;

        var remaining = arguments.Where((_, position) => position != index && position != index + 1).ToArray();
        if (index + 1 >= arguments.Length)
            return remaining;

        var portablePath = arguments[index + 1];
        try
        {
            var currentPath = Environment.ProcessPath;
            if (!portablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(portablePath) ||
                currentPath is null ||
                PathsEqual(portablePath, currentPath) ||
                PathsEqual(portablePath, InstalledExecutablePath) ||
                !FilesEqual(portablePath, InstalledExecutablePath))
                return remaining;

            // The portable copy exits right after starting this one; wait for it.
            for (var attempt = 0; attempt < 40; attempt++)
            {
                try
                {
                    File.Delete(portablePath);
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(150);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(150);
                }
            }
        }
        catch
        {
            // Leaving the portable EXE behind is harmless.
        }

        return remaining;
    }

    private static void InstallExecutable(string sourcePath)
    {
        var sourceDirectory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException("Could not locate the K-Launcher output directory.");

        // The portable release is one self-contained EXE that may be run from
        // the Desktop or Downloads: copy only that file, never its folder.
        var sideBySideAssembly = Path.Combine(
            sourceDirectory,
            Path.GetFileNameWithoutExtension(sourcePath) + ".dll");
        if (!File.Exists(sideBySideAssembly))
        {
            Directory.CreateDirectory(InstallationDirectory);
            var stagedExecutable = InstalledExecutablePath + $".{Guid.NewGuid():N}.installing";
            try
            {
                File.Copy(sourcePath, stagedExecutable, overwrite: true);
                File.Move(stagedExecutable, InstalledExecutablePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(stagedExecutable))
                    File.Delete(stagedExecutable);
            }

            return;
        }

        var stagedDirectory = Path.Combine(
            InstallationDirectory,
            $"KLauncher.{Guid.NewGuid():N}.installing");

        try
        {
            CopyDirectory(sourceDirectory, stagedDirectory);

            Directory.CreateDirectory(InstallationDirectory);
            CopyDirectory(stagedDirectory, InstallationDirectory);

            if (!File.Exists(InstalledExecutablePath))
                throw new InvalidOperationException("K-Launcher executable installation did not complete.");
        }
        finally
        {
            if (Directory.Exists(stagedDirectory))
                Directory.Delete(stagedDirectory, recursive: true);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var destinationPath = Path.Combine(
                destinationDirectory,
                Path.GetFileName(file));
            File.Copy(file, destinationPath, overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var destinationPath = Path.Combine(
                destinationDirectory,
                Path.GetFileName(directory));
            CopyDirectory(directory, destinationPath);
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

    private static bool AreInstalledFilesCurrent(string sourceExecutablePath)
    {
        var sourceDirectory = Path.GetDirectoryName(sourceExecutablePath)
            ?? throw new InvalidOperationException("Could not locate the K-Launcher output directory.");
        var sourceAssemblyPath = Path.Combine(
            sourceDirectory,
            Path.GetFileNameWithoutExtension(sourceExecutablePath) + ".dll");

        // Framework-dependent builds have a side-by-side DLL, while the
        // portable release is a single-file executable. Compare the DLL when
        // it exists and fall back to comparing the executable for single-file
        // builds.
        if (File.Exists(sourceAssemblyPath) && File.Exists(InstalledAssemblyPath))
        {
            return FilesEqual(sourceExecutablePath, InstalledExecutablePath) &&
                   FilesEqual(sourceAssemblyPath, InstalledAssemblyPath);
        }

        return File.Exists(sourceExecutablePath) &&
               File.Exists(InstalledExecutablePath) &&
               FilesEqual(sourceExecutablePath, InstalledExecutablePath);
    }

    private static bool PathsEqual(string firstPath, string secondPath) =>
        Path.GetFullPath(firstPath).Equals(
            Path.GetFullPath(secondPath),
            StringComparison.OrdinalIgnoreCase);
}
