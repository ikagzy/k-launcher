using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using KLauncher.Models;
using KLauncher.Services;

namespace KLauncher.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly GameScanner _scanner = new();
    private bool _isScanning;

    public ObservableCollection<AppModel> Apps { get; } = new();
    public IReadOnlyList<HiddenApp> HiddenApps => HiddenAppStore.Load();

    public void HideApplication(AppModel app)
    {
        HiddenAppStore.Hide(app.Name, app.LaunchTarget);
        Apps.Remove(app);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HiddenApps)));
    }

    public async Task RestoreHiddenApplicationAsync(string launchTarget)
    {
        HiddenAppStore.Restore(launchTarget);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HiddenApps)));
        await RefreshAsync();
    }

    public async Task RestoreAllHiddenApplicationsAsync()
    {
        HiddenAppStore.RestoreAll();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HiddenApps)));
        await RefreshAsync();
    }

    public bool AddUserApplication(string launchTarget)
    {
        if (string.IsNullOrWhiteSpace(launchTarget) || !File.Exists(launchTarget))
            return false;

        var extension = Path.GetExtension(launchTarget);
        if (!new[] { ".exe", ".lnk", ".appref-ms" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return false;

        var name = Path.GetFileNameWithoutExtension(launchTarget);
        if (Apps.Any(app => app.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!CustomAppStore.Add(name, launchTarget))
            return false;

        return true;
    }

    public void RemoveUserApplication(AppModel app)
    {
        if (!app.IsUserAdded)
            return;

        CustomAppStore.Remove(app);
        Apps.Remove(app);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (_isScanning == value)
                return;

            _isScanning = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsScanning)));
        }
    }

    public async Task RefreshAsync()
    {
        if (IsScanning)
            return;

        try
        {
            IsScanning = true;

            var scanned = await _scanner.ScanAsync();
            var hiddenTargets = HiddenAppStore.Load()
                .Select(app => app.LaunchTarget)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Apps.Clear();

            foreach (var app in scanned)
            {
                if (app == null)
                    continue;

                if (string.IsNullOrWhiteSpace(app.LaunchTarget))
                    continue;

                var valid =
                    app.LaunchTarget.StartsWith(
                        "shell:",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    app.LaunchTarget.StartsWith(
                        "http://",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    app.LaunchTarget.StartsWith(
                        "https://",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    File.Exists(app.LaunchTarget);

                if (!valid)
                    continue;

                if (hiddenTargets.Contains(app.LaunchTarget))
                    continue;

                EnsureQuickLinks(app);

                Apps.Add(app);
            }
        }
        finally
        {
            IsScanning = false;
        }
    }

    private static void EnsureQuickLinks(AppModel app)
    {
        if (app.Name.Equals("Xbox", StringComparison.OrdinalIgnoreCase))
        {
            app.QuickLinks.Clear();
            return;
        }
        if (app.Name.Equals("Xbox", StringComparison.OrdinalIgnoreCase))
            return;

        var links = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        switch (app.Name)
        {
            case "Opera GX":
            case "Opera":
            case "Google Chrome":
            case "Microsoft Edge":
            case "Mozilla Firefox":
            case "Brave":
            case "Vivaldi":
                links["YouTube"] = "https://www.youtube.com/";
                links["Twitch"] = "https://www.twitch.tv/";
                links["ChatGPT"] = "https://chatgpt.com/";
                links["GitHub"] = "https://github.com/";
                links["Gmail"] = "https://mail.google.com/";
                break;

            case "Discord":
                links["Discord Web"] = "https://discord.com/app";
                links["Discord Status"] = "https://discordstatus.com/";
                break;

            case "Steam":
                links["Steam Store"] = "https://store.steampowered.com/";
                links["Steam Community"] = "https://steamcommunity.com/";
                break;

            case "Epic Games":
                links["Epic Store"] = "https://store.epicgames.com/";
                break;

            case "EA App":
                links["EA"] = "https://www.ea.com/";
                links["EA Account"] = "https://myaccount.ea.com/";
                break;

            case "Ubisoft Connect":
                links["Ubisoft"] = "https://www.ubisoft.com/";
                links["Ubisoft Store"] = "https://store.ubisoft.com/";
                break;

            case "Battle.net":
                links["Battle.net"] = "https://www.battle.net/";
                links["Blizzard"] = "https://www.blizzard.com/";
                break;

            case "GOG Galaxy":
                links["GOG"] = "https://www.gog.com/";
                break;

            case "Riot Client":
                links["Riot Games"] = "https://www.riotgames.com/";
                links["VALORANT"] = "https://playvalorant.com/";
                links["League of Legends"] = "https://www.leagueoflegends.com/";
                break;

            case "Rockstar Games Launcher":
                links["Rockstar Games"] = "https://www.rockstargames.com/";
                break;

            case "Amazon Games":
                links["Amazon Games"] = "https://gaming.amazon.com/";
                break;

            case "itch.io":
                links["itch.io"] = "https://itch.io/";
                break;

            case "HoYoPlay":
                links["HoYoverse"] = "https://www.hoyoverse.com/";
                break;

            case "Medal":
                links["Medal"] = "https://medal.tv/";
                break;

            case "Minecraft":
            case "Minecraft Launcher":
                links["Minecraft"] = "https://www.minecraft.net/";
                links["Download"] = "https://www.minecraft.net/download";
                break;

            case "Spotify":
                links["Spotify Web Player"] = "https://open.spotify.com/";
                break;

            case "VS Code":
                links["GitHub"] = "https://github.com/";
                links["Stack Overflow"] = "https://stackoverflow.com/";
                links["NuGet"] = "https://www.nuget.org/";
                break;
        }

        foreach (var pair in links)
        {
            if (!app.QuickLinks.Any(x =>
                x.Name.Equals(
                    pair.Key,
                    StringComparison.OrdinalIgnoreCase)))
            {
                app.QuickLinks.Add(
                    new QuickLink(pair.Key, pair.Value));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

