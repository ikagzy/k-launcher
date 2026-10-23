using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KLauncher.Services;

namespace KLauncher;

/// <summary>
/// Ninite-style "pick what you want, install everything in one go" window.
/// Applications are installed, updated or removed silently through winget;
/// Steam/Epic games are handed to their official launchers through the store
/// install flow.
/// </summary>
public sealed class NiniteWindow : Window
{
    private static readonly string[] CategoryOrder =
        ["SearchResults", "Browsers", "Communication", "Gaming", "Media", "Imaging", "Office", "Storage", "Security", "Development", "Tools", "Drivers", "Runtimes"];

    // Same amber in every theme: it means "an update is waiting".
    private static readonly Brush UpdateBrush = CreateFrozenBrush(Color.FromRgb(0xFF, 0xB5, 0x47));

    /// <summary>One line of a job: a winget action on a package, or a game install.</summary>
    private sealed record JobItem(WingetAction Action, WingetPackage? Package, string Name, string? GamePlatform);

    private readonly Func<string, string> _text;
    private readonly Func<string, string, CancellationToken, IProgress<string>, Task<StoreLauncherAutomationResult?>> _installGame;
    private readonly string _defaultGamePlatform;
    private readonly Color _accent;
    private readonly Color _background;

    private readonly Dictionary<string, WingetPackage> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WingetPackage> _searchResults = [];
    private readonly Dictionary<string, List<(string Name, TextBlock Badge)>> _badges = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, WingetInstalledPackage>? _installed;
    private bool _statusLoading;

    private readonly StackPanel _catalogPanel = new();
    private readonly ScrollViewer _catalogScroll;
    private readonly Border _catalogBorder;
    private readonly TextBox _searchBox;
    private readonly Button _searchButton;
    private readonly TextBlock _searchStatus;
    private readonly Grid _toolsRow;
    private readonly TextBlock _statusSummary;
    private readonly Button _updateAllButton;
    private readonly TextBox _gamesBox;
    private readonly Border _gamesSection;
    private readonly Grid _progressSection;
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _progressText;
    private readonly StackPanel _progressRows = new();
    private readonly TextBlock _selectionText;
    private readonly CheckBox _elevateBox;
    private readonly Button _clearButton;
    private readonly Button _uninstallButton;
    private readonly Button _closeButton;
    private readonly Button _retryButton;
    private readonly Button _installButton;

    private CancellationTokenSource? _installCancellation;
    private CancellationTokenSource? _itemCancellation;
    private bool _skipRequested;
    private readonly Button _skipButton;
    private readonly Button _logButton;

    // A single game hand-off must never block the whole queue.
    private static readonly TimeSpan GameStepTimeout = TimeSpan.FromMinutes(3);
    private bool _isInstalling;
    private bool _finished;
    private List<JobItem> _failedItems = [];

    /// <summary>True once anything was installed, updated or removed, so the launcher rescans.</summary>
    public bool HasChanges { get; private set; }

    /// <summary>Raised when a job installed, updated or removed something, while the window stays open.</summary>
    public event Action? ChangesApplied;

