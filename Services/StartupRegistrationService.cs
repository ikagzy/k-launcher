using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace KLauncher.Services;

internal static class StartupRegistrationService
{
    private const string RegistryRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValueName = "KLauncher";

    public static bool IsEnabled
    {
        get
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RegistryRunKey);
            return runKey?.GetValue(RegistryValueName) is string command &&
                   command.Equals(GetCommandLine(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RegistryRunKey, writable: true)
                ?? throw new InvalidOperationException("Could not open the current user's startup registry key.");
            runKey.SetValue(RegistryValueName, GetCommandLine(), RegistryValueKind.String);
            return;
        }

        using var existingRunKey = Registry.CurrentUser.OpenSubKey(RegistryRunKey, writable: true);
        existingRunKey?.DeleteValue(RegistryValueName, throwOnMissingValue: false);
    }

    private static string GetCommandLine()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not locate the K-Launcher executable.");

        if (Path.GetFileNameWithoutExtension(executablePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new InvalidOperationException("Could not locate the K-Launcher application assembly.");

            return $"\"{executablePath}\" \"{assemblyPath}\"";
        }

        return $"\"{executablePath}\"";
    }
}
