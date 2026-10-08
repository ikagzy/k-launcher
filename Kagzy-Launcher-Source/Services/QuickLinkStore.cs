using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KLauncher.Models;

namespace KLauncher.Services;

public static class QuickLinkStore
{
    private static readonly object Sync = new();

    private static readonly string Folder =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "K-Launcher");

    private static readonly string FilePath =
        Path.Combine(Folder, "quick-links.json");

    private static readonly string RemovedFilePath =
        Path.Combine(Folder, "quick-links-removed.json");

    private static List<QuickLink> Links(
        params (string Name, string Url)[] values)
    {
        return values
            .Select(x => new QuickLink
            {
                Name = x.Name,
                Url = x.Url
            })
            .ToList();
    }

    private static readonly Dictionary<string, List<QuickLink>> Defaults =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Opera GX"] = Links(
                ("YouTube", "https://www.youtube.com/"),
                ("Twitch", "https://www.twitch.tv/"),
                ("WhatsApp Web", "https://web.whatsapp.com/"),
                ("GitHub", "https://github.com/"),
                ("ChatGPT", "https://chatgpt.com/")),

            ["Google Chrome"] = Links(
                ("Google", "https://www.google.com/"),
                ("YouTube", "https://www.youtube.com/"),
                ("ChatGPT", "https://chatgpt.com/"),
                ("GitHub", "https://github.com/"),
                ("Gmail", "https://mail.google.com/")),

            ["Mozilla Firefox"] = Links(
                ("Google", "https://www.google.com/"),
                ("YouTube", "https://www.youtube.com/"),
                ("ChatGPT", "https://chatgpt.com/"),
                ("GitHub", "https://github.com/"),
                ("Gmail", "https://mail.google.com/")),

            ["Brave"] = Links(
                ("Google", "https://www.google.com/"),
                ("YouTube", "https://www.youtube.com/"),
                ("ChatGPT", "https://chatgpt.com/"),
                ("GitHub", "https://github.com/"),
                ("Gmail", "https://mail.google.com/")),

            ["Vivaldi"] = Links(
                ("Google", "https://www.google.com/"),
                ("YouTube", "https://www.youtube.com/"),
                ("ChatGPT", "https://chatgpt.com/"),
                ("GitHub", "https://github.com/"),
                ("Gmail", "https://mail.google.com/")),

            ["Discord"] = Links(
                ("Discord Web", "https://discord.com/app"),
                ("Discord Status", "https://discordstatus.com/")),

            ["Steam"] = Links(
                ("Steam Store", "https://store.steampowered.com/"),
                ("Steam Community", "https://steamcommunity.com/"),
                ("Steam Support", "https://help.steampowered.com/")),

            ["Epic Games"] = Links(
                ("Epic Games Store", "https://store.epicgames.com/"),
                ("Epic Account", "https://store.epicgames.com/account/")),

            ["EA App"] = Links(
                ("EA", "https://www.ea.com/"),
                ("EA Account", "https://myaccount.ea.com/")),

            ["Ubisoft Connect"] = Links(
                ("Ubisoft", "https://www.ubisoft.com/"),
                ("Ubisoft Store", "https://store.ubisoft.com/")),

            ["Battle.net"] = Links(
                ("Battle.net", "https://www.battle.net/"),
                ("Blizzard", "https://www.blizzard.com/")),

            ["GOG Galaxy"] = Links(
                ("GOG", "https://www.gog.com/"),
                ("GOG Support", "https://support.gog.com/")),

            ["Riot Client"] = Links(
                ("Riot Games", "https://www.riotgames.com/"),
                ("VALORANT", "https://playvalorant.com/"),
                ("League of Legends", "https://www.leagueoflegends.com/")),

            ["Rockstar Games Launcher"] = Links(
                ("Rockstar Games", "https://www.rockstargames.com/"),
                ("Rockstar Support", "https://support.rockstargames.com/")),

            ["Amazon Games"] = Links(
                ("Amazon Games", "https://gaming.amazon.com/")),

            ["itch.io"] = Links(
                ("itch.io", "https://itch.io/")),

            ["HoYoPlay"] = Links(
                ("HoYoverse", "https://www.hoyoverse.com/")),

            ["Medal"] = Links(
                ("Medal", "https://medal.tv/")),

            ["Minecraft Launcher"] = Links(
                ("Minecraft", "https://www.minecraft.net/"),
                ("Minecraft Download", "https://www.minecraft.net/download")),

            ["Spotify"] = Links(
                ("Spotify Web Player", "https://open.spotify.com/")),

            ["VS Code"] = Links(
                ("GitHub", "https://github.com/"),
                ("Stack Overflow", "https://stackoverflow.com/"),
                ("NuGet", "https://www.nuget.org/"))
        };

    public static void Apply(AppModel app)
    {
        if (app == null)
            return;

        // Xbox hiçbir zaman Quick Links kullanmaz.
        if (app.Name.Equals(
            "Xbox",
            StringComparison.OrdinalIgnoreCase))
        {
            app.QuickLinks.Clear();
            RemoveStoredLinks("Xbox");
            return;
        }

        var merged = new List<QuickLink>();
        var removedDefaults = LoadRemoved(app.Name);

        if (Defaults.TryGetValue(
            app.Name,
            out var defaults))
        {
            merged.AddRange(defaults.Where(link =>
                !removedDefaults.Any(removed => SameLink(removed, link))));
        }

        foreach (var link in LoadCustom(app.Name))
        {
            if (!merged.Any(x =>
                x.Name.Equals(
                    link.Name,
                    StringComparison.OrdinalIgnoreCase) &&
                x.Url.Equals(
                    link.Url,
                    StringComparison.OrdinalIgnoreCase)))
            {
                merged.Add(link);
            }
        }

        app.QuickLinks.Clear();

        foreach (var link in merged)
            app.QuickLinks.Add(link);
    }

    public static void Add(
        string appName,
        string name,
        string url)
    {
        if (string.IsNullOrWhiteSpace(appName))
            throw new ArgumentException(
                "Uygulama adi bos olamaz.",
                nameof(appName));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Link adi bos olamaz.",
                nameof(name));

        if (!Uri.TryCreate(
            url,
            UriKind.Absolute,
            out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "Gecerli bir HTTP veya HTTPS adresi gir.",
                nameof(url));
        }

        // Xbox'a link eklenmesini tamamen engelle.
        if (appName.Equals(
            "Xbox",
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (Sync)
        {
            Directory.CreateDirectory(Folder);

            var all = LoadAllUnsafe();

            if (!all.TryGetValue(
                appName,
                out var list))
            {
                list = new List<QuickLink>();
                all[appName] = list;
            }

            list.RemoveAll(x =>
                x.Name.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase));

            list.Add(new QuickLink
            {
                Name = name,
                Url = uri.ToString()
            });

            var removed = LoadRemovedUnsafe();
            if (removed.TryGetValue(appName, out var removedLinks))
            {
                removedLinks.RemoveAll(x =>
                    x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    x.Url.Equals(uri.ToString(), StringComparison.OrdinalIgnoreCase));

                if (removedLinks.Count == 0)
                    removed.Remove(appName);

                SaveRemovedUnsafe(removed);
            }

            SaveAllUnsafe(all);
        }
    }

    public static void Remove(string appName, QuickLink link)
    {
        if (string.IsNullOrWhiteSpace(appName) || link == null)
            return;

        if (appName.Equals("Xbox", StringComparison.OrdinalIgnoreCase))
            return;

        lock (Sync)
        {
            var all = LoadAllUnsafe();
            var customChanged = false;
            if (all.TryGetValue(appName, out var customLinks))
            {
                customChanged = customLinks.RemoveAll(x => SameLink(x, link)) > 0;
                if (customLinks.Count == 0)
                    all.Remove(appName);
            }

            if (customChanged)
                SaveAllUnsafe(all);

            var isDefault = Defaults.TryGetValue(appName, out var defaults) &&
                            defaults.Any(x => SameLink(x, link));
            if (!isDefault)
                return;

            var removed = LoadRemovedUnsafe();
            if (!removed.TryGetValue(appName, out var removedLinks))
            {
                removedLinks = new List<QuickLink>();
                removed[appName] = removedLinks;
            }

            if (!removedLinks.Any(x => SameLink(x, link)))
            {
                removedLinks.Add(new QuickLink
                {
                    Name = link.Name,
                    Url = link.Url
                });
                SaveRemovedUnsafe(removed);
            }
        }
    }

    private static bool SameLink(QuickLink first, QuickLink second)
    {
        return first.Name.Equals(second.Name, StringComparison.OrdinalIgnoreCase) &&
               first.Url.Equals(second.Url, StringComparison.OrdinalIgnoreCase);
    }

    private static List<QuickLink> LoadCustom(
        string appName)
    {
        lock (Sync)
        {
            var all = LoadAllUnsafe();

            if (all.TryGetValue(
                appName,
                out var list))
            {
                return list;
            }

            return new List<QuickLink>();
        }
    }

    private static void RemoveStoredLinks(
        string appName)
    {
        lock (Sync)
        {
            try
            {
                var all = LoadAllUnsafe();

                if (all.Remove(appName))
                    SaveAllUnsafe(all);
            }
            catch
            {
            }
        }
    }

    private static Dictionary<string, List<QuickLink>>
        LoadAllUnsafe()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new Dictionary<string, List<QuickLink>>(
                    StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(FilePath);

            return JsonSerializer.Deserialize<
                Dictionary<string, List<QuickLink>>>(
                json)
                ?? new Dictionary<string, List<QuickLink>>(
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, List<QuickLink>>(
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private static List<QuickLink> LoadRemoved(string appName)
    {
        lock (Sync)
        {
            return LoadRemovedUnsafe().TryGetValue(appName, out var links)
                ? links
                : new List<QuickLink>();
        }
    }

    private static Dictionary<string, List<QuickLink>> LoadRemovedUnsafe()
    {
        try
        {
            if (!File.Exists(RemovedFilePath))
            {
                return new Dictionary<string, List<QuickLink>>(
                    StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(RemovedFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, List<QuickLink>>>(json)
                   ?? new Dictionary<string, List<QuickLink>>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, List<QuickLink>>(
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveRemovedUnsafe(Dictionary<string, List<QuickLink>> removed)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(
            RemovedFilePath,
            JsonSerializer.Serialize(removed, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void SaveAllUnsafe(
        Dictionary<string, List<QuickLink>> all)
    {
        Directory.CreateDirectory(Folder);

        File.WriteAllText(
            FilePath,
            JsonSerializer.Serialize(
                all,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
    }
}