    public NiniteWindow(
        Color accent,
        Color background,
        Func<string, string> text,
        Func<string, string, CancellationToken, IProgress<string>, Task<StoreLauncherAutomationResult?>> installGame,
        string defaultGamePlatform = "Steam",
        string? initialGames = null)
    {
        _accent = accent;
        _background = background;
        _text = text;
        _installGame = installGame;
        _defaultGamePlatform = defaultGamePlatform;

        Title = _text("NiniteTitle");
        Width = 860;
        Height = 720;
        MinWidth = 700;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;

        var root = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16, 20, 18)
        };
        root.SetResourceReference(Border.BackgroundProperty, "K.Bg");
        root.SetResourceReference(Border.BorderBrushProperty, "K.OutlineStrong");
        root.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is Border or Grid or TextBlock)
            {
                try { DragMove(); }
                catch { }
            }
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // title
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // search
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // presets / status
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // catalog / progress
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // games
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // buttons

        // ---- Title
        var titleRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = _text("NiniteTitle"),
            Foreground = T("K.Accent"),
            FontFamily = (FontFamily)Application.Current.FindResource("K.Font.Display"),
            FontWeight = FontWeights.Bold,
            FontSize = 19
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = _text("NiniteSubtitle"),
            Foreground = T("K.TextMuted"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        titleRow.Children.Add(titleStack);
        var closeGlyph = new Button
        {
            Content = Glyphs.Close,
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            FontFamily = new FontFamily(Glyphs.FontFamily),
            FontWeight = FontWeights.Normal,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12, -4, -6, 0)
        };
        closeGlyph.SetResourceReference(FrameworkElement.StyleProperty, "K.Button.Ghost");
        closeGlyph.Click += (_, _) => Close();
        Grid.SetColumn(closeGlyph, 1);
        titleRow.Children.Add(closeGlyph);
        Grid.SetRow(titleRow, 0);
        layout.Children.Add(titleRow);

        // ---- Search
        var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _searchBox = new TextBox
        {
            Height = 32,
            Padding = new Thickness(8, 5, 8, 0),
            FontSize = 13,
            ToolTip = _text("NiniteSearchHint")
        };
        var searchHost = new Grid();
        searchHost.Children.Add(_searchBox);
        var searchPlaceholder = new TextBlock
        {
            Text = _text("NiniteSearchHint"),
            Foreground = T("K.TextFaint"),
            FontSize = 12,
            Margin = new Thickness(11, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        searchHost.Children.Add(searchPlaceholder);
        _searchBox.TextChanged += (_, _) =>
            searchPlaceholder.Visibility = _searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _searchBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SearchAsync();
            }
        };
        searchRow.Children.Add(searchHost);

        _searchButton = MakeButton(_text("NiniteSearch"), primary: false, width: 90);
        _searchButton.Margin = new Thickness(8, 0, 0, 0);
        _searchButton.Click += async (_, _) => await SearchAsync();
        Grid.SetColumn(_searchButton, 1);
        searchRow.Children.Add(_searchButton);

        _searchStatus = new TextBlock
        {
            Foreground = T("K.TextMuted"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            MaxWidth = 220,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(_searchStatus, 2);
        searchRow.Children.Add(_searchStatus);
        Grid.SetRow(searchRow, 1);
        layout.Children.Add(searchRow);

        // ---- Presets and installed status
        _toolsRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        _toolsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _toolsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var presets = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        presets.Children.Add(new TextBlock
        {
            Text = _text("NinitePresets"),
            Foreground = T("K.Accent"),
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        foreach (var preset in WingetService.Presets)
        {
            var presetButton = MakeButton(_text("NinitePreset" + preset.Key), primary: false, width: double.NaN);
            presetButton.Height = 28;
            presetButton.Padding = new Thickness(12, 0, 12, 0);
            presetButton.Margin = new Thickness(0, 2, 6, 2);
            presetButton.ToolTip = string.Join(", ", preset.PackageIds
                .Select(id => WingetService.PopularPackages.FirstOrDefault(p => p.Id == id)?.Name ?? id));
            presetButton.Click += (_, _) => TogglePreset(preset);
            presets.Children.Add(presetButton);
        }
        _toolsRow.Children.Add(presets);

        var statusStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        _statusSummary = new TextBlock
        {
            Foreground = T("K.TextMuted"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            MaxWidth = 230,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        statusStack.Children.Add(_statusSummary);
        _updateAllButton = MakeButton(_text("NiniteUpdateAll"), primary: false, width: double.NaN);
        _updateAllButton.Height = 28;
        _updateAllButton.Padding = new Thickness(12, 0, 12, 0);
        _updateAllButton.IsEnabled = false;
        _updateAllButton.ToolTip = _text("NiniteUpdateAllTooltip");
        _updateAllButton.Click += async (_, _) => await UpdateAllAsync();
        statusStack.Children.Add(_updateAllButton);
        Grid.SetColumn(statusStack, 1);
        _toolsRow.Children.Add(statusStack);
        Grid.SetRow(_toolsRow, 2);
        layout.Children.Add(_toolsRow);

        // ---- Catalog
        _catalogScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _catalogPanel
        };
        _catalogBorder = new Border
        {
            Background = T("K.Surface"),
            BorderBrush = T("K.Outline"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 6, 6, 10),
            Child = _catalogScroll
        };
        Grid.SetRow(_catalogBorder, 3);
        layout.Children.Add(_catalogBorder);

        // ---- Progress (replaces the catalog while a job runs)
        _progressSection = new Grid { Visibility = Visibility.Collapsed };
        _progressSection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _progressSection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _progressSection.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _progressText = new TextBlock
        {
            Foreground = T("K.Text"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _progressSection.Children.Add(_progressText);
        _progressBar = new ProgressBar
        {
            Height = 6,
            Minimum = 0,
            Foreground = T("K.Accent"),
            Background = T("K.Control"),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(_progressBar, 1);
        _progressSection.Children.Add(_progressBar);
        var progressScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _progressRows
        };
        var progressBorder = new Border
        {
            Background = T("K.Surface"),
            BorderBrush = T("K.Outline"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Child = progressScroll
        };
        Grid.SetRow(progressBorder, 2);
        _progressSection.Children.Add(progressBorder);
        Grid.SetRow(_progressSection, 3);
        layout.Children.Add(_progressSection);

        // ---- Games
        var gamesStack = new StackPanel();
        gamesStack.Children.Add(new TextBlock
        {
            Text = _text("NiniteGames"),
            Foreground = T("K.Accent"),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(0, 12, 0, 2)
        });
        gamesStack.Children.Add(new TextBlock
        {
            Text = _text("NiniteGamesHint"),
            Foreground = T("K.TextMuted"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });
        _gamesBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 56,
            Padding = new Thickness(8),
            FontSize = 12,
            Text = initialGames ?? string.Empty
        };
        _gamesBox.TextChanged += (_, _) => UpdateSelectionText();
        gamesStack.Children.Add(_gamesBox);
        _gamesSection = new Border { Child = gamesStack };
        Grid.SetRow(_gamesSection, 4);
        layout.Children.Add(_gamesSection);

        // ---- Buttons
        var buttonRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var selectionStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _selectionText = new TextBlock
        {
            Foreground = T("K.TextMuted"),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        selectionStack.Children.Add(_selectionText);
        _elevateBox = new CheckBox
        {
            Content = _text("NiniteElevateOnce"),
            ToolTip = _text("NiniteElevateOnceTooltip"),
            IsChecked = true,
            Foreground = T("K.TextMuted"),
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0),
            Cursor = Cursors.Hand
        };
        selectionStack.Children.Add(_elevateBox);
        buttonRow.Children.Add(selectionStack);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _clearButton = MakeButton(_text("NiniteClear"), primary: false, width: 100);
        _clearButton.Margin = new Thickness(0, 0, 8, 0);
        _clearButton.Click += (_, _) => ClearSelection();
        _uninstallButton = MakeButton(_text("NiniteUninstall"), primary: false, width: 150);
        _uninstallButton.SetResourceReference(FrameworkElement.StyleProperty, "K.Button.Danger");
        _uninstallButton.Margin = new Thickness(0, 0, 8, 0);
        _uninstallButton.ToolTip = _text("NiniteUninstallTooltip");
        _uninstallButton.Click += async (_, _) => await UninstallSelectedAsync();
        _closeButton = MakeButton(_text("Cancel"), primary: false, width: 100);
        _closeButton.Margin = new Thickness(0, 0, 8, 0);
        _closeButton.Click += (_, _) =>
        {
            if (_isInstalling)
                _installCancellation?.Cancel();
            else
                Close();
        };
        _skipButton = MakeButton(_text("NiniteSkip"), primary: false, width: 100);
        _skipButton.Margin = new Thickness(0, 0, 8, 0);
        _skipButton.Visibility = Visibility.Collapsed;
        _skipButton.ToolTip = _text("NiniteSkipTooltip");
        _skipButton.Click += (_, _) =>
        {
            _skipRequested = true;
            _itemCancellation?.Cancel();
        };
        _logButton = MakeButton(_text("NiniteLog"), primary: false, width: 100);
        _logButton.Margin = new Thickness(0, 0, 8, 0);
        _logButton.Visibility = Visibility.Collapsed;
        _logButton.Click += (_, _) =>
        {
            try
            {
                if (System.IO.File.Exists(InstallLog.FilePath))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", $"\"{InstallLog.FilePath}\"") { UseShellExecute = true });
            }
            catch { }
        };
        _retryButton = MakeButton(_text("NiniteRetryFailed"), primary: false, width: double.NaN);
        _retryButton.Padding = new Thickness(14, 0, 14, 0);
        _retryButton.Margin = new Thickness(0, 0, 8, 0);
        _retryButton.Visibility = Visibility.Collapsed;
        _retryButton.ToolTip = _text("NiniteRetryFailedTooltip");
        _retryButton.Click += async (_, _) =>
        {
            var retry = _failedItems.ToList();
            if (retry.Count > 0)
                await RunJobAsync(retry, allowElevation: false);
        };
        _installButton = MakeButton(_text("NiniteInstall"), primary: true, width: 170);
        _installButton.Click += async (_, _) =>
        {
            if (_finished)
            {
                ReturnToCatalog();
                return;
            }

            await InstallSelectedAsync();
        };
        buttons.Children.Add(_clearButton);
        buttons.Children.Add(_uninstallButton);
        buttons.Children.Add(_logButton);
        buttons.Children.Add(_retryButton);
        buttons.Children.Add(_skipButton);
        buttons.Children.Add(_closeButton);
        buttons.Children.Add(_installButton);
        Grid.SetColumn(buttons, 1);
        buttonRow.Children.Add(buttons);
        Grid.SetRow(buttonRow, 5);
        layout.Children.Add(buttonRow);

        root.Child = layout;
        Content = root;

        Closing += (_, e) =>
        {
            if (_isInstalling)
            {
                var answer = LauncherMessageBox.Show(
                    this,
                    _text("NiniteConfirmStop"),
                    "K-LAUNCHER",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                _installCancellation?.Cancel();
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !_isInstalling)
                Close();
        };

        BuildCatalog();
        UpdateSelectionText();
        Loaded += async (_, _) =>
        {
            _searchBox.Focus();
            await LoadStatusAsync();
        };

        // Open animation and rounded corners; Esc and closing keep their own
        // handling above (they must ask before stopping a running install).
        DialogChrome.Attach(this, animateOpen: true, animateClose: false, escapeCloses: false);
    }

    // ------------------------------------------------------------ Catalog UI

    private void BuildCatalog()
    {
        _catalogPanel.Children.Clear();
        _badges.Clear();
        var all = _searchResults
            .Concat(WingetService.PopularPackages)
            .GroupBy(package => package.Category)
            .OrderBy(group =>
            {
                var index = Array.IndexOf(CategoryOrder, group.Key);
                return index < 0 ? int.MaxValue : index;
            });

        foreach (var group in all)
        {
            _catalogPanel.Children.Add(new TextBlock
            {
                Text = _text("NiniteCat" + group.Key).ToUpperInvariant(),
                Foreground = T("K.Accent"),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 4)
            });

            var wrap = new WrapPanel();
            foreach (var package in group)
                wrap.Children.Add(MakePackageCheckBox(package));

            _catalogPanel.Children.Add(wrap);
        }
    }

    private CheckBox MakePackageCheckBox(WingetPackage package)
    {
        var badge = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        if (!_badges.TryGetValue(package.Id, out var list))
            _badges[package.Id] = list = [];
        list.Add((package.Name, badge));
        ApplyBadge(package.Id, package.Name, badge);

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = package.Name,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 112,
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(badge);

        var box = new CheckBox
        {
            Content = content,
            Foreground = T("K.Text"),
            FontSize = 12,
            Width = 192,
            Margin = new Thickness(0, 4, 6, 4),
            ToolTip = package.Id,
            Cursor = Cursors.Hand,
            IsChecked = _selected.ContainsKey(package.Id),
            Tag = package
        };
        box.Checked += (_, _) =>
        {
            _selected[package.Id] = package;
            SyncCheckBoxes(package.Id, true);
            UpdateSelectionText();
        };
        box.Unchecked += (_, _) =>
        {
            _selected.Remove(package.Id);
            SyncCheckBoxes(package.Id, false);
            UpdateSelectionText();
        };
        return box;
    }

    private void ApplyBadge(string packageId, string packageName, TextBlock badge)
    {
        var installed = _installed is null ? null : WingetService.FindInstalled(_installed, packageId, packageName);
        if (installed is null)
        {
            badge.Text = string.Empty;
            badge.ToolTip = null;
            return;
        }

        if (installed.HasUpdate)
        {
            badge.Text = "↑ " + _text("NiniteBadgeUpdate");
            badge.Foreground = UpdateBrush;
            badge.ToolTip = $"{installed.Version} → {installed.Available}";
        }
        else
        {
            badge.Text = "✓ " + _text("NiniteBadgeInstalled");
            badge.Foreground = T("K.Accent2");
            badge.ToolTip = string.Format(_text("NiniteBadgeUpToDate"), installed.Version);
        }
    }

    private void RefreshBadges()
    {
        foreach (var (id, entries) in _badges)
            foreach (var (name, badge) in entries)
                ApplyBadge(id, name, badge);
    }

    private async Task LoadStatusAsync()
    {
        if (_statusLoading)
            return;

        _statusLoading = true;
        _updateAllButton.IsEnabled = false;
        _statusSummary.Text = _text("NiniteStatusLoading");
        try
        {
            _installed = await WingetService.GetInstalledAsync(CancellationToken.None);
            var listed = WingetService.PopularPackages
                .Concat(_searchResults)
                .DistinctBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
                .Count(package => WingetService.FindInstalled(_installed, package.Id, package.Name) is not null);
            var updates = UpdatablePackages().Count;
            _statusSummary.Text = string.Format(_text("NiniteStatusSummary"), listed, updates);
            _updateAllButton.Content = updates > 0
                ? $"{_text("NiniteUpdateAll")} ({updates})"
                : _text("NiniteUpdateAll");
            _updateAllButton.IsEnabled = updates > 0 && !_isInstalling;
            RefreshBadges();
        }
        catch
        {
            _statusSummary.Text = _text("NiniteStatusFailed");
        }
        finally
        {
            _statusLoading = false;
        }
    }

    private List<WingetInstalledPackage> UpdatablePackages() =>
        _installed is null
            ? []
            : _installed.Values
                .Where(package => package.HasUpdate && !package.Id.EndsWith('…'))
                .OrderBy(package => package.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

    private void TogglePreset(WingetPreset preset)
    {
        if (_isInstalling)
            return;

        var packages = preset.PackageIds
            .Select(id => WingetService.PopularPackages.FirstOrDefault(package => package.Id == id))
            .OfType<WingetPackage>()
            .ToList();

        // Second click on a fully selected set clears it again.
        var allSelected = packages.All(package => _selected.ContainsKey(package.Id));
        foreach (var package in packages)
        {
            if (allSelected)
                _selected.Remove(package.Id);
            else
                _selected[package.Id] = package;
            SyncCheckBoxes(package.Id, !allSelected);
        }

        UpdateSelectionText();
    }

    private void SyncCheckBoxes(string id, bool isChecked)
    {
        // The same package can appear both in search results and the list.
        foreach (var wrap in _catalogPanel.Children.OfType<WrapPanel>())
        {
            foreach (var box in wrap.Children.OfType<CheckBox>())
            {
                if (box.Tag is WingetPackage package &&
                    package.Id.Equals(id, StringComparison.OrdinalIgnoreCase) &&
                    box.IsChecked != isChecked)
                    box.IsChecked = isChecked;
            }
        }
    }

    private void ClearSelection()
    {
        _selected.Clear();
        foreach (var wrap in _catalogPanel.Children.OfType<WrapPanel>())
            foreach (var box in wrap.Children.OfType<CheckBox>())
                box.IsChecked = false;
        _gamesBox.Text = string.Empty;
        UpdateSelectionText();
    }

    private void UpdateSelectionText()
    {
        var games = ParseGames(_gamesBox?.Text ?? string.Empty).Count;
        _selectionText.Text = string.Format(_text("NiniteSelected"), _selected.Count, games);
    }

    private async Task SearchAsync()
    {
        var query = _searchBox.Text.Trim();
        if (query.Length < 2 || _isInstalling)
            return;

        _searchButton.IsEnabled = false;
        _searchStatus.Text = _text("NiniteSearching");
        try
        {
            var results = await WingetService.SearchAsync(query, CancellationToken.None);
            _searchResults.Clear();
            _searchResults.AddRange(results.DistinctBy(result => result.Id, StringComparer.OrdinalIgnoreCase));
            _searchStatus.Text = results.Count == 0
                ? _text("NiniteNoResults")
                : string.Format(_text("NiniteResultCount"), results.Count);
            BuildCatalog();
            _catalogScroll.ScrollToTop();
        }
        catch (Exception ex)
        {
            _searchStatus.Text = ex.Message;
        }
        finally
        {
            _searchButton.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------------- Jobs

    private List<(string Platform, string Name)> ParseGames(string text)
    {
        var entries = new List<(string Platform, string Name)>();
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var platform = _defaultGamePlatform;
            var name = line;
            if (line.StartsWith("Steam:", StringComparison.OrdinalIgnoreCase))
            {
                platform = "Steam";
                name = line[6..].Trim();
            }
            else if (line.StartsWith("Epic:", StringComparison.OrdinalIgnoreCase))
            {
                platform = "Epic";
                name = line[5..].Trim();
            }

            if (name.Length > 0 && !entries.Any(entry =>
                    entry.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase) &&
                    entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                entries.Add((platform, name));
        }

        return entries;
    }

    private async Task InstallSelectedAsync()
    {
        if (_isInstalling)
            return;

        var items = _selected.Values
            .Select(package => new JobItem(WingetAction.Install, package, package.Name, null))
            .Concat(ParseGames(_gamesBox.Text)
                .Select(game => new JobItem(WingetAction.Install, null, game.Name, game.Platform)))
            .ToList();
        if (items.Count == 0)
        {
            LauncherMessageBox.Show(this, _text("NiniteNothingSelected"), "K-LAUNCHER",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunJobAsync(items, allowElevation: true);
    }

    private async Task UpdateAllAsync()
    {
        if (_isInstalling)
            return;

        var items = UpdatablePackages()
            .Select(package => new JobItem(
                WingetAction.Upgrade,
                new WingetPackage(package.Id, package.Name, string.Empty),
                package.Name,
                null))
            .ToList();
        if (items.Count > 0)
            await RunJobAsync(items, allowElevation: true);
    }

    private async Task UninstallSelectedAsync()
    {
        if (_isInstalling)
            return;

        var packages = _selected.Values.ToList();
        if (packages.Count == 0)
        {
            LauncherMessageBox.Show(this, _text("NiniteNothingToUninstall"), "K-LAUNCHER",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var names = string.Join("\n", packages.Take(12).Select(package => "• " + package.Name));
        if (packages.Count > 12)
            names += $"\n• … (+{packages.Count - 12})";
        var answer = LauncherMessageBox.Show(
            this,
            string.Format(_text("NiniteConfirmUninstall"), packages.Count, names),
            "K-LAUNCHER",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        await RunJobAsync(
            packages.Select(package => new JobItem(WingetAction.Uninstall, package, package.Name, null)).ToList(),
            allowElevation: true);
    }

    private async Task RunJobAsync(List<JobItem> items, bool allowElevation)
    {
        if (_isInstalling || items.Count == 0)
            return;

        _isInstalling = true;
        _finished = false;
        _installCancellation = new CancellationTokenSource();
        var token = _installCancellation.Token;

        _catalogBorder.Visibility = Visibility.Collapsed;
        _gamesSection.Visibility = Visibility.Collapsed;
        _toolsRow.Visibility = Visibility.Collapsed;
        _progressSection.Visibility = Visibility.Visible;
        _progressRows.Children.Clear();
        _searchBox.IsEnabled = false;
        _searchButton.IsEnabled = false;
        _clearButton.Visibility = Visibility.Collapsed;
        _uninstallButton.Visibility = Visibility.Collapsed;
        _retryButton.Visibility = Visibility.Collapsed;
        _logButton.Visibility = Visibility.Collapsed;
        _elevateBox.IsEnabled = false;
        _installButton.IsEnabled = false;
        _closeButton.Visibility = Visibility.Visible;
        _closeButton.Content = _text("NiniteStop");
        _skipButton.Visibility = Visibility.Visible;

        var appItems = items.Where(item => item.Package is not null).ToList();
        var gameItems = items.Where(item => item.Package is null).ToList();
        InstallLog.Write($"=== Toplu işlem başladı: {appItems.Count} uygulama, {gameItems.Count} oyun ===");

        var rows = new Dictionary<JobItem, (TextBlock Glyph, TextBlock Status)>();
        foreach (var item in appItems.Concat(gameItems))
            rows[item] = AddProgressRow(item.Name, SourceLabel(item));

        var total = rows.Count;
        _progressBar.Maximum = total;
        _progressBar.Value = 0;

        int succeeded = 0, nothingToDo = 0, failed = 0, completed = 0;
        var failedItems = new List<JobItem>();

        void Finish(JobItem item, WingetInstallResult result)
        {
            var (glyph, status) = rows[item];
            switch (result.Status)
            {
                case WingetInstallStatus.Installed:
                    succeeded++;
                    SetRow(glyph, status, "✓", T("K.Accent"), result.Message);
                    break;
                case WingetInstallStatus.AlreadyInstalled:
                    nothingToDo++;
                    SetRow(glyph, status, "↺", T("K.Accent2"), result.Message);
                    break;
                case WingetInstallStatus.Cancelled:
                    SetRow(glyph, status, "–", T("K.TextMuted"), result.Message);
                    break;
                default:
                    failed++;
                    failedItems.Add(item);
                    SetRow(glyph, status, "×", T("K.Danger"), result.Message);
                    break;
            }

            completed++;
            _progressBar.Value = completed;
        }

        if (appItems.Count > 0)
        {
            if (!await WingetService.IsAvailableAsync())
            {
                foreach (var item in appItems)
                    Finish(item, new(WingetInstallStatus.Failed, _text("NiniteWingetMissing")));
            }
            else
            {
                var ranElevated = false;
                if (allowElevation && _elevateBox.IsChecked == true && !token.IsCancellationRequested)
                {
                    _progressText.Text = _text("NiniteWaitingForApproval");
                    var reported = new HashSet<int>();
                    ranElevated = await WingetBatchHost.RunElevatedAsync(
                        appItems.Select(item => new WingetBatchItem(item.Action, item.Package!.Id)).ToList(),
                        started: index =>
                        {
                            _skipRequested = false;
                            var item = appItems[index];
                            _progressText.Text = string.Format(_text(ProgressKey(item.Action)), item.Name, completed + 1, total);
                            var (glyph, status) = rows[item];
                            SetRow(glyph, status, "▶", T("K.Accent"), _text(InProgressKey(item.Action)));
                            InstallLog.Write($"winget (yönetici): {item.Action} {item.Package!.Id}");
                        },
                        line: (index, text) =>
                        {
                            if (!reported.Contains(index))
                            {
                                text = Localize(text);
                                rows[appItems[index]].Status.Text = text.Length > 90 ? text[..90] + "…" : text;
                            }
                        },
                        done: (index, result) =>
                        {
                            if (!reported.Add(index))
                                return;
                            var item = appItems[index];
                            InstallLog.Write($"winget (yönetici): {item.Package!.Id} → {result.Status}: {result.Message}");
                            Finish(item, result);
                        },
                        skipRequested: _ => _skipRequested,
                        token);

                    if (!ranElevated)
                        InstallLog.Write("Yönetici onayı verilmedi, tek tek devam ediliyor");
                }

                if (!ranElevated)
                {
                    foreach (var item in appItems)
                        Finish(item, await RunWingetItemAsync(item, rows[item], completed, total, token));
                }
            }
        }

        foreach (var item in gameItems)
        {
            Finish(item, await RunGameItemAsync(item, rows[item], completed, total, token));
            Activate();
        }

        _isInstalling = false;
        _finished = true;
        _failedItems = failedItems;
        if (succeeded > 0)
        {
            HasChanges = true;
            ChangesApplied?.Invoke();
        }
        _progressBar.Value = total;
        _progressText.Text = string.Format(_text("NiniteDoneAll"), succeeded, nothingToDo, failed);
        _skipButton.Visibility = Visibility.Collapsed;
        _logButton.Visibility = Visibility.Visible;
        _closeButton.Content = _text("NiniteClose");
        _retryButton.Content = $"{_text("NiniteRetryFailed")} ({failedItems.Count})";
        _retryButton.Visibility = failedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        InstallLog.Write($"=== Bitti: {succeeded} başarılı, {nothingToDo} gerek yoktu, {failed} başarısız ===");
        _installButton.Content = _text("NiniteBackToList");
        _installButton.IsEnabled = true;
        Activate();

        // Installed / update marks follow what just changed.
        _ = LoadStatusAsync();
    }

    private async Task<WingetInstallResult> RunWingetItemAsync(
        JobItem item,
        (TextBlock Glyph, TextBlock Status) row,
        int completed,
        int total,
        CancellationToken token)
    {
        var (glyph, status) = row;
        if (token.IsCancellationRequested)
            return new(WingetInstallStatus.Cancelled, _text("NiniteSkipped"));

        _progressText.Text = string.Format(_text(ProgressKey(item.Action)), item.Name, completed + 1, total);
        SetRow(glyph, status, "▶", T("K.Accent"), _text(InProgressKey(item.Action)));
        var progress = new Progress<string>(line =>
        {
            line = Localize(line);
            status.Text = line.Length > 90 ? line[..90] + "…" : line;
        });

        using var itemCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _itemCancellation = itemCancellation;
        _skipRequested = false;
        InstallLog.Write($"winget: {item.Action} {item.Package!.Id}");
        var result = await WingetService.ExecuteAsync(item.Action, item.Package.Id, progress, itemCancellation.Token);
        _itemCancellation = null;
        if (result.Status == WingetInstallStatus.Cancelled && _skipRequested && !token.IsCancellationRequested)
            result = new(WingetInstallStatus.Cancelled, _text("NiniteSkippedByUser"));
        InstallLog.Write($"winget: {item.Package.Id} → {result.Status}: {result.Message}");
        return result;
    }

    private async Task<WingetInstallResult> RunGameItemAsync(
        JobItem item,
        (TextBlock Glyph, TextBlock Status) row,
        int completed,
        int total,
        CancellationToken token)
    {
        var (glyph, status) = row;
        var platform = item.GamePlatform ?? _defaultGamePlatform;
        if (token.IsCancellationRequested)
            return new(WingetInstallStatus.Cancelled, _text("NiniteSkipped"));

        _progressText.Text = string.Format(_text("NiniteInstalling"), item.Name, completed + 1, total);
        SetRow(glyph, status, "▶", T("K.Accent"), string.Format(_text("NiniteHandingOff"), platform));

        StoreLauncherAutomationResult? result;
        using (var itemCancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            _itemCancellation = itemCancellation;
            _skipRequested = false;
            itemCancellation.CancelAfter(GameStepTimeout);
            var gameProgress = new Progress<string>(line =>
                status.Text = line.Length > 90 ? line[..90] + "…" : line);
            InstallLog.Write($"oyun: {platform}: {item.Name} başlıyor");

            var work = _installGame(platform, item.Name, itemCancellation.Token, gameProgress);
            var stopped = new TaskCompletionSource();
            using (itemCancellation.Token.Register(() => stopped.TrySetResult()))
                await Task.WhenAny(work, stopped.Task);

            if (work.IsCompleted)
            {
                try
                {
                    result = await work;
                }
                catch (OperationCanceledException)
                {
                    result = null;
                }
                catch (Exception ex)
                {
                    result = new(false, ex.Message);
                }
            }
            else
            {
                // The launcher did not answer. Leave it to finish in the
                // background and move on with the queue.
                _ = work.ContinueWith(task => _ = task.Exception, TaskScheduler.Default);
                result = null;
            }

            if (result is null && !token.IsCancellationRequested)
            {
                result = new(false, _skipRequested
                    ? _text("NiniteSkippedByUser")
                    : string.Format(_text("NiniteTimedOut"), platform));
            }

            _itemCancellation = null;
            InstallLog.Write($"oyun: {platform}: {item.Name} → {(result is { Success: true } ? "tamam" : "olmadı")}: {result?.Message}");
        }

        if (result is null)
            return new(WingetInstallStatus.Cancelled, _text("NiniteSkipped"));

        if (!result.Success)
            return new(WingetInstallStatus.Failed, result.Message);

        return result.Message.Contains("zaten kurulu", StringComparison.OrdinalIgnoreCase)
            ? new(WingetInstallStatus.AlreadyInstalled, result.Message)
            : new(WingetInstallStatus.Installed, result.Message);
    }

    private void ReturnToCatalog()
    {
        _finished = false;
        _progressSection.Visibility = Visibility.Collapsed;
        _progressRows.Children.Clear();
        _catalogBorder.Visibility = Visibility.Visible;
        _gamesSection.Visibility = Visibility.Visible;
        _toolsRow.Visibility = Visibility.Visible;
        _searchBox.IsEnabled = true;
        _searchButton.IsEnabled = true;
        _clearButton.Visibility = Visibility.Visible;
        _uninstallButton.Visibility = Visibility.Visible;
        _retryButton.Visibility = Visibility.Collapsed;
        _logButton.Visibility = Visibility.Collapsed;
        _elevateBox.IsEnabled = true;
        _closeButton.Content = _text("Cancel");
        _installButton.Content = _text("NiniteInstall");
        _failedItems = [];
        _updateAllButton.IsEnabled = UpdatablePackages().Count > 0;
    }

    private string SourceLabel(JobItem item) => item.Package is null
        ? item.GamePlatform ?? _defaultGamePlatform
        : item.Action switch
        {
            WingetAction.Upgrade => $"winget • {_text("NiniteActionUpgrade")}",
            WingetAction.Uninstall => $"winget • {_text("NiniteActionUninstall")}",
            _ => "winget"
        };

    private static string ProgressKey(WingetAction action) => action switch
    {
        WingetAction.Upgrade => "NiniteUpdating",
        WingetAction.Uninstall => "NiniteUninstalling",
        _ => "NiniteInstalling"
    };

    private static string InProgressKey(WingetAction action) => action switch
    {
        WingetAction.Upgrade => "NiniteUpdateInProgress",
        WingetAction.Uninstall => "NiniteUninstallInProgress",
        _ => "NiniteInProgress"
    };

    private (TextBlock Glyph, TextBlock Status) AddProgressRow(string name, string source)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var glyph = new TextBlock { Text = "•", Foreground = T("K.TextFaint"), FontSize = 13, FontWeight = FontWeights.Bold };
        row.Children.Add(glyph);

        var nameBlock = new TextBlock
        {
            Text = $"{name}",
            Foreground = T("K.Text"),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = source
        };
        var sourceRun = new System.Windows.Documents.Run($"  {source}") { Foreground = T("K.TextFaint"), FontSize = 10 };
        nameBlock.Inlines.Add(sourceRun);
        Grid.SetColumn(nameBlock, 1);
        row.Children.Add(nameBlock);

        var status = new TextBlock
        {
            Text = _text("NiniteWaiting"),
            Foreground = T("K.TextMuted"),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(status, 2);
        row.Children.Add(status);

        _progressRows.Children.Add(row);
        return (glyph, status);
    }

    /// <summary>
    /// winget results and progress lines arrive as localization keys
    /// ("WingetMsgInstalled", "WingetMsgTryingAlternate:Id"); anything else is
    /// raw winget output and is shown as it is.
    /// </summary>
    private string Localize(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message, @"^((?:WingetMsg|Ninite)\w+)(?::(.*))?$");
        if (!match.Success)
            return message;

        var text = _text(match.Groups[1].Value);
        return match.Groups[2].Success ? string.Format(text, match.Groups[2].Value) : text;
    }

    private void SetRow(TextBlock glyph, TextBlock status, string symbol, Brush color, string message)
    {
        message = Localize(message);
        glyph.Text = symbol;
        glyph.Foreground = color;
        status.Text = message;
        status.Foreground = symbol == "×" ? T("K.Danger") : T("K.TextMuted");
        status.ToolTip = message;
    }

    // --------------------------------------------------------------- Helpers

    private Button MakeButton(string content, bool primary, double width)
    {
        var button = new Button
        {
            Content = content,
            Width = width,
            Height = 34
        };
        button.SetResourceReference(
            FrameworkElement.StyleProperty,
            primary ? "K.Button.Primary" : "K.Button.Secondary");
        return button;
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // Theme tokens (K.*) are frozen brushes owned by the application, so
    // reading them costs nothing and no per-control brush is allocated.
    private static Brush T(string key) => (Brush)Application.Current.FindResource(key);
}
