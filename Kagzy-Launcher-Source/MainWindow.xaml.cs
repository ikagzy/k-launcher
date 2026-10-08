using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using KLauncher.Models;
using KLauncher.Services;
using KLauncher.ViewModels;

namespace KLauncher;

public partial class MainWindow : Window
{
    private const int SwRestore = 9;
    private const int SwShow = 5;
    private const int DwmwaBorderColor = 34;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const int WmSysCommand = 0x0112;
    private const long ScMinimize = 0xF020;
    private const string AppsFolderPrefix = "shell:AppsFolder\\";

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager
    {
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            uint options,
            out uint processId);
    }

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref uint value, int valueSize);

    private static readonly HttpClient IconHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private static readonly ConcurrentDictionary<string, Task<BitmapSource?>> RemoteIconLoads = new(StringComparer.OrdinalIgnoreCase);

    private static Color CardBackgroundColor = Color.FromRgb(19, 19, 25);
    private static Color CardBorderColor = Color.FromRgb(27, 27, 36);
    private static Color ThemeAccentColor = Color.FromRgb(0, 255, 102);
    private static Color ThemeSecondaryColor = Color.FromRgb(0, 169, 74);
    private static Color ThemeBackgroundColor = Color.FromRgb(16, 16, 20);
    private static Color ThemeSurfaceColor = Color.FromRgb(17, 17, 22);
    private static Color ThemeCardColor = Color.FromRgb(19, 19, 25);
    private static Color ThemeControlColor = Color.FromRgb(20, 25, 26);
    private static Color ThemeOutlineColor = Color.FromRgb(27, 27, 36);
    private static Color PreviousThemeAccentColor = Color.FromRgb(0, 255, 102);
    private static Color PreviousThemeSecondaryColor = Color.FromRgb(0, 169, 74);
    private static Color CardHoverBorderColor => ThemeAccentColor;
    private static readonly string ThemeSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "K-Launcher",
        "theme.txt");
    private sealed record AccentPalette(
        string Id,
        string Name,
        string IconFileName,
        Color Primary,
        Color Secondary,
        Color Background,
        Color Surface,
        Color Card,
        Color Control,
        Color Outline);
    private static readonly AccentPalette[] AccentPalettes =
    [
        new("emerald-cyan", "Emerald + Cyan", "klauncher-icon.png", Color.FromRgb(0, 255, 102), Color.FromRgb(0, 217, 255), Color.FromRgb(16, 16, 20), Color.FromRgb(17, 17, 22), Color.FromRgb(19, 19, 25), Color.FromRgb(20, 25, 26), Color.FromRgb(27, 27, 36)),
        new("violet-pink", "Violet + Pink", "theme-violet-pink.png", Color.FromRgb(177, 92, 255), Color.FromRgb(255, 85, 180), Color.FromRgb(16, 16, 20), Color.FromRgb(17, 17, 22), Color.FromRgb(19, 19, 25), Color.FromRgb(20, 25, 26), Color.FromRgb(27, 27, 36)),
        new("blue-cyan", "Blue + Cyan", "theme-blue-cyan.png", Color.FromRgb(64, 132, 255), Color.FromRgb(0, 225, 230), Color.FromRgb(16, 16, 20), Color.FromRgb(17, 17, 22), Color.FromRgb(19, 19, 25), Color.FromRgb(20, 25, 26), Color.FromRgb(27, 27, 36)),
        new("amber-orange", "Amber + Orange", "theme-amber-orange.png", Color.FromRgb(255, 190, 64), Color.FromRgb(255, 105, 64), Color.FromRgb(16, 16, 20), Color.FromRgb(17, 17, 22), Color.FromRgb(19, 19, 25), Color.FromRgb(20, 25, 26), Color.FromRgb(27, 27, 36)),
        new("midnight", "Midnight", "theme-midnight.png", Color.FromRgb(112, 166, 255), Color.FromRgb(55, 93, 160), Color.FromRgb(10, 15, 26), Color.FromRgb(14, 21, 36), Color.FromRgb(17, 26, 43), Color.FromRgb(22, 32, 52), Color.FromRgb(38, 54, 78)),
        new("aurora", "Aurora", "theme-aurora.png", Color.FromRgb(77, 255, 190), Color.FromRgb(142, 91, 255), Color.FromRgb(12, 17, 23), Color.FromRgb(16, 24, 32), Color.FromRgb(20, 30, 39), Color.FromRgb(25, 37, 44), Color.FromRgb(38, 56, 62)),
        new("minimal", "Minimal", "theme-minimal.png", Color.FromRgb(235, 235, 240), Color.FromRgb(125, 132, 144), Color.FromRgb(18, 19, 22), Color.FromRgb(24, 25, 29), Color.FromRgb(29, 30, 35), Color.FromRgb(37, 38, 43), Color.FromRgb(48, 49, 56))
    ];
    private static AccentPalette _activePalette = AccentPalettes[0];
    private static AccentPalette _previousPalette = AccentPalettes[0];

    private readonly MainViewModel _viewModel = new();
    private int _opacityAnimationVersion;
    private bool _isMinimizing;
    private bool _isClosing;
    private bool _isContextMenuOpen;
    private bool _startupFadeCompleted;

    private LocalizationService Localization =>
        (LocalizationService)Application.Current.Resources["Localization"];

    public MainWindow()
    {
        InitializeComponent();
        LoadAccentPalette();
        SetApplicationIcon();
        Opacity = 0;
        DataContext = _viewModel;
        Localization.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LocalizationService.LanguageCode))
                UpdateLocalizedAppNames();
        };
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Activated += MainWindow_Activated;
        Deactivated += MainWindow_Deactivated;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        HideSystemBorder(this);
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(450),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (_, _) =>
            {
                Opacity = 1;
                BeginAnimation(Window.OpacityProperty, null);
                _startupFadeCompleted = true;
            };
            BeginAnimation(Window.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }));
        await _viewModel.RefreshAsync();
        UpdateLocalizedAppNames();
        ApplyAccentPaletteToWindow();
        UpdateDesktopShortcutIcon(_activePalette.Id);
    }

    private static void LoadAccentPalette()
    {
        try
        {
            var paletteId = File.ReadAllText(ThemeSettingsPath).Trim();
            _activePalette = AccentPalettes.FirstOrDefault(palette =>
                palette.Id.Equals(paletteId, StringComparison.OrdinalIgnoreCase)) ?? AccentPalettes[0];
        }
        catch (FileNotFoundException)
        {
            _activePalette = AccentPalettes[0];
        }
        catch (DirectoryNotFoundException)
        {
            _activePalette = AccentPalettes[0];
        }

        ThemeAccentColor = _activePalette.Primary;
        ThemeSecondaryColor = _activePalette.Secondary;
        ThemeBackgroundColor = _activePalette.Background;
        ThemeSurfaceColor = _activePalette.Surface;
        ThemeCardColor = _activePalette.Card;
        ThemeControlColor = _activePalette.Control;
        ThemeOutlineColor = _activePalette.Outline;
        CardBackgroundColor = _activePalette.Card;
        CardBorderColor = _activePalette.Outline;
        PreviousThemeAccentColor = ThemeAccentColor;
        PreviousThemeSecondaryColor = ThemeSecondaryColor;
        _previousPalette = _activePalette;
        Application.Current.Resources["AccentHoverBrush"] = new SolidColorBrush(Darken(ThemeSecondaryColor, 0.32));
        Application.Current.Resources["NeonBrush"] = new SolidColorBrush(ThemeAccentColor);
        Application.Current.Resources["AccentGlowColor"] = ThemeAccentColor;
    }

    private void SelectAccentPalette(AccentPalette palette)
    {
        _previousPalette = _activePalette;
        PreviousThemeAccentColor = ThemeAccentColor;
        PreviousThemeSecondaryColor = ThemeSecondaryColor;
        _activePalette = palette;
        ThemeAccentColor = palette.Primary;
        ThemeSecondaryColor = palette.Secondary;
        ThemeBackgroundColor = palette.Background;
        ThemeSurfaceColor = palette.Surface;
        ThemeCardColor = palette.Card;
        ThemeControlColor = palette.Control;
        ThemeOutlineColor = palette.Outline;
        CardBackgroundColor = palette.Card;
        CardBorderColor = palette.Outline;
        Application.Current.Resources["AccentHoverBrush"] = new SolidColorBrush(Darken(palette.Secondary, 0.32));
        Application.Current.Resources["NeonBrush"] = new SolidColorBrush(palette.Primary);
        Application.Current.Resources["AccentGlowColor"] = palette.Primary;
        SetApplicationIcon();
        UpdateDesktopShortcutIcon(palette.Id);
        ApplyAccentPaletteToWindow();
        foreach (Window window in Application.Current.Windows)
        {
            if (!ReferenceEquals(window, this) && window.IsLoaded && window.Content is DependencyObject content)
                ApplyAccentPaletteToElement(content);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemeSettingsPath)!);
            File.WriteAllText(ThemeSettingsPath, palette.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Tema bu oturum için uygulandı ancak kaydedilemedi: {ex.Message}",
                "K-LAUNCHER",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ApplyAccentPaletteToWindow()
    {
        if (!IsLoaded)
            return;

        ApplyAccentPaletteToElement(MainSurface);
    }

    private static void ApplyAccentPaletteToElement(DependencyObject element)
    {
        if (element is FrameworkElement { Tag: "ThemeSettingsPanel" })
            return;

        switch (element)
        {
            case Border border:
                if (Equals(border.Tag, "AppCard"))
                {
                    ApplyAccentPaletteToAppCard(border);
                }
                else if (!Equals(border.Tag, "ThemePaletteSwatch"))
                {
                    border.Background = RecolorBrush(border.Background);
                    border.BorderBrush = RecolorBrush(border.BorderBrush);
                }
                break;
            case Control control:
                control.Background = RecolorBrush(control.Background);
                control.Foreground = RecolorBrush(control.Foreground);
                control.BorderBrush = RecolorBrush(control.BorderBrush);
                break;
            case TextBlock textBlock:
                textBlock.Foreground = RecolorBrush(textBlock.Foreground);
                break;
            case Panel panel:
                panel.Background = RecolorBrush(panel.Background);
                break;
            case System.Windows.Shapes.Shape shape:
                shape.Fill = RecolorBrush(shape.Fill);
                shape.Stroke = RecolorBrush(shape.Stroke);
                break;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            ApplyAccentPaletteToElement(VisualTreeHelper.GetChild(element, index));
    }

    private static void ApplyAccentPaletteToAppCard(Border border)
    {
        border.Background = new SolidColorBrush(CardBackgroundColor);
        border.BorderBrush = new SolidColorBrush(CardBorderColor);

        var transform = GetMutableCardTransform(border);
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        transform.ScaleX = border.IsMouseOver ? 1.035 : 1;
        transform.ScaleY = border.IsMouseOver ? 1.035 : 1;
    }

    private static Brush? RecolorBrush(Brush? brush)
    {
        if (brush is not SolidColorBrush solidBrush)
            return brush;

        var mapped = MapPaletteColor(solidBrush.Color);
        if (mapped == solidBrush.Color)
            return brush;

        return new SolidColorBrush(mapped);
    }

    private static Color MapPaletteColor(Color color)
    {
        if (color == ThemeAccentColor || color == Color.FromRgb(0, 255, 102) ||
            color == PreviousThemeAccentColor)
            return ThemeAccentColor;

        if (color == Darken(_previousPalette.Secondary, 0.18) ||
            color == Color.FromRgb(25, 29, 27))
            return Darken(ThemeSecondaryColor, 0.18);

        if (color == ThemeSecondaryColor || color == PreviousThemeSecondaryColor ||
            color == Color.FromRgb(0, 169, 74))
            return ThemeSecondaryColor;

        if (color == ThemeBackgroundColor || color == _previousPalette.Background ||
            color == Color.FromRgb(16, 16, 20))
            return ThemeBackgroundColor;
        if (color == ThemeSurfaceColor || color == _previousPalette.Surface ||
            color == Color.FromRgb(17, 17, 22))
            return ThemeSurfaceColor;
        if (color == ThemeCardColor || color == _previousPalette.Card ||
            color == Color.FromRgb(19, 19, 25))
            return ThemeCardColor;
        if (color == ThemeControlColor || color == _previousPalette.Control ||
            color == Color.FromRgb(20, 25, 26) || color == Color.FromRgb(24, 24, 31) ||
            color == Color.FromRgb(23, 23, 30))
            return ThemeControlColor;
        if (color == ThemeOutlineColor || color == _previousPalette.Outline ||
            color == Color.FromRgb(27, 27, 36) || color == Color.FromRgb(41, 41, 48) ||
            color == Color.FromRgb(48, 48, 57))
            return ThemeOutlineColor;
        if (color.A == 210 &&
            (color.R == 21 && color.G == 21 && color.B == 27 ||
             color.R == 20 && color.G == 20 && color.B == 26))
            return Color.FromArgb(210, ThemeSurfaceColor.R, ThemeSurfaceColor.G, ThemeSurfaceColor.B);

        var secondaryStrength = color switch
        {
            var value when value == Color.FromRgb(36, 90, 56) => 0.4,
            var value when value == Color.FromRgb(20, 56, 34) => 0.28,
            var value when value == Color.FromRgb(18, 49, 31) => 0.26,
            var value when value == Color.FromRgb(24, 60, 42) => 0.32,
            _ => 0
        };
        if (secondaryStrength == 0)
        {
            var previousStrength = new[] { 0.4, 0.28, 0.26, 0.32 }
                .FirstOrDefault(strength => color == Darken(_previousPalette.Secondary, strength));
            if (previousStrength > 0)
                secondaryStrength = previousStrength;
        }

        return secondaryStrength > 0
            ? Darken(ThemeSecondaryColor, secondaryStrength)
            : color;
    }

    private static Color Darken(Color color, double factor) =>
        Color.FromRgb(
            (byte)(color.R * factor),
            (byte)(color.G * factor),
            (byte)(color.B * factor));

    private void UpdateLocalizedAppNames()
    {
        foreach (var app in _viewModel.Apps)
            app.LocalizedName = Localization.GetAppDisplayName(app.Name);
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (_isClosing)
            return;

        var restoringFromMinimize = _isMinimizing;
        _isMinimizing = false;
        HideSystemBorder(this);
        if (!_startupFadeCompleted || _isContextMenuOpen)
            return;

        if (restoringFromMinimize)
        {
            MainSurface.BeginAnimation(UIElement.OpacityProperty, null);
            MainSurface.Opacity = 0;
        }

        FadeWindowOpacity(1, 320);
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (_isClosing || _isMinimizing || _isContextMenuOpen || OwnedWindows.Count > 0)
            return;

        FadeWindowOpacity(0, 240);
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WindowMessageHook);

        HideSystemBorder(this);
    }

    private void SetApplicationIcon()
    {
        var iconPath = AssetPath.Get(_activePalette.IconFileName);
        if (!File.Exists(iconPath))
            return;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(iconPath, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        Icon = image;
    }

    private void UpdateDesktopShortcutIcon(string themeId)
    {
        try
        {
            DesktopShortcutService.UpdateForTheme(themeId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Tema uygulandı ancak masaüstü kısayolunun simgesi güncellenemedi: {ex.Message}",
                "K-LAUNCHER",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static void HideSystemBorder(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            var noBorder = DwmColorNone;
            _ = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref noBorder, sizeof(uint));
        }
        catch (DllNotFoundException)
        {
            // DWM is available on supported Windows versions; keep startup safe on unusual hosts.
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows builds do not expose the border-color attribute.
        }
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmSysCommand
            && (wParam.ToInt64() & 0xFFF0) == ScMinimize
            && !_isMinimizing
            && WindowState != WindowState.Minimized)
        {
            handled = true;
            _isMinimizing = true;
            FadeWindowOpacity(0, 320, () =>
            {
                WindowState = WindowState.Minimized;
                RestoreSurfaceForTaskbarPreview();
            });
        }

        return IntPtr.Zero;
    }

    private void FadeWindowOpacity(double target, int durationMilliseconds, Action? completed = null)
    {
        var version = ++_opacityAnimationVersion;
        var animation = new DoubleAnimation
        {
            From = MainSurface.Opacity,
            To = target,
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };

        animation.Completed += (_, _) =>
        {
            if (version != _opacityAnimationVersion)
                return;

            MainSurface.Opacity = target;
            MainSurface.BeginAnimation(UIElement.OpacityProperty, null);
            completed?.Invoke();
        };

        MainSurface.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void RestoreSurfaceForTaskbarPreview()
    {
        if (_isClosing)
            return;

        MainSurface.BeginAnimation(UIElement.OpacityProperty, null);
        MainSurface.Opacity = 1;
        MainSurface.InvalidateVisual();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private async void AddApplication_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localization["AddAppDialogTitle"],
            Filter = Localization["AppFileFilter"],
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var addedCount = 0;
        foreach (var path in dialog.FileNames)
        {
            if (_viewModel.AddUserApplication(path))
                addedCount++;
        }

        if (addedCount > 0)
        {
            await _viewModel.RefreshAsync();
            UpdateLocalizedAppNames();
            return;
        }

        MessageBox.Show(
            Localization["DuplicateAppsMessage"],
            "K-LAUNCHER",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement settingsButton)
            return;

        var menu = new ContextMenu
        {
            Background = Brush("#D215151B"),
            Foreground = Brush("#F1F1F1"),
            BorderBrush = Brush("#FF00FF66"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            HasDropShadow = false,
            Placement = PlacementMode.Bottom,
            PlacementTarget = settingsButton,
            VerticalOffset = 8,
            StaysOpen = true
        };
        menu.Style = (Style)FindResource("LauncherContextMenuStyle");
        menu.Resources[typeof(MenuItem)] = (Style)FindResource("LauncherContextMenuItemStyle");

        Action? closeSettingsMenu = null;
        var pendingPanelSection = string.Empty;
        Point? languageMenuAnchorScreenPoint = null;
        string? pendingRestoreTarget = null;
        var restoreAllHiddenApps = false;

        var themesItem = new MenuItem
        {
            Header = Localization["SettingsThemes"],
            Icon = MakeGlyph("T")
        };
        themesItem.Click += (_, _) =>
        {
            pendingPanelSection = "Temalar";
            closeSettingsMenu?.Invoke();
        };
        menu.Items.Add(themesItem);

        var languagesItem = new MenuItem
        {
            Header = Localization["SettingsLanguages"],
            Icon = MakeGlyph("L")
        };

        languagesItem.Click += (_, _) =>
        {
            languageMenuAnchorScreenPoint = languagesItem.PointToScreen(
                new Point(languagesItem.ActualWidth, 0));
            closeSettingsMenu?.Invoke();
        };
        menu.Items.Add(languagesItem);

        var pluginsItem = new MenuItem
        {
            Header = Localization["SettingsPlugins"],
            Icon = MakeGlyph("+")
        };
        pluginsItem.Click += (_, _) =>
        {
            pendingPanelSection = "Eklentiler";
            closeSettingsMenu?.Invoke();
        };
        menu.Items.Add(pluginsItem);

        var hiddenApps = _viewModel.HiddenApps;
        if (hiddenApps.Count > 0)
        {
            var hiddenAppsItem = new MenuItem
            {
                Header = $"{Localization["HiddenApps"]}  [{hiddenApps.Count}]",
                Icon = MakeGlyph("H")
            };
            var restoreAllItem = new MenuItem
            {
                Header = Localization["RestoreAllApps"],
                Icon = MakeGlyph("+")
            };
            restoreAllItem.Click += (_, _) =>
            {
                restoreAllHiddenApps = true;
                closeSettingsMenu?.Invoke();
            };
            hiddenAppsItem.Items.Add(restoreAllItem);
            hiddenAppsItem.Items.Add(new Separator());

            foreach (var hiddenApp in hiddenApps)
            {
                var restoreItem = new MenuItem
                {
                    Header = $"{Localization["RestoreApp"]}: {hiddenApp.Name}",
                    Tag = hiddenApp.LaunchTarget
                };
                restoreItem.Click += (_, _) =>
                {
                    pendingRestoreTarget = hiddenApp.LaunchTarget;
                    closeSettingsMenu?.Invoke();
                };
                hiddenAppsItem.Items.Add(restoreItem);
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(hiddenAppsItem);
        }

        closeSettingsMenu = ConfigureAnimatedContextMenu(menu, afterClosed: () =>
        {
            if (languageMenuAnchorScreenPoint is Point languageAnchor)
            {
                languageMenuAnchorScreenPoint = null;
                ShowLanguageMenu(languageAnchor);
            }
            else if (!string.IsNullOrEmpty(pendingPanelSection))
            {
                if (pendingPanelSection == "Temalar")
                    ShowAccentThemePanel(settingsButton);
                else
                    ShowSettingsPlaceholder(pendingPanelSection, settingsButton);
            }
            else if (pendingRestoreTarget is not null)
            {
                RestoreHiddenApplication(pendingRestoreTarget);
            }
            else if (restoreAllHiddenApps)
            {
                RestoreAllHiddenApplications();
            }
        });
    }

    private async void RestoreHiddenApplication(string launchTarget)
    {
        try
        {
            await _viewModel.RestoreHiddenApplicationAsync(launchTarget);
            UpdateLocalizedAppNames();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "K-LAUNCHER",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void RestoreAllHiddenApplications()
    {
        try
        {
            await _viewModel.RestoreAllHiddenApplicationsAsync();
            UpdateLocalizedAppNames();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "K-LAUNCHER",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ShowAccentThemePanel(FrameworkElement settingsButton)
    {
        const double panelWidth = 430;
        const double panelHeight = 500;
        var workArea = SystemParameters.WorkArea;
        var anchor = settingsButton.TransformToAncestor(this)
            .Transform(new Point(settingsButton.ActualWidth, 0));
        var panel = new Window
        {
            Title = Localization["SettingsThemes"],
            Width = panelWidth,
            Height = panelHeight,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = Math.Clamp(Left + anchor.X + 8, workArea.Left, workArea.Right - panelWidth),
            Top = Math.Clamp(Top + anchor.Y - 20, workArea.Top, workArea.Bottom - panelHeight),
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Opacity = 0
        };
        var panelTransform = new TranslateTransform(-48, 0);
        var root = new Border
        {
            Tag = "ThemeSettingsPanel",
            Background = Brush("#101014"),
            BorderBrush = Brush("#00A94A"),
            BorderThickness = new Thickness(1),
            RenderTransform = panelTransform
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Border
        {
            Background = Brush("#111116"),
            BorderBrush = Brush("#12311F"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 0, 12, 0)
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(new TextBlock
        {
            Text = $"K-LAUNCHER  //  {Localization["SettingsThemes"].ToUpperInvariant()}",
            Foreground = Brush("#F1F1F1"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var closeButton = new Button
        {
            Content = "×",
            Width = 32,
            Height = 32,
            Foreground = Brush("#909098"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 23,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Cursor = Cursors.Hand
        };
        Grid.SetColumn(closeButton, 1);
        headerGrid.Children.Add(closeButton);
        header.Child = headerGrid;
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        var content = new StackPanel { Margin = new Thickness(18, 12, 18, 14) };
        var accentHeading = new TextBlock
        {
            Text = Localization.LanguageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                ? "VURGU RENKLERİ"
                : "ACCENT COLORS",
            Foreground = new SolidColorBrush(ThemeAccentColor),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, 0, 0, 8)
        };
        content.Children.Add(accentHeading);

        var paletteButtons = new List<Button>();
        foreach (var palette in AccentPalettes)
        {
            var paletteButton = new Button
            {
                Height = 48,
                Margin = new Thickness(0, 3, 0, 3),
                Padding = new Thickness(10, 4, 10, 4),
                Background = Brush("#14191A"),
                BorderBrush = new SolidColorBrush(palette.Id == _activePalette.Id ? ThemeAccentColor : ThemeOutlineColor),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand,
                Tag = palette.Id,
                Template = (ControlTemplate)FindResource("ThemePaletteButtonTemplate")
            };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var swatches = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            swatches.Children.Add(CreatePaletteSwatch(palette.Primary));
            swatches.Children.Add(CreatePaletteSwatch(palette.Secondary));
            row.Children.Add(swatches);
            var name = new TextBlock
            {
                Text = palette.Name,
                Foreground = Brush("#F1F1F1"),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13
            };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var selectedMark = new TextBlock
            {
                Text = palette.Id == _activePalette.Id ? "✓" : string.Empty,
                Foreground = Brush("#00FF66"),
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(selectedMark, 2);
            row.Children.Add(selectedMark);
            paletteButton.Content = row;
            paletteButton.Click += (_, _) =>
            {
                SelectAccentPalette(palette);
                foreach (var button in paletteButtons)
                {
                    var selected = Equals(button.Tag, palette.Id);
                    button.BorderBrush = new SolidColorBrush(selected ? ThemeAccentColor : ThemeOutlineColor);
                    if (button.Content is Grid buttonGrid &&
                        buttonGrid.Children.OfType<TextBlock>().LastOrDefault() is { } mark)
                    {
                        mark.Text = selected ? "✓" : string.Empty;
                        mark.Foreground = new SolidColorBrush(ThemeAccentColor);
                    }
                }
                root.BorderBrush = new SolidColorBrush(ThemeAccentColor);
                accentHeading.Foreground = new SolidColorBrush(ThemeAccentColor);
            };
            paletteButtons.Add(paletteButton);
            content.Children.Add(paletteButton);
        }

        content.Children.Add(new TextBlock
        {
            Text = Localization.LanguageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                ? Localization["SettingsFullThemes"].ToUpper(new CultureInfo("tr-TR"))
                : Localization["SettingsFullThemes"].ToUpperInvariant(),
            Foreground = Brush("#00FF66"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, 16, 0, 8)
        });
        content.Children.Add(new TextBlock
        {
            Text = Localization["SettingsComingSoon"],
            Foreground = Brush("#666670"),
            FontSize = 10,
            Margin = new Thickness(8, 0, 8, 6)
        });

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        root.Child = layout;
        panel.Content = root;

        var closing = false;
        void CloseWithAnimation()
        {
            if (closing)
                return;
            closing = true;
            closeButton.IsEnabled = false;
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)));
            panelTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -48, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
            var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(190) };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                panel.Close();
            };
            closeTimer.Start();
        }

        closeButton.Click += (_, _) => CloseWithAnimation();
        panel.Deactivated += (_, _) => CloseWithAnimation();
        panel.Loaded += (_, _) =>
        {
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            panelTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-48, 0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        };
        panel.Show();
    }

    private static Border CreatePaletteSwatch(Color color) =>
        new()
        {
            Tag = "ThemePaletteSwatch",
            Width = 15,
            Height = 22,
            Margin = new Thickness(0, 0, 4, 0),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(color)
        };

    private void ShowLanguageMenu(Point anchorScreenPoint)
    {
        var anchor = PointFromScreen(anchorScreenPoint);
        var workArea = SystemParameters.WorkArea;
        var languageWidth = 190d;
        var languageHeight = Math.Min(620, Math.Max(220, workArea.Bottom - (Top + anchor.Y) - 12));
        var menuWindow = new Window
        {
            Title = Localization["LanguageMenuTitle"],
            Width = languageWidth,
            Height = languageHeight,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = Math.Clamp(Left + anchor.X + 5, workArea.Left, workArea.Right - languageWidth),
            Top = Math.Clamp(Top + anchor.Y - 4, workArea.Top, workArea.Bottom - languageHeight),
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Opacity = 0
        };
        var root = new Border
        {
            Background = Brush("#D215151B"),
            BorderBrush = Brush("#FF00FF66"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            RenderTransform = new TranslateTransform(-42, 0)
        };
        var languageItems = new StackPanel();
        var heading = new TextBlock
        {
            Text = Localization["LanguageMenuTitle"],
            Foreground = Brush("#00FF66"),
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(10, 8, 14, 8)
        };
        languageItems.Children.Add(heading);
        languageItems.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(6, 0, 6, 4),
            Background = Brush("#44444C")
        });

        var isClosing = false;
        void CloseLanguageMenu()
        {
            if (isClosing)
                return;

            isClosing = true;
            foreach (var child in languageItems.Children.OfType<FrameworkElement>())
            {
                child.BeginAnimation(OpacityProperty, new DoubleAnimation(child.Opacity, 0, TimeSpan.FromMilliseconds(100))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });
                if (child.RenderTransform is TranslateTransform childTransform)
                    childTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -22, TimeSpan.FromMilliseconds(120))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    });
            }
            var close = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            close.Completed += (_, _) => menuWindow.Close();
            menuWindow.BeginAnimation(OpacityProperty, close);
            if (root.RenderTransform is TranslateTransform rootTransform)
                rootTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -42, TimeSpan.FromMilliseconds(170))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });
        }

        foreach (var language in LocalizationService.Languages)
        {
            var item = new Button
            {
                Content = language.DisplayName,
                Tag = language.Code,
                Height = 32,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 0, 8, 0),
                Foreground = Brush("#F1F1F1"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Cursor = Cursors.Hand,
                Opacity = 0,
                RenderTransform = new TranslateTransform(-24, 0)
            };
            if (Localization.LanguageCode.Equals(language.Code, StringComparison.OrdinalIgnoreCase))
                item.Content = $"✓  {language.DisplayName}";

            item.Click += (_, _) =>
            {
                Localization.SetLanguage(language.Code);
                CloseLanguageMenu();
            };
            languageItems.Children.Add(item);
        }

        var scroll = new ScrollViewer
        {
            Content = languageItems,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = languageHeight - 10
        };
        root.Child = scroll;
        menuWindow.Content = root;
        menuWindow.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                CloseLanguageMenu();
            }
        };
        menuWindow.Deactivated += (_, _) => CloseLanguageMenu();
        menuWindow.Loaded += (_, _) =>
        {
            menuWindow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            if (root.RenderTransform is TranslateTransform rootTransform)
                rootTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-42, 0, TimeSpan.FromMilliseconds(270))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            var index = 0;
            foreach (var child in languageItems.Children.OfType<FrameworkElement>().Skip(2))
            {
                var delay = TimeSpan.FromMilliseconds(index++ * 18);
                child.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                if (child.RenderTransform is TranslateTransform childTransform)
                    childTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-24, 0, TimeSpan.FromMilliseconds(210))
                    {
                        BeginTime = delay,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
        };
        menuWindow.Show();
    }

    private void ShowSettingsPlaceholder(string sectionTitle, FrameworkElement settingsButton)
    {
        const double panelWidth = 480;
        const double panelHeight = 340;

        var workArea = SystemParameters.WorkArea;
        var anchorWindowPoint = settingsButton.TransformToAncestor(this)
            .Transform(new Point(settingsButton.ActualWidth, 0));
        var left = Math.Clamp(Left + anchorWindowPoint.X + 8, workArea.Left, workArea.Right - panelWidth);
        var top = Math.Clamp(Top + anchorWindowPoint.Y - 20, workArea.Top, workArea.Bottom - panelHeight);
        var panel = new Window
        {
            Title = $"K-LAUNCHER // {sectionTitle.ToUpperInvariant()}",
            Width = panelWidth,
            Height = panelHeight,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = left,
            Top = top,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Opacity = 0
        };

        var root = new Border
        {
            Background = Brush("#101014"),
            BorderBrush = Brush("#00A94A"),
            BorderThickness = new Thickness(1),
            RenderTransform = new TranslateTransform(-56, 0)
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Border
        {
            Background = Brush("#111116"),
            BorderBrush = Brush("#12311F"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 0, 12, 0)
        };
        var headerLayout = new Grid();
        headerLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerLayout.Children.Add(new TextBlock
        {
            Text = $"K-LAUNCHER  //  {sectionTitle.ToUpperInvariant()}",
            Foreground = Brush("#F1F1F1"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var soon = new TextBlock
        {
            Text = Localization["SettingsComingSoon"],
            Foreground = Brush("#00FF66"),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var closeButton = new Button
        {
            Content = "×",
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Foreground = Brush("#909098"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 23,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Cursor = Cursors.Hand
        };
        var panelIsClosing = false;
        void ClosePanelWithAnimation()
        {
            if (panelIsClosing)
                return;

            panelIsClosing = true;
            closeButton.IsEnabled = false;

            var contentTransform = new TranslateTransform();
            soon.RenderTransform = contentTransform;
            soon.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
            contentTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -28, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });

            var exitAnimation = new DoubleAnimation(0, -56, TimeSpan.FromMilliseconds(180))
            {
                BeginTime = TimeSpan.FromMilliseconds(70),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            exitAnimation.Completed += (_, _) => panel.Close();
            if (root.RenderTransform is TranslateTransform rootTransform)
                rootTransform.BeginAnimation(TranslateTransform.XProperty, exitAnimation);

            panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
        }

        closeButton.Click += (_, _) => ClosePanelWithAnimation();
        Grid.SetColumn(closeButton, 1);
        headerLayout.Children.Add(closeButton);
        header.Child = headerLayout;
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        Grid.SetRow(soon, 1);
        layout.Children.Add(soon);
        root.Child = layout;
        panel.Content = root;

        panel.Loaded += (_, _) =>
        {
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            if (root.RenderTransform is TranslateTransform transform)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-56, 0, TimeSpan.FromMilliseconds(340))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }
        };

        panel.Show();
    }

    private void SupportedApplications_Click(object sender, RoutedEventArgs e)
    {
        var details = new Window
        {
            Title = Localization["InfoTitle"],
            Width = 600,
            Height = 580,
            MinWidth = 520,
            MinHeight = 460,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Opacity = 0,
            Background = Brushes.Transparent,
            Foreground = Brush("#F1F1F1"),
            Icon = Icon
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleBar = new Border
        {
            Background = Brush("#111116"),
            BorderBrush = Brush("#12311F"),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var titleBarGrid = new Grid { Margin = new Thickness(16, 0, 10, 0) };
        titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = Localization["InfoTitle"],
            Foreground = Brush("#F1F1F1"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleBarGrid.Children.Add(title);
        var closeWindowButton = new Button
        {
            Content = "×",
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            Foreground = Brush("#909098"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 23,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(closeWindowButton, 1);
        titleBarGrid.Children.Add(closeWindowButton);
        titleBar.Child = titleBarGrid;
        titleBar.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ChangedButton == MouseButton.Left)
                details.DragMove();
        };
        Grid.SetRow(titleBar, 0);
        layout.Children.Add(titleBar);

        var content = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        var detectedHeading = new TextBlock
        {
            Text = Localization["AutoDetected"],
            Foreground = Brush("#00FF66"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        content.Children.Add(detectedHeading);

        var detailsText = new TextBlock
        {
            Text = Localization.GetSupportedApplicationsText(),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#D5D5DC"),
            FontSize = 13,
            LineHeight = 20
        };
        var scroll = new ScrollViewer
        {
            Content = detailsText,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        content.Children.Add(scroll);

        var closeButton = new Button
        {
            Content = Localization["Ok"],
            Width = 100,
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 18, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = Brush("#00FF66"),
            Background = Brush("#14191A"),
            BorderBrush = Brush("#245A38"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        content.Children.Add(closeButton);

        void UpdateInformationText()
        {
            details.Title = Localization["InfoTitle"];
            title.Text = Localization["InfoTitle"];
            detectedHeading.Text = Localization["AutoDetected"];
            detailsText.Text = Localization.GetSupportedApplicationsText();
            closeButton.Content = Localization["Ok"];
        }

        PropertyChangedEventHandler languageChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(LocalizationService.LanguageCode))
                UpdateInformationText();
        };
        Localization.PropertyChanged += languageChanged;
        details.Closed += (_, _) => Localization.PropertyChanged -= languageChanged;

        var contentBorder = new Border
        {
            Background = Brush("#111116"),
            Child = content
        };
        Grid.SetRow(contentBorder, 1);
        layout.Children.Add(contentBorder);

        details.Content = new Border
        {
            BorderBrush = Brush("#00FF66"),
            BorderThickness = new Thickness(1),
            Background = Brush("#111116"),
            Child = layout
        };

        var animationVersion = 0;
        var closeStarted = false;
        var allowClose = false;
        void AnimateDetailsOpacity(double target, int durationMilliseconds, Action? completed = null)
        {
            var version = ++animationVersion;
            var animation = new DoubleAnimation
            {
                From = details.Opacity,
                To = target,
                Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            animation.Completed += (_, _) =>
            {
                if (version != animationVersion)
                    return;

                details.Opacity = target;
                details.BeginAnimation(OpacityProperty, null);
                completed?.Invoke();
            };
            details.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        void CloseDetails()
        {
            if (closeStarted)
                return;

            closeStarted = true;
            AnimateDetailsOpacity(0, 180, () =>
            {
                allowClose = true;
                details.Close();
            });
        }

        closeButton.Click += (_, _) => CloseDetails();
        closeWindowButton.Click += (_, _) => CloseDetails();
        details.Closing += (_, args) =>
        {
            if (allowClose)
                return;

            args.Cancel = true;
            CloseDetails();
        };
        details.SourceInitialized += (_, _) => HideSystemBorder(details);
        details.Loaded += (_, _) => AnimateDetailsOpacity(1, 320);
        details.Activated += (_, _) => AnimateDetailsOpacity(1, 320);
        details.Deactivated += (_, _) => AnimateDetailsOpacity(0, 320);
        details.ShowDialog();
    }

    private void Minimize_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _isMinimizing = true;
        FadeWindowOpacity(0, 320, () =>
        {
            WindowState = WindowState.Minimized;
            RestoreSurfaceForTaskbarPreview();
        });
    }

    private void Close_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _isClosing = true;
        FadeWindowOpacity(0, 180, Close);
    }

    private void Card_MouseEnter(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border)
            return;

        AnimateCardScale(border, 1.035, 140);
    }

    private void AppCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border)
            return;

        border.Background = new SolidColorBrush(CardBackgroundColor);
        border.BorderBrush = new SolidColorBrush(CardBorderColor);
    }

    private void Card_MouseLeave(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border)
            return;

        AnimateCardScale(border, 1, 120);
    }

    private void Card_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isContextMenuOpen)
        {
            e.Handled = true;
            return;
        }

        if (sender is Border border)
            AnimateCardPress(border);
    }

    private static void AnimateCardScale(Border border, double scale, int durationMilliseconds)
    {
        var transform = GetMutableCardTransform(border);

        var animation = new DoubleAnimation
        {
            To = scale,
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateCardPress(Border border)
    {
        var transform = GetMutableCardTransform(border);
        var finalScale = border.IsMouseOver ? 1.035 : 1;
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(190)
        };

        animation.KeyFrames.Add(new EasingDoubleKeyFrame(transform.ScaleX)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero)
        });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0.965)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(55)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1.055)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(finalScale)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(190)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation, HandoffBehavior.SnapshotAndReplace);

        var brush = GetMutableCardBrush(border, isBackground: false);
        var endColor = border.IsMouseOver ? CardHoverBorderColor : CardBorderColor;
        var pulse = new ColorAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(190)
        };
        pulse.KeyFrames.Add(new EasingColorKeyFrame(brush.Color)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero)
        });
        pulse.KeyFrames.Add(new EasingColorKeyFrame(CardHoverBorderColor)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(65))
        });
        pulse.KeyFrames.Add(new EasingColorKeyFrame(endColor)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(190)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        brush.BeginAnimation(SolidColorBrush.ColorProperty, pulse, HandoffBehavior.SnapshotAndReplace);
    }

    private static ScaleTransform GetMutableCardTransform(Border border)
    {
        if (border.RenderTransform is ScaleTransform transform && !transform.IsFrozen)
            return transform;

        transform = border.RenderTransform is ScaleTransform frozenTransform
            ? new ScaleTransform(frozenTransform.ScaleX, frozenTransform.ScaleY)
            : new ScaleTransform(1, 1);

        border.RenderTransform = transform;
        return transform;
    }

    private static SolidColorBrush GetMutableCardBrush(Border border, bool isBackground)
    {
        var brush = (isBackground ? border.Background : border.BorderBrush) as SolidColorBrush;

        if (brush is null)
        {
            brush = new SolidColorBrush(isBackground ? CardBackgroundColor : CardBorderColor);
        }
        else if (brush.IsFrozen)
        {
            brush = brush.CloneCurrentValue();
        }

        if (isBackground)
            border.Background = brush;
        else
            border.BorderBrush = brush;

        return brush;
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border)
            return;

        if (border.DataContext is not AppModel app)
            return;

        Launch(app.LaunchTarget, app.LaunchArguments, app.Name);
    }

    private void Card_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (_isContextMenuOpen)
            return;

        if (sender is not Border border)
            return;

        if (border.DataContext is not AppModel app)
            return;

        ShowContextMenu(app, border);
    }

    private void AnimateCardPressReverse(Border border)
    {
        var transform = GetMutableCardTransform(border);
        var animation = new DoubleAnimation
        {
            From = Math.Max(transform.ScaleX, 0.001),
            To = 1,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation.Clone(), HandoffBehavior.SnapshotAndReplace);

        var brush = GetMutableCardBrush(border, isBackground: false);
        var endColor = border.IsMouseOver ? CardHoverBorderColor : CardBorderColor;
        var reverse = new ColorAnimation
        {
            From = brush.Color,
            To = endColor,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, reverse, HandoffBehavior.SnapshotAndReplace);
    }

    private Action ConfigureAnimatedContextMenu(
        ContextMenu menu,
        Border? pressedCard = null,
        Action? afterClosed = null,
        bool useDirectSlideAnimation = false)
    {
        menu.StaysOpen = true;
        var closeAnimationStarted = false;
        var openAnimationStarted = false;
        var animationPrepared = false;
        var allowClose = false;

        void PrepareAnimation()
        {
            if (animationPrepared)
                return;

            animationPrepared = true;
            var animationTarget = GetContextMenuAnimationTarget(menu);
            var transform = new TransformGroup();
            transform.Children.Add(new ScaleTransform());
            transform.Children.Add(new RotateTransform());
            transform.Children.Add(new TranslateTransform());
            animationTarget.RenderTransformOrigin = new Point(0.5, 0.5);
            animationTarget.RenderTransform = transform;
            animationTarget.Opacity = 0;

            menu.ApplyTemplate();
            ApplyAccentPaletteToElement(menu);
            if (menu.Template?.FindName("MenuSurface", menu) is UIElement surface)
                surface.Opacity = 0;
            if (menu.Template?.FindName("MenuOutline", menu) is UIElement outline)
                outline.Opacity = 0;
            foreach (var item in menu.Items.OfType<UIElement>())
                item.Opacity = 0;
        }

        void BeginCloseAnimation()
        {
            if (closeAnimationStarted || !menu.IsOpen)
                return;

            closeAnimationStarted = true;
            InputManager.Current.PreProcessInput -= OnPreProcessInput;
            PrepareAnimation();
            menu.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (!openAnimationStarted)
                {
                    openAnimationStarted = true;
                    if (useDirectSlideAnimation)
                        AnimateContextMenuWithDirectSlide(menu);
                    else
                        AnimateContextMenu(menu);
                }

                AnimateContextMenuClose(menu, () =>
                {
                    allowClose = true;
                    _isContextMenuOpen = false;
                    menu.IsOpen = false;
                });
            }));
        }

        void OnPreProcessInput(object? _, PreProcessInputEventArgs inputEvent)
        {
            if (closeAnimationStarted)
                return;

            if (inputEvent.StagingItem.Input is KeyEventArgs keyEvent && keyEvent.Key == Key.Escape)
            {
                keyEvent.Handled = true;
                BeginCloseAnimation();
                return;
            }

            if (inputEvent.StagingItem.Input is not MouseButtonEventArgs mouseEvent ||
                mouseEvent.ButtonState != MouseButtonState.Pressed)
                return;

            var source = mouseEvent.OriginalSource as DependencyObject;
            var clickSource = source is null ? null : PresentationSource.FromDependencyObject(source);
            var menuSource = PresentationSource.FromVisual(menu);
            if (IsCursorWithinContextMenu(menu) ||
                (source is not null &&
                 (ReferenceEquals(clickSource, menuSource) ||
                  ItemsControl.ContainerFromElement(menu, source) is not null)))
                return;

            var launcherSource = PresentationSource.FromVisual(this);
            if (clickSource is not null && ReferenceEquals(clickSource, launcherSource))
                mouseEvent.Handled = true;

            BeginCloseAnimation();
        }

        menu.Opened += (_, _) =>
        {
            if (closeAnimationStarted || openAnimationStarted)
                return;

            PrepareAnimation();
            menu.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    if (closeAnimationStarted || openAnimationStarted)
                        return;

                    openAnimationStarted = true;
                    if (useDirectSlideAnimation)
                        AnimateContextMenuWithDirectSlide(menu);
                    else
                        AnimateContextMenu(menu);
                }));
        };
        menu.Closed += (_, _) =>
        {
            InputManager.Current.PreProcessInput -= OnPreProcessInput;
            if (!allowClose)
                _isContextMenuOpen = false;

            if (pressedCard is not null)
                AnimateCardPressReverse(pressedCard);

            afterClosed?.Invoke();
        };

        InputManager.Current.PreProcessInput += OnPreProcessInput;
        _isContextMenuOpen = true;
        menu.IsOpen = true;
        return BeginCloseAnimation;
    }

    private static bool IsCursorWithinContextMenu(ContextMenu menu)
    {
        if (!GetCursorPos(out var cursor))
            return false;

        var cursorPosition = new Point(cursor.X, cursor.Y);
        if (IsScreenPointWithinElement(menu, cursorPosition))
            return true;

        return menu.Items.OfType<MenuItem>()
            .Any(item => IsCursorWithinOpenSubmenu(item, cursorPosition));
    }

    private static bool IsCursorWithinOpenSubmenu(MenuItem item, Point cursorPosition)
    {
        if (!item.IsSubmenuOpen)
            return false;

        item.ApplyTemplate();
        if (item.Template?.FindName("PART_Popup", item) is Popup popup &&
            popup.IsOpen &&
            popup.Child is FrameworkElement popupChild &&
            IsScreenPointWithinElement(popupChild, cursorPosition))
            return true;

        return item.Items.OfType<MenuItem>()
            .Any(child => IsCursorWithinOpenSubmenu(child, cursorPosition));
    }

    private static bool IsScreenPointWithinElement(FrameworkElement element, Point screenPoint)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;

        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return screenPoint.X >= Math.Min(topLeft.X, bottomRight.X)
            && screenPoint.X <= Math.Max(topLeft.X, bottomRight.X)
            && screenPoint.Y >= Math.Min(topLeft.Y, bottomRight.Y)
            && screenPoint.Y <= Math.Max(topLeft.Y, bottomRight.Y);
    }

    private void ShowContextMenu(AppModel app, FrameworkElement target)
    {
        if (_isContextMenuOpen)
            return;

        var menu = new ContextMenu
        {
            Background = Brush("#D215151B"),
            Foreground = Brush("#F1F1F1"),
            BorderBrush = Brush("#FF00FF66"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            HasDropShadow = false,
            PlacementTarget = target,
            StaysOpen = false
        };

        menu.Style = (Style)FindResource("LauncherContextMenuStyle");
        menu.Resources[typeof(MenuItem)] = (Style)FindResource("LauncherContextMenuItemStyle");

        menu.Resources[SystemColors.MenuBrushKey] = Brush("#0015151B");
        menu.Resources[SystemColors.MenuTextBrushKey] = Brush("#F1F1F1");
        menu.Resources[SystemColors.HighlightBrushKey] = Brush("#183C2A");
        menu.Resources[SystemColors.HighlightTextBrushKey] = Brush("#00FF66");
        menu.Resources[SystemColors.ControlBrushKey] = Brush("#0015151B");

        var header = new MenuItem
        {
            Header = $"{app.Name.ToUpperInvariant()}  //  {Localization["QuickMenu"]}",
            IsEnabled = false,
            Foreground = Brush("#74747D"),
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(10, 8, 14, 8)
        };
        menu.Items.Add(header);
        menu.Items.Add(new Separator());

        menu.Items.Add(MakeItem(
            Localization["Launch"],
            "?",
            "#00FF66",
            (_, _) => Launch(app.LaunchTarget, app.LaunchArguments, app.Name)));

        if (File.Exists(app.LaunchTarget) &&
            Path.GetExtension(app.LaunchTarget).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            menu.Items.Add(MakeItem(
                Localization["RunAsAdministrator"],
                "A",
                "#FFBE40",
                (_, _) => Launch(app.LaunchTarget, app.LaunchArguments, app.Name, runAsAdministrator: true)));
        }

        menu.Items.Add(MakeItem(
            Localization["HideApp"],
            "H",
            "#B7BEC3",
            (_, _) => _viewModel.HideApplication(app)));

        if (app.IsUserAdded)
        {
            menu.Items.Add(MakeItem(
                Localization["RemoveApp"],
                "-",
                "#FF6B75",
                (_, _) => _viewModel.RemoveUserApplication(app)));
        }

        if (app.Games.Count > 0)
        {
            menu.Items.Add(new Separator());

            var gamesHeader = new MenuItem
            {
                Header = $"{Localization["Games"]}  [{app.Games.Count}]",
                IsEnabled = false,
                Foreground = Brush("#00FF66"),
                FontWeight = FontWeights.Bold
            };
            menu.Items.Add(gamesHeader);

            foreach (var game in app.Games)
            {
                var item = new MenuItem
                {
                    Header = game.Name,
                    Tag = game,
                    Icon = MakeExeIcon(game.IconPath, game.ExePath)
                };

                item.Click += Game_Click;
                menu.Items.Add(item);
            }
        }

        if (app.QuickLinks.Count > 0)
        {
            menu.Items.Add(new Separator());

            var linksHeader = new MenuItem
            {
                Header = $"{Localization["QuickLinks"]}  [{app.QuickLinks.Count}]",
                IsEnabled = false,
                Foreground = Brush("#00FF66"),
                FontWeight = FontWeights.Bold
            };
            menu.Items.Add(linksHeader);

            foreach (var link in app.QuickLinks)
            {
                var item = new MenuItem
                {
                    Header = link.Name,
                    Tag = link,
                    Icon = MakeGlyph("↗")
                };

                item.Click += (_, _) =>
                {
                    if (IsBrowserApplication(app.Name) && File.Exists(app.ExePath))
                        Launch(app.ExePath, link.Url, app.Name);
                    else
                        Launch(link.Url, string.Empty);
                };
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeItem(
            Localization["AddQuickLink"],
            "+",
            "#00FF66",
            (_, _) => AddQuickLink(app)));

        if (app.QuickLinks.Count > 0)
        {
            menu.Items.Add(MakeItem(
                Localization["RemoveQuickLink"],
                "-",
                "#FF6B75",
                (_, _) => RemoveQuickLinks(app)));
        }

        if (!string.IsNullOrWhiteSpace(app.ExePath) &&
            Path.GetExtension(app.ExePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(app.ExePath))
        {
            menu.Items.Add(MakeItem(
                Localization["OpenInstallFolder"],
                "F",
                "#B7BEC3",
                (_, _) => OpenInstallFolder(app.ExePath)));
        }

        menu.Items.Add(MakeItem(
            Localization["Rescan"],
            "R",
            "#B7BEC3",
            async (_, _) =>
            {
                await _viewModel.RefreshAsync();
                UpdateLocalizedAppNames();
            }));

        ConfigureAnimatedContextMenu(menu, target as Border);
    }

    private static void AnimateContextMenuWithDirectSlide(ContextMenu menu)
    {
        var animationTarget = GetContextMenuAnimationTarget(menu);
        AnimateContextMenuActions(menu);

        var translate = new TranslateTransform(-48, 0);
        animationTarget.RenderTransformOrigin = new Point(0, 0);
        animationTarget.RenderTransform = translate;
        animationTarget.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        }, HandoffBehavior.SnapshotAndReplace);
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-48, 0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        }, HandoffBehavior.SnapshotAndReplace);

        menu.ApplyTemplate();
        AnimateMenuFrameElement(menu.Template?.FindName("MenuSurface", menu) as UIElement, TimeSpan.Zero);
        AnimateMenuFrameElement(menu.Template?.FindName("MenuOutline", menu) as UIElement, TimeSpan.Zero);
    }

    private static void AnimateContextMenu(ContextMenu menu)
    {
        var animationTarget = GetContextMenuAnimationTarget(menu);
        if (animationTarget.RenderTransform is not TransformGroup transform)
            return;

        var scale = transform.Children.OfType<ScaleTransform>().FirstOrDefault();
        var rotate = transform.Children.OfType<RotateTransform>().FirstOrDefault();
        var translate = transform.Children.OfType<TranslateTransform>().FirstOrDefault();
        if (scale is null || rotate is null || translate is null)
            return;

        var startY = 30d;
        try
        {
            var menuTop = menu.PointToScreen(new Point(0, 0)).Y;
            if (GetCursorPos(out var cursor) && menuTop < cursor.Y)
                startY = -30;
        }
        catch
        {
        }

        menu.Tag = startY;
        var startAngle = startY < 0 ? -12d : 12d;
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            From = -36,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(330),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        }, HandoffBehavior.SnapshotAndReplace);

        var yAnimation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(330),
            FillBehavior = FillBehavior.Stop
        };
        yAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(startY)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero)
        });
        yAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(215)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        yAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(startY < 0 ? 2 : -2)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        yAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        translate.BeginAnimation(TranslateTransform.YProperty, yAnimation, HandoffBehavior.SnapshotAndReplace);

        var rotationAnimation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(330),
            FillBehavior = FillBehavior.Stop
        };
        rotationAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(startAngle)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero)
        });
        rotationAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(215)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        rotationAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(startAngle < 0 ? 1 : -1)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        rotationAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        rotate.BeginAnimation(RotateTransform.AngleProperty, rotationAnimation, HandoffBehavior.SnapshotAndReplace);

        var scaleAnimation = new DoubleAnimation
        {
            From = 0.93,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(330),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation.Clone(), HandoffBehavior.SnapshotAndReplace);

        AnimateContextMenuActions(menu);

        animationTarget.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        }, HandoffBehavior.SnapshotAndReplace);

        menu.ApplyTemplate();
        var surface = menu.Template?.FindName("MenuSurface", menu) as UIElement;
        var outline = menu.Template?.FindName("MenuOutline", menu) as UIElement;
        AnimateMenuFrameElement(surface, TimeSpan.Zero);
        AnimateMenuFrameElement(outline, TimeSpan.Zero);
    }

    private static FrameworkElement GetContextMenuAnimationTarget(ContextMenu menu)
    {
        menu.ApplyTemplate();
        return menu.Template?.FindName("MenuRoot", menu) as FrameworkElement ?? menu;
    }

    private void AnimateContextMenuClose(ContextMenu menu, Action completed)
    {
        var animationTarget = GetContextMenuAnimationTarget(menu);
        var items = menu.Items.OfType<UIElement>().Reverse().ToArray();
        animationTarget.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(animationTarget);
        var screenOrigin = animationTarget.PointToScreen(new Point(0, 0));
        var windowOrigin = PointFromScreen(screenOrigin);
        var menuBitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(animationTarget.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(animationTarget.ActualHeight * dpi.DpiScaleY)),
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        menuBitmap.Render(animationTarget);
        menuBitmap.Freeze();
        var rootTransform = new TranslateTransform();
        var surface = new Border
        {
            Width = animationTarget.ActualWidth,
            Height = animationTarget.ActualHeight,
            Background = menu.Background,
            BorderBrush = menu.BorderBrush,
            BorderThickness = menu.BorderThickness,
            IsHitTestVisible = false
        };
        var itemCanvas = new Canvas
        {
            Width = animationTarget.ActualWidth,
            Height = animationTarget.ActualHeight,
            IsHitTestVisible = false,
            ClipToBounds = true
        };
        var visualRoot = new Grid
        {
            Width = animationTarget.ActualWidth,
            Height = animationTarget.ActualHeight,
            RenderTransform = rootTransform,
            IsHitTestVisible = false
        };
        visualRoot.Children.Add(surface);
        visualRoot.Children.Add(itemCanvas);
        var clipGeometry = new RectangleGeometry(new Rect(0, 0, visualRoot.Width, visualRoot.Height));
        visualRoot.Clip = clipGeometry;

        var itemImages = new List<Image>(items.Length);
        foreach (var item in items)
        {
            var bounds = item.TransformToAncestor(animationTarget)
                .TransformBounds(new Rect(item.RenderSize));
            var left = Math.Clamp((int)Math.Floor(bounds.X * dpi.DpiScaleX), 0, menuBitmap.PixelWidth - 1);
            var top = Math.Clamp((int)Math.Floor(bounds.Y * dpi.DpiScaleY), 0, menuBitmap.PixelHeight - 1);
            var right = Math.Clamp((int)Math.Ceiling(bounds.Right * dpi.DpiScaleX), left + 1, menuBitmap.PixelWidth);
            var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom * dpi.DpiScaleY), top + 1, menuBitmap.PixelHeight);
            var bitmap = new CroppedBitmap(
                menuBitmap,
                new Int32Rect(left, top, right - left, bottom - top));
            bitmap.Freeze();

            var itemImage = new Image
            {
                Source = bitmap,
                Width = bounds.Width,
                Height = bounds.Height,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
                RenderTransform = new TranslateTransform()
            };
            Canvas.SetLeft(itemImage, bounds.X);
            Canvas.SetTop(itemImage, bounds.Y);
            itemCanvas.Children.Add(itemImage);
            itemImages.Add(itemImage);
        }

        var exitWindow = new Window
        {
            Owner = this,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Left = Left + windowOrigin.X,
            Top = Top + windowOrigin.Y,
            Width = visualRoot.Width,
            Height = visualRoot.Height,
            Content = visualRoot
        };

        menu.IsOpen = false;
        exitWindow.Show();
        var lastItemDelay = TimeSpan.FromMilliseconds(20 + Math.Max(0, itemImages.Count - 1) * 24);
        var boxCloseDuration = lastItemDelay + TimeSpan.FromMilliseconds(500);
        clipGeometry.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation
        {
            From = new Rect(0, 0, visualRoot.Width, visualRoot.Height),
            To = new Rect(0, 0, visualRoot.Width, 0),
            Duration = boxCloseDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        }, HandoffBehavior.SnapshotAndReplace);

        void BeginMenuExit()
        {
            var pause = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            pause.Tick += (_, _) =>
            {
                pause.Stop();
                var translate = (TranslateTransform)visualRoot.RenderTransform;
                var exit = new DoubleAnimation
                {
                    From = 0,
                    To = -36,
                    Duration = TimeSpan.FromMilliseconds(330),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                exit.Completed += (_, _) =>
                {
                    exitWindow.Close();
                    completed();
                };
                visualRoot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                }, HandoffBehavior.SnapshotAndReplace);
                translate.BeginAnimation(TranslateTransform.XProperty, exit, HandoffBehavior.SnapshotAndReplace);
            };
            pause.Start();
        }

        for (var index = 0; index < itemImages.Count; index++)
        {
            var delay = TimeSpan.FromMilliseconds(20 + index * 24);
            var itemImage = itemImages[index];
            var slide = new DoubleAnimation
            {
                From = 0,
                To = -42,
                Duration = TimeSpan.FromMilliseconds(330),
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            slide.Completed += (_, _) =>
            {
                var fade = new DoubleAnimation
                {
                    From = itemImage.Opacity,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(170),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                itemImage.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
            };
            ((TranslateTransform)itemImage.RenderTransform).BeginAnimation(
                TranslateTransform.XProperty,
                slide,
                HandoffBehavior.SnapshotAndReplace);
        }

        if (itemImages.Count == 0)
        {
            BeginMenuExit();
            return;
        }

        var finishItems = new DispatcherTimer
        {
            Interval = boxCloseDuration
        };
        finishItems.Tick += (_, _) =>
        {
            finishItems.Stop();
            BeginMenuExit();
        };
        finishItems.Start();
    }

    private static void AnimateMenuFrameClose(UIElement? element)
    {
        if (element is null)
            return;

        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = element.Opacity,
            To = 0,
            BeginTime = TimeSpan.FromMilliseconds(60),
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateContextMenuActions(ContextMenu menu)
    {
        var items = menu.Items.OfType<UIElement>().ToArray();
        var animationTarget = GetContextMenuAnimationTarget(menu);
        var bounds = new Rect(0, 0, animationTarget.ActualWidth, animationTarget.ActualHeight);
        var clip = new RectangleGeometry(new Rect(0, 0, bounds.Width, 0));
        animationTarget.Clip = clip;
        var index = 0;
        foreach (var item in items)
        {
            var delay = TimeSpan.FromMilliseconds(20 + index * 24);
            var translate = new TranslateTransform();
            var scale = new ScaleTransform();
            var transform = new TransformGroup();
            transform.Children.Add(scale);
            transform.Children.Add(translate);
            item.RenderTransformOrigin = new Point(0.5, 0.5);
            item.RenderTransform = transform;
            item.Opacity = 0;

            var slide = new DoubleAnimation
            {
                From = -42,
                To = 0,
                BeginTime = delay,
                Duration = TimeSpan.FromMilliseconds(330),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            translate.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);

            var scaleXAnimation = new DoubleAnimation
            {
                From = 0.92,
                To = 1,
                BeginTime = delay,
                Duration = TimeSpan.FromMilliseconds(330),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            var scaleYAnimation = scaleXAnimation.Clone();
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation, HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnimation, HandoffBehavior.SnapshotAndReplace);

            var opacityAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                BeginTime = delay,
                Duration = TimeSpan.FromMilliseconds(170),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            item.BeginAnimation(UIElement.OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
            index++;
        }

        var revealDuration = TimeSpan.FromMilliseconds(20 + Math.Max(0, items.Length - 1) * 24 + 330);
        clip.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation
        {
            From = new Rect(0, 0, bounds.Width, 0),
            To = bounds,
            Duration = revealDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        }, HandoffBehavior.SnapshotAndReplace);
        AnimateContextMenuFrame(menu, TimeSpan.Zero);
    }

    private static void AnimateContextMenuFrame(ContextMenu menu, TimeSpan delay)
    {
        menu.ApplyTemplate();
        var surface = menu.Template?.FindName("MenuSurface", menu) as UIElement;
        var outline = menu.Template?.FindName("MenuOutline", menu) as UIElement;
        AnimateMenuFrameElement(surface, delay);
        AnimateMenuFrameElement(outline, delay);
    }

    private static void AnimateMenuFrameElement(UIElement? element, TimeSpan delay)
    {
        if (element is null)
            return;

        var opacityAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            BeginTime = delay,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private async void Game_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not GameItem game)
            return;

        if (game.Name.Equals("VALORANT", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var error = await RiotProductLauncher.LaunchValorantAsync(game.LaunchTarget);
                if (error is not null)
                {
                    MessageBox.Show(
                        error,
                        "K-LAUNCHER",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    _ = BringTargetWindowToFrontAsync(null, "VALORANT-Win64-Shipping");
                    _ = BringTargetWindowToFrontAsync(null, "VALORANT");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"VALORANT başlatılamadı: {ex.Message}",
                    "K-LAUNCHER",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

        Launch(game.LaunchTarget, game.LaunchArguments, game.Name);
    }

    private static bool IsBrowserApplication(string name) =>
        name.Equals("Google Chrome", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Microsoft Edge", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Mozilla Firefox", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Brave", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Vivaldi", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Opera GX", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Opera", StringComparison.OrdinalIgnoreCase);

    private void RemoveQuickLinks(AppModel app)
    {
        var dialog = new Window
        {
            Title = Localization["LinkRemoveTitle"],
            Width = 470,
            Height = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent
        };

        var root = new Border
        {
            Background = Brush("#111116"),
            BorderBrush = Brush("#00FF66"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18)
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = Localization["LinkRemoveTitle"],
            Foreground = Brush("#00FF66"),
            FontWeight = FontWeights.Bold,
            FontSize = 15
        });
        heading.Children.Add(new TextBlock
        {
            Text = Localization["LinkRemoveHint"],
            Foreground = Brush("#777780"),
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0)
        });
        Grid.SetRow(heading, 0);
        layout.Children.Add(heading);

        var linksPanel = new StackPanel();
        var scrollViewer = new ScrollViewer
        {
            Content = linksPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scrollViewer, 1);
        layout.Children.Add(scrollViewer);

        var close = new Button
        {
            Content = Localization["Close"],
            Width = 88,
            Height = 30,
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brush("#18181F"),
            Foreground = Brush("#C2C2C8"),
            BorderBrush = Brush("#303039")
        };
        close.Click += (_, _) => dialog.Close();
        Grid.SetRow(close, 2);
        layout.Children.Add(close);

        void RefreshLinks()
        {
            linksPanel.Children.Clear();
            var links = app.QuickLinks.ToArray();
            if (links.Length == 0)
            {
                linksPanel.Children.Add(new TextBlock
                {
                    Text = Localization["NoLinksToRemove"],
                    Foreground = Brush("#777780"),
                    Margin = new Thickness(2, 8, 0, 0)
                });
                return;
            }

            foreach (var link in links)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                details.Children.Add(new TextBlock
                {
                    Text = link.Name,
                    Foreground = Brush("#F1F1F1"),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12
                });
                details.Children.Add(new TextBlock
                {
                    Text = link.Url,
                    Foreground = Brush("#777780"),
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = link.Url,
                    Margin = new Thickness(0, 3, 8, 0)
                });
                Grid.SetColumn(details, 0);
                row.Children.Add(details);

                var remove = new Button
                {
                    Content = Localization["Remove"],
                    Width = 76,
                    Height = 28,
                    Margin = new Thickness(10, 0, 0, 0),
                    Background = Brush("#21151A"),
                    Foreground = Brush("#FF6B75"),
                    BorderBrush = Brush("#63313A"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                remove.Click += (_, _) =>
                {
                    try
                    {
                        QuickLinkStore.Remove(app.Name, link);
                        QuickLinkStore.Apply(app);
                        RefreshLinks();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            dialog,
                            ex.Message,
                            "K-LAUNCHER",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                };
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);

                linksPanel.Children.Add(new Border
                {
                    Background = Brush("#17171E"),
                    BorderBrush = Brush("#292930"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 7),
                    Child = row
                });
            }
        }

        RefreshLinks();
        root.Child = layout;
        dialog.Content = root;
        dialog.ShowDialog();
    }

    private void AddQuickLink(AppModel app)
    {
        var dialog = new Window
        {
            Title = Localization["LinkAddTitle"],
            Width = 390,
            Height = 235,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent
        };

        var root = new Border
        {
            Background = Brush("#111116"),
            BorderBrush = Brush("#00FF66"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18)
        };

        var grid = new Grid();

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = Localization["LinkAddTitle"],
            Foreground = Brush("#00FF66"),
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        var nameBox = new TextBox
        {
            Height = 30,
            Margin = new Thickness(0, 0, 0, 8),
            Background = Brush("#18181F"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#303039"),
            Padding = new Thickness(8),
            Text = Localization["LinkNamePlaceholder"]
        };
        Grid.SetRow(nameBox, 1);
        grid.Children.Add(nameBox);

        var urlBox = new TextBox
        {
            Height = 30,
            Margin = new Thickness(0, 0, 0, 14),
            Background = Brush("#18181F"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#303039"),
            Padding = new Thickness(8),
            Text = "https://"
        };
        Grid.SetRow(urlBox, 2);
        grid.Children.Add(urlBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancel = new Button
        {
            Content = Localization["Cancel"],
            Width = 85,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            Background = Brush("#18181F"),
            Foreground = Brush("#AAAAAF"),
            BorderBrush = Brush("#303039")
        };
        cancel.Click += (_, _) => dialog.Close();

        var save = new Button
        {
            Content = Localization["Save"],
            Width = 85,
            Height = 30,
            Background = Brush("#003B1A"),
            Foreground = Brush("#00FF66"),
            BorderBrush = Brush("#00FF66")
        };

        save.Click += (_, _) =>
        {
            try
            {
                QuickLinkStore.Add(
                    app.Name,
                    nameBox.Text,
                    urlBox.Text);

                QuickLinkStore.Apply(app);
                dialog.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "K-LAUNCHER",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);

        var hint = new TextBlock
        {
            Text = Localization["HttpHint"],
            Foreground = Brush("#55555E"),
            FontSize = 10,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(hint, 4);
        grid.Children.Add(hint);

        root.Child = grid;
        dialog.Content = root;
        dialog.ShowDialog();
    }

    private static MenuItem MakeItem(
        string text,
        string glyph,
        string color,
        RoutedEventHandler handler)
    {
        var item = new MenuItem
        {
            Header = text,
            Icon = MakeGlyph(glyph),
            Foreground = Brush(color),
            Padding = new Thickness(9, 7, 14, 7)
        };

        item.Click += handler;
        return item;
    }

    private static FrameworkElement MakeGlyph(string text)
    {
        return new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = Brush("#0D0D12"),
            Child = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Segoe UI Symbol"),
                Foreground = Brush("#00FF66"),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
    }

    private static FrameworkElement MakeExeIcon(string path, string fallbackExePath)
    {
        var image = new Image
        {
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var converter = new ShellIconConverter();
        image.Source = converter.Convert(
            path,
            typeof(ImageSource),
            null,
            CultureInfo.InvariantCulture) as ImageSource;

        if (image.Source is null && !string.IsNullOrWhiteSpace(fallbackExePath))
        {
            image.Source = converter.Convert(
                fallbackExePath,
                typeof(ImageSource),
                null,
                CultureInfo.InvariantCulture) as ImageSource;
        }

        if (TryGetRemoteIconUri(path, out var iconUri))
            _ = LoadRemoteIconAsync(iconUri, image);

        return new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = Brush("#0D0D12"),
            ClipToBounds = true,
            Child = image
        };
    }

    private static bool TryGetRemoteIconUri(string? path, out Uri uri)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static async Task LoadRemoteIconAsync(Uri uri, Image image)
    {
        try
        {
            var bitmapTask = RemoteIconLoads.GetOrAdd(
                uri.AbsoluteUri,
                _ => DownloadRemoteIconAsync(uri));
            var bitmap = await bitmapTask.ConfigureAwait(false);

            if (bitmap is not null)
                await image.Dispatcher.InvokeAsync(() => image.Source = bitmap);
        }
        catch
        {
            // The executable icon remains visible if a remote game icon is unavailable.
        }
    }

    private static async Task<BitmapSource?> DownloadRemoteIconAsync(Uri uri)
    {
        try
        {
            var bytes = await IconHttpClient.GetByteArrayAsync(uri).ConfigureAwait(false);
            if (bytes.Length == 0)
                return null;

            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 256;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static void OpenInstallFolder(string exe)
    {
        var folder = Path.GetDirectoryName(exe);

        if (string.IsNullOrWhiteSpace(folder))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true
        });
    }

    private bool Launch(
        string target,
        string arguments,
        string? appName = null,
        bool runAsAdministrator = false)
    {
        if (string.IsNullOrWhiteSpace(target))
            return false;

        try
        {
            if (TryActivateAppFolder(target, out var appProcessId))
            {
                AllowSetForegroundWindow((uint)appProcessId);
                _ = BringTargetWindowToFrontAsync(appProcessId, null, appName);
                return true;
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = target,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
                Verb = runAsAdministrator ? "runas" : string.Empty,
                WorkingDirectory =
                    File.Exists(target) && Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                        ? (Path.GetDirectoryName(target) ?? string.Empty)
                        : string.Empty
            });

            int? processId = null;
            try
            {
                if (process is not null && !process.HasExited)
                    processId = process.Id;
            }
            catch (InvalidOperationException)
            {
                // Some shell launches finish the starter process before the app window appears.
            }

            if (processId is int pid)
                AllowSetForegroundWindow((uint)pid);

            var targetProcessName = File.Exists(target) && Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(target)
                : null;
            _ = BringTargetWindowToFrontAsync(processId, targetProcessName, appName);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Baslatilamadi:\n\n{ex.Message}",
                "K-LAUNCHER",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private static bool TryActivateAppFolder(string target, out int processId)
    {
        processId = 0;
        if (!target.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var appUserModelId = target[AppsFolderPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(appUserModelId))
            return false;

        object? managerObject = null;
        try
        {
            managerObject = new ApplicationActivationManager();
            var manager = (IApplicationActivationManager)managerObject;
            var result = manager.ActivateApplication(appUserModelId, null, 0, out var activatedProcessId);
            if (result < 0 || activatedProcessId == 0)
                return false;

            processId = checked((int)activatedProcessId);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (managerObject is not null && Marshal.IsComObject(managerObject))
                Marshal.FinalReleaseComObject(managerObject);
        }
    }

    private static async Task BringTargetWindowToFrontAsync(int? processId, string? processName, string? appName = null)
    {
        var relatedProcessNames = appName?.ToLowerInvariant() switch
        {
            "opera gx" => new[] { "opera", "opera_gx" },
            "riot client" => new[] { "riotclientux", "riotclientuxrender" },
            "medal" => new[] { "medalapp" },
            _ => Array.Empty<string>()
        };

        for (var attempt = 0; attempt < 80; attempt++)
        {
            var windowHandle = processId is int pid ? FindVisibleWindow(pid) : IntPtr.Zero;
            if (windowHandle == IntPtr.Zero && !string.IsNullOrWhiteSpace(processName))
                windowHandle = FindVisibleWindow(processName);
            if (windowHandle == IntPtr.Zero)
            {
                foreach (var relatedProcessName in relatedProcessNames)
                {
                    windowHandle = FindVisibleWindow(relatedProcessName);
                    if (windowHandle != IntPtr.Zero)
                        break;
                }
            }

            if (windowHandle != IntPtr.Zero)
            {
                ShowWindowAsync(windowHandle, SwRestore);
                ShowWindowAsync(windowHandle, SwShow);
                BringWindowToTop(windowHandle);
                SetForegroundWindow(windowHandle);
                return;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }
    }

    private static IntPtr FindVisibleWindow(int processId)
    {
        var unownedWindows = new List<IntPtr>();
        var ownedWindows = new List<IntPtr>();
        EnumWindows((windowHandle, _) =>
        {
            if (!IsWindowVisible(windowHandle))
                return true;

            GetWindowThreadProcessId(windowHandle, out var windowProcessId);
            if (windowProcessId == (uint)processId)
            {
                if (GetWindow(windowHandle, 4) == IntPtr.Zero)
                    unownedWindows.Add(windowHandle);
                else
                    ownedWindows.Add(windowHandle);
            }

            return true;
        }, IntPtr.Zero);

        return unownedWindows.FirstOrDefault() is var unowned && unowned != IntPtr.Zero
            ? unowned
            : ownedWindows.FirstOrDefault();
    }

    private static IntPtr FindVisibleWindow(string processName)
    {
        try
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    var windowHandle = FindVisibleWindow(process.Id);
                    if (windowHandle != IntPtr.Zero)
                        return windowHandle;
                }
            }
        }
        catch
        {
            // The app may exit or restart while its window is being located.
        }

        return IntPtr.Zero;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        var brush = new SolidColorBrush(MapPaletteColor(color));

        brush.Freeze();
        return brush;
    }
}
