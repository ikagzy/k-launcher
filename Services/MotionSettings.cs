using System.IO;
using System.Windows;

namespace KLauncher.Services;

public enum MotionSpeed
{
    Fast,
    Normal,
    Slow,
    Off
}

/// <summary>
/// One speed setting for every animation in the launcher. Code-built
/// animations ask <see cref="Ms"/> for their duration, so a change applies to
/// the next animation that starts. Template animations in KTheme.xaml read the
/// K.Fast / K.Normal / K.Press tokens, which are rewritten here.
/// </summary>
public static class MotionSettings
{
    private static readonly string SettingsPath =
        LauncherStorage.GetFilePath("motion.txt");

    // Base values of the motion tokens in Themes/KTheme.xaml.
    private static readonly (string Key, double Milliseconds)[] Tokens =
    [
        ("K.Fast", 120),
        ("K.Normal", 180),
        ("K.Press", 70)
    ];

    public static MotionSpeed Current { get; private set; } = Load();

    public static bool Enabled => Current != MotionSpeed.Off;

    public static double Scale => Current switch
    {
        MotionSpeed.Fast => 0.55,
        MotionSpeed.Slow => 1.6,
        MotionSpeed.Off => 0,
        _ => 1
    };

    public static event Action? Changed;

    /// <summary>Scales a design-time duration by the selected speed.</summary>
    public static TimeSpan Ms(double milliseconds) =>
        TimeSpan.FromMilliseconds(milliseconds * Scale);

    public static void Set(MotionSpeed speed)
    {
        if (speed == Current)
            return;

        Current = speed;
        Save(speed);
        ApplyThemeTokens();
        Changed?.Invoke();
    }

    /// <summary>
    /// Rewrites the duration tokens. Call before the first window is created:
    /// templates copy the token values when their style is first used.
    /// </summary>
    public static void ApplyThemeTokens()
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
            return;

        foreach (var (key, milliseconds) in Tokens)
        {
            var duration = new Duration(Ms(milliseconds));
            var owner = FindOwner(resources, key) ?? resources;
            owner[key] = duration;
        }
    }

    private static ResourceDictionary? FindOwner(ResourceDictionary dictionary, string key)
    {
        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (FindOwner(merged, key) is { } owner)
                return owner;
        }

        return dictionary.Contains(key) ? dictionary : null;
    }

    private static MotionSpeed Load()
    {
        try
        {
            return File.ReadAllText(SettingsPath).Trim().ToLowerInvariant() switch
            {
                "fast" => MotionSpeed.Fast,
                "slow" => MotionSpeed.Slow,
                "off" => MotionSpeed.Off,
                _ => MotionSpeed.Normal
            };
        }
        catch
        {
            return MotionSpeed.Normal;
        }
    }

    private static void Save(MotionSpeed speed)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, speed.ToString().ToLowerInvariant());
        }
        catch
        {
            // A missing preference must never stop the launcher.
        }
    }
}
