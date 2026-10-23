using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace KLauncher.Services;

public sealed record WingetPackage(string Id, string Name, string Category)
{
    /// <summary>False for runtimes and command-line tools that have nothing to open.</summary>
    public bool Launchable => WingetService.IsLaunchable(Id, Category);
}

public enum WingetInstallStatus
{
    Installed,
    AlreadyInstalled,
    Failed,
    Cancelled
}

public sealed record WingetInstallResult(WingetInstallStatus Status, string Message);

public enum WingetAction
{
    Install,
    Upgrade,
    Uninstall
}

/// <summary>A package winget reports as installed; Available is set when an update exists.</summary>
public sealed record WingetInstalledPackage(string Id, string Name, string Version, string? Available)
{
    public bool HasUpdate => !string.IsNullOrWhiteSpace(Available);
}

/// <summary>One-click selections. Ids must exist in <see cref="WingetService.PopularPackages"/>.</summary>
public sealed record WingetPreset(string Key, IReadOnlyList<string> PackageIds);

/// <summary>
/// Ninite-style installer built on Windows Package Manager (winget), which
/// ships with Windows 10/11. Packages come from the official winget source
/// and are installed silently one after another.
/// </summary>
public static class WingetService
{
    // winget exit codes (HRESULTs) that mean "nothing to do".
    private const int UpdateNotApplicable = unchecked((int)0x8A15002B);
    private const int PackageAlreadyInstalled = unchecked((int)0x8A150061);
    private const int NoPackageFound = unchecked((int)0x8A150014);
    private const int RebootRequiredToFinish = unchecked((int)0x8A150109);
    private const int RebootInitiated = unchecked((int)0x8A15010A);
    private const int InstallerHashMismatch = unchecked((int)0x8A150011);

