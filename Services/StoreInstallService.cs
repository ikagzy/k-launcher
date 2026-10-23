using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Microsoft.Win32;

namespace KLauncher.Services;

/// <summary>
/// Fast install path for Steam and Epic. Instead of typing the game name into
/// the launcher's search box and walking its whole UI tree, the game id is
/// resolved first and the launcher's own install URI is opened:
///   Steam: steam://install/&lt;appid&gt;
///   Epic:  com.epicgames.launcher://apps/&lt;ns&gt;%3A&lt;id&gt;%3A&lt;app&gt;?action=install
/// Both launchers then show their own install confirmation window, whose
/// "Yükle/Install" button is pressed here. No credentials are used.
/// </summary>
public static class StoreInstallService
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static readonly string[] SteamProcessNames = ["steamwebhelper", "steam"];
    private static readonly string[] EpicProcessNames = ["EpicGamesLauncher", "EpicWebHelper"];

    // Buttons that move an install dialog forward. Order matters: the final
    // action wins over "next" when both exist.
    private static readonly string[] InstallButtonNames = ["Yükle", "Install", "Kur", "Yüklemeye başla"];
    private static readonly string[] NextButtonNames = ["İleri", "Next", "Kabul ediyorum", "I Agree", "Kabul Et", "Accept"];
    private static readonly string[] FinishButtonNames = ["Bitti", "Finish"];
    private static readonly string[] CancelButtonNames = ["İptal", "Cancel"];

    private static readonly object EpicCatalogLock = new();
    private static (DateTime Stamp, List<EpicCatalogEntry> Entries)? _epicCatalogCache;

    private sealed record SteamCandidate(int Id, string Name);

    private sealed record EpicCatalogEntry(
        string Title,
        string Namespace,
        string CatalogItemId,
        string AppName,
        bool IsGame);

    /// <summary>
    /// Returns null when the fast path cannot resolve the game, so the caller
    /// can fall back to the UI-driven flow.
    /// </summary>
    public static Task<StoreLauncherAutomationResult?> TryInstallAsync(
        string platform,
        string gameName,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null) =>
        platform.Equals("Epic", StringComparison.OrdinalIgnoreCase)
            ? Task.Run(() => TryInstallEpic(gameName, cancellationToken, progress), cancellationToken)
            : TryInstallSteamAsync(gameName, cancellationToken, progress);

    private static void Report(IProgress<string>? progress, string message)
    {
        InstallLog.Write(message);
        progress?.Report(message);
    }

    // ------------------------------------------------------------------ Steam

    private static async Task<StoreLauncherAutomationResult?> TryInstallSteamAsync(
        string gameName,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        Report(progress, LocalizationService.Text("AutoSteamSearching", gameName));
        var (appId, resolvedName) = await ResolveSteamAppAsync(gameName, cancellationToken);
        if (appId is null)
        {
            Report(progress, LocalizationService.Text("AutoSteamNoAppId", gameName));
            return null;
        }

        var displayName = resolvedName ?? gameName;
        Report(progress, LocalizationService.Text("AutoSteamFound", displayName, appId.Value));
        if (IsSteamAppInstalled(appId.Value))
            return new(true, LocalizationService.Text("AutoSteamAlreadyInstalled", displayName));

        var steamWasRunning = IsAnyProcessRunning(SteamProcessNames);
        Report(progress, LocalizationService.Text("AutoSteamOpening", appId.Value));
        if (!OpenUri($"steam://install/{appId.Value}"))
            return new(false, LocalizationService.Text("AutoSteamLinkFailed"), RequiresUserAction: true);

        return await Task.Run(() =>
        {
            var timeout = TimeSpan.FromSeconds(steamWasRunning ? 40 : 100);
            var confirmed = ConfirmInstallDialog(
                SteamProcessNames,
                requireCancelButton: true,
                timeout,
                cancellationToken,
                () => IsSteamAppInstalled(appId.Value),
                progress,
                "Steam");

            if (confirmed)
            {
                Report(progress, LocalizationService.Text("AutoSteamQueued", displayName));
                return new StoreLauncherAutomationResult(true, LocalizationService.Text("AutoSteamStarted", displayName));
            }

            Report(progress, LocalizationService.Text("AutoSteamNoDialog", displayName));

            // Paid games that are not in the account cannot be installed.
            // Show the store page so the user sees why.
            OpenUri($"steam://store/{appId.Value}");
            return new StoreLauncherAutomationResult(
                false,
                LocalizationService.Text("AutoSteamNoWindow", displayName),
                RequiresUserAction: true);
        }, cancellationToken);
    }

    private static async Task<(int? AppId, string? Name)> ResolveSteamAppAsync(
        string gameName,
        CancellationToken cancellationToken)
    {
        var text = gameName.Trim();
        if (int.TryParse(text, out var direct) && direct > 0)
            return (direct, null);

        var urlMatch = Regex.Match(text, @"(?:store\.steampowered\.com/app/|steam://[a-z]+/)(\d+)", RegexOptions.IgnoreCase);
        if (urlMatch.Success)
            return (int.Parse(urlMatch.Groups[1].Value), null);

        var local = FindInstalledSteamAppByName(text);
        if (local is not null)
            return (local.Value.AppId, local.Value.Name);

        // Steam's own app database knows every game in the account, even
        // ones without a store page (e.g. "Condition Zero Deleted Scenes").
        var known = FindSteamAppInAppInfo(text, exactOnly: true);
        if (known is not null)
            return (known.Id, known.Name);

        try
        {
            var url = "https://store.steampowered.com/api/storesearch/?l=turkish&cc=TR&term=" +
                      Uri.EscapeDataString(text);
            using var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (null, null);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                return (null, null);

            var candidates = new List<SteamCandidate>();
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var type) &&
                    !string.Equals(type.GetString(), "app", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (item.TryGetProperty("id", out var id) && id.TryGetInt32(out var appId) &&
                    item.TryGetProperty("name", out var name) && name.GetString() is { } appName)
                    candidates.Add(new SteamCandidate(appId, appName));
            }

            var best = PickBestTitle(candidates, candidate => candidate.Name, text)
                       ?? FindSteamAppInAppInfo(text, exactOnly: false);
            return best is null ? ((int?)null, (string?)null) : (best.Id, best.Name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (null, null);
        }
    }

    private static readonly object SteamAppInfoLock = new();
    private static (DateTime Stamp, List<(SteamCandidate App, bool IsGame)> Apps)? _steamAppInfoCache;

    private static string? GetSteamPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steamPath = (key?.GetValue("SteamPath") as string)?.Replace('/', '\\');
            return string.IsNullOrWhiteSpace(steamPath) ? null : steamPath;
        }
        catch
        {
            return null;
        }
    }

    private static SteamCandidate? FindSteamAppInAppInfo(string gameName, bool exactOnly)
    {
        var apps = LoadSteamAppInfo();
        if (apps.Count == 0)
            return null;

        var wanted = NormalizeTitle(gameName);
        var exact = apps
            .Where(app => NormalizeTitle(app.App.Name) == wanted)
            .OrderByDescending(app => app.IsGame)
            .ThenBy(app => app.App.Id)
            .Select(app => app.App)
            .FirstOrDefault();
        if (exact is not null || exactOnly)
            return exact;

        return PickBestTitle(
            apps.Where(app => app.IsGame).Select(app => app.App).ToList(),
            app => app.Name,
            gameName);
    }

    /// <summary>
    /// Reads names from Steam's binary appcache\appinfo.vdf (formats 27-29).
    /// </summary>
    private static List<(SteamCandidate App, bool IsGame)> LoadSteamAppInfo()
    {
        var steamPath = GetSteamPath();
        if (steamPath is null)
            return [];

        var path = Path.Combine(steamPath, "appcache", "appinfo.vdf");
        try
        {
            if (!File.Exists(path))
                return [];

            var stamp = File.GetLastWriteTimeUtc(path);
            lock (SteamAppInfoLock)
            {
                if (_steamAppInfoCache is { } cached && cached.Stamp == stamp)
                    return cached.Apps;
            }

            byte[] data;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                data = new byte[stream.Length];
                stream.ReadExactly(data);
            }

            var apps = ParseAppInfo(data);
            lock (SteamAppInfoLock)
                _steamAppInfoCache = (stamp, apps);
            return apps;
        }
        catch
        {
            return [];
        }
    }

    private static List<(SteamCandidate App, bool IsGame)> ParseAppInfo(byte[] data)
    {
        var apps = new List<(SteamCandidate App, bool IsGame)>();
        var magic = BitConverter.ToUInt32(data, 0);
        var version = magic & 0xFF;
        if ((magic & 0xFFFFFF00) != 0x07564400 || version < 0x27 || version > 0x29)
            return apps;

        var offset = 8;
        string[]? strings = null;
        if (version >= 0x29)
        {
            var tableOffset = (int)BitConverter.ToInt64(data, 8);
            offset = 16;
            var count = BitConverter.ToInt32(data, tableOffset);
            strings = new string[count];
            var position = tableOffset + 4;
            for (var index = 0; index < count; index++)
            {
                var end = Array.IndexOf(data, (byte)0, position);
                strings[index] = Encoding.UTF8.GetString(data, position, end - position);
                position = end + 1;
            }
        }

        // infoState, lastUpdated, picsToken, sha1 text, changeNumber
        // (+ binary sha1 from v28 on) precede the key/value data.
        var headerSize = version >= 0x28 ? 60 : 40;
        while (offset + 8 <= data.Length)
        {
            var appId = BitConverter.ToUInt32(data, offset);
            if (appId == 0)
                break;

            var size = BitConverter.ToInt32(data, offset + 4);
            var start = offset + 8;
            try
            {
                var position = start + headerSize;
                string? name = null;
                string? type = null;
                ReadAppInfoSection(data, ref position, strings, [], ref name, ref type);
                if (!string.IsNullOrWhiteSpace(name))
                    apps.Add((new SteamCandidate((int)appId, name),
                        string.Equals(type, "game", StringComparison.OrdinalIgnoreCase)));
            }
            catch { }

            offset = start + size;
        }

        return apps;
    }

    private static void ReadAppInfoSection(
        byte[] data,
        ref int position,
        string[]? strings,
        List<string> path,
        ref string? name,
        ref string? type)
    {
        while (true)
        {
            var kind = data[position++];
            if (kind == 8)
                return;

            string key;
            if (strings is not null)
            {
                key = strings[BitConverter.ToInt32(data, position)];
                position += 4;
            }
            else
            {
                var end = Array.IndexOf(data, (byte)0, position);
                key = Encoding.UTF8.GetString(data, position, end - position);
                position = end + 1;
            }

            switch (kind)
            {
                case 0:
                    path.Add(key);
                    ReadAppInfoSection(data, ref position, strings, path, ref name, ref type);
                    path.RemoveAt(path.Count - 1);
                    break;
                case 1:
                    var stringEnd = Array.IndexOf(data, (byte)0, position);
                    var value = Encoding.UTF8.GetString(data, position, stringEnd - position);
                    position = stringEnd + 1;
                    // appinfo > common > name / type
                    if (path.Count == 2 && path[1] == "common")
                    {
                        if (key == "name") name = value;
                        else if (key == "type") type = value;
                    }
                    break;
                case 2:
                case 3:
                case 4:
                    position += 4;
                    break;
                case 7:
                case 10:
                    position += 8;
                    break;
                default:
                    throw new InvalidDataException($"Unknown KeyValue type {kind}");
            }
        }
    }

    private static IReadOnlyList<string> GetSteamLibraryFolders()
    {
        var folders = new List<string>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steamPath = (key?.GetValue("SteamPath") as string)?.Replace('/', '\\');
            if (string.IsNullOrWhiteSpace(steamPath))
                return folders;

            folders.Add(Path.Combine(steamPath, "steamapps"));
            var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    var path = Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), "steamapps");
                    if (!folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                        folders.Add(path);
                }
            }
        }
        catch { }

        return folders;
    }

    private static bool IsSteamAppInstalled(int appId) =>
        GetSteamLibraryFolders().Any(folder =>
            File.Exists(Path.Combine(folder, $"appmanifest_{appId}.acf")));

    private static (int AppId, string Name)? FindInstalledSteamAppByName(string gameName)
    {
        var wanted = NormalizeTitle(gameName);
        foreach (var folder in GetSteamLibraryFolders())
        {
            string[] manifests;
            try { manifests = Directory.GetFiles(folder, "appmanifest_*.acf"); }
            catch { continue; }

            foreach (var manifest in manifests)
            {
                try
                {
                    var content = File.ReadAllText(manifest);
                    var name = Regex.Match(content, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                    var id = Regex.Match(content, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                    if (name.Length > 0 && NormalizeTitle(name) == wanted && int.TryParse(id, out var appId))
                        return (appId, name);
                }
                catch { }
            }
        }

        return null;
    }

    // ------------------------------------------------------------------- Epic

    private static StoreLauncherAutomationResult? TryInstallEpic(
        string gameName,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        Report(progress, LocalizationService.Text("AutoEpicSearching", gameName));
        var catalog = LoadEpicCatalog();
        if (catalog.Count == 0)
        {
            Report(progress, LocalizationService.Text("AutoEpicNoCatalog"));
            return null;
        }

        var best = PickBestTitle(
            catalog.Where(entry => entry.IsGame).ToList(),
            entry => entry.Title,
            gameName.Trim())
            ?? PickBestTitle(catalog, entry => entry.Title, gameName.Trim());
        if (best is null)
        {
            Report(progress, LocalizationService.Text("AutoEpicNotInLibrary", gameName));
            return null;
        }

        Report(progress, LocalizationService.Text("AutoEpicFound", best.Title, best.AppName));
        if (IsEpicAppInstalled(best.AppName))
            return new(true, LocalizationService.Text("AutoAlreadyInstalled", best.Title));

        var epicWasRunning = IsAnyProcessRunning(EpicProcessNames);
        var uri = "com.epicgames.launcher://apps/" +
                  $"{best.Namespace}%3A{best.CatalogItemId}%3A{best.AppName}?action=install";
        if (!OpenUri(uri))
            return new(false, LocalizationService.Text("AutoEpicLinkFailed"), RequiresUserAction: true);

        var confirmed = ConfirmInstallDialog(
            EpicProcessNames,
            requireCancelButton: false,
            TimeSpan.FromSeconds(epicWasRunning ? 40 : 100),
            cancellationToken,
            () => IsEpicInstallQueued(best.AppName),
            progress,
            "Epic");

        return confirmed
            ? new(true, LocalizationService.Text("AutoEpicStarted", best.Title))
            : new(false,
                LocalizationService.Text("AutoEpicNoWindow", best.Title),
                RequiresUserAction: true);
    }

    private static string EpicV2Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "com.epicgames.eglv2");

    private static string EpicLegacyCatalogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Catalog", "catcache.bin");

    private static List<EpicCatalogEntry> LoadEpicCatalog()
    {
        var v2Catalog = Path.Combine(EpicV2Folder, "cache", "prod", "catalog-cache.json");
        var stamp = new[] { EpicLegacyCatalogPath, v2Catalog }
            .Where(path => File.Exists(path))
            .Select(path => File.GetLastWriteTimeUtc(path))
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        lock (EpicCatalogLock)
        {
            if (_epicCatalogCache is { } cached && cached.Stamp == stamp && stamp != DateTime.MinValue)
                return cached.Entries;
        }

        var entries = new List<EpicCatalogEntry>();

        // New Epic Games Launcher (eglv2, 2026 preview):
        //   %LOCALAPPDATA%\com.epicgames.eglv2\cache\prod\catalog-cache.json
        //   { "items": { "<itemId>": { "item": { title, namespace, releaseInfo, ... } } } }
        try
        {
            if (File.Exists(v2Catalog))
            {
                using var document = JsonDocument.Parse(ReadShared(v2Catalog));
                if (document.RootElement.TryGetProperty("items", out var items) &&
                    items.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in items.EnumerateObject())
                    {
                        var item = property.Value.TryGetProperty("item", out var inner) ? inner : property.Value;
                        if (ParseCatalogItem(item) is { } entry)
                            entries.Add(entry);
                    }
                }
            }
        }
        catch { }

        // Classic Epic Games Launcher: base64 encoded JSON array.
        try
        {
            if (File.Exists(EpicLegacyCatalogPath))
            {
                var raw = ReadShared(EpicLegacyCatalogPath).Trim();
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
                using var document = JsonDocument.Parse(json);
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (ParseCatalogItem(item) is { } entry &&
                        !entries.Any(existing => existing.AppName.Equals(entry.AppName, StringComparison.OrdinalIgnoreCase)))
                        entries.Add(entry);
                }
            }
        }
        catch { }

        // Prefer games the signed-in account owns (eglv2 library cache).
        var owned = LoadEpicOwnedAppNames();
        if (owned.Count > 0)
        {
            entries = entries
                .OrderByDescending(entry => owned.Contains(entry.AppName))
                .ToList();
        }

        lock (EpicCatalogLock)
            _epicCatalogCache = (stamp, entries);

        return entries;
    }

    private static HashSet<string> LoadEpicOwnedAppNames()
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var accounts = Path.Combine(EpicV2Folder, "cache", "prod", "account");
            if (!Directory.Exists(accounts))
                return owned;

            foreach (var file in Directory.EnumerateFiles(accounts, "library-cache.json", SearchOption.AllDirectories))
            {
                using var document = JsonDocument.Parse(ReadShared(file));
                if (!document.RootElement.TryGetProperty("items", out var items) ||
                    items.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("appId", out var appId) &&
                        GetString(appId, "appName") is { } appName)
                        owned.Add(appName);
                }
            }
        }
        catch { }

        return owned;
    }

    private static EpicCatalogEntry? ParseCatalogItem(JsonElement item)
    {
        var title = GetString(item, "title");
        var ns = GetString(item, "namespace");
        var id = GetString(item, "id");
        if (title is null || ns is null || id is null)
            return null;

        if (!item.TryGetProperty("releaseInfo", out var releases) ||
            releases.ValueKind != JsonValueKind.Array)
            return null;

        string? appName = null;
        foreach (var release in releases.EnumerateArray())
        {
            var candidate = GetString(release, "appId");
            if (candidate is null)
                continue;

            var windows = !release.TryGetProperty("platform", out var platforms) ||
                          platforms.ValueKind != JsonValueKind.Array ||
                          platforms.EnumerateArray().Any(platform =>
                              string.Equals(platform.GetString(), "Windows", StringComparison.OrdinalIgnoreCase));
            if (!windows)
                continue;

            appName = candidate;
            break;
        }

        if (appName is null)
            return null;

        var isAddon = item.TryGetProperty("mainGameItem", out var mainGame) &&
                      mainGame.ValueKind == JsonValueKind.Object;
        var isGame = !isAddon &&
                     item.TryGetProperty("categories", out var categories) &&
                     categories.ValueKind == JsonValueKind.Array &&
                     categories.EnumerateArray().Any(category =>
                         string.Equals(GetString(category, "path"), "games", StringComparison.OrdinalIgnoreCase));

        return new EpicCatalogEntry(title, ns, id, appName, isGame);
    }

    private static string ReadShared(string path)
    {
        // The launcher may be writing the file; open it without locking.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static bool IsEpicAppInstalled(string appName)
    {
        if (EpicV2Installation(appName) is true)
            return true;

        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
            if (!File.Exists(path))
                return false;

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("InstallationList", out var list))
                return false;

            return list.EnumerateArray().Any(item =>
                string.Equals(GetString(item, "AppName"), appName, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(GetString(item, "InstallLocation") ?? string.Empty));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// eglv2 keeps one JSON object per line in installations.jsonl.
    /// Returns true for a complete install, false for one in progress and
    /// null when the game is not listed.
    /// </summary>
    private static bool? EpicV2Installation(string appName)
    {
        try
        {
            var path = Path.Combine(EpicV2Folder, "installations.jsonl");
            if (!File.Exists(path))
                return null;

            foreach (var line in ReadShared(path).Split('\n'))
            {
                if (!line.Contains(appName, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!string.Equals(GetString(root, "artifactId"), appName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return root.TryGetProperty("complete", out var complete) &&
                           complete.ValueKind == JsonValueKind.True;
                }
                catch { }
            }
        }
        catch { }

        return null;
    }

    private static bool IsEpicInstallQueued(string appName)
    {
        if (IsEpicAppInstalled(appName) || EpicV2Installation(appName) is not null)
            return true;

        try
        {
            var queue = Path.Combine(EpicV2Folder, "download", "queue.json");
            if (File.Exists(queue) && ReadShared(queue).Contains(appName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch { }

        try
        {
            // Epic writes Data\Manifests\<id>.item when a download starts.
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(folder))
                return false;

            foreach (var file in Directory.EnumerateFiles(folder, "*.item"))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    if (string.Equals(GetString(document.RootElement, "AppName"), appName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { }
            }
        }
        catch { }

        return false;
    }

    // -------------------------------------------------------- Dialog handling

    /// <summary>
    /// Used by the UI fallback after the library "Yükle" button was clicked:
    /// both launchers open a separate confirmation that must be accepted.
    /// </summary>
    public static bool ConfirmLauncherInstallDialog(string platform, CancellationToken cancellationToken)
    {
        var isEpic = platform.Equals("Epic", StringComparison.OrdinalIgnoreCase);
        return ConfirmInstallDialog(
            isEpic ? EpicProcessNames : SteamProcessNames,
            requireCancelButton: !isEpic,
            TimeSpan.FromSeconds(12),
            cancellationToken);
    }

    /// <summary>
    /// Waits for the launcher's install window and presses its install button.
    /// Only button-type elements in small, visible launcher windows are read,
    /// which is much faster than walking the whole library page.
    /// </summary>
    private static bool ConfirmInstallDialog(
        string[] processNames,
        bool requireCancelButton,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<bool>? installStarted = null,
        IProgress<string>? progress = null,
        string platform = "")
    {
        // Epic may first land on the game page whose own "Yükle" opens the
        // real dialog, so a second press is allowed there.
        var maxInstallPresses = requireCancelButton ? 1 : 3;
        var started = DateTime.UtcNow;
        var deadline = started + timeout;
        var lastReportSecond = -1;
        var loggedWindows = new HashSet<IntPtr>();
        var installPresses = 0;
        var nextPresses = 0;
        var lastPress = DateTime.MinValue;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The launcher writes its install manifest as soon as the game
            // enters the download queue. That is a cheap and reliable
            // success signal, whether or not a dialog was shown.
            if (installStarted?.Invoke() == true)
            {
                InstallLog.Write($"{platform}: kurulum kaydı oluştu.");
                return true;
            }

            var waited = (int)(DateTime.UtcNow - started).TotalSeconds;
            if (waited != lastReportSecond && waited % 2 == 0)
            {
                lastReportSecond = waited;
                progress?.Report(LocalizationService.Text("AutoWaitingConfirm", platform, waited));
            }

            var sawInstallButton = false;
            var pressedThisRound = false;

            var windows = EnumerateVisibleLauncherWindows(processNames).ToList();
            foreach (var (handle, window) in windows)
            {
                // Never walk the full library window looking for a dialog:
                // Steam's library tree can take a very long time to read.
                // Windows are sorted by size, so the main window is last.
                // After a few seconds the main window is checked too, in case
                // the launcher shows the confirmation inside it.
                if (requireCancelButton && windows.Count > 1 && handle == windows[^1].Handle &&
                    DateTime.UtcNow - started < TimeSpan.FromSeconds(8))
                    continue;

                var buttons = FindButtonsWithTimeout(window, TimeSpan.FromSeconds(3));
                if (loggedWindows.Add(handle))
                {
                    InstallLog.Write(
                        $"{platform}: pencere 0x{handle.ToInt64():X} '{SafeWindowName(window)}' düğmeler: " +
                        string.Join(" | ", buttons.Take(20).Select(button => button.Name)));
                }

                if (buttons.Length == 0)
                    continue;

                var hasCancel = buttons.Any(button => NameEqualsAny(button.Name, CancelButtonNames));
                if (requireCancelButton && !hasCancel)
                    continue;

                var install = buttons.FirstOrDefault(button => NameEqualsAny(button.Name, InstallButtonNames));
                if (install.Element is not null)
                {
                    sawInstallButton = true;
                    if (installPresses < maxInstallPresses &&
                        DateTime.UtcNow - lastPress > TimeSpan.FromSeconds(1.2) &&
                        PressButton(handle, install.Element, install.Name))
                    {
                        progress?.Report(LocalizationService.Text("AutoConfirmed", platform, install.Name));
                        installPresses++;
                        lastPress = DateTime.UtcNow;
                        pressedThisRound = true;
                        var minimumDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
                        if (deadline < minimumDeadline)
                            deadline = minimumDeadline;
                        break;
                    }
                }

                var next = buttons.FirstOrDefault(button => NameEqualsAny(button.Name, NextButtonNames));
                if (next.Element is not null && nextPresses < 4 && PressButton(handle, next.Element, next.Name))
                {
                    nextPresses++;
                    lastPress = DateTime.UtcNow;
                    pressedThisRound = true;
                    break;
                }

                if (installPresses > 0 && !hasCancel)
                {
                    var finish = buttons.FirstOrDefault(button => NameEqualsAny(button.Name, FinishButtonNames));
                    if (finish.Element is not null && buttons.Length <= 4)
                    {
                        PressButton(handle, finish.Element, finish.Name);
                        return true;
                    }
                }
            }

            // The install button is gone after the press: the launcher has
            // accepted it and moved the game into its download queue.
            if (installPresses > 0 && !pressedThisRound && !sawInstallButton &&
                DateTime.UtcNow - lastPress > TimeSpan.FromSeconds(1.5))
                return true;

            Thread.Sleep(pressedThisRound ? 700 : 350);
        }

        var result = installPresses > 0 || installStarted?.Invoke() == true;
        InstallLog.Write($"{platform}: onay beklemesi bitti, sonuç={result}, basış={installPresses}.");
        return result;
    }

    private static string SafeWindowName(AutomationElement window)
    {
        try { return window.Current.Name ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static (AutomationElement Element, string Name)[] FindButtonsWithTimeout(
        AutomationElement window,
        TimeSpan timeout)
    {
        // UI Automation calls cannot be cancelled. Run the read on a worker
        // and give up on that window if it does not answer in time.
        var task = Task.Run(() => FindButtons(window));
        return task.Wait(timeout) ? task.Result : [];
    }

    private static (AutomationElement Element, string Name)[] FindButtons(AutomationElement window)
    {
        try
        {
            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink));

            return window.FindAll(TreeScope.Descendants, condition)
                .Cast<AutomationElement>()
                .Select(element =>
                {
                    try
                    {
                        if (element.Current.IsOffscreen || !element.Current.IsEnabled)
                            return (Element: (AutomationElement?)null, Name: string.Empty);
                        return (Element: (AutomationElement?)element, Name: (element.Current.Name ?? string.Empty).Trim());
                    }
                    catch
                    {
                        return (Element: (AutomationElement?)null, Name: string.Empty);
                    }
                })
                .Where(pair => pair.Element is not null && pair.Name.Length > 0)
                .Select(pair => (pair.Element!, pair.Name))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool NameEqualsAny(string name, IEnumerable<string> candidates) =>
        candidates.Any(candidate =>
            string.Equals(name, candidate, StringComparison.CurrentCultureIgnoreCase) ||
            string.Equals(name, candidate, StringComparison.InvariantCultureIgnoreCase) ||
            string.Equals(
                name.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR")),
                candidate.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR")),
                StringComparison.Ordinal));

    private static bool PressButton(IntPtr windowHandle, AutomationElement button, string label)
    {
        // 1) UI Automation "Invoke": presses the button without touching the
        //    user's mouse and works even if the user is busy in another app.
        try
        {
            if (button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) &&
                pattern is InvokePattern invoke)
            {
                invoke.Invoke();
                Thread.Sleep(700);
                if (!IsStillPresent(button))
                {
                    InstallLog.Write($"'{label}' Invoke ile basıldı.");
                    return true;
                }

                InstallLog.Write($"'{label}' Invoke sonrası hâlâ görünüyor.");
            }
        }
        catch
        {
            // The element vanished while being invoked: it was handled.
            InstallLog.Write($"'{label}' Invoke ile basıldı (pencere kapandı).");
            return true;
        }

        // 2) Real click, but only when the launcher window is truly in front.
        //    Otherwise the click could land in whatever the user is using.
        ActivateWindow(windowHandle);
        if (GetForegroundWindow() != windowHandle)
        {
            InstallLog.Write($"'{label}': pencere öne alınamadı, fare tıklaması yapılmadı.");
            return false;
        }

        try
        {
            if (button.TryGetClickablePoint(out var point))
            {
                SetCursorPos((int)point.X, (int)point.Y);
                mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(300);
                InstallLog.Write($"'{label}' fare ile tıklandı.");
                return true;
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    private static bool IsStillPresent(AutomationElement element)
    {
        try
        {
            return !element.Current.IsOffscreen && element.Current.IsEnabled;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<(IntPtr Handle, AutomationElement Window)> EnumerateVisibleLauncherWindows(
        string[] processNames)
    {
        var processIds = new HashSet<uint>();
        foreach (var name in processNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try { processIds.Add((uint)process.Id); }
                    catch { }
                    finally { process.Dispose(); }
                }
            }
            catch { }
        }

        if (processIds.Count == 0)
            yield break;

        var handles = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            try
            {
                if (IsWindowVisible(handle) && !IsIconic(handle))
                {
                    GetWindowThreadProcessId(handle, out var processId);
                    if (processIds.Contains(processId) &&
                        GetWindowRect(handle, out var rect) &&
                        rect.Right - rect.Left > 120 && rect.Bottom - rect.Top > 80)
                        handles.Add(handle);
                }
            }
            catch { }

            return true;
        }, IntPtr.Zero);

        // Smaller windows first: install dialogs are smaller than the main
        // launcher window, so they are found before the large library tree.
        handles.Sort((left, right) => WindowArea(left).CompareTo(WindowArea(right)));

        foreach (var handle in handles)
        {
            AutomationElement? window = null;
            try { window = AutomationElement.FromHandle(handle); }
            catch { }

            if (window is not null)
                yield return (handle, window);
        }
    }

    private static long WindowArea(IntPtr handle) =>
        GetWindowRect(handle, out var rect)
            ? (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top)
            : long.MaxValue;

    // ---------------------------------------------------------------- Helpers

    private static T? PickBestTitle<T>(IReadOnlyList<T> candidates, Func<T, string> title, string wanted)
        where T : class
    {
        if (candidates.Count == 0)
            return default;

        var normalizedWanted = NormalizeTitle(wanted);
        if (normalizedWanted.Length == 0)
            return default;

        foreach (var candidate in candidates)
        {
            if (NormalizeTitle(title(candidate)) == normalizedWanted)
                return candidate;
        }

        var startsWith = candidates
            .Where(candidate => NormalizeTitle(title(candidate)).StartsWith(normalizedWanted, StringComparison.Ordinal))
            .OrderBy(candidate => title(candidate).Length)
            .FirstOrDefault();
        if (startsWith is not null)
            return startsWith;

        return candidates
            .Where(candidate => NormalizeTitle(title(candidate)).Contains(normalizedWanted, StringComparison.Ordinal))
            .OrderBy(candidate => title(candidate).Length)
            .FirstOrDefault();
    }

    public static string NormalizeTitle(string value)
    {
        var buffer = new StringBuilder(value.Length);
        foreach (var character in value.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
                buffer.Append(character);
        }

        return buffer.ToString();
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsAnyProcessRunning(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                var running = processes.Length > 0;
                foreach (var process in processes)
                    process.Dispose();
                if (running)
                    return true;
            }
            catch { }
        }

        return false;
    }

    private static bool OpenUri(string uri)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("K-Launcher/4.0");
        return client;
    }

    private static void ActivateWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;

        ShowWindowAsync(handle, SwRestore);
        SetForegroundWindow(handle);
        Thread.Sleep(120);
    }

    private const int SwRestore = 9;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr windowHandle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);
}
