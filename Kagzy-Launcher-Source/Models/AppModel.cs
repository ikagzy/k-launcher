using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace KLauncher.Models;

public sealed class AppModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _localizedName = string.Empty;
    private string _exePath = string.Empty;
    private string _iconPath = string.Empty;
    private string _launchTarget = string.Empty;
    private string _launchArguments = string.Empty;
    private bool _isSpecialLogo;
    private bool _isLauncher;
    private bool _isUserAdded;
    private string _fallbackGlyph = "APP";

    public string Name { get => _name; set => SetField(ref _name, value); }
    public string LocalizedName { get => _localizedName; set => SetField(ref _localizedName, value); }
    public string ExePath { get => _exePath; set => SetField(ref _exePath, value); }
    public string IconPath { get => _iconPath; set => SetField(ref _iconPath, value); }
    public string LaunchTarget { get => _launchTarget; set => SetField(ref _launchTarget, value); }
    public string LaunchArguments { get => _launchArguments; set => SetField(ref _launchArguments, value); }
    public bool IsSpecialLogo { get => _isSpecialLogo; set => SetField(ref _isSpecialLogo, value); }
    public bool IsLauncher { get => _isLauncher; set => SetField(ref _isLauncher, value); }
    public bool IsUserAdded { get => _isUserAdded; set => SetField(ref _isUserAdded, value); }
    public string FallbackGlyph { get => _fallbackGlyph; set => SetField(ref _fallbackGlyph, value); }

    public ObservableCollection<GameItem> Games { get; } = new();
    public ObservableCollection<QuickLink> QuickLinks { get; } = new();

    public bool CanLaunch => !string.IsNullOrWhiteSpace(LaunchTarget) &&
        (LaunchTarget.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
         LaunchTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         LaunchTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
         File.Exists(LaunchTarget));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(LaunchTarget))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanLaunch)));
    }
}

public sealed class GameItem
{
    public string Name { get; init; } = string.Empty;
    public string ExePath { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
    public string LaunchTarget { get; init; } = string.Empty;
    public string LaunchArguments { get; init; } = string.Empty;
}

public sealed class QuickLink
{
    public QuickLink() { }
    public QuickLink(string name, string url)
    {
        Name = name;
        Url = url;
    }

    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}