    // Packages whose manifest points at a "latest" download URL break with a
    // hash mismatch every time the vendor ships a new build. These siblings
    // use versioned URLs and keep working.
    private static readonly Dictionary<string, string> AlternatePackageIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Google.Chrome"] = "Google.Chrome.EXE"
        };

    public static IReadOnlyList<WingetPackage> PopularPackages { get; } =
    [
        new("Google.Chrome.EXE", "Google Chrome", "Browsers"),
        new("Mozilla.Firefox", "Firefox", "Browsers"),
        new("Microsoft.Edge", "Microsoft Edge", "Browsers"),
        new("Brave.Brave", "Brave", "Browsers"),
        new("Opera.OperaGX", "Opera GX", "Browsers"),
        new("Opera.Opera", "Opera", "Browsers"),
        new("Vivaldi.Vivaldi", "Vivaldi", "Browsers"),
        new("LibreWolf.LibreWolf", "LibreWolf", "Browsers"),
        new("Waterfox.Waterfox", "Waterfox", "Browsers"),
        new("TorProject.TorBrowser", "Tor Browser", "Browsers"),

        new("Discord.Discord", "Discord", "Communication"),
        new("Telegram.TelegramDesktop", "Telegram", "Communication"),
        new("Zoom.Zoom", "Zoom", "Communication"),
        new("Microsoft.Teams", "Microsoft Teams", "Communication"),
        new("SlackTechnologies.Slack", "Slack", "Communication"),
        new("OpenWhisperSystems.Signal", "Signal", "Communication"),
        new("Mozilla.Thunderbird", "Thunderbird", "Communication"),
        new("Pidgin.Pidgin", "Pidgin", "Communication"),
        new("Element.Element", "Element", "Communication"),
        new("TeamSpeakSystems.TeamSpeakClient", "TeamSpeak", "Communication"),

        new("Valve.Steam", "Steam", "Gaming"),
        new("EpicGames.EpicGamesLauncher", "Epic Games Launcher", "Gaming"),
        new("ElectronicArts.EADesktop", "EA App", "Gaming"),
        new("Ubisoft.Connect", "Ubisoft Connect", "Gaming"),
        new("GOG.Galaxy", "GOG Galaxy", "Gaming"),
        new("Blizzard.BattleNet", "Battle.net", "Gaming"),
        new("Amazon.Games", "Amazon Games", "Gaming"),
        new("ItchIo.Itch", "itch", "Gaming"),
        new("Mojang.MinecraftLauncher", "Minecraft Launcher", "Gaming"),
        new("PrismLauncher.PrismLauncher", "Prism Launcher", "Gaming"),
        new("Overwolf.CurseForge", "CurseForge", "Gaming"),
        new("MedalB.V.Medal", "Medal", "Gaming"),
        new("Guru3D.Afterburner", "MSI Afterburner", "Gaming"),

        new("VideoLAN.VLC", "VLC", "Media"),
        new("Spotify.Spotify", "Spotify", "Media"),
        new("Apple.iTunes", "iTunes", "Media"),
        new("AIMP.AIMP", "AIMP", "Media"),
        new("PeterPawlowski.foobar2000", "foobar2000", "Media"),
        new("Winamp.Winamp", "Winamp", "Media"),
        new("CodecGuide.K-LiteCodecPack.Standard", "K-Lite Codec Pack", "Media"),
        new("GOMLab.GOMPlayer", "GOM Player", "Media"),
        new("clsid2.mpc-hc", "MPC-HC", "Media"),
        new("Daum.PotPlayer", "PotPlayer", "Media"),
        new("OBSProject.OBSStudio", "OBS Studio", "Media"),
        new("Audacity.Audacity", "Audacity", "Media"),
        new("HandBrake.HandBrake", "HandBrake", "Media"),
        new("KDE.Kdenlive", "Kdenlive", "Media"),
        new("Meltytech.Shotcut", "Shotcut", "Media"),

        new("BlenderFoundation.Blender", "Blender", "Imaging"),
        new("KDE.Krita", "Krita", "Imaging"),
        new("dotPDN.PaintDotNet", "Paint.NET", "Imaging"),
        new("GIMP.GIMP.3", "GIMP", "Imaging"),
        new("Inkscape.Inkscape", "Inkscape", "Imaging"),
        new("IrfanSkiljan.IrfanView", "IrfanView", "Imaging"),
        new("XnSoft.XnViewMP", "XnView MP", "Imaging"),
        new("FastStone.Viewer", "FastStone Image Viewer", "Imaging"),
        new("Greenshot.Greenshot", "Greenshot", "Imaging"),
        new("ShareX.ShareX", "ShareX", "Imaging"),

        new("TheDocumentFoundation.LibreOffice", "LibreOffice", "Office"),
        new("Apache.OpenOffice", "OpenOffice", "Office"),
        new("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader", "Office"),
        new("Foxit.FoxitReader", "Foxit PDF Reader", "Office"),
        new("SumatraPDF.SumatraPDF", "SumatraPDF", "Office"),
        new("Obsidian.Obsidian", "Obsidian", "Office"),
        new("Notion.Notion", "Notion", "Office"),
        new("Evernote.Evernote", "Evernote", "Office"),
        new("GeoGebra.Classic", "GeoGebra", "Office"),
        new("Google.EarthPro", "Google Earth Pro", "Office"),

        new("Dropbox.Dropbox", "Dropbox", "Storage"),
        new("Google.GoogleDrive", "Google Drive", "Storage"),
        new("Microsoft.OneDrive", "OneDrive", "Storage"),
        new("Mega.MEGASync", "MEGA", "Storage"),
        new("Proton.ProtonDrive", "Proton Drive", "Storage"),

        new("Malwarebytes.Malwarebytes", "Malwarebytes", "Security"),
        new("Bitwarden.Bitwarden", "Bitwarden", "Security"),
        new("DominikReichl.KeePass", "KeePass", "Security"),
        new("KeePassXCTeam.KeePassXC", "KeePassXC", "Security"),
        new("Proton.ProtonVPN", "Proton VPN", "Security"),

        new("Microsoft.VisualStudioCode", "VS Code", "Development"),
        new("Git.Git", "Git", "Development"),
        new("GitHub.GitHubDesktop", "GitHub Desktop", "Development"),
        new("Python.Python.3.13", "Python 3.13", "Development"),
        new("OpenJS.NodeJS.LTS", "Node.js LTS", "Development"),
        new("Notepad++.Notepad++", "Notepad++", "Development"),
        new("Microsoft.WindowsTerminal", "Windows Terminal", "Development"),
        new("Microsoft.PowerShell", "PowerShell 7", "Development"),
        new("Postman.Postman", "Postman", "Development"),
        new("WinSCP.WinSCP", "WinSCP", "Development"),
        new("PuTTY.PuTTY", "PuTTY", "Development"),
        new("WinMerge.WinMerge", "WinMerge", "Development"),
        new("EclipseAdoptium.Temurin.21.JDK", "Java JDK 21", "Development"),
        new("Docker.DockerDesktop", "Docker Desktop", "Development"),
        new("JetBrains.Toolbox", "JetBrains Toolbox", "Development"),
        new("SublimeHQ.SublimeText.4", "Sublime Text", "Development"),
        new("Google.AndroidStudio", "Android Studio", "Development"),
        new("Microsoft.VisualStudio.2022.Community", "Visual Studio Community", "Development"),

        new("7zip.7zip", "7-Zip", "Tools"),
        new("RARLab.WinRAR", "WinRAR", "Tools"),
        new("Giorgiotani.Peazip", "PeaZip", "Tools"),
        new("M2Team.NanaZip", "NanaZip", "Tools"),
        new("voidtools.Everything", "Everything", "Tools"),
        new("Microsoft.PowerToys", "PowerToys", "Tools"),
        new("qBittorrent.qBittorrent", "qBittorrent", "Tools"),
        new("AnyDesk.AnyDesk", "AnyDesk", "Tools"),
        new("TeamViewer.TeamViewer", "TeamViewer", "Tools"),
        new("RealVNC.VNCViewer", "RealVNC Viewer", "Tools"),
        new("CPUID.CPU-Z", "CPU-Z", "Tools"),
        new("TechPowerUp.GPU-Z", "GPU-Z", "Tools"),
        new("REALiX.HWiNFO", "HWiNFO", "Tools"),
        new("CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo", "Tools"),
        new("CrystalDewWorld.CrystalDiskMark", "CrystalDiskMark", "Tools"),
        new("Rufus.Rufus", "Rufus", "Tools"),
        new("Ventoy.Ventoy", "Ventoy", "Tools"),
        new("LIGHTNINGUK.ImgBurn", "ImgBurn", "Tools"),
        new("RevoUninstaller.RevoUninstaller", "Revo Uninstaller", "Tools"),
        new("Klocman.BulkCrapUninstaller", "Bulk Crap Uninstaller", "Tools"),
        new("JAMSoftware.TreeSize.Free", "TreeSize Free", "Tools"),
        new("WinDirStat.WinDirStat", "WinDirStat", "Tools"),
        new("AntibodySoftware.WizTree", "WizTree", "Tools"),
        new("BleachBit.BleachBit", "BleachBit", "Tools"),
        new("Glarysoft.GlaryUtilities", "Glary Utilities", "Tools"),
        new("Microsoft.Sysinternals.ProcessExplorer", "Process Explorer", "Tools"),
        new("Microsoft.Sysinternals.Autoruns", "Autoruns", "Tools"),
        new("NVAccess.NVDA", "NVDA", "Tools"),

        new("Intel.IntelDriverAndSupportAssistant", "Intel Driver & Support Assistant", "Drivers"),
        new("TechPowerUp.NVCleanstall", "NVCleanstall", "Drivers"),
        new("Hawaii_Beach.TinyNvidiaUpdateChecker", "TinyNvidiaUpdateChecker", "Drivers"),
        new("GlennDelahoy.SnappyDriverInstallerOrigin", "Snappy Driver Installer Origin", "Drivers"),
        new("IObit.DriverBooster", "Driver Booster", "Drivers"),
        new("Easeware.DriverEasy", "Driver Easy", "Drivers"),
        new("Wagnardsoft.DisplayDriverUninstaller", "Display Driver Uninstaller", "Drivers"),
        new("lostindark.DriverStoreExplorer", "Driver Store Explorer", "Drivers"),
        new("Dell.CommandUpdate", "Dell Command | Update", "Drivers"),
        new("Lenovo.SystemUpdate", "Lenovo System Update", "Drivers"),
        new("HP.SupportAssistant", "HP Support Assistant", "Drivers"),
        new("Asus.ArmouryCrate", "Armoury Crate", "Drivers"),
        new("MSI.MSICenter", "MSI Center", "Drivers"),

        new("Microsoft.DotNet.DesktopRuntime.8", ".NET Desktop Runtime 8", "Runtimes"),
        new("Microsoft.DotNet.DesktopRuntime.9", ".NET Desktop Runtime 9", "Runtimes"),
        new("Microsoft.VCRedist.2015+.x64", "Visual C++ 2015-2022 (x64)", "Runtimes"),
        new("Microsoft.VCRedist.2015+.x86", "Visual C++ 2015-2022 (x86)", "Runtimes"),
        new("EclipseAdoptium.Temurin.21.JRE", "Java 21 (JRE)", "Runtimes"),
        new("EclipseAdoptium.Temurin.8.JRE", "Java 8 (JRE)", "Runtimes"),
        new("Microsoft.DirectX", "DirectX", "Runtimes")
    ];

    // Runtimes and command-line tools: installable, but nothing to open.
    private static readonly HashSet<string> NonLaunchableIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Git.Git", "Python.Python.3.13", "OpenJS.NodeJS.LTS", "EclipseAdoptium.Temurin.21.JDK",
        "CodecGuide.K-LiteCodecPack.Standard", "Microsoft.PowerShell"
    };

    public static bool IsLaunchable(string packageId, string category) =>
        category is not ("Runtimes" or "SearchResults") && !NonLaunchableIds.Contains(packageId);

    public static IReadOnlyList<WingetPreset> Presets { get; } =
    [
        new("Gaming",
        [
            "Valve.Steam", "EpicGames.EpicGamesLauncher", "ElectronicArts.EADesktop",
            "Ubisoft.Connect", "Discord.Discord", "OBSProject.OBSStudio",
            "Spotify.Spotify", "7zip.7zip", "CPUID.CPU-Z", "TechPowerUp.GPU-Z"
        ]),
        new("Software",
        [
            "Microsoft.VisualStudioCode", "Git.Git", "GitHub.GitHubDesktop",
            "Python.Python.3.13", "OpenJS.NodeJS.LTS", "Notepad++.Notepad++",
            "Microsoft.WindowsTerminal", "Microsoft.PowerShell", "Postman.Postman", "7zip.7zip"
        ]),
        new("School",
        [
            "Google.Chrome.EXE", "Zoom.Zoom", "Microsoft.Teams",
            "TheDocumentFoundation.LibreOffice", "GeoGebra.Classic", "Obsidian.Obsidian",
            "SumatraPDF.SumatraPDF", "VideoLAN.VLC", "7zip.7zip"
        ])
    ];

    /// <summary>
    /// Installed packages that winget can match to its own source, keyed by id.
    /// Ids winget truncated ("Foo.Ba…") are kept as they are; use <see cref="FindInstalled"/>.
    /// </summary>
    public static async Task<Dictionary<string, WingetInstalledPackage>> GetInstalledAsync(
        CancellationToken cancellationToken)
    {
        // Without "--source winget": that filter silently drops some packages
        // (Discord, for one). Rows are kept when their Source column says winget.
        var result = await RunAsync(
            ["list", "--accept-source-agreements", "--disable-interactivity"],
            null,
            cancellationToken,
            TimeSpan.FromMinutes(2));

        var packages = new Dictionary<string, WingetInstalledPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in ParseListTable(result.Output))
            packages.TryAdd(package.Id, package);
        return packages;
    }

    private const string NameKeyPrefix = "name:";

    public static WingetInstalledPackage? FindInstalled(
        IReadOnlyDictionary<string, WingetInstalledPackage> installed,
        string packageId,
        string? packageName = null)
    {
        var match = FindInstalledById(installed, packageId);
        if (match is not null || string.IsNullOrWhiteSpace(packageName))
            return match;

        // Not linked to the catalog: match the display name ("Discord", or
        // "CPUID CPU-Z 3.01" for "CPU-Z").
        var name = packageName.Trim().ToLowerInvariant();
        if (installed.TryGetValue(NameKeyPrefix + name, out match))
            return match;

        // Only the name followed by a version, so "Opera" never matches "Opera GX".
        var pattern = new Regex(
            "^" + Regex.Escape(NameKeyPrefix) + @"(?:[\w.-]+ )?" + Regex.Escape(name) + @"(?: (?:stable|beta|x64|\(x64\)))?(?: v?\d[\w.\-]*)?$");
        return installed.Values.FirstOrDefault(package => pattern.IsMatch(package.Id));
    }

    private static WingetInstalledPackage? FindInstalledById(
        IReadOnlyDictionary<string, WingetInstalledPackage> installed,
        string packageId)
    {
        if (installed.TryGetValue(packageId, out var exact))
            return exact;

        if (AlternatePackageIds.TryGetValue(packageId, out var alternate) &&
            installed.TryGetValue(alternate, out exact))
            return exact;

        foreach (var (key, value) in AlternatePackageIds)
        {
            if (value.Equals(packageId, StringComparison.OrdinalIgnoreCase) &&
                installed.TryGetValue(key, out exact))
                return exact;
        }

        // winget shortens long ids with an ellipsis when the table is narrow.
        return installed.Values.FirstOrDefault(package =>
            package.Id.EndsWith('…') &&
            packageId.StartsWith(package.Id.TrimEnd('…'), StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            var result = await RunAsync(["--version"], null, CancellationToken.None, TimeSpan.FromSeconds(15));
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<IReadOnlyList<WingetPackage>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var result = await RunAsync(
            ["search", query.Trim(), "--source", "winget", "--count", "40",
             "--accept-source-agreements", "--disable-interactivity"],
            null,
            cancellationToken,
            TimeSpan.FromSeconds(60));

        return ParseTable(result.Output)
            .Select(row => new WingetPackage(row.Id, row.Name, "SearchResults"))
            .ToList();
    }

    public static Task<WingetInstallResult> InstallAsync(
        string packageId,
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        ExecuteAsync(WingetAction.Install, packageId, progress, cancellationToken);

    /// <summary>Package ids are passed to an elevated helper, so only plain ids are accepted.</summary>
    public static bool IsValidPackageId(string packageId) =>
        packageId.Length is > 2 and < 128 &&
        Regex.IsMatch(packageId, @"^[A-Za-z0-9][A-Za-z0-9.\-_+]*$");

    public static async Task<WingetInstallResult> ExecuteAsync(
        WingetAction action,
        string packageId,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!IsValidPackageId(packageId))
                return new(WingetInstallStatus.Failed, "WingetMsgInvalidId");

            var result = await RunActionAsync(action, packageId, progress, cancellationToken);

            if (result.ExitCode == InstallerHashMismatch && action != WingetAction.Uninstall)
            {
                // Usually the vendor has just shipped a new build. A fresh
                // source index may already contain the corrected manifest.
                progress?.Report("WingetMsgRefreshing");
                await RunAsync(
                    ["source", "update", "--name", "winget", "--disable-interactivity"],
                    null,
                    cancellationToken,
                    TimeSpan.FromMinutes(3));
                result = await RunActionAsync(action, packageId, progress, cancellationToken);

                if (result.ExitCode == InstallerHashMismatch &&
                    action == WingetAction.Install &&
                    AlternatePackageIds.TryGetValue(packageId, out var alternateId))
                {
                    progress?.Report($"WingetMsgTryingAlternate:{alternateId}");
                    result = await RunActionAsync(action, alternateId, progress, cancellationToken);
                }
            }

            return Interpret(action, result);
        }
        catch (OperationCanceledException)
        {
            return new(WingetInstallStatus.Cancelled, "WingetMsgCancelled");
        }
        catch (Exception ex)
        {
            return new(WingetInstallStatus.Failed, ex.Message);
        }
    }

    private static WingetInstallResult Interpret(WingetAction action, (int ExitCode, string Output) result)
    {
        // Messages are localization keys; the window translates them, so the
        // elevated helper can pass them through unchanged.
        var success = action switch
        {
            WingetAction.Upgrade => "WingetMsgUpdated",
            WingetAction.Uninstall => "WingetMsgRemoved",
            _ => "WingetMsgInstalled"
        };

        return result.ExitCode switch
        {
            0 => new(WingetInstallStatus.Installed, success),
            RebootRequiredToFinish or RebootInitiated =>
                new(WingetInstallStatus.Installed, success + "Restart"),
            UpdateNotApplicable =>
                new(WingetInstallStatus.AlreadyInstalled, action == WingetAction.Upgrade ? "WingetMsgUpToDate" : "WingetMsgAlreadyInstalled"),
            PackageAlreadyInstalled =>
                new(WingetInstallStatus.AlreadyInstalled, "WingetMsgAlreadyInstalled"),
            NoPackageFound when action == WingetAction.Uninstall =>
                new(WingetInstallStatus.AlreadyInstalled, "WingetMsgNotInstalled"),
            NoPackageFound =>
                new(WingetInstallStatus.Failed, "WingetMsgNotFound"),
            InstallerHashMismatch =>
                new(WingetInstallStatus.Failed, "WingetMsgHashMismatch"),
            _ => new(WingetInstallStatus.Failed, DescribeFailure(result))
        };
    }

    private static Task<(int ExitCode, string Output)> RunActionAsync(
        WingetAction action,
        string packageId,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        string[] arguments = action switch
        {
            WingetAction.Upgrade =>
                ["upgrade", "--id", packageId, "--exact", "--source", "winget",
                 "--silent", "--accept-package-agreements",
                 "--accept-source-agreements", "--disable-interactivity"],
            WingetAction.Uninstall =>
                ["uninstall", "--id", packageId, "--exact", "--silent",
                 "--accept-source-agreements", "--disable-interactivity"],
            // --no-upgrade: skip apps that are already installed instead of
            // silently upgrading them.
            _ =>
                ["install", "--id", packageId, "--exact", "--source", "winget",
                 "--silent", "--no-upgrade", "--accept-package-agreements",
                 "--accept-source-agreements", "--disable-interactivity"]
        };

        return RunAsync(arguments, progress, cancellationToken, TimeSpan.FromMinutes(30));
    }

    private static string DescribeFailure((int ExitCode, string Output) result)
    {
        var lastLine = result.Output
            .Split('\n')
            .Select(CleanLine)
            .LastOrDefault(line => line.Length > 3 && !IsProgressLine(line));
        return string.IsNullOrWhiteSpace(lastLine)
            ? $"Hata kodu 0x{result.ExitCode:X8}"
            : $"{lastLine} (0x{result.ExitCode:X8})";
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        IReadOnlyList<string> arguments,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo("winget")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var output = new StringBuilder();
        void OnData(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null)
                return;

            lock (output)
                output.AppendLine(e.Data);

            var line = CleanLine(e.Data);
            if (line.Length > 0 && !IsProgressLine(line))
                progress?.Report(line);
        }

        process.OutputDataReceived += OnData;
        process.ErrorDataReceived += OnData;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
            throw;
        }

        process.WaitForExit();
        lock (output)
            return (process.ExitCode, output.ToString());
    }

    private static string CleanLine(string line)
    {
        // winget redraws spinners/progress bars with carriage returns.
        var lastSegment = line.Split('\r').LastOrDefault(segment => segment.Trim().Length > 0) ?? string.Empty;
        return lastSegment.Trim();
    }

    private static bool IsProgressLine(string line) =>
        line.Length <= 2 ||
        line.All(character => character is '-' or '\\' or '|' or '/' or ' ') ||
        line.Contains('█') || line.Contains('▒');

    private static IEnumerable<WingetInstalledPackage> ParseListTable(string output)
    {
        var lines = output
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Split('\r').Last())
            .ToList();

        var separatorIndex = lines.FindIndex(line => Regex.IsMatch(line.Trim(), "^-{10,}$"));
        if (separatorIndex <= 0)
            yield break;

        // Header is localized ("Name Id Version Available Source" / "Ad Kimlik
        // Sürüm Kullanılabilir Kaynak"); only the column positions are used.
        // Available is missing when nothing can be updated; Source is last.
        var header = lines[separatorIndex - 1];
        var columns = Regex.Matches(header, @"\S+").Select(match => match.Index).ToArray();
        if (columns.Length < 4)
            yield break;
        var hasAvailable = columns.Length >= 5;
        var sourceColumn = columns.Length - 1;

        for (var index = separatorIndex + 1; index < lines.Count; index++)
        {
            var row = lines[index].TrimEnd();
            if (row.Length <= columns[1] || row.Trim().Length == 0)
                continue;

            string Cell(int column)
            {
                if (column >= columns.Length || columns[column] >= row.Length)
                    return string.Empty;
                var end = column + 1 < columns.Length ? Math.Min(columns[column + 1], row.Length) : row.Length;
                return row[columns[column]..end].Trim();
            }

            var name = row[..columns[1]].Trim();
            var id = Cell(1);
            var version = Cell(2);
            var available = hasAvailable ? Cell(3) : string.Empty;
            var source = Cell(sourceColumn);

            // Wide characters in the name shift every column. The id has no
            // spaces and is followed by two or more spaces, so find it instead.
            if (id.Contains(' ') || row[columns[1] - 1] != ' ')
            {
                var match = Regex.Match(row, @"\s([A-Za-z0-9][^\s]*[.\-][^\s]*)\s{2,}(.+)$");
                if (!match.Success)
                    continue;

                name = row[..match.Index].Trim();
                id = match.Groups[1].Value;
                var rest = Regex.Split(match.Groups[2].Value.Trim(), @"\s{2,}");
                source = rest.Length > 1 ? rest[^1] : string.Empty;
                version = rest.ElementAtOrDefault(0) ?? string.Empty;
                available = hasAvailable && rest.Length > 2 ? rest[1] : string.Empty;
            }

            // Apps winget does not link to its catalog (Discord shows up as
            // "ARP\User\X64\Discord") are kept by name, without update info.
            if (!source.Equals("winget", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Length > 0)
                    yield return new WingetInstalledPackage(NameKeyPrefix + name.ToLowerInvariant(), name, version, null);
                continue;
            }

            if (id.Length == 0 || (!id.Contains('.') && !id.Contains('-')))
                continue;

            yield return new WingetInstalledPackage(
                id,
                name,
                version,
                string.IsNullOrWhiteSpace(available) ? null : available);
        }
    }

    private static IEnumerable<(string Name, string Id)> ParseTable(string output)
    {
        var lines = output
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Split('\r').Last())
            .ToList();

        var separatorIndex = lines.FindIndex(line => Regex.IsMatch(line.Trim(), "^-{10,}$"));
        if (separatorIndex <= 0)
            yield break;

        // Column positions come from the header, which winget localizes
        // ("Name Id Version" / "Ad Kimlik Sürüm"), so use word offsets.
        var header = lines[separatorIndex - 1];
        var columnStarts = Regex.Matches(header, @"\S+").Select(match => match.Index).ToArray();
        if (columnStarts.Length < 2)
            yield break;

        for (var index = separatorIndex + 1; index < lines.Count; index++)
        {
            var row = lines[index];
            if (row.Trim().Length == 0)
                continue;

            if (row.Length <= columnStarts[1])
                continue;

            var name = row[..columnStarts[1]].Trim();
            var idEnd = columnStarts.Length > 2 ? Math.Min(columnStarts[2], row.Length) : row.Length;
            var id = row[columnStarts[1]..idEnd].Trim();

            // Fall back to whitespace splitting when wide characters shift
            // the columns (the Id never contains spaces).
            if (id.Contains(' ') || id.Length == 0)
            {
                var parts = Regex.Split(row.Trim(), @"\s{2,}|\s(?=\S+\s+\S+$)");
                if (parts.Length >= 2)
                {
                    name = parts[0].Trim();
                    id = parts[1].Trim().Split(' ')[0];
                }
            }

            if (id.Length == 0 || id.EndsWith('…'))
                continue;

            if (!id.Contains('.') && !id.Contains('-'))
                continue;

            yield return (name.TrimEnd('…'), id);
        }
    }
}
