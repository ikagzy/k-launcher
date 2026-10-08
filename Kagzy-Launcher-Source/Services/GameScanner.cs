using System.IO;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;
using KLauncher.Models;

namespace KLauncher.Services;

public sealed class GameScanner
{
    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string CommonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public Task<IReadOnlyList<AppModel>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(cancellationToken), cancellationToken);

    private IReadOnlyList<AppModel> Scan(CancellationToken token)
    {
        var found = new Dictionary<string, AppModel>(StringComparer.OrdinalIgnoreCase);

        AddLauncher(found, "Steam", true, "STEAM", [
            Path.Combine(ProgramFilesX86, "Steam", "steam.exe"),
            Path.Combine(ProgramFiles, "Steam", "steam.exe")],
            ["steam.exe"], ["Steam"], ["Valve Steam"]);

        AddLauncher(found, "Epic Games", true, "EPIC", [
            Path.Combine(ProgramFilesX86, "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"),
            Path.Combine(ProgramFiles, "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe")],
            ["EpicGamesLauncher.exe"], ["Epic Games Launcher", "Epic Games"], ["Epic Games"]);

        AddLauncher(found, "EA App", true, "EA", [
            @"C:\Program Files\Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe",
            @"C:\Program Files (x86)\Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe"],
            ["EADesktop.exe"], ["EA app", "EA Desktop"], ["Electronic Arts"]);

        AddLauncher(found, "Ubisoft Connect", true, "UBI", [
            @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe",
            @"C:\Program Files\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe"],
            ["UbisoftConnect.exe"], ["Ubisoft Connect", "Ubisoft Game Launcher"], ["Ubisoft"]);

        AddLauncher(found, "Battle.net", true, "BNET", [
            @"C:\Program Files (x86)\Battle.net\Battle.net Launcher.exe",
            @"C:\Program Files\Battle.net\Battle.net Launcher.exe"],
            ["Battle.net Launcher.exe"], ["Battle.net", "Blizzard Battle.net"], ["Blizzard Entertainment", "Blizzard"]);

        AddLauncher(found, "GOG Galaxy", true, "GOG", [
            @"C:\Program Files (x86)\GOG Galaxy\GalaxyClient.exe",
            @"C:\Program Files\GOG Galaxy\GalaxyClient.exe"],
            ["GalaxyClient.exe"], ["GOG GALAXY", "GOG Galaxy"], ["GOG.com", "GOG"]);

        AddLauncher(found, "Riot Client", true, "RIOT", [
            @"C:\Riot Games\Riot Client\RiotClientServices.exe",
            Path.Combine(ProgramFiles, "Riot Games", "Riot Client", "RiotClientServices.exe"),
            Path.Combine(ProgramFilesX86, "Riot Games", "Riot Client", "RiotClientServices.exe")],
            ["RiotClientServices.exe"], ["Riot Client"], ["Riot Games"]);

        AddLauncher(found, "Rockstar Games Launcher", true, "R*", [
            @"C:\Program Files\Rockstar Games\Launcher\Launcher.exe",
            @"C:\Program Files (x86)\Rockstar Games\Launcher\Launcher.exe"],
            ["Launcher.exe"], ["Rockstar Games Launcher"], ["Rockstar Games"] , new[] { Path.Combine(ProgramFiles, "Rockstar Games") });

        AddLauncher(found, "Amazon Games", true, "AMZ", [
            @"C:\Program Files (x86)\Amazon Games\App\Amazon Games.exe",
            @"C:\Program Files\Amazon Games\App\Amazon Games.exe"],
            ["Amazon Games.exe"], ["Amazon Games"], ["Amazon Games"], new[] { Path.Combine(ProgramFilesX86, "Amazon Games"), Path.Combine(ProgramFiles, "Amazon Games") });

        AddLauncher(found, "itch.io", true, "ITCH", [
            Path.Combine(LocalAppData, "itch", "itch.exe"),
            Path.Combine(LocalAppData, "itch", "app-itch.exe")],
            ["itch.exe"], ["itch", "itch app"], ["itch corp"], new[] { Path.Combine(LocalAppData, "itch") });

        AddLauncher(found, "HoYoPlay", true, "HOYO", [
            @"C:\Program Files\HoYoPlay\launcher.exe",
            @"C:\Program Files\HoYoPlay\launcher\launcher.exe",
            @"C:\Program Files (x86)\HoYoPlay\launcher.exe"],
            ["launcher.exe"], ["HoYoPlay"], ["HoYoverse", "miHoYo"], new[] { Path.Combine(ProgramFiles, "HoYoPlay"), Path.Combine(ProgramFilesX86, "HoYoPlay") });

        AddOperaGx(found);

        AddApp(found, "Discord", "DC", FindDiscord(), ["Discord.exe"], ["Discord"], ["Discord Inc.", "Discord"]);
        AddApp(found, "Medal", "MEDAL", [
            Path.Combine(LocalAppData, "Medal", "Medal.exe"),
            Path.Combine(LocalAppData, "Medal", "MedalApp.exe"),
            Path.Combine(ProgramFiles, "Medal", "Medal.exe")], ["Medal.exe", "MedalApp.exe"], ["Medal"], ["Medal.tv", "Medal"]);

        // Common desktop apps are discovered from known install locations and
        // uninstall metadata only. Arbitrary Start Menu shortcuts are not imported.
        AddApp(found, "WhatsApp", "WA", [
            Path.Combine(LocalAppData, "WhatsApp", "WhatsApp.exe"),
            Path.Combine(ProgramFiles, "WhatsApp", "WhatsApp.exe")],
            ["WhatsApp.exe"], ["WhatsApp"], ["WhatsApp LLC"]);
        AddApp(found, "Telegram", "TG", [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Telegram Desktop", "Telegram.exe"),
            Path.Combine(LocalAppData, "Programs", "Telegram Desktop", "Telegram.exe")],
            ["Telegram.exe"], ["Telegram Desktop", "Telegram"], ["Telegram FZ-LLC", "Telegram"]);
        AddApp(found, "Zoom", "ZOOM", [
            Path.Combine(ProgramFiles, "Zoom", "bin", "Zoom.exe"),
            Path.Combine(ProgramFilesX86, "Zoom", "bin", "Zoom.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zoom", "bin", "Zoom.exe")],
            ["Zoom.exe"], ["Zoom Workplace", "Zoom Meetings", "Zoom"], ["Zoom Video Communications"]);
        AddApp(found, "Microsoft Teams", "TEAMS", [
            Path.Combine(LocalAppData, "Microsoft", "WindowsApps", "ms-teams.exe"),
            Path.Combine(LocalAppData, "Programs", "Microsoft Teams", "current", "Teams.exe"),
            Path.Combine(ProgramFiles, "WindowsApps", "MSTeams_8wekyb3d8bbwe", "ms-teams.exe")],
            ["ms-teams.exe", "Teams.exe"], ["Microsoft Teams"], []);
        AddApp(found, "Slack", "SLACK", [
            Path.Combine(LocalAppData, "slack", "slack.exe"),
            Path.Combine(LocalAppData, "Programs", "Slack", "slack.exe")],
            ["slack.exe"], ["Slack"], ["Slack Technologies"]);
        AddApp(found, "Signal", "SIGNAL", [
            Path.Combine(LocalAppData, "Programs", "signal-desktop", "Signal.exe"),
            Path.Combine(LocalAppData, "Programs", "Signal", "Signal.exe")],
            ["Signal.exe"], ["Signal"], ["Signal Messenger"]);
        AddApp(found, "VLC media player", "VLC", [
            Path.Combine(ProgramFiles, "VideoLAN", "VLC", "vlc.exe"),
            Path.Combine(ProgramFilesX86, "VideoLAN", "VLC", "vlc.exe")],
            ["vlc.exe"], ["VLC media player", "VLC"], ["VideoLAN"]);
        AddApp(found, "Google Drive", "DRIVE", [
            Path.Combine(ProgramFiles, "Google", "Drive File Stream", "GoogleDriveFS.exe"),
            Path.Combine(ProgramFilesX86, "Google", "Drive File Stream", "GoogleDriveFS.exe"),
            Path.Combine(LocalAppData, "Google", "Drive File Stream", "GoogleDriveFS.exe")],
            ["GoogleDriveFS.exe"], ["Google Drive", "Drive for desktop"], ["Google LLC", "Google"]);
        AddApp(found, "OBS Studio", "OBS", [
            Path.Combine(ProgramFiles, "obs-studio", "bin", "64bit", "obs64.exe"),
            Path.Combine(ProgramFilesX86, "obs-studio", "bin", "64bit", "obs64.exe")],
            ["obs64.exe"], ["OBS Studio"], ["OBS Project"]);
        var minecraftRoots = new[]
            {
                Path.Combine(ProgramFilesX86, "Minecraft Launcher"),
                Path.Combine(ProgramFiles, "Minecraft Launcher")
            }
            .Concat(FindXboxGamesRoots("Minecraft Launcher"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var minecraftPaths = minecraftRoots
            .SelectMany(root => new[]
            {
                Path.Combine(root, "Content", "GameLaunchHelper.exe"),
                Path.Combine(root, "MinecraftLauncher.exe"),
                Path.Combine(root, "Content", "Minecraft.exe")
            })
            .ToArray();
        AddApp(found, "Minecraft Launcher", "MC", minecraftPaths,
            ["GameLaunchHelper.exe", "MinecraftLauncher.exe", "Minecraft.exe"],
            ["Minecraft Launcher"], ["Mojang", "Microsoft"], minecraftRoots);
        AddApp(found, "Spotify", "SPOT", [
            Path.Combine(LocalAppData, "Spotify", "Spotify.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe")], ["Spotify.exe"], ["Spotify"], ["Spotify"]);
        AddApp(found, "VS Code", "CODE", [
            Path.Combine(LocalAppData, "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(ProgramFiles, "Microsoft VS Code", "Code.exe"),
            Path.Combine(ProgramFilesX86, "Microsoft VS Code", "Code.exe")], ["Code.exe"], ["Visual Studio Code", "Microsoft Visual Studio Code"], ["Microsoft Corporation", "Microsoft"]);

        AddApp(found, "Google Chrome", "CHROME", [
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Path.Combine(LocalAppData, "Google", "Chrome", "Application", "chrome.exe")
        ], ["chrome.exe"], ["Google Chrome"], ["Google LLC", "Google"]);

        AddApp(found, "Microsoft Edge", "EDGE", [
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
        ], ["msedge.exe"], ["Microsoft Edge"], ["Microsoft"]);

        AddApp(found, "Mozilla Firefox", "FIREFOX", [
            @"C:\Program Files\Mozilla Firefox\firefox.exe",
            @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe"
        ], ["firefox.exe"], ["Mozilla Firefox"], ["Mozilla"]);

        AddApp(found, "Brave", "BRAVE", [
            @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe",
            @"C:\Program Files (x86)\BraveSoftware\Brave-Browser\Application\brave.exe",
            Path.Combine(LocalAppData, "BraveSoftware", "Brave-Browser", "Application", "brave.exe")
        ], ["brave.exe"], ["Brave"], ["Brave Software"]);

        AddApp(found, "Vivaldi", "VIVALDI", [
            @"C:\Program Files\Vivaldi\Application\vivaldi.exe",
            @"C:\Program Files (x86)\Vivaldi\Application\vivaldi.exe",
            Path.Combine(LocalAppData, "Vivaldi", "Application", "vivaldi.exe")
        ], ["vivaldi.exe"], ["Vivaldi"], ["Vivaldi"]);
        ScanXbox(found);
        ScanKnownStoreApps(found);
        ScanSteam(found, token);
        ScanEpic(found, token);
        ScanRiot(found);
        ScanFolderGames(found, "EA App", [Path.Combine(ProgramFiles, "EA Games"), Path.Combine(ProgramFilesX86, "EA Games")], ["Electronic Arts", "EA"], token);
        ScanFolderGames(found, "Ubisoft Connect", [Path.Combine(ProgramFilesX86, "Ubisoft"), Path.Combine(ProgramFiles, "Ubisoft")], ["Ubisoft"], token);
        ScanFolderGames(found, "Amazon Games", [Path.Combine(ProgramFilesX86, "Amazon Games", "Library"), Path.Combine(ProgramFiles, "Amazon Games", "Library")], ["Amazon Games"], token);
        ScanFolderGames(found, "GOG Galaxy", [Path.Combine(ProgramFilesX86, "GOG Galaxy", "Games"), Path.Combine(ProgramFiles, "GOG Galaxy", "Games")], ["GOG.com", "GOG"], token);
        ScanRockstar(found, token);

        foreach (var customApp in CustomAppStore.Load())
        {
            if (!found.ContainsKey(customApp.Name))
                found[customApp.Name] = customApp;
        }

        WriteScanLog(found);
        return found.Values.OrderBy(x => x.Name).ToArray();
    }

    private static void AddLauncher(Dictionary<string, AppModel> found, string name, bool launcher, string fallback, string[] exactPaths, string[] exeNames, string[] displayPatterns, string[] publisherPatterns, string[]? searchRoots = null)
    {
        var exe = Existing(exactPaths) ?? ResolveFromAppPaths(exeNames) ?? ResolveFromUninstall(exeNames, displayPatterns, publisherPatterns) ?? ResolveFromRoots(exeNames, searchRoots ?? []);
        if (exe is null) return;
        var app = Make(name, exe, launcher, true, fallback, ResolveIconFromUninstall(displayPatterns));
        QuickLinkStore.Apply(app);
        found[name] = app;
    }

    private static void AddApp(Dictionary<string, AppModel> found, string name, string fallback, string[] exactPaths, string[] exeNames, string[] displayPatterns, string[] publisherPatterns, string[]? searchRoots = null)
    {
        var exe = Existing(exactPaths) ?? ResolveFromAppPaths(exeNames) ?? ResolveFromUninstall(exeNames, displayPatterns, publisherPatterns) ?? ResolveFromRoots(exeNames, searchRoots ?? []);
        if (exe is null) return;
        var app = Make(name, exe, false, false, fallback, ResolveIconFromUninstall(displayPatterns));
        QuickLinkStore.Apply(app);
        found[name] = app;
    }

    private static void AddOperaGx(Dictionary<string, AppModel> found)
    {
        var roots = new[]
        {
            Path.Combine(LocalAppData, "Programs", "Opera GX"),
            Path.Combine(ProgramFiles, "Opera GX"),
            Path.Combine(ProgramFilesX86, "Opera GX")
        };
        var exeNames = new[] { "launcher.exe", "opera.exe", "Opera GX.exe" };
        var exactPaths = roots
            .SelectMany(root => exeNames.Select(name => Path.Combine(root, name)))
            .ToArray();
        var exe = Existing(exactPaths)
            ?? ResolveFromUninstall(exeNames, ["Opera GX"], []);

        if (exe is null || !exe.Contains("Opera GX", StringComparison.OrdinalIgnoreCase))
            return;

        var app = Make("Opera GX", exe, false, false, "GX");
        QuickLinkStore.Apply(app);
        found[app.Name] = app;
    }

    private static AppModel Make(string name, string exe, bool isLauncher, bool special, string fallback, string? preferredIcon = null) => new()
    {
        Name = name,
        ExePath = exe,
        IconPath = GetAppIconPath(name, exe, preferredIcon),
        LaunchTarget = exe,
        LaunchArguments = string.Empty,
        IsLauncher = isLauncher,
        IsSpecialLogo = special,
        FallbackGlyph = fallback
    };

    private static string GetAppIconPath(string name, string exe, string? preferredIcon)
    {
        var assetName = name switch
        {
            "Medal" => "medal.png",
            "Steam" => "steam.svg",
            "Epic Games" => "epicgames.svg",
            "Discord" => "discord.svg",
            "Opera GX" => "operagx.svg",
            "Riot Client" => "riotgames.svg",
            _ => null
        };

        if (name.Equals("Minecraft Launcher", StringComparison.OrdinalIgnoreCase))
        {
            var appDirectory = Path.GetDirectoryName(exe);
            if (!string.IsNullOrWhiteSpace(appDirectory))
            {
                foreach (var logoName in new[] { "GraphicsLogo.png", "SmallLogo.png", "StoreLogo.png" })
                {
                    var logoPath = Path.Combine(appDirectory, logoName);
                    if (File.Exists(logoPath))
                        return logoPath;
                }
            }
        }

        if (assetName is not null)
        {
            var assetPath = AssetPath.Get(assetName);
            if (File.Exists(assetPath))
                return assetPath;
        }

        if (!string.IsNullOrWhiteSpace(preferredIcon) && File.Exists(preferredIcon))
            return preferredIcon;

        return File.Exists(exe) ? exe : string.Empty;
    }

    private static string? ResolveIconFromUninstall(string[] displayPatterns)
    {
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
                    var displayName = sub?.GetValue("DisplayName") as string ?? string.Empty;
                    if (!Matches(displayName, displayPatterns))
                        continue;

                    var icon = CleanPath(sub?.GetValue("DisplayIcon") as string);
                    if (icon is not null && File.Exists(icon))
                        return icon;
                }
            }
            catch { }
        }

        return null;
    }

    private static string[] FindDiscord()
    {
        var root = Path.Combine(LocalAppData, "Discord");
        if (!Directory.Exists(root)) return [];
        try { return Directory.EnumerateFiles(root, "Discord.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).ToArray(); }
        catch { return []; }
    }

    private static string[] FindXboxGamesRoots(string appFolderName)
    {
        return Environment.GetLogicalDrives()
            .Select(drive => Path.Combine(drive, "XboxGames", appFolderName))
            .Where(Directory.Exists)
            .ToArray();
    }

    private static string? Existing(IEnumerable<string> paths) => paths.FirstOrDefault(File.Exists);
    private static string? Existing(params string[] paths) => Existing((IEnumerable<string>)paths);

    private static string? ResolveFromAppPaths(string[] exeNames)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var exe in exeNames)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey($"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\{exe}");
                var path = CleanPath(key?.GetValue("") as string);
                if (path is not null && File.Exists(path))
                    return path;
            }
            catch { }
        }
        return null;
    }

    private static string? ResolveFromUninstall(string[] exeNames, string[] displayPatterns, string[] publisherPatterns)
    {
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
                    var display = sub?.GetValue("DisplayName") as string ?? string.Empty;
                    var publisher = sub?.GetValue("Publisher") as string ?? string.Empty;
                    if (!Matches(display, displayPatterns) && !Matches(publisher, publisherPatterns))
                        continue;

                    var icon = CleanPath(sub?.GetValue("DisplayIcon") as string);
                    if (icon is not null && File.Exists(icon)
                        && exeNames.Any(name => Path.GetFileName(icon).Equals(name, StringComparison.OrdinalIgnoreCase)))
                        return icon;

                    var install = sub?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(install))
                    {
                        var hit = ResolveFromRoots(exeNames, [install]);
                        if (hit is not null)
                            return hit;
                    }
                }
            }
            catch { }
        }
        return null;
    }

    private static bool Matches(string value, string[] patterns) =>
        patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static string? CleanPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var path = value.Trim().Trim('"');
        var comma = path.LastIndexOf(',');
        if (comma > 0 && path[(comma + 1)..].All(character => char.IsDigit(character) || char.IsWhiteSpace(character)))
            path = path[..comma];
        return path.Trim().Trim('"');
    }

    private static string? ResolveFromRoots(string[] exeNames, string[] roots)
    {
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var exe in exeNames)
                {
                    var exact = Directory.EnumerateFiles(root, exe, SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (exact is not null)
                        return exact;
                }

                foreach (var exe in exeNames)
                {
                    var match = Directory.EnumerateFiles(root, exe, SearchOption.AllDirectories)
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                    if (match is not null)
                        return match;
                }
            }
            catch { }
        }
        return null;
    }

    private static void ScanXbox(Dictionary<string, AppModel> found)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"$apps = Get-StartApps; $app = $apps | Where-Object { $_.Name -eq 'Xbox' } | Select-Object -First 1; if (-not $app) { $app = $apps | Where-Object { $_.Name -match '^Xbox( |$)' -and $_.Name -notmatch 'Game Bar|Accessories|Identity' } | Select-Object -First 1 }; if ($app) { $app.AppID }\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (p is null) return;
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(3000))
            {
                p.Kill(entireProcessTree: true);
                return;
            }

            var appId = output.GetAwaiter().GetResult().Trim();
            if (string.IsNullOrWhiteSpace(appId)) return;
            var app = Make("Xbox", "", true, true, "XBOX");
            app.LaunchTarget = $"shell:AppsFolder\\{appId}";
            var bundledIcon = AssetPath.Get("xbox.png");
            app.IconPath = File.Exists(bundledIcon) ? bundledIcon : app.LaunchTarget;
            QuickLinkStore.Apply(app);
            found[app.Name] = app;
        }
        catch { }
    }

    private static void ScanKnownStoreApps(Dictionary<string, AppModel> found)
    {
        const string command = "Get-StartApps | Where-Object { $_.Name -match '^(WhatsApp|Telegram( Desktop)?|Microsoft Teams|Zoom( Workplace)?|ChatGPT|Spotify|VLC( media player)?|Signal|YouTube|Instagram|TikTok|Facebook|Messenger|Google Maps)$' } | Select-Object Name,AppID | ConvertTo-Json -Compress";
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{command}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (process is null)
                return;

            var outputTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(4000))
            {
                process.Kill(entireProcessTree: true);
                return;
            }

            var output = outputTask.GetAwaiter().GetResult().Trim();
            if (output.Length == 0)
                return;

            using var document = JsonDocument.Parse(output);
            var items = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToArray()
                : [document.RootElement];

            foreach (var item in items)
            {
                var sourceName = GetString(item, "Name")?.Trim();
                var appId = GetString(item, "AppID")?.Trim();
                if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(appId))
                    continue;

                var name = sourceName switch
                {
                    "Telegram Desktop" => "Telegram",
                    "Zoom Workplace" => "Zoom",
                    "Zoom" => "Zoom",
                    "Messenger" => "Facebook Messenger",
                    "VLC" or "VLC media player" => "VLC media player",
                    _ => sourceName
                };
                if (found.ContainsKey(name))
                    continue;

                var target = $"shell:AppsFolder\\{appId}";
                var app = Make(name, string.Empty, false, false, name.ToUpperInvariant());
                app.LaunchTarget = target;
                app.IconPath = target;
                QuickLinkStore.Apply(app);
                found[name] = app;
            }
        }
        catch
        {
            // Classic desktop install detection continues if Start Apps cannot be read.
        }
    }

    private static void ScanSteam(Dictionary<string, AppModel> found, CancellationToken token)
    {
        if (!found.TryGetValue("Steam", out var steam)) return;
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in new[] { Path.Combine(ProgramFilesX86, "Steam"), Path.Combine(ProgramFiles, "Steam") }) if (Directory.Exists(p)) roots.Add(p);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(@"SOFTWARE\Valve\Steam");
            var install = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(install) && Directory.Exists(install)) roots.Add(install);
        } catch { }

        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
            var libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFile))
            {
                foreach (var line in SafeLines(libraryFile))
                {
                    var path = ExtractSecondQuoted(line);
                    if (line.Contains('"') && line.Contains("path", StringComparison.OrdinalIgnoreCase) && path is not null)
                    {
                        path = path.Replace("\\\\", "\\");
                        if (Directory.Exists(path)) libraries.Add(path);
                    }
                }
            }

            foreach (var lib in libraries)
            {
                var manifestDir = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(manifestDir)) continue;
                foreach (var manifest in Directory.EnumerateFiles(manifestDir, "appmanifest_*.acf"))
                {
                    token.ThrowIfCancellationRequested();
                    var text = SafeRead(manifest);

                    var name = ExtractVdf(text, "name");
                    var installdir = ExtractVdf(text, "installdir");
                    var iconHash = ExtractVdf(text, "icon");

                    if (string.IsNullOrWhiteSpace(name) ||
                        string.IsNullOrWhiteSpace(installdir))
                        continue;

                    var rootDir = Path.Combine(
                        manifestDir,
                        "common",
                        installdir);

                    var exe = FindBestGameExe(
                        rootDir,
                        name);

                    if (exe is not null)
                    {
                        string? iconUrl = null;

                        var fileName =
                            Path.GetFileNameWithoutExtension(manifest);

                        var appId = fileName.Replace(
                            "appmanifest_",
                            "",
                            StringComparison.OrdinalIgnoreCase);

                        if (!string.IsNullOrWhiteSpace(appId) &&
                            !string.IsNullOrWhiteSpace(iconHash))
                        {
                            iconUrl =
                                $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{appId}/{iconHash}.jpg";
                        }

                        var localIcon = GetBundledSteamIcon(appId) ?? FindSteamGameIcon(roots, appId);
                        var gameIcon = localIcon ?? GetSteamGameIconUrl(appId);

                        AddGame(
                            steam,
                            name,
                            exe,
                            iconPath: gameIcon ?? iconUrl,
                            launchTarget: steam.ExePath,
                            launchArguments: $"-applaunch {appId}");
                    }
                }
            }
        }
    }

    private static void ScanEpic(Dictionary<string, AppModel> found, CancellationToken token)
    {
        if (!found.TryGetValue("Epic Games", out var epic)) return;
        var dirs = new[]
        {
            Path.Combine(CommonAppData, "Epic", "EpicGamesLauncher", "Data", "Manifests"),
            Path.Combine(CommonAppData, "Epic", "EpicGamesLauncher", "Data", "Manifests")
        }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirs.Where(Directory.Exists))
        foreach (var file in Directory.EnumerateFiles(dir, "*.item"))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var name = GetString(root, "DisplayName");
                var appName = GetString(root, "AppName");
                var install = GetString(root, "InstallLocation");
                var launcher = GetString(root, "LaunchExecutable");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(install)) continue;
                var exe = !string.IsNullOrWhiteSpace(launcher) ? Path.Combine(install, launcher) : null;
                exe = exe is not null && File.Exists(exe) ? exe : FindBestGameExe(install, name);
                if (exe is not null)
                {
                    var launchArguments = string.IsNullOrWhiteSpace(appName)
                        ? string.Empty
                        : $"-com.epicgames.launcher://apps/{Uri.EscapeDataString(appName)}?action=launch&silent=true";

                    AddGame(
                        epic,
                        name,
                        exe,
                        iconPath: GetKnownGameIcon(name),
                        launchTarget: epic.ExePath,
                        launchArguments: launchArguments);
                }
            }
            catch { }
        }
    }

    private static void ScanRiot(Dictionary<string, AppModel> found)
    {
        if (!found.TryGetValue("Riot Client", out var riot)) return;
        var valorant = Existing(
            @"C:\Riot Games\VALORANT\live\VALORANT.exe",
            @"C:\Riot Games\VALORANT\live\ShooterGame\Binaries\Win64\VALORANT-Win64-Shipping.exe");
        var valorantShortcut = FindRiotShortcut("VALORANT");
        if (valorant is not null || valorantShortcut is not null)
        {
            AddGame(
                riot,
                "VALORANT",
                valorant ?? riot.ExePath,
                iconPath: valorant ?? GetKnownGameIcon("VALORANT"),
                launchTarget: riot.ExePath,
                launchArguments: string.Empty);
        }

        var league = Existing(@"C:\Riot Games\League of Legends\LeagueClient.exe");
        var leagueShortcut = FindRiotShortcut("League of Legends");
        if (league is not null || leagueShortcut is not null)
        {
            AddGame(
                riot,
                "League of Legends",
                league ?? riot.ExePath,
                iconPath: league ?? GetKnownGameIcon("League of Legends"),
                launchTarget: leagueShortcut ?? league ?? riot.ExePath);
        }

        var lor = Existing(@"C:\Riot Games\LoR\Live\Game\LoR.exe");
        if (lor is not null)
            AddGame(riot, "Legends of Runeterra", lor);
    }

    private static string? FindRiotShortcut(string gameName)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        }.Where(Directory.Exists);

        return roots
            .SelectMany(root => new[]
            {
                Path.Combine(root, $"{gameName}.lnk"),
                Path.Combine(root, "Riot Games", $"{gameName}.lnk")
            })
            .FirstOrDefault(File.Exists);
    }

    private static void ScanRockstar(Dictionary<string, AppModel> found, CancellationToken token)
    {
        if (!found.TryGetValue("Rockstar Games Launcher", out var launcher)) return;
        foreach (var root in new[] { Path.Combine(ProgramFiles, "Rockstar Games"), Path.Combine(ProgramFilesX86, "Rockstar Games") }.Where(Directory.Exists))
        {
            foreach (var exe in Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories).Where(x => !IsBadExe(x)))
            {
                token.ThrowIfCancellationRequested();
                var file = Path.GetFileNameWithoutExtension(exe);
                if (file.Equals("Launcher", StringComparison.OrdinalIgnoreCase)) continue;
                if (file.Contains("GTA", StringComparison.OrdinalIgnoreCase) || file.Equals("RDR2", StringComparison.OrdinalIgnoreCase) || file.Equals("PlayGTAV", StringComparison.OrdinalIgnoreCase))
                    AddGame(launcher, Path.GetFileName(Path.GetDirectoryName(exe) ?? file)!, exe);
            }
        }
    }

    private static void ScanFolderGames(Dictionary<string, AppModel> found, string launcherName, string[] roots, string[] publisherPatterns, CancellationToken token)
    {
        if (!found.TryGetValue(launcherName, out var launcher)) return;
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
                {
                    token.ThrowIfCancellationRequested();
                    var folderName = Path.GetFileName(dir);
                    if (folderName.Contains("launcher", StringComparison.OrdinalIgnoreCase) || folderName.Contains("client", StringComparison.OrdinalIgnoreCase) || folderName.Contains("cache", StringComparison.OrdinalIgnoreCase)) continue;
                    var exe = FindBestGameExe(dir, folderName);
                    if (exe is not null) AddGame(launcher, Path.GetFileName(dir), exe);
                }
            }
            catch { }
        }

        foreach (var entry in RegistryGameInstalls(publisherPatterns))
        {
            token.ThrowIfCancellationRequested();
            if (entry.installLocation is null || !Directory.Exists(entry.installLocation)) continue;
            var exe = FindBestGameExe(entry.installLocation, entry.name);
            if (exe is not null) AddGame(launcher, entry.name, exe);
        }
    }

    private static IEnumerable<(string name, string? installLocation)> RegistryGameInstalls(string[] publisherPatterns)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            using var baseKey = TryOpenBase(hive, view);
            using var root = baseKey?.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (root is null) continue;
            foreach (var subName in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(subName);
                var name = sub?.GetValue("DisplayName") as string;
                var publisher = sub?.GetValue("Publisher") as string ?? string.Empty;
                var install = sub?.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(install)) continue;
                if (!Matches(publisher, publisherPatterns)) continue;
                if (name.Contains("launcher", StringComparison.OrdinalIgnoreCase) || name.Contains("client", StringComparison.OrdinalIgnoreCase)) continue;
                if (seen.Add(name)) yield return (name, install);
            }
        }
    }

    private static RegistryKey? TryOpenBase(RegistryHive hive, RegistryView view)
    {
        try { return RegistryKey.OpenBaseKey(hive, view); } catch { return null; }
    }

    private static void AddGame(
        AppModel launcher,
        string name,
        string exe,
        string args = "",
        string? iconPath = null,
        string? launchTarget = null,
        string? launchArguments = null)
    {
        if (!File.Exists(exe))
            return;

        if (IsNonGame(name))
            return;

        if (launcher.Games.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return;

        launcher.Games.Add(new GameItem
        {
            Name = name,
            ExePath = exe,
            Arguments = args,
            IconPath = string.IsNullOrWhiteSpace(iconPath)
                ? exe
                : iconPath,
            LaunchTarget = launchTarget ?? exe,
            LaunchArguments = launchArguments ?? args
        });
    }

    private static string? FindSteamGameIcon(IEnumerable<string> steamRoots, string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return null;

        var fileNames = new[]
        {
            $"{appId}_icon.jpg",
            $"{appId}_icon.png",
            $"{appId}_logo.png"
        };

        foreach (var root in steamRoots)
        {
            var cache = Path.Combine(root, "appcache", "librarycache");
            foreach (var fileName in fileNames)
            {
                var path = Path.Combine(cache, fileName);
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }

    private static string? GetBundledSteamIcon(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return null;

        var path = AssetPath.Get(Path.Combine("Games", "Steam", $"{appId}.jpg"));
        return File.Exists(path) ? path : null;
    }

    private static string? GetSteamGameIconUrl(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return null;

        return $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg";
    }

    private static string? GetKnownGameIcon(string name)
    {
        if (name.Equals("Fortnite", StringComparison.OrdinalIgnoreCase))
            return "https://www.fortnite.com/favicon.ico";

        if (name.Contains("VALORANT", StringComparison.OrdinalIgnoreCase))
            return "https://playvalorant.com/favicon.ico";

        if (name.Contains("League of Legends", StringComparison.OrdinalIgnoreCase))
            return "https://www.leagueoflegends.com/favicon.ico";

        if (name.Contains("Minecraft", StringComparison.OrdinalIgnoreCase))
            return "https://www.minecraft.net/favicon.ico";

        return null;
    }
    private static bool IsNonGame(string name)
    {
        return name.Equals(
                   "Steamworks Common Redistributables",
                   StringComparison.OrdinalIgnoreCase)
            || name.Equals(
                   "Steam Linux Runtime",
                   StringComparison.OrdinalIgnoreCase)
            || name.Equals(
                   "Steam Linux Runtime 2.0",
                   StringComparison.OrdinalIgnoreCase)
            || name.Equals(
                   "Steam Linux Runtime 3.0",
                   StringComparison.OrdinalIgnoreCase)
            || name.Equals(
                   "Steam Runtime",
                   StringComparison.OrdinalIgnoreCase)
            || name.Equals(
                   "Proton 8.0",
                   StringComparison.OrdinalIgnoreCase)
            || name.Equals(
                   "Proton 9.0",
                   StringComparison.OrdinalIgnoreCase)
            || name.Contains(
                   "Redistributable",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindBestGameExe(string root, string gameName)
    {
        if (!Directory.Exists(root)) return null;
        try
        {
            var candidates = Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)
                .Where(x => !IsBadExe(x))
                .Take(400)
                .Select(x => (path: x, score: ScoreExe(x, gameName)))
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.path.Length)
                .FirstOrDefault();
            return candidates.score > 0 ? candidates.path : null;
        }
        catch { return null; }
    }

    private static int ScoreExe(string path, string gameName)
    {
        var file = Path.GetFileNameWithoutExtension(path);
        var score = 10;
        if (file.Equals(gameName, StringComparison.OrdinalIgnoreCase)) score += 120;
        var ng = Normalize(gameName);
        var nf = Normalize(file);
        if (!string.IsNullOrWhiteSpace(ng) && nf.Contains(ng, StringComparison.OrdinalIgnoreCase)) score += 60;
        foreach (var bad in new[] { "launcher", "updater", "update", "crash", "helper", "bootstrap", "unins", "repair", "eac", "easyanticheat" })
            if (file.Contains(bad, StringComparison.OrdinalIgnoreCase)) score -= 100;
        if (path.Contains("Binaries\\Win64", StringComparison.OrdinalIgnoreCase)) score += 25;
        if (file.EndsWith("-Win64-Shipping", StringComparison.OrdinalIgnoreCase)) score += 80;
        return score;
    }

    private static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).ToArray());
    private static bool IsBadExe(string path) => Path.GetFileNameWithoutExtension(path).Contains("unins", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractVdf(string text, string key)
    {
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2 && parts[0].Equals(key, StringComparison.OrdinalIgnoreCase)) return parts[1];
        }
        return null;
    }

    private static string? ExtractSecondQuoted(string line)
    {
        var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? parts[^1] : null;
    }

    private static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); } catch { return string.Empty; }
    }

    private static IEnumerable<string> SafeLines(string path)
    {
        try { return File.ReadLines(path); } catch { return []; }
    }

    private static string? GetString(JsonElement root, string property)
        => root.TryGetProperty(property, out var p) ? p.GetString() : null;

    private static void WriteScanLog(Dictionary<string, AppModel> found)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "K-Launcher");
            Directory.CreateDirectory(folder);
            var lines = found.Values.OrderBy(x => x.Name).Select(x => $"{x.Name} | {x.ExePath} | games={x.Games.Count} | links={x.QuickLinks.Count}");
            File.WriteAllLines(Path.Combine(folder, "scanner.log"), lines);
        }
        catch { }
    }
}





