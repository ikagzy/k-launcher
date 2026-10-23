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
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Data;
using System.Windows.Threading;
using KLauncher.Models;
using KLauncher.Services;
using KLauncher.ViewModels;
using Ellipse = System.Windows.Shapes.Ellipse;
using PathShape = System.Windows.Shapes.Path;

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
    // Segoe Print is installed with Windows and is visibly handwritten while
    // still working in a published self-contained/dist copy of the app.
    private const string IntroFontName = "Segoe Print";

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
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr processHandle);

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
    private static readonly string ThemeSettingsPath =
        LauncherStorage.GetFilePath("theme.txt");
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
    private ICollectionView? _appsView;
    private string _appSearchQuery = string.Empty;
    private int _opacityAnimationVersion;
    private bool _isMinimizing;
    private bool _isClosing;
    private bool _allowWindowClose;
    private bool _isHiddenToTray;
    private bool _isContextMenuOpen;
    private bool _startupFadeCompleted;
    private bool _isSuspendedForBackground;
    private bool _isBatchSelectionMode;
    private LauncherInterfaceMode _interfaceMode;
    private AppModel? _listSelectedApp;
    private readonly Dictionary<AppModel, Border> _batchSelectionCards = new();
    private readonly Dictionary<string, OverviewEntry> _batchSelectedEntries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _batchSelectionEntryCards =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<BackgroundParticle> _backgroundParticles = new();
    private readonly Random _particleRandom = new();
    private bool _particleAnimationStarted;
    private bool _particlesPaused;
    private long _lastParticleTimestamp;
    private double _particleClock;
    private Vector _particleParallax;
    private RadialGradientBrush _particleGlowAccent = null!;
    private RadialGradientBrush _particleGlowSecondary = null!;
    private DispatcherTimer? _particleTimer;
    // The window is layered (AllowsTransparency), so every particle frame
    // re-composites most of it. Display-rate frames (144-240 Hz on laptops)
    // cost a lot of GPU for dust moving under 1 px per frame; 30 fps looks
    // the same. Motion is time-based, so the speed does not change.
    // 30 ms rather than 33: the timer fires on the next 15.6 ms system tick,
    // so 33 ms would land on 47 ms (21 fps); 30 ms lands on 31 ms (~32 fps).
    private static readonly TimeSpan ParticleFrameInterval = TimeSpan.FromMilliseconds(30);
    // Each soft dot is rasterized once and then only moved and faded.
    private static readonly BitmapCache ParticleCache = CreateParticleCache();
    private readonly Dictionary<ScrollViewer, EventHandler> _smoothScrollHandlers = new();
    private sealed record OverviewEntry(
        string Name,
        string IconPath,
        string FallbackPath,
        string FallbackGlyph,
        string SelectionKey,
        Action Launch,
        GameItem? StoreGame = null,
        // Set for the app's own actions (launch, hide...) in the showcase.
        string? ActionGlyph = null,
        string? ActionColor = null)
    {
        public bool IsAction => ActionGlyph is not null;
    }
    private sealed record ContentVisibilityEntry(
        string Kind,
        string Name,
        string Target,
        string IconPath,
        string FallbackPath,
        string FallbackGlyph);
    private sealed class BackgroundParticle
    {
        public required Ellipse Shape { get; init; }
        public required TranslateTransform Transform { get; init; }
        public bool UsesSecondaryColor { get; init; }
        public double X { get; set; }
        public double Y { get; set; }
        public double VelocityX { get; init; }
        public double VelocityY { get; init; }
        // 0 = far away (small, dim, slow), 1 = close (large, bright, fast).
        public double Depth { get; init; }
        public double BaseOpacity { get; init; }
        public double SwayAmplitude { get; init; }
        public double SwayFrequency { get; init; }
        public double SwayPhase { get; init; }
        public double TwinkleFrequency { get; init; }
        public double TwinklePhase { get; init; }
    }
    private SystemTrayIcon? _systemTrayIcon;
    private MenuItem? _trayOpenMenuItem;
    private MenuItem? _trayExitMenuItem;

    private LocalizationService Localization =>
        (LocalizationService)Application.Current.Resources["Localization"];

    private void StopSmoothScroll(ScrollViewer scrollViewer)
    {
        if (_smoothScrollHandlers.Remove(scrollViewer, out var handler))
            CompositionTarget.Rendering -= handler;
    }

    private void SmoothScrollTo(
        ScrollViewer scrollViewer,
        double targetOffset,
        bool horizontal,
        TimeSpan? duration = null)
    {
        StopSmoothScroll(scrollViewer);

        var startOffset = horizontal
            ? scrollViewer.HorizontalOffset
            : scrollViewer.VerticalOffset;
        var maxOffset = horizontal
            ? scrollViewer.ScrollableWidth
            : scrollViewer.ScrollableHeight;
        targetOffset = Math.Clamp(targetOffset, 0, Math.Max(0, maxOffset));

        if (Math.Abs(targetOffset - startOffset) < 0.5)
            return;

        var startedAt = Stopwatch.GetTimestamp();
        var animationDuration = duration ?? MotionSettings.Ms(230);
        if (animationDuration <= TimeSpan.Zero)
        {
            if (horizontal)
                scrollViewer.ScrollToHorizontalOffset(targetOffset);
            else
                scrollViewer.ScrollToVerticalOffset(targetOffset);
            return;
        }

        EventHandler handler = null!;
        handler = (_, _) =>
        {
            var elapsed = TimeSpan.FromSeconds(
                (Stopwatch.GetTimestamp() - startedAt) / (double)Stopwatch.Frequency);
            var progress = Math.Clamp(
                elapsed.TotalMilliseconds / animationDuration.TotalMilliseconds,
                0,
                1);
            var easedProgress = 1 - Math.Pow(1 - progress, 3);
            var offset = startOffset + ((targetOffset - startOffset) * easedProgress);

            if (horizontal)
                scrollViewer.ScrollToHorizontalOffset(offset);
            else
                scrollViewer.ScrollToVerticalOffset(offset);

            if (progress >= 1)
            {
                StopSmoothScroll(scrollViewer);
                if (horizontal)
                    scrollViewer.ScrollToHorizontalOffset(targetOffset);
                else
                    scrollViewer.ScrollToVerticalOffset(targetOffset);
            }
        };

        _smoothScrollHandlers[scrollViewer] = handler;
        CompositionTarget.Rendering += handler;
    }

    private void ConfigureSmoothVerticalScroll(ScrollViewer scrollViewer)
    {
        scrollViewer.CanContentScroll = false;
        scrollViewer.PanningMode = PanningMode.VerticalOnly;
        scrollViewer.PreviewMouseWheel += (_, args) =>
        {
            args.Handled = true;
            SmoothScrollTo(
                scrollViewer,
                scrollViewer.VerticalOffset - (args.Delta * 1.08),
                horizontal: false,
                duration: MotionSettings.Ms(165));
        };
    }

    public MainWindow()
    {
        InitializeComponent();
        // Rounded window: clip the content so the bars and the intro overlay
        // follow the 12 px corners of MainSurface.
        MainRoot.SizeChanged += (_, args) =>
        {
            var clip = new RectangleGeometry(new Rect(args.NewSize), 11, 11);
            clip.Freeze();
            MainRoot.Clip = clip;
        };
        ConfigureSmoothVerticalScroll(GridAppsScrollViewer);
        ConfigureSmoothVerticalScroll(ListAppsScrollViewer);
        ConfigureSmoothVerticalScroll(ListDetailsScrollViewer);
        LoadAccentPalette();
        SetApplicationIcon();
        Opacity = 0;
        DataContext = _viewModel;
        _appsView = CollectionViewSource.GetDefaultView(_viewModel.Apps);
        _appsView.Filter = FilterApps;
        SetInterfaceMode(LauncherInterfaceSettings.Load(), persist: false);
        FlowDirection = Localization.FlowDirection;
        Localization.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LocalizationService.LanguageCode))
            {
                FlowDirection = Localization.FlowDirection;
                UpdateLocalizedAppNames();
                if (_trayOpenMenuItem is not null)
                    _trayOpenMenuItem.Header = Localization["ShowLauncher"];
                if (_trayExitMenuItem is not null)
                    _trayExitMenuItem.Header = Localization["ExitLauncher"];
                if (_interfaceMode == LauncherInterfaceMode.List && _listSelectedApp is not null)
                    ShowListDetails(_listSelectedApp);
                UpdateBatchSelectionUi();
            }
        };
        SourceInitialized += MainWindow_SourceInitialized;
        BeepWatch.Attach(this);
        Loaded += MainWindow_Loaded;
        Activated += MainWindow_Activated;
        Deactivated += MainWindow_Deactivated;
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        MotionSettings.Changed += MotionSettings_Changed;
        ParticleSettings.Changed += ParticleSettings_Changed;
        _systemTrayIcon = new SystemTrayIcon(this, CreateTrayMenu(), RestoreFromTray);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        HideSystemBorder(this);

        // The intro overlay replaces the old 450 ms window fade.
        Opacity = 1;
        LoadAccentPalette();
        ApplyAccentPaletteToWindow();
        WatchThemeFile();
        StartBackgroundParticles();
        var refreshTask = _viewModel.RefreshAsync();
        await PlayIntroAnimationAsync();
        await refreshTask;

        UpdateLocalizedAppNames();
        ApplyAccentPaletteToWindow();
        UpdateDesktopShortcutIcon(_activePalette.Id);
        _startupFadeCompleted = true;
        ScheduleIdleWorkingSetTrim();
        _ = WarmQuickLinkIconsAsync();
    }

    private async Task WarmQuickLinkIconsAsync()
    {
        var links = _viewModel.Apps
            .SelectMany(app => app.QuickLinks)
            .ToArray();
        if (links.Length == 0)
            return;

        using var gate = new SemaphoreSlim(3);
        var results = await Task.WhenAll(links.Select(async link =>
        {
            var cached = GetQuickLinkIconPath(link);
            if (!string.IsNullOrWhiteSpace(cached))
                return (Link: link, IconPath: cached);

            await gate.WaitAsync();
            try
            {
                var iconPath = await QuickLinkIconStore.TryFetchAndSaveAsync(link.Url);
                return (Link: link, IconPath: iconPath ?? string.Empty);
            }
            finally
            {
                gate.Release();
            }
        }));

        var changed = false;
        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.IconPath) ||
                result.IconPath.Equals(result.Link.IconPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Link.IconPath = result.IconPath;
            changed = true;
        }

        if (!changed)
            return;

        if (_isClosing || _isSuspendedForBackground || _viewModel.Apps.Count == 0)
            return;

        AppCacheStore.Save(_viewModel.Apps);
        if (_interfaceMode == LauncherInterfaceMode.List && _listSelectedApp is not null)
            ShowListDetails(_listSelectedApp);
    }

    private void StartBackgroundParticles()
    {
        if (_particleAnimationStarted || ParticleCanvas is null || !ParticleSettings.Enabled)
            return;

        _particleAnimationStarted = true;
        _backgroundParticles.Clear();
        ParticleCanvas.Children.Clear();
        UpdateParticleGlowBrushes();
        _particleClock = 0;
        _particleParallax = default;

        var width = Math.Max(ActualWidth, Width);
        var height = Math.Max(ActualHeight, Height);

        // Three depth layers: many faint far dots, fewer bright near ones.
        const int farCount = 16;
        const int midCount = 12;
        const int nearCount = 6;
        const int particleCount = farCount + midCount + nearCount;

        for (var index = 0; index < particleCount; index++)
        {
            var depth = index < farCount
                ? 0.12 + (_particleRandom.NextDouble() * 0.2)
                : index < farCount + midCount
                    ? 0.45 + (_particleRandom.NextDouble() * 0.2)
                    : 0.82 + (_particleRandom.NextDouble() * 0.18);
            var coreSize = 1.2 + (depth * 3.0) + (_particleRandom.NextDouble() * 0.7);
            // The ellipse also holds the soft halo, so it is larger than the dot.
            var diameter = coreSize * (2.2 + depth);
            var usesSecondary = index % 5 == 0;
            var glow = usesSecondary ? _particleGlowSecondary : _particleGlowAccent;
            var transform = new TranslateTransform();
            var shape = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = glow,
                IsHitTestVisible = false,
                RenderTransform = transform,
                CacheMode = ParticleCache
            };

            var speed = 5 + (depth * 19) + (_particleRandom.NextDouble() * 4);
            var particle = new BackgroundParticle
            {
                Shape = shape,
                Transform = transform,
                UsesSecondaryColor = usesSecondary,
                X = _particleRandom.NextDouble() * width,
                Y = _particleRandom.NextDouble() * height,
                // A slow upward drift, like dust in warm air.
                VelocityX = (-0.55 + (_particleRandom.NextDouble() * 1.1)) * speed,
                VelocityY = -(0.35 + (_particleRandom.NextDouble() * 0.65)) * speed,
                Depth = depth,
                BaseOpacity = 0.16 + (depth * 0.42) + (_particleRandom.NextDouble() * 0.1),
                SwayAmplitude = 3 + (depth * 9),
                SwayFrequency = 0.12 + (_particleRandom.NextDouble() * 0.3),
                SwayPhase = _particleRandom.NextDouble() * Math.Tau,
                TwinkleFrequency = 0.15 + (_particleRandom.NextDouble() * 0.55),
                TwinklePhase = _particleRandom.NextDouble() * Math.Tau
            };

            _backgroundParticles.Add(particle);
            ParticleCanvas.Children.Add(shape);
            Canvas.SetLeft(shape, -diameter / 2);
            Canvas.SetTop(shape, -diameter / 2);
            PlaceBackgroundParticle(particle);
        }

        _particlesPaused = true;
        ResumeBackgroundParticles();
    }

    private static BitmapCache CreateParticleCache()
    {
        var cache = new BitmapCache { SnapsToDevicePixels = false };
        cache.Freeze();
        return cache;
    }

    private DispatcherTimer ParticleTimer
    {
        get
        {
            if (_particleTimer is null)
            {
                _particleTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
                {
                    Interval = ParticleFrameInterval
                };
                _particleTimer.Tick += RenderBackgroundParticles;
            }

            return _particleTimer;
        }
    }

    // Frozen and shared by every particle of the same colour, so the render
    // thread realizes two gradients instead of one per particle.
    private static RadialGradientBrush CreateParticleGlow(Color color)
    {
        var glow = new RadialGradientBrush();
        glow.GradientStops.Add(new GradientStop(color, 0));
        glow.GradientStops.Add(new GradientStop(color, 0.28));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(70, color.R, color.G, color.B), 0.5));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        glow.Freeze();
        return glow;
    }

    private void UpdateParticleGlowBrushes()
    {
        _particleGlowAccent = CreateParticleGlow(ThemeAccentColor);
        _particleGlowSecondary = CreateParticleGlow(ThemeSecondaryColor);
    }

    private void RenderBackgroundParticles(object? sender, EventArgs e)
    {
        if (!_particleAnimationStarted || _backgroundParticles.Count == 0)
            return;

        var now = Stopwatch.GetTimestamp();
        var realElapsed = Math.Clamp(
            (now - _lastParticleTimestamp) / (double)Stopwatch.Frequency,
            0,
            0.1);
        _lastParticleTimestamp = now;

        // Follow the animation speed setting, but gently: Fast ~1.35x, Slow ~0.8x.
        var speedFactor = MotionSettings.Scale > 0 ? 1 / Math.Sqrt(MotionSettings.Scale) : 1;
        var elapsed = realElapsed * speedFactor;
        _particleClock += elapsed;

        var width = Math.Max(ParticleCanvas.ActualWidth, ActualWidth);
        var height = Math.Max(ParticleCanvas.ActualHeight, ActualHeight);

        // Subtle parallax: near particles lean away from the pointer.
        var target = default(Vector);
        if (IsActive && width > 0 && height > 0)
        {
            var pointer = Mouse.GetPosition(ParticleCanvas);
            target = new Vector(
                Math.Clamp((pointer.X / width) - 0.5, -0.5, 0.5),
                Math.Clamp((pointer.Y / height) - 0.5, -0.5, 0.5));
        }
        _particleParallax += (target - _particleParallax) * Math.Min(1, realElapsed * 2.5);

        foreach (var particle in _backgroundParticles)
        {
            particle.X += particle.VelocityX * elapsed;
            particle.Y += particle.VelocityY * elapsed;

            // Wrap around outside the clipped edges, so nothing pops in or out.
            var margin = particle.Shape.Width + particle.SwayAmplitude + 12;
            if (particle.X < -margin)
                particle.X += width + (margin * 2);
            else if (particle.X > width + margin)
                particle.X -= width + (margin * 2);
            if (particle.Y < -margin)
                particle.Y += height + (margin * 2);
            else if (particle.Y > height + margin)
                particle.Y -= height + (margin * 2);

            PlaceBackgroundParticle(particle);
        }
    }

    private void PlaceBackgroundParticle(BackgroundParticle particle)
    {
        var sway = Math.Sin((_particleClock * particle.SwayFrequency * Math.Tau) + particle.SwayPhase);
        var twinkle = Math.Sin((_particleClock * particle.TwinkleFrequency * Math.Tau) + particle.TwinklePhase);
        particle.Transform.X = particle.X + (sway * particle.SwayAmplitude) -
            (_particleParallax.X * particle.Depth * 14);
        particle.Transform.Y = particle.Y - (_particleParallax.Y * particle.Depth * 9);
        particle.Shape.Opacity = particle.BaseOpacity * (0.62 + (0.38 * twinkle));
    }

    // Hidden, minimized or faded out after losing focus: stop the frame
    // timer so the render loop can idle.
    private void PauseBackgroundParticles()
    {
        if (!_particleAnimationStarted || _particlesPaused)
            return;

        _particleTimer?.Stop();
        _particlesPaused = true;
    }

    private void ResumeBackgroundParticles()
    {
        if (!_particleAnimationStarted || !_particlesPaused || !MotionSettings.Enabled)
            return;

        _particlesPaused = false;
        _lastParticleTimestamp = Stopwatch.GetTimestamp();
        ParticleTimer.Start();
    }

    // Off drops the particle shapes and the frame callback; on rebuilds them.
    private void ParticleSettings_Changed()
    {
        if (!ParticleSettings.Enabled)
        {
            StopBackgroundParticles();
            // After the next frame the removed shapes are unreferenced; collect
            // them and hand the pages back so Task Manager shows the drop.
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(BackgroundEfficiency.TrimOnce));
        }
        else if (IsVisible && WindowState != WindowState.Minimized && !_isSuspendedForBackground)
            StartBackgroundParticles();
    }

    // Off also freezes the background particles, so nothing asks for frames.
    private void MotionSettings_Changed()
    {
        if (!MotionSettings.Enabled)
            PauseBackgroundParticles();
        else if (IsVisible && WindowState != WindowState.Minimized && !_isSuspendedForBackground)
            ResumeBackgroundParticles();
    }

    private void StopBackgroundParticles()
    {
        if (!_particleAnimationStarted)
            return;

        _particleTimer?.Stop();
        _particleAnimationStarted = false;
        _particlesPaused = false;
        _backgroundParticles.Clear();
        ParticleCanvas?.Children.Clear();
    }

    private void UpdateBackgroundParticleTheme()
    {
        if (_backgroundParticles.Count == 0)
            return;

        UpdateParticleGlowBrushes();
        foreach (var particle in _backgroundParticles)
            particle.Shape.Fill = particle.UsesSecondaryColor ? _particleGlowSecondary : _particleGlowAccent;
    }

    private void SetInterfaceMode(LauncherInterfaceMode mode, bool persist = true)
    {
        if (mode != LauncherInterfaceMode.List)
            ClearBatchSelection();

        _interfaceMode = mode;
        ApplyInterfaceMode();

        if (mode != LauncherInterfaceMode.List)
            ClearListDetails();

        if (persist)
            LauncherInterfaceSettings.Save(mode);
    }

    private void ApplyInterfaceMode()
    {
        if (GridAppsScrollViewer is null || ListInterfacePanel is null)
            return;

        var isList = _interfaceMode == LauncherInterfaceMode.List;
        var showDetails = isList && _listSelectedApp is not null;
        GridAppsScrollViewer.Visibility = isList
            ? Visibility.Collapsed
            : Visibility.Visible;
        ListInterfacePanel.Visibility = isList
            ? Visibility.Visible
            : Visibility.Collapsed;
        ListAppsColumn.Width = new GridLength(0.78, GridUnitType.Star);
        ListDetailsColumn.Width = new GridLength(1.22, GridUnitType.Star);
        ListDetailsPanel.Visibility = showDetails
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ClearListDetails()
    {
        _listSelectedApp = null;
        if (ListDetailsEmptyState is null || ListDetailsScrollViewer is null)
            return;

        ListDetailsPanel.Visibility = Visibility.Collapsed;
        ListDetailsPanel.BeginAnimation(UIElement.OpacityProperty, null);
        ListDetailsPanel.Opacity = 1;
        if (ListDetailsPanel.RenderTransform is TranslateTransform detailsTransform)
            detailsTransform.X = -48;
        ListDetailsEmptyState.Visibility = Visibility.Visible;
        ListDetailsScrollViewer.Visibility = Visibility.Collapsed;
        ListDetailsItemsHost.Children.Clear();
        ListDetailsActionsHost.Children.Clear();
        ListDetailsTitle.Text = Localization["InterfaceListHeader"];
        ListDetailsSubtitle.Text = Localization["InterfaceListSelectHint"];
        ListDetailsEmptyText.Text = Localization["InterfaceListEmpty"];
    }

    private void ListBatchSelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBatchSelectionMode)
        {
            ClearBatchSelection();
            return;
        }

        _isBatchSelectionMode = true;
        UpdateBatchSelectionUi();
    }

    private void ListBatchLaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isBatchSelectionMode)
            return;

        var selectedApps = _viewModel.Apps
            .Where(app => app.IsBatchSelected && app.CanLaunch)
            .ToArray();

        if (selectedApps.Length == 0 && _batchSelectedEntries.Count == 0)
            return;

        foreach (var app in selectedApps)
            Launch(app.LaunchTarget, app.LaunchArguments, app.Name);

        foreach (var entry in _batchSelectedEntries.Values.ToArray())
            entry.Launch();

        ClearBatchSelection();
    }

    private void ListBatchCancelButton_Click(object sender, RoutedEventArgs e) =>
        ClearBatchSelection();

    private void ToggleBatchSelection(AppModel app, Border? border = null)
    {
        if (!_isBatchSelectionMode)
            _isBatchSelectionMode = true;

        app.IsBatchSelected = !app.IsBatchSelected;
        if (border is not null)
        {
            _batchSelectionCards[app] = border;
            ApplyListAppSelectionVisual(border, app.IsBatchSelected);
        }
        UpdateBatchSelectionUi();
    }

    private void ToggleBatchSelection(OverviewEntry entry, Border card)
    {
        if (!_isBatchSelectionMode)
            _isBatchSelectionMode = true;

        if (_batchSelectedEntries.Remove(entry.SelectionKey))
        {
            ApplyListAppSelectionVisual(card, selected: false);
            _batchSelectionEntryCards.Remove(entry.SelectionKey);
        }
        else
        {
            _batchSelectedEntries[entry.SelectionKey] = entry;
            _batchSelectionEntryCards[entry.SelectionKey] = card;
            ApplyListAppSelectionVisual(card, selected: true);
        }

        UpdateBatchSelectionUi();
    }

    private static string BuildBatchSelectionKey(
        AppModel app,
        string kind,
        string target) =>
        $"{app.LaunchTarget}\u001f{kind}\u001f{target}";

    private void ClearBatchSelection()
    {
        foreach (var app in _viewModel.Apps)
            app.IsBatchSelected = false;

        foreach (var border in _batchSelectionCards.Values)
            ApplyListAppSelectionVisual(border, selected: false);
        _batchSelectionCards.Clear();

        foreach (var border in _batchSelectionEntryCards.Values)
            ApplyListAppSelectionVisual(border, selected: false);
        _batchSelectionEntryCards.Clear();
        _batchSelectedEntries.Clear();

        _isBatchSelectionMode = false;
        UpdateBatchSelectionUi();
    }

    private void UpdateBatchSelectionUi()
    {
        if (ListBatchSelectButton is null)
            return;

        var count = _viewModel.Apps.Count(app => app.IsBatchSelected) +
                    _batchSelectedEntries.Count;
        ListBatchSelectButton.Content = Localization[
            _isBatchSelectionMode ? "BatchSelectActive" : "BatchSelect"];
        ListBatchLaunchButton.Content = Localization["LaunchSelected"];
        ListBatchCancelButton.Content = Localization["CancelSelection"];
        // Keep the button enabled so WPF does not replace the themed surface
        // with the system disabled (white) button template.
        ListBatchLaunchButton.IsEnabled = true;
        ListBatchLaunchButton.IsHitTestVisible = count > 0;
        ListBatchLaunchButton.Opacity = count > 0 ? 1 : 0.48;
        ListBatchCancelButton.Visibility = _isBatchSelectionMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        ListBatchSelectionStatus.Text = _isBatchSelectionMode
            ? string.Format(
                CultureInfo.CurrentCulture,
                Localization["SelectedCount"],
                count)
            : string.Empty;
    }

    private void ScheduleIdleWorkingSetTrim()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(5)
        };

        timer.Tick += OnTrimTimerTick;
        timer.Start();

        void OnTrimTimerTick(object? sender, EventArgs e)
        {
            timer.Stop();
            timer.Tick -= OnTrimTimerTick;
            TrimIdleWorkingSet();
        }
    }

    private static void TrimIdleWorkingSet(bool aggressive = false)
    {
        try
        {
            if (aggressive)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
            else
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);
            }

            using var process = Process.GetCurrentProcess();
            WriteMemorySnapshot(process, aggressive ? "derin kırpma" : "boşta kırpma");
            EmptyWorkingSet(process.Handle);
        }
        catch
        {
            // Memory trimming is optional and must never affect startup.
        }
    }

    private static void WriteMemorySnapshot(Process process, string reason)
    {
        // Where the private memory goes: managed GC heap vs. everything else
        // (WPF render buffers, native images, JIT). Used to measure RAM work.
        try
        {
            process.Refresh();
            var info = GC.GetGCMemoryInfo();
            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss}  {1}: ozel={2:N1} MB, calisma={3:N1} MB, GC yigin={4:N1} MB, GC ayrilan={5:N1} MB, parcali={6:N1} MB{7}",
                DateTime.Now,
                reason,
                process.PrivateMemorySize64 / 1048576.0,
                process.WorkingSet64 / 1048576.0,
                info.HeapSizeBytes / 1048576.0,
                info.TotalCommittedBytes / 1048576.0,
                info.FragmentedBytes / 1048576.0,
                Environment.NewLine);
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "K-Launcher", "logs", "memory.log");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line);
        }
        catch
        {
            // Diagnostics only.
        }
    }

    private async Task PlayIntroAnimationAsync()
    {
        const string title = "K-LAUNCHER";
        const double letterSpacing = 1.5;

        if (!MotionSettings.Enabled)
        {
            IntroOverlay.Children.Clear();
            IntroOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        IntroOverlay.Visibility = Visibility.Visible;
        IntroOverlay.Opacity = 1;
        IntroOverlay.Background = new SolidColorBrush(ThemeBackgroundColor);
        IntroOverlay.Children.Clear();

        var letters = new List<TextBlock>();
        var totalWidth = 0d;
        var maxHeight = 0d;

        foreach (var character in title)
        {
            var letter = new TextBlock
            {
                Text = character.ToString(),
                FontFamily = new FontFamily(IntroFontName),
                FontSize = 52,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(ThemeAccentColor),
                Opacity = 0,
                RenderTransform = new TranslateTransform(-72, 0)
            };

            letter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            totalWidth += letter.DesiredSize.Width + letterSpacing;
            maxHeight = Math.Max(maxHeight, letter.DesiredSize.Height);
            letters.Add(letter);
        }

        var x = Math.Max(18, (ActualWidth - totalWidth) / 2);
        var y = Math.Max(18, (ActualHeight - maxHeight) / 2);

        for (var index = 0; index < letters.Count; index++)
        {
            var letter = letters[index];
            Canvas.SetLeft(letter, x);
            Canvas.SetTop(letter, y);
            IntroOverlay.Children.Add(letter);

            var delay = MotionSettings.Ms(index * 34);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            letter.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, MotionSettings.Ms(150))
                {
                    BeginTime = delay,
                    EasingFunction = ease
                });

            ((TranslateTransform)letter.RenderTransform).BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(-72, 0, MotionSettings.Ms(180))
                {
                    BeginTime = delay,
                    EasingFunction = ease
                });

            x += letter.DesiredSize.Width + letterSpacing;
        }

        var entranceDuration = 34 * Math.Max(0, letters.Count - 1) + 210;
        await Task.Delay(MotionSettings.Ms(entranceDuration));

        // Draw a fixed horizontal bar. Its Y position never animates; only ScaleX
        // changes, so it cannot accidentally look like a vertical travelling line.
        var scanLine = new Border
        {
            Width = Math.Max(ActualWidth, 1),
            Height = 6,
            Background = new SolidColorBrush(ThemeAccentColor),
            RenderTransformOrigin = new Point(0, 0.5),
            RenderTransform = new ScaleTransform(0, 1),
            Effect = new DropShadowEffect
            {
                Color = ThemeAccentColor,
                BlurRadius = 22,
                ShadowDepth = 0,
                Opacity = 0.95
            }
        };

        Canvas.SetLeft(scanLine, 0);
        Canvas.SetTop(scanLine, y + maxHeight / 2 - 3);
        IntroOverlay.Children.Add(scanLine);
        ((ScaleTransform)scanLine.RenderTransform).BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0, 1, MotionSettings.Ms(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            });

        await Task.Delay(MotionSettings.Ms(190));

        foreach (var letter in letters)
            letter.Visibility = Visibility.Collapsed;

        IntroOverlay.Children.Remove(scanLine);

        var word = CreateIntroWord(title);
        word.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var wordWidth = word.DesiredSize.Width;
        var wordHeight = word.DesiredSize.Height;
        var wordX = Math.Max(18, (ActualWidth - wordWidth) / 2);
        var wordY = Math.Max(18, (ActualHeight - wordHeight) / 2);

        var topHalf = CreateIntroWord(title, wordWidth, wordHeight);
        var bottomHalf = CreateIntroWord(title, wordWidth, wordHeight);
        topHalf.Clip = new RectangleGeometry(new Rect(0, 0, wordWidth, wordHeight / 2 + 1));
        bottomHalf.Clip = new RectangleGeometry(new Rect(0, wordHeight / 2 - 1, wordWidth, wordHeight / 2 + 1));

        Canvas.SetLeft(topHalf, wordX);
        Canvas.SetTop(topHalf, wordY);
        Canvas.SetLeft(bottomHalf, wordX);
        Canvas.SetTop(bottomHalf, wordY);

        var topTranslate = new TranslateTransform();
        topHalf.RenderTransform = topTranslate;

        var bottomTranslate = new TranslateTransform();
        bottomHalf.RenderTransform = bottomTranslate;

        IntroOverlay.Children.Add(topHalf);
        IntroOverlay.Children.Add(bottomHalf);

        topTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0, -105, MotionSettings.Ms(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });
        bottomTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0, 72, MotionSettings.Ms(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });

        await Task.Delay(MotionSettings.Ms(285));

        IntroOverlay.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1, 0, MotionSettings.Ms(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });

        await Task.Delay(MotionSettings.Ms(230));
        IntroOverlay.Visibility = Visibility.Collapsed;
        IntroOverlay.Opacity = 1;
    }

    private TextBlock CreateIntroWord(string title) => new()
    {
        Text = title,
        FontFamily = new FontFamily(IntroFontName),
        FontSize = 52,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(ThemeAccentColor)
    };

    private TextBlock CreateIntroWord(string title, double width, double height)
    {
        var word = CreateIntroWord(title);
        word.Width = width;
        word.Height = height;
        return word;
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
        ThemeResources.Apply(
            _activePalette.Primary,
            _activePalette.Secondary,
            _activePalette.Background,
            _activePalette.Surface,
            _activePalette.Card,
            _activePalette.Control,
            _activePalette.Outline);
    }

    private FileSystemWatcher? _themeWatcher;
    private DispatcherTimer? _themeReloadTimer;

    // K-Series / K-Optimizer write theme.txt when the user picks a theme there; follow it while running.
    private void WatchThemeFile()
    {
        try
        {
            _themeReloadTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _themeReloadTimer.Tick += (_, _) =>
            {
                _themeReloadTimer!.Stop();
                try
                {
                    var id = File.ReadAllText(ThemeSettingsPath).Trim();
                    var palette = AccentPalettes.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (palette is not null && !palette.Id.Equals(_activePalette.Id, StringComparison.OrdinalIgnoreCase))
                        SelectAccentPalette(palette);
                }
                catch (IOException)
                {
                }
            };
            _themeWatcher = new FileSystemWatcher(Path.GetDirectoryName(ThemeSettingsPath)!, Path.GetFileName(ThemeSettingsPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };
            FileSystemEventHandler changed = (_, _) => Dispatcher.BeginInvoke(() =>
            {
                _themeReloadTimer!.Stop();
                _themeReloadTimer.Start();
            });
            _themeWatcher.Changed += changed;
            _themeWatcher.Created += changed;
            _themeWatcher.Renamed += (_, _) => changed(null!, null!);
            _themeWatcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            // No watcher: the theme is still read at start-up.
        }
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
        ThemeResources.Apply(
            palette.Primary,
            palette.Secondary,
            palette.Background,
            palette.Surface,
            palette.Card,
            palette.Control,
            palette.Outline);
        UpdateBackgroundParticleTheme();
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
            LauncherMessageBox.Show(
                string.Format(Localization["ThemeSaveFailed"], ex.Message),
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

    private void ApplyAccentPaletteToElement(DependencyObject element)
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
                    RecolorLocalBrush(border, Border.BackgroundProperty);
                    RecolorLocalBrush(border, Border.BorderBrushProperty);
                }
                break;
            case Control control:
                RecolorLocalBrush(control, Control.BackgroundProperty);
                RecolorLocalBrush(control, Control.ForegroundProperty);
                RecolorLocalBrush(control, Control.BorderBrushProperty);
                break;
            case TextBlock textBlock:
                RecolorLocalBrush(textBlock, TextBlock.ForegroundProperty);
                break;
            case Panel panel:
                RecolorLocalBrush(panel, Panel.BackgroundProperty);
                break;
            case System.Windows.Shapes.Shape shape:
                RecolorLocalBrush(shape, System.Windows.Shapes.Shape.FillProperty);
                RecolorLocalBrush(shape, System.Windows.Shapes.Shape.StrokeProperty);
                break;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            ApplyAccentPaletteToElement(VisualTreeHelper.GetChild(element, index));
    }

    private void ApplyAccentPaletteToAppCard(Border border)
    {
        var isSelected = border.DataContext is AppModel { IsBatchSelected: true } ||
                         _batchSelectionEntryCards.Values.Contains(border);
        ApplyListAppSelectionVisual(border, isSelected);

        var transform = GetMutableCardTransform(border);
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        transform.ScaleX = border.IsMouseOver ? 1.035 : 1;
        transform.ScaleY = border.IsMouseOver ? 1.035 : 1;
    }

    // XAML cards and rows carry a style bound to the K.* tokens (selection
    // included, through IsBatchSelected), so any local override is dropped.
    // Cards built in code point at the same tokens, so they never need a
    // repaint when the palette changes and no brush is allocated per card.
    private static void ApplyListAppSelectionVisual(Border border, bool selected)
    {
        if (border.Style is not null && border.DataContext is AppModel)
        {
            border.ClearValue(Border.BackgroundProperty);
            border.ClearValue(Border.BorderBrushProperty);
            return;
        }

        border.SetResourceReference(Border.BackgroundProperty, selected ? "K.AccentSoft" : "K.Card");
        border.SetResourceReference(Border.BorderBrushProperty, selected ? "K.AccentLine" : "K.Outline");
    }

    // Only touch colours that code assigned directly. Values coming from a
    // style, a template trigger or a K.* DynamicResource already follow the
    // theme; overwriting them with a local value would freeze hover/press
    // states and detach the element from future palette changes.
    private static void RecolorLocalBrush(DependencyObject element, DependencyProperty property)
    {
        var source = DependencyPropertyHelper.GetValueSource(element, property);
        if (source.BaseValueSource != BaseValueSource.Local || source.IsExpression || source.IsAnimated)
            return;

        var current = element.GetValue(property) as Brush;
        var recolored = RecolorBrush(current);
        if (!ReferenceEquals(current, recolored))
            element.SetValue(property, recolored);
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

        _appsView?.Refresh();
    }

    private bool FilterApps(object item)
    {
        if (item is not AppModel app || _appSearchQuery.Length == 0)
            return item is AppModel;

        return app.LocalizedName.Contains(_appSearchQuery, StringComparison.CurrentCultureIgnoreCase) ||
               app.Name.Contains(_appSearchQuery, StringComparison.CurrentCultureIgnoreCase);
    }

    private void MainSearchButton_Click(object sender, RoutedEventArgs e) =>
        SetMainSearchExpanded(true);

    private void MainSearchCloseButton_Click(object sender, RoutedEventArgs e) =>
        SetMainSearchExpanded(false);

    private void MainSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _appSearchQuery = MainSearchBox.Text.Trim();
        MainSearchHint.Visibility = MainSearchBox.Text.Length == 0 && MainSearchExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        _appsView?.Refresh();
    }

    private bool MainSearchExpanded { get; set; }

    private void SetMainSearchExpanded(bool expanded)
    {
        if (MainSearchExpanded == expanded)
            return;

        MainSearchExpanded = expanded;
        var widthAnimation = new DoubleAnimation
        {
            To = expanded ? Math.Min(360, Math.Max(36, ActualWidth - 480)) : 36,
            Duration = MotionSettings.Ms(230),
            EasingFunction = new CubicEase
            {
                EasingMode = expanded ? EasingMode.EaseOut : EasingMode.EaseIn
            }
        };
        MainSearchBorder.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);
        MainSearchBox.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        MainSearchCloseButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        MainSearchHint.Visibility = expanded && MainSearchBox.Text.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (expanded)
        {
            MainSearchBox.Opacity = 0;
            MainSearchBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, MotionSettings.Ms(160))
            {
                BeginTime = MotionSettings.Ms(70),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            MainSearchBox.Focus();
            return;
        }

        MainSearchBox.Clear();
        Keyboard.ClearFocus();
    }

    private void MainSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        SetMainSearchExpanded(false);
        e.Handled = true;
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
            _ = ResumeFromBackgroundAsync();
        }

        ResumeBackgroundParticles();
        if (IsWindowFadeActive)
            FadeWindowOpacity(1, 320);
        else
            ShowSurfaceOpaque();
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (!_startupFadeCompleted ||
            _isClosing ||
            _isMinimizing ||
            _isHiddenToTray ||
            _isContextMenuOpen ||
            OwnedWindows.Count > 0)
            return;

        // Once faded out the window shows nothing, so the particles rest.
        if (IsWindowFadeActive)
            FadeWindowOpacity(0, 240, PauseBackgroundParticles);
        else
            PauseBackgroundParticles();
    }

    private async void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (!_startupFadeCompleted || _isClosing)
            return;

        if (WindowState == WindowState.Minimized)
        {
            SuspendForBackground();
            return;
        }

        // Back before the deferred suspend ran: cancel it, nothing to rebuild.
        ++_suspendVersion;
        if (WindowState == WindowState.Normal && _isSuspendedForBackground)
            await ResumeFromBackgroundAsync();
    }

    // Minimizing releases the app cards and lowers the process priority; doing
    // that on every minimize made each quick restore rebuild every card on a
    // throttled UI thread (~130 ms busy), and taskbar clicks landing in that
    // window made Windows beep. The release now waits until the window has
    // stayed minimized for a moment; the tray still releases at once.
    private int _suspendVersion;

    private async void SuspendForBackground()
    {
        if (_isSuspendedForBackground)
            return;

        var version = ++_suspendVersion;
        if (!_isHiddenToTray)
        {
            await Task.Delay(1500);
            if (version != _suspendVersion ||
                _isSuspendedForBackground ||
                _isClosing ||
                _isHiddenToTray ||
                WindowState != WindowState.Minimized)
                return;
        }

        _viewModel.SuspendForBackground();
        RemoteIconLoads.Clear();
        _isSuspendedForBackground = true;
        // Drop the particle shapes entirely; they are rebuilt on restore.
        StopBackgroundParticles();
        BackgroundEfficiency.Enter();
    }

    private async Task ResumeFromBackgroundAsync()
    {
        if (!_isSuspendedForBackground)
            return;

        BackgroundEfficiency.Exit();
        await _viewModel.ResumeFromBackgroundAsync();
        UpdateLocalizedAppNames();
        ApplyAccentPaletteToWindow();
        _isSuspendedForBackground = false;
        StartBackgroundParticles();
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
            LauncherMessageBox.Show(
                this,
                string.Format(Localization["ThemeShortcutIconFailed"], ex.Message),
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
        // The fade-out minimizes from code, so Windows still treats the window
        // as the active one and the next taskbar click arrives as another
        // minimize. The default handler beeps on an already minimized window;
        // that click means "bring it back".
        if (message == WmSysCommand
            && (wParam.ToInt64() & 0xFFF0) == ScMinimize
            && WindowState == WindowState.Minimized)
        {
            handled = true;
            WindowState = WindowState.Normal;
            Activate();
            return IntPtr.Zero;
        }

        // A second taskbar click while the minimize fade is still running
        // means "keep it open": cancel the fade instead of letting Windows
        // minimize underneath it, which left the taskbar out of step and the
        // following click beeped.
        if (message == WmSysCommand
            && (wParam.ToInt64() & 0xFFF0) == ScMinimize
            && _isMinimizing
            && !_isHiddenToTray)
        {
            handled = true;
            _isMinimizing = false;
            FadeWindowOpacity(1, 200);
            return IntPtr.Zero;
        }

        if (message == WmSysCommand
            && (wParam.ToInt64() & 0xFFF0) == ScMinimize
            && !_isMinimizing
            && WindowState != WindowState.Minimized
            && IsWindowFadeActive)
        {
            handled = true;
            _isMinimizing = true;
            FadeWindowOpacity(0, 320, () =>
            {
                MinimizeLikeWindows();
                SuspendForBackground();
                RestoreSurfaceForTaskbarPreview();
            });
        }

        return IntPtr.Zero;
    }

    // "Pencere solması" setting; animations turned off in Motion also turn it off.
    private static bool IsWindowFadeActive => WindowFadeSettings.Enabled && MotionSettings.Enabled;

    // Ends any fade and shows the window fully opaque.
    private void ShowSurfaceOpaque()
    {
        ++_opacityAnimationVersion;
        MainSurface.BeginAnimation(UIElement.OpacityProperty, null);
        MainSurface.Opacity = 1;
    }

    private void FadeWindowOpacity(double target, int durationMilliseconds, Action? completed = null)
    {
        var version = ++_opacityAnimationVersion;
        var animation = new DoubleAnimation
        {
            From = MainSurface.Opacity,
            To = target,
            Duration = MotionSettings.Ms(durationMilliseconds),
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
            await _viewModel.RefreshAsync(forceScan: true);
            UpdateLocalizedAppNames();
            _ = WarmQuickLinkIconsAsync();
            return;
        }

        LauncherMessageBox.Show(
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
            Width = 270,
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
            Icon = MakeGlyph(Glyphs.Theme),
            MinWidth = 254,
            Padding = new Thickness(10, 9, 14, 9),
            Margin = new Thickness(0, 2, 0, 2)
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
            Icon = MakeGlyph(Glyphs.Language),
            MinWidth = 254,
            Padding = new Thickness(10, 9, 14, 9),
            Margin = new Thickness(0, 2, 0, 2)
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
            Icon = MakeGlyph(Glyphs.Plugin),
            MinWidth = 254,
            Padding = new Thickness(10, 9, 14, 9),
            Margin = new Thickness(0, 2, 0, 2)
        };
        pluginsItem.Click += (_, _) =>
        {
            pendingPanelSection = "Eklentiler";
            closeSettingsMenu?.Invoke();
        };
        menu.Items.Add(pluginsItem);

        var touchCodeItem = new MenuItem
        {
            Header = Localization["TouchMenu"],
            Icon = MakeGlyph(Glyphs.Code),
            MinWidth = 254,
            Padding = new Thickness(10, 9, 14, 9),
            Margin = new Thickness(0, 2, 0, 2)
        };
        touchCodeItem.Click += (_, _) =>
        {
            pendingPanelSection = "TouchCode";
            closeSettingsMenu?.Invoke();
        };
        menu.Items.Add(touchCodeItem);

        var startupItem = new MenuItem
        {
            Header = Localization["LaunchAtStartup"],
            IsCheckable = true,
            IsChecked = StartupRegistrationService.IsEnabled,
            Icon = MakeGlyph(Glyphs.Startup),
            MinWidth = 254,
            Padding = new Thickness(10, 9, 14, 9),
            Margin = new Thickness(0, 2, 0, 2)
        };
        startupItem.Click += (_, _) =>
        {
            try
            {
                StartupRegistrationService.SetEnabled(startupItem.IsChecked);
            }
            catch (Exception ex)
            {
                startupItem.IsChecked = !startupItem.IsChecked;
                LauncherMessageBox.Show(
                    this,
                    $"{Localization["StartupSettingError"]}\n\n{ex.Message}",
                    "K-LAUNCHER",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };
        menu.Items.Add(startupItem);

        var hiddenApps = _viewModel.HiddenApps;
        if (hiddenApps.Count > 0)
        {
            var hiddenAppsItem = new MenuItem
            {
                Header = $"{Localization["HiddenApps"]}  [{hiddenApps.Count}]",
                Icon = MakeGlyph(Glyphs.Hide)
            };
            var restoreAllItem = new MenuItem
            {
                Header = Localization["RestoreAllApps"],
                Icon = MakeGlyph(Glyphs.Restore)
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
                    Header = $"{Localization["RestoreApp"]}: {Localization.GetAppDisplayName(hiddenApp.Name)}",
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
                else if (pendingPanelSection == "TouchCode")
                    ShowTouchCodeWindow();
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

    private void ShowTouchCodeWindow()
    {
        var window = new TouchCodeWindow(
            key => Localization[key],
            Localization.LanguageCode.Equals("tr", StringComparison.OrdinalIgnoreCase))
        {
            Owner = this
        };
        window.Show();
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
            LauncherMessageBox.Show(
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
            LauncherMessageBox.Show(
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
            Text = Localization["AccentColors"],
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
            Text = Localization["SettingsInterface"].ToUpperInvariant(),
            Foreground = new SolidColorBrush(ThemeAccentColor),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, 16, 0, 8)
        });

        var interfaceButtons = new List<Button>();
        var interfaceSelectionMarks = new Dictionary<LauncherInterfaceMode, TextBlock>();
        var interfaceOptions = new[]
        {
            (Mode: LauncherInterfaceMode.Grid,
             Name: Localization["InterfaceGrid"],
             Description: Localization["InterfaceGridDescription"]),
            (Mode: LauncherInterfaceMode.List,
             Name: Localization["InterfaceList"],
             Description: Localization["InterfaceListDescription"]),
            (Mode: LauncherInterfaceMode.Overview,
             Name: Localization["InterfaceOverview"],
             Description: Localization["InterfaceOverviewDescription"])
        };

        foreach (var option in interfaceOptions)
        {
            var interfaceButton = new Button
            {
                Height = 60,
                Margin = new Thickness(0, 3, 0, 3),
                Padding = new Thickness(10, 5, 10, 5),
                Background = Brush("#14191A"),
                BorderBrush = new SolidColorBrush(
                    option.Mode == _interfaceMode ? ThemeAccentColor : ThemeOutlineColor),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand,
                Tag = option.Mode,
                Template = (ControlTemplate)FindResource("ThemePaletteButtonTemplate")
            };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = option.Name,
                Foreground = Brush("#F1F1F1"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            });
            text.Children.Add(new TextBlock
            {
                Text = option.Description,
                Foreground = Brush("#777780"),
                FontSize = 10,
                Margin = new Thickness(0, 3, 0, 0)
            });
            row.Children.Add(text);

            var selectedMark = new TextBlock
            {
                Text = option.Mode == _interfaceMode ? "✓" : string.Empty,
                Foreground = new SolidColorBrush(ThemeAccentColor),
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(selectedMark, 1);
            row.Children.Add(selectedMark);
            interfaceSelectionMarks[option.Mode] = selectedMark;
            interfaceButton.Content = row;
            interfaceButton.Click += (_, _) =>
            {
                SetInterfaceMode(option.Mode);
                foreach (var button in interfaceButtons)
                {
                    var selected = Equals(button.Tag, option.Mode);
                    button.BorderBrush = new SolidColorBrush(
                        selected ? ThemeAccentColor : ThemeOutlineColor);
                }

                foreach (var mark in interfaceSelectionMarks)
                {
                    mark.Value.Text = mark.Key == option.Mode ? "✓" : string.Empty;
                    mark.Value.Foreground = new SolidColorBrush(ThemeAccentColor);
                }
            };
            interfaceButtons.Add(interfaceButton);
            content.Children.Add(interfaceButton);
        }

        content.Children.Add(new TextBlock
        {
            Text = Localization.LanguageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                ? Localization["SettingsMotion"].ToUpper(new CultureInfo("tr-TR"))
                : Localization["SettingsMotion"].ToUpperInvariant(),
            Foreground = new SolidColorBrush(ThemeAccentColor),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, 16, 0, 8)
        });

        var motionButtons = new List<Button>();
        var motionGrid = new UniformGrid { Columns = 4, Rows = 1 };
        var motionOptions = new[]
        {
            (Speed: MotionSpeed.Fast, Name: Localization["MotionFast"]),
            (Speed: MotionSpeed.Normal, Name: Localization["MotionNormal"]),
            (Speed: MotionSpeed.Slow, Name: Localization["MotionSlow"]),
            (Speed: MotionSpeed.Off, Name: Localization["MotionOff"])
        };

        void RefreshMotionButtons()
        {
            foreach (var button in motionButtons)
            {
                var selected = Equals(button.Tag, MotionSettings.Current);
                button.BorderBrush = new SolidColorBrush(selected ? ThemeAccentColor : ThemeOutlineColor);
                if (button.Content is TextBlock label)
                {
                    label.Foreground = selected
                        ? new SolidColorBrush(ThemeAccentColor)
                        : Brush("#F1F1F1");
                    label.FontWeight = selected ? FontWeights.Bold : FontWeights.SemiBold;
                }
            }
        }

        foreach (var option in motionOptions)
        {
            var motionButton = new Button
            {
                Height = 40,
                Margin = new Thickness(0, 3, option.Speed == MotionSpeed.Off ? 0 : 6, 3),
                Padding = new Thickness(4),
                Background = Brush("#14191A"),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                Tag = option.Speed,
                Template = (ControlTemplate)FindResource("ThemePaletteButtonTemplate"),
                Content = new TextBlock
                {
                    Text = option.Name,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            motionButton.Click += (_, _) =>
            {
                MotionSettings.Set(option.Speed);
                RefreshMotionButtons();
            };
            motionButtons.Add(motionButton);
            motionGrid.Children.Add(motionButton);
        }

        RefreshMotionButtons();
        content.Children.Add(motionGrid);
        content.Children.Add(new TextBlock
        {
            Text = Localization["MotionDescription"],
            Foreground = Brush("#777780"),
            FontSize = 10,
            Margin = new Thickness(4, 4, 4, 0)
        });

        content.Children.Add(new TextBlock
        {
            Text = Localization.LanguageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                ? Localization["SettingsParticles"].ToUpper(new CultureInfo("tr-TR"))
                : Localization["SettingsParticles"].ToUpperInvariant(),
            Foreground = new SolidColorBrush(ThemeAccentColor),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, 16, 0, 8)
        });

        var particleButtons = new List<Button>();
        var particleGrid = new UniformGrid { Columns = 2, Rows = 1 };
        var particleOptions = new[]
        {
            (Enabled: true, Name: Localization["ParticlesOn"]),
            (Enabled: false, Name: Localization["MotionOff"])
        };

        void RefreshParticleButtons()
        {
            foreach (var button in particleButtons)
            {
                var selected = Equals(button.Tag, ParticleSettings.Enabled);
                button.BorderBrush = new SolidColorBrush(selected ? ThemeAccentColor : ThemeOutlineColor);
                if (button.Content is TextBlock label)
                {
                    label.Foreground = selected
                        ? new SolidColorBrush(ThemeAccentColor)
                        : Brush("#F1F1F1");
                    label.FontWeight = selected ? FontWeights.Bold : FontWeights.SemiBold;
                }
            }
        }

        foreach (var option in particleOptions)
        {
            var particleButton = new Button
            {
                Height = 40,
                Margin = new Thickness(0, 3, option.Enabled ? 6 : 0, 3),
                Padding = new Thickness(4),
                Background = Brush("#14191A"),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                Tag = option.Enabled,
                Template = (ControlTemplate)FindResource("ThemePaletteButtonTemplate"),
                Content = new TextBlock
                {
                    Text = option.Name,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            particleButton.Click += (_, _) =>
            {
                ParticleSettings.Set(option.Enabled);
                RefreshParticleButtons();
            };
            particleButtons.Add(particleButton);
            particleGrid.Children.Add(particleButton);
        }

        RefreshParticleButtons();
        content.Children.Add(particleGrid);
        content.Children.Add(new TextBlock
        {
            Text = Localization["ParticlesDescription"],
            Foreground = Brush("#777780"),
            FontSize = 10,
            Margin = new Thickness(4, 4, 4, 0)
        });

        content.Children.Add(new TextBlock
        {
            Text = Localization.LanguageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                ? Localization["SettingsWindowFade"].ToUpper(new CultureInfo("tr-TR"))
                : Localization["SettingsWindowFade"].ToUpperInvariant(),
            Foreground = new SolidColorBrush(ThemeAccentColor),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, 16, 0, 8)
        });

        var fadeButtons = new List<Button>();
        var fadeGrid = new UniformGrid { Columns = 2, Rows = 1 };

        void RefreshFadeButtons()
        {
            foreach (var button in fadeButtons)
            {
                var selected = Equals(button.Tag, WindowFadeSettings.Enabled);
                button.BorderBrush = new SolidColorBrush(selected ? ThemeAccentColor : ThemeOutlineColor);
                if (button.Content is TextBlock label)
                {
                    label.Foreground = selected
                        ? new SolidColorBrush(ThemeAccentColor)
                        : Brush("#F1F1F1");
                    label.FontWeight = selected ? FontWeights.Bold : FontWeights.SemiBold;
                }
            }
        }

        foreach (var option in particleOptions)
        {
            var fadeButton = new Button
            {
                Height = 40,
                Margin = new Thickness(0, 3, option.Enabled ? 6 : 0, 3),
                Padding = new Thickness(4),
                Background = Brush("#14191A"),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                Tag = option.Enabled,
                Template = (ControlTemplate)FindResource("ThemePaletteButtonTemplate"),
                Content = new TextBlock
                {
                    Text = option.Name,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            fadeButton.Click += (_, _) =>
            {
                WindowFadeSettings.Set(option.Enabled);
                ShowSurfaceOpaque();
                RefreshFadeButtons();
            };
            fadeButtons.Add(fadeButton);
            fadeGrid.Children.Add(fadeButton);
        }

        RefreshFadeButtons();
        content.Children.Add(fadeGrid);
        content.Children.Add(new TextBlock
        {
            Text = Localization["WindowFadeDescription"],
            Foreground = Brush("#777780"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 4, 4, 0)
        });

        var openLogButton = new Button
        {
            Height = 36,
            Margin = new Thickness(0, 16, 0, 3),
            Padding = new Thickness(4),
            Background = Brush("#14191A"),
            BorderBrush = new SolidColorBrush(ThemeOutlineColor),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Template = (ControlTemplate)FindResource("ThemePaletteButtonTemplate"),
            Content = new TextBlock
            {
                Text = Localization["OpenErrorLog"],
                FontSize = 12,
                Foreground = Brush("#F1F1F1"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        openLogButton.Click += (_, _) =>
        {
            try
            {
                var logPath = LauncherLog.FilePath;
                Process.Start(new ProcessStartInfo("explorer.exe",
                    File.Exists(logPath) ? $"/select,\"{logPath}\"" : $"\"{Path.GetDirectoryName(logPath)}\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LauncherLog.Error("Open log", ex);
            }
        };
        content.Children.Add(openLogButton);

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        ConfigureSmoothVerticalScroll(scroll);
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
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, MotionSettings.Ms(180)));
            panelTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -48, MotionSettings.Ms(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
            var closeTimer = new DispatcherTimer { Interval = MotionSettings.Ms(190) };
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
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, MotionSettings.Ms(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            panelTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-48, 0, MotionSettings.Ms(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        };
        DialogChrome.Attach(panel, animateOpen: false, animateClose: false);
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

        var searchBorder = new Border
        {
            Width = 34,
            Height = 32,
            Margin = new Thickness(0, 4, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Brush("#18181F"),
            BorderBrush = Brush("#45454E"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true
        };
        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var searchGlyph = new Grid { Width = 20, Height = 20 };
        searchGlyph.Children.Add(new Ellipse
        {
            Width = 12,
            Height = 12,
            Margin = new Thickness(2, 1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Stroke = Brush("#00FF66"),
            StrokeThickness = 1.7
        });
        searchGlyph.Children.Add(new PathShape
        {
            Data = Geometry.Parse("M 11,11 L 17,17"),
            Stroke = Brush("#00FF66"),
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        var searchButton = new Button
        {
            Content = searchGlyph,
            Width = 32,
            Height = 30,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Brush("#00FF66"),
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = 20,
            Cursor = Cursors.Hand,
            ToolTip = Localization["SearchLanguages"]
        };
        var searchBox = new TextBox
        {
            MinWidth = 0,
            Height = 28,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(4, 2, 4, 2),
            Background = Brushes.Transparent,
            Foreground = Brush("#F1F1F1"),
            CaretBrush = Brush("#00FF66"),
            BorderThickness = new Thickness(0),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        var searchHint = new TextBlock
        {
            Text = Localization["SearchHint"],
            Foreground = Brush("#777780"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        var closeSearchButton = new Button
        {
            Content = "×",
            Width = 26,
            Height = 28,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Brush("#909098"),
            BorderThickness = new Thickness(0),
            FontSize = 18,
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(searchBox, 1);
        Grid.SetColumn(searchHint, 1);
        Grid.SetColumn(closeSearchButton, 2);
        searchGrid.Children.Add(searchButton);
        searchGrid.Children.Add(searchBox);
        searchGrid.Children.Add(searchHint);
        searchGrid.Children.Add(closeSearchButton);
        searchBorder.Child = searchGrid;
        languageItems.Children.Add(searchBorder);

        var languageButtons = new List<(LanguageOption Language, Button Button)>();
        var isSearchExpanded = false;

        void FilterLanguages()
        {
            var query = searchBox.Text.Trim();
            foreach (var (language, button) in languageButtons)
            {
                var matches = query.Length == 0 ||
                    language.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    language.Code.Contains(query, StringComparison.OrdinalIgnoreCase);
                button.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        void SetSearchExpanded(bool expanded)
        {
            if (isSearchExpanded == expanded)
                return;

            isSearchExpanded = expanded;
            searchHint.Visibility = expanded && searchBox.Text.Length == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            var widthAnimation = new DoubleAnimation
            {
                To = expanded ? 174 : 34,
                Duration = MotionSettings.Ms(230),
                EasingFunction = new CubicEase
                {
                    EasingMode = expanded ? EasingMode.EaseOut : EasingMode.EaseIn
                }
            };
            searchBorder.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);

            searchBox.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            closeSearchButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            if (expanded)
            {
                searchBox.Opacity = 0;
                searchBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, MotionSettings.Ms(160))
                {
                    BeginTime = MotionSettings.Ms(70),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                searchBox.Focus();
            }
            else
            {
                searchBox.Clear();
                FilterLanguages();
            }
        }

        searchButton.Click += (_, _) =>
        {
            if (!isSearchExpanded)
                SetSearchExpanded(true);
        };
        closeSearchButton.Click += (_, _) => SetSearchExpanded(false);
        searchBox.TextChanged += (_, _) =>
        {
            searchHint.Visibility = searchBox.Text.Length == 0 && isSearchExpanded
                ? Visibility.Visible
                : Visibility.Collapsed;
            FilterLanguages();
        };

        var isClosing = false;
        void CloseLanguageMenu()
        {
            if (isClosing)
                return;

            isClosing = true;
            foreach (var child in languageItems.Children.OfType<FrameworkElement>())
            {
                child.BeginAnimation(OpacityProperty, new DoubleAnimation(child.Opacity, 0, MotionSettings.Ms(100))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });
                if (child.RenderTransform is TranslateTransform childTransform)
                    childTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -22, MotionSettings.Ms(120))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    });
            }
            var close = new DoubleAnimation(1, 0, MotionSettings.Ms(150));
            close.Completed += (_, _) => menuWindow.Close();
            menuWindow.BeginAnimation(OpacityProperty, close);
            if (root.RenderTransform is TranslateTransform rootTransform)
                rootTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -42, MotionSettings.Ms(170))
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

            languageButtons.Add((language, item));
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
            MaxHeight = languageHeight - 10,
            CanContentScroll = false,
            PanningMode = PanningMode.VerticalOnly
        };
        ConfigureSmoothVerticalScroll(scroll);
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
        menuWindow.Closed += (_, _) => StopSmoothScroll(scroll);
        menuWindow.Loaded += (_, _) =>
        {
            menuWindow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, MotionSettings.Ms(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            if (root.RenderTransform is TranslateTransform rootTransform)
                rootTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-42, 0, MotionSettings.Ms(270))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            var index = 0;
            foreach (var child in languageItems.Children.OfType<FrameworkElement>().Skip(3))
            {
                var delay = MotionSettings.Ms(index++ * 18);
                child.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, MotionSettings.Ms(150))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                if (child.RenderTransform is TranslateTransform childTransform)
                    childTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-24, 0, MotionSettings.Ms(210))
                    {
                        BeginTime = delay,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
        };
        DialogChrome.Attach(menuWindow, animateOpen: false, animateClose: false);
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
            soon.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, MotionSettings.Ms(100))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
            contentTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -28, MotionSettings.Ms(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });

            var exitAnimation = new DoubleAnimation(0, -56, MotionSettings.Ms(180))
            {
                BeginTime = MotionSettings.Ms(70),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            exitAnimation.Completed += (_, _) => panel.Close();
            if (root.RenderTransform is TranslateTransform rootTransform)
                rootTransform.BeginAnimation(TranslateTransform.XProperty, exitAnimation);

            panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, MotionSettings.Ms(220))
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
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, MotionSettings.Ms(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            if (root.RenderTransform is TranslateTransform transform)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-56, 0, MotionSettings.Ms(340))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }
        };

        DialogChrome.Attach(panel, animateOpen: false, animateClose: false);
        panel.Show();
    }

    // Built on click and dropped on close, so the notes cost nothing until opened.
    private void ReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = Localization["ReleaseNotesTitle"],
            Width = 560,
            Height = 560,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent
        };

        var root = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 18, 22, 18)
        };
        root.SetResourceReference(Border.BackgroundProperty, "K.Bg");
        root.SetResourceReference(Border.BorderBrushProperty, "K.OutlineStrong");

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = Localization["ReleaseNotesTitle"],
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "K.Accent");
        title.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ChangedButton == MouseButton.Left)
                dialog.DragMove();
        };
        Grid.SetRow(title, 0);
        layout.Children.Add(title);

        var notes = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        foreach (var line in Localization["ReleaseNotesText"].Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = new TextBlock
                {
                    Text = line[3..].ToUpper(Localization.LanguageCode == "tr"
                        ? new CultureInfo("tr-TR")
                        : CultureInfo.InvariantCulture),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, notes.Children.Count == 0 ? 0 : 14, 0, 6)
                };
                heading.SetResourceReference(TextBlock.ForegroundProperty, "K.Accent");
                notes.Children.Add(heading);
                continue;
            }

            var bullet = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            bullet.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            bullet.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dot = new TextBlock { Text = "•", FontSize = 13 };
            dot.SetResourceReference(TextBlock.ForegroundProperty, "K.Accent");
            var text = new TextBlock
            {
                Text = line,
                FontSize = 13,
                LineHeight = 19,
                TextWrapping = TextWrapping.Wrap
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "K.Text");
            Grid.SetColumn(text, 1);
            bullet.Children.Add(dot);
            bullet.Children.Add(text);
            notes.Children.Add(bullet);
        }

        var scroll = new ScrollViewer
        {
            Content = notes,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        ConfigureSmoothVerticalScroll(scroll);
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);

        var close = new Button
        {
            Content = Localization["Ok"],
            Width = 100,
            Height = 34,
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)FindResource("K.Button.Primary"),
            IsDefault = true,
            IsCancel = true
        };
        close.Click += (_, _) => dialog.Close();
        Grid.SetRow(close, 2);
        layout.Children.Add(close);

        root.Child = layout;
        dialog.Content = root;
        dialog.Closed += (_, _) => StopSmoothScroll(scroll);
        DialogChrome.Attach(dialog);
        dialog.ShowDialog();
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

        // A Grid (not a StackPanel) so the list gets the leftover height and
        // actually scrolls instead of growing past the window.
        var content = new Grid { Margin = new Thickness(22, 18, 22, 18) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var detectedHeading = new TextBlock
        {
            Text = Localization["AutoDetected"],
            Foreground = Brush("#00FF66"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(detectedHeading, 0);
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
        ConfigureSmoothVerticalScroll(scroll);
        Grid.SetRow(scroll, 1);
        content.Children.Add(scroll);

        // Where the launcher copies itself on first start; click opens it.
        var installPanel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var installHeading = new TextBlock
        {
            Text = Localization["InstallLocationHeading"],
            Foreground = Brush("#00FF66"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var installDescription = new TextBlock
        {
            Text = Localization["InstallLocationDescription"],
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#D5D5DC"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var installPath = new TextBlock
        {
            Text = AppInstallationBootstrapper.InstallationDirectory,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#00FF66"),
            FontSize = 12,
            TextDecorations = TextDecorations.Underline,
            Cursor = Cursors.Hand,
            ToolTip = Localization["InstallLocationOpen"]
        };
        installPath.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppInstallationBootstrapper.InstallationDirectory}\"")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // Opening the folder is a convenience; the path stays readable.
            }
        };
        installPanel.Children.Add(installHeading);
        installPanel.Children.Add(installDescription);
        installPanel.Children.Add(installPath);
        Grid.SetRow(installPanel, 2);
        content.Children.Add(installPanel);

        var closeButton = new Button
        {
            Content = Localization["Ok"],
            Width = 100,
            Height = 34,
            Margin = new Thickness(0, 18, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)FindResource("K.Button.Primary"),
            IsDefault = true
        };
        Grid.SetRow(closeButton, 3);
        content.Children.Add(closeButton);

        void UpdateInformationText()
        {
            details.Title = Localization["InfoTitle"];
            title.Text = Localization["InfoTitle"];
            detectedHeading.Text = Localization["AutoDetected"];
            installHeading.Text = Localization["InstallLocationHeading"];
            installDescription.Text = Localization["InstallLocationDescription"];
            installPath.ToolTip = Localization["InstallLocationOpen"];
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
                Duration = MotionSettings.Ms(durationMilliseconds),
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
        DialogChrome.Attach(details, animateOpen: false, animateClose: false);
        details.ShowDialog();
    }

    private void Minimize_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!IsWindowFadeActive)
        {
            MinimizeLikeWindows();
            return;
        }

        _isMinimizing = true;
        FadeWindowOpacity(0, 320, () =>
        {
            MinimizeLikeWindows();
            SuspendForBackground();
            RestoreSurfaceForTaskbarPreview();
        });
    }

    // Minimize the way the taskbar does: SW_MINIMIZE also hands activation to
    // the next window. Setting WindowState from code left this window active
    // while minimized, so the next taskbar click arrived as another minimize
    // (Windows beeped) and the window stayed faded out.
    private void MinimizeLikeWindows()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            WindowState = WindowState.Minimized;
            return;
        }

        const int SwMinimize = 6;
        ShowWindow(handle, SwMinimize);
    }

    private void Close_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        HideToTray();
    }

    private ContextMenu CreateTrayMenu()
    {
        var menu = new ContextMenu
        {
            Background = Brush("#D215151B"),
            Foreground = Brush("#F1F1F1"),
            BorderBrush = Brush("#FF00FF66"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            HasDropShadow = false,
            StaysOpen = false
        };
        menu.Style = (Style)FindResource("LauncherContextMenuStyle");
        menu.Resources[typeof(MenuItem)] = (Style)FindResource("LauncherContextMenuItemStyle");

        _trayOpenMenuItem = new MenuItem
        {
            Header = Localization["ShowLauncher"],
            Icon = MakeGlyph(Glyphs.Home)
        };
        _trayOpenMenuItem.Click += (_, _) => RestoreFromTray();
        menu.Items.Add(_trayOpenMenuItem);

        _trayExitMenuItem = new MenuItem
        {
            Header = Localization["ExitLauncher"],
            Icon = MakeGlyph(Glyphs.Exit)
        };
        _trayExitMenuItem.Click += (_, _) =>
        {
            _isClosing = true;
            _allowWindowClose = true;
            Close();
            Application.Current.Shutdown();
        };
        menu.Items.Add(_trayExitMenuItem);
        ConfigureTrayMenuAnimation(menu);
        return menu;
    }

    // The tray menu closes when it loses focus (StaysOpen=false), so only the
    // opening is animated: the menu starts hidden and the shared menu
    // animation plays when it opens.
    private static void ConfigureTrayMenuAnimation(ContextMenu menu)
    {
        void Prepare()
        {
            var target = GetContextMenuAnimationTarget(menu);
            var transform = new TransformGroup();
            transform.Children.Add(new ScaleTransform());
            transform.Children.Add(new RotateTransform());
            transform.Children.Add(new TranslateTransform());
            target.RenderTransformOrigin = new Point(0.5, 0);
            target.RenderTransform = transform;
            target.BeginAnimation(UIElement.OpacityProperty, null);
            target.Opacity = 0;

            foreach (var name in new[] { "MenuSurface", "MenuOutline" })
            {
                if (menu.Template?.FindName(name, menu) is UIElement frame)
                {
                    frame.BeginAnimation(UIElement.OpacityProperty, null);
                    frame.Opacity = 0;
                }
            }

            foreach (var item in menu.Items.OfType<UIElement>())
            {
                item.BeginAnimation(UIElement.OpacityProperty, null);
                item.Opacity = 0;
            }
        }

        Prepare();
        menu.Closed += (_, _) => Prepare();
        menu.Opened += (_, _) => menu.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => AnimateContextMenu(menu)));
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowWindowClose || Application.Current.Dispatcher.HasShutdownStarted)
        {
            StopBackgroundParticles();
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        if (_isHiddenToTray || _isClosing)
            return;

        _isMinimizing = true;
        FadeWindowOpacity(0, 180, () =>
        {
            try
            {
                _systemTrayIcon?.Show();
                _isHiddenToTray = true;
                ShowInTaskbar = false;
                Hide();
                SuspendForBackground();
                RestoreSurfaceForTaskbarPreview();
                _isMinimizing = false;
            }
            catch (Exception ex)
            {
                _isMinimizing = false;
                RestoreSurfaceForTaskbarPreview();
                FadeWindowOpacity(1, 180);
                LauncherMessageBox.Show(
                    this,
                    ex.Message,
                    "K-LAUNCHER",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        });
    }

    // Called when K-Launcher is started again while it is already running:
    // bring this window back instead of letting the second start show a box.
    internal void BringToFront()
    {
        if (_isClosing)
            return;

        if (_isHiddenToTray)
        {
            RestoreFromTray();
            return;
        }

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private async void RestoreFromTray()
    {
        if (!_isHiddenToTray)
            return;

        _systemTrayIcon?.Hide();
        _isHiddenToTray = false;
        ShowInTaskbar = true;
        MainSurface.BeginAnimation(UIElement.OpacityProperty, null);
        MainSurface.Opacity = 0;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        await ResumeFromBackgroundAsync();
        FadeWindowOpacity(1, 240);
    }

    private void Card_MouseEnter(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border)
            return;

        AnimateCardScale(border, 1.035, 140);
        AnimateCardHover(border, visible: true);
    }

    // Cards carry a "CardHover" layer (accent wash + ring) as the first child
    // of their content grid; fading it is cheaper than animating brushes.
    private static void AnimateCardHover(Border card, bool visible)
    {
        if (card.Child is not Panel { Children.Count: > 0 } panel ||
            panel.Children[0] is not Border { Tag: "CardHover" } hover)
            return;

        hover.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation
            {
                To = visible ? 1 : 0,
                Duration = MotionSettings.Ms(visible ? 140 : 200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void AppCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border)
            return;

        if (_batchSelectionEntryCards.Values.Contains(border))
        {
            ApplyListAppSelectionVisual(border, selected: true);
            return;
        }

        ApplyListAppSelectionVisual(
            border,
            border.DataContext is AppModel { IsBatchSelected: true });
    }

    private void Card_MouseLeave(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border)
            return;

        AnimateCardScale(border, 1, 120);
        AnimateCardHover(border, visible: false);
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
            Duration = MotionSettings.Ms(durationMilliseconds),
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
            Duration = MotionSettings.Ms(190)
        };

        animation.KeyFrames.Add(new EasingDoubleKeyFrame(transform.ScaleX)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero)
        });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0.965)
        {
            KeyTime = KeyTime.FromTimeSpan(MotionSettings.Ms(55)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1.055)
        {
            KeyTime = KeyTime.FromTimeSpan(MotionSettings.Ms(120)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(finalScale)
        {
            KeyTime = KeyTime.FromTimeSpan(MotionSettings.Ms(190)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation, HandoffBehavior.SnapshotAndReplace);

        var brush = GetMutableCardBrush(border, isBackground: false);
        var styled = border.Style is not null;
        var endColor = !styled && border.IsMouseOver ? CardHoverBorderColor : CardBorderColor;
        var pulse = new ColorAnimationUsingKeyFrames
        {
            Duration = MotionSettings.Ms(190)
        };
        if (styled)
        {
            // Hand the border back to the style (and its live theme tokens).
            pulse.Completed += (_, _) =>
            {
                if (ReferenceEquals(border.BorderBrush, brush))
                    border.ClearValue(Border.BorderBrushProperty);
            };
        }
        pulse.KeyFrames.Add(new EasingColorKeyFrame(brush.Color)
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero)
        });
        pulse.KeyFrames.Add(new EasingColorKeyFrame(CardHoverBorderColor)
        {
            KeyTime = KeyTime.FromTimeSpan(MotionSettings.Ms(65))
        });
        pulse.KeyFrames.Add(new EasingColorKeyFrame(endColor)
        {
            KeyTime = KeyTime.FromTimeSpan(MotionSettings.Ms(190)),
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

    private void ListApp_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
            AnimateCardScale(border, 1.012, 120);
    }

    private void ListApp_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
            AnimateCardScale(border, 1, 120);
    }

    private void ListApp_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (_isContextMenuOpen || sender is not Border border ||
            border.DataContext is not AppModel app)
            return;

        if (_isBatchSelectionMode)
        {
            ToggleBatchSelection(app, border);
            return;
        }

        Launch(app.LaunchTarget, app.LaunchArguments, app.Name);
    }

    private async void ListApp_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (_isContextMenuOpen || sender is not Border border ||
            border.DataContext is not AppModel app)
            return;

        await _viewModel.LoadDetailsAsync(app);
        ShowListDetails(app);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || _interfaceMode != LauncherInterfaceMode.List ||
            _listSelectedApp is not AppModel app || _isContextMenuOpen ||
            Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                Launch(app.LaunchTarget, app.LaunchArguments, app.Name);
                e.Handled = true;
                break;

            case Key.A when File.Exists(app.LaunchTarget) &&
                            Path.GetExtension(app.LaunchTarget).Equals(".exe", StringComparison.OrdinalIgnoreCase):
                Launch(app.LaunchTarget, app.LaunchArguments, app.Name, runAsAdministrator: true);
                e.Handled = true;
                break;

            case Key.F when !string.IsNullOrWhiteSpace(app.ExePath) &&
                           Path.GetExtension(app.ExePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                           File.Exists(app.ExePath):
                OpenInstallFolder(app.ExePath);
                e.Handled = true;
                break;

            case Key.H:
                _viewModel.HideApplication(app);
                ClearListDetails();
                e.Handled = true;
                break;

            case Key.C when !HiddenContentStore.IsHidden(app.Name, app.LaunchTarget):
                ShowContentVisibilityDialog(app);
                e.Handled = true;
                break;

            case Key.Add or Key.OemPlus:
                AddQuickLink(app);
                ShowListDetails(app);
                e.Handled = true;
                break;

            case Key.Subtract or Key.OemMinus when app.QuickLinks.Count > 0:
                RemoveQuickLinks(app);
                ShowListDetails(app);
                e.Handled = true;
                break;
        }
    }

    private void ShowListDetails(AppModel app)
    {
        _listSelectedApp = app;
        var contentsHidden = HiddenContentStore.IsHidden(app.Name, app.LaunchTarget);
        ApplyInterfaceMode();
        ListDetailsPanel.BeginAnimation(UIElement.OpacityProperty, null);
        ListDetailsPanel.Opacity = 0;
        if (ListDetailsPanel.RenderTransform is TranslateTransform initialTransform)
        {
            initialTransform.BeginAnimation(TranslateTransform.XProperty, null);
            initialTransform.X = -48;
        }
        ListDetailsItemsHost.Children.Clear();
        ListDetailsActionsHost.Children.Clear();
        ListDetailsActionsScrollViewer.ScrollToHorizontalOffset(0);

        ListDetailsActionsHost.Children.Add(CreateListActionButton(
            Localization["Launch"],
            Glyphs.Play,
            "#00FF66",
            () => Launch(app.LaunchTarget, app.LaunchArguments, app.Name)));

        if (IsStoreLauncherApp(app))
        {
            ListDetailsActionsHost.Children.Add(CreateListActionButton(
                Localization["StoreInstallByName"],
                Glyphs.Download,
                "#FFBE40",
                () => ShowStoreGameNameDialog(app)));

            ListDetailsActionsHost.Children.Add(CreateListActionButton(
                Localization["StoreBatchInstall"],
                Glyphs.Batch,
                "#00C8FF",
                () => ShowStoreBatchInstallDialog(app)));
        }

        ListDetailsActionsHost.Children.Add(CreateListActionButton(
            Localization["AddQuickLink"],
            Glyphs.Link,
            "#00FF66",
            () =>
            {
                AddQuickLink(app);
                ShowListDetails(app);
            }));

        ListDetailsActionsHost.Children.Add(CreateListActionButton(
            contentsHidden
                ? Localization["ShowContents"]
                : Localization["SelectContents"],
            contentsHidden ? Glyphs.View : Glyphs.Checklist,
            contentsHidden ? "#00FF66" : "#B7BEC3",
            () =>
            {
                if (contentsHidden)
                {
                    HiddenContentStore.SetHidden(app.Name, app.LaunchTarget, false);
                    HiddenContentStore.ClearItems(app.Name, app.LaunchTarget);
                    ShowListDetails(app);
                    return;
                }

                ShowContentVisibilityDialog(app);
            }));

        if (app.QuickLinks.Count > 0)
        {
            ListDetailsActionsHost.Children.Add(CreateListActionButton(
                Localization["RemoveQuickLink"],
                Glyphs.Remove,
                "#FF6B75",
                () =>
                {
                    RemoveQuickLinks(app);
                    ShowListDetails(app);
                }));
        }

        if (File.Exists(app.LaunchTarget) &&
            Path.GetExtension(app.LaunchTarget).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            ListDetailsActionsHost.Children.Add(CreateListActionButton(
                Localization["RunAsAdministrator"],
                Glyphs.Admin,
                "#FFBE40",
                () => Launch(
                    app.LaunchTarget,
                    app.LaunchArguments,
                    app.Name,
                    runAsAdministrator: true)));
        }

        if (!string.IsNullOrWhiteSpace(app.ExePath) &&
            Path.GetExtension(app.ExePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(app.ExePath))
        {
            ListDetailsActionsHost.Children.Add(CreateListActionButton(
                Localization["OpenInstallFolder"],
                Glyphs.Folder,
                "#B7BEC3",
                () => OpenInstallFolder(app.ExePath)));
        }

        ListDetailsActionsHost.Children.Add(CreateListActionButton(
            Localization["HideApp"],
            Glyphs.Hide,
            "#B7BEC3",
            () =>
            {
                _viewModel.HideApplication(app);
                ClearListDetails();
            }));

        if (app.IsUserAdded)
        {
            ListDetailsActionsHost.Children.Add(CreateListActionButton(
                Localization["RemoveApp"],
                Glyphs.Delete,
                "#FF6B75",
                () =>
                {
                    _viewModel.RemoveUserApplication(app);
                    ClearListDetails();
                }));
        }

        var entries = new List<OverviewEntry>();
        if (!contentsHidden)
        {
            foreach (var game in app.Games)
            {
                if (HiddenContentStore.IsItemHidden(
                        app.Name,
                        app.LaunchTarget,
                        "game",
                        game.Name,
                        game.LaunchTarget))
                    continue;

                entries.Add(new OverviewEntry(
                    game.Name,
                    game.IconPath,
                    game.ExePath,
                    Localization["Games"],
                    BuildBatchSelectionKey(app, "game", game.LaunchTarget),
                    () => Launch(game.LaunchTarget, game.LaunchArguments, game.Name),
                    game));
            }

            foreach (var link in app.QuickLinks)
            {
                if (HiddenContentStore.IsItemHidden(
                        app.Name,
                        app.LaunchTarget,
                        "link",
                        link.Name,
                        link.Url))
                    continue;

                entries.Add(new OverviewEntry(
                    link.Name,
                    GetQuickLinkIconPath(link),
                    string.Empty,
                    "↗",
                    BuildBatchSelectionKey(app, "link", link.Url),
                    () =>
                    {
                        if (IsBrowserApplication(app.Name) && File.Exists(app.ExePath))
                            Launch(app.ExePath, link.Url, app.Name);
                        else
                            Launch(link.Url, string.Empty);
                    }));
            }
        }

        ListDetailsTitle.Text = app.LocalizedName.Length > 0
            ? app.LocalizedName
            : app.Name;

        var summaryParts = new List<string>();
        if (app.Games.Count > 0)
            summaryParts.Add($"{Localization["Games"]}: {app.Games.Count}");
        if (app.QuickLinks.Count > 0)
            summaryParts.Add($"{Localization["QuickLinks"]}: {app.QuickLinks.Count}");
        ListDetailsSubtitle.Text = contentsHidden
            ? Localization["ContentsHidden"]
            : summaryParts.Count > 0
            ? string.Join("   •   ", summaryParts)
            : Localization["InterfaceListNoEntries"];

        if (entries.Count == 0)
        {
            ListDetailsEmptyText.Text = contentsHidden
                ? Localization["ContentsHidden"]
                : Localization["InterfaceListNoEntries"];
            ListDetailsEmptyState.Visibility = Visibility.Visible;
            ListDetailsScrollViewer.Visibility = Visibility.Collapsed;
        }

        if (entries.Count > 0)
        {
            foreach (var entry in entries)
                ListDetailsItemsHost.Children.Add(CreateOverviewEntryCard(entry, () => { }, () => false));

            ListDetailsEmptyState.Visibility = Visibility.Collapsed;
            ListDetailsScrollViewer.Visibility = Visibility.Visible;
            ListDetailsScrollViewer.ScrollToTop();
            AnimateListDetailsCards(ListDetailsItemsHost);
        }

        ApplyAccentPaletteToElement(ListDetailsItemsHost);
        ApplyAccentPaletteToElement(ListDetailsActionsHost);

        if (ListDetailsPanel.RenderTransform is TranslateTransform detailsTransform)
        {
            detailsTransform.BeginAnimation(TranslateTransform.XProperty, null);
            detailsTransform.X = -48;
            ListDetailsPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, MotionSettings.Ms(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            detailsTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-48, 0, MotionSettings.Ms(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
    }

    private void ShowContentVisibilityDialog(AppModel app)
    {
        var entries = app.Games
            .Select(game => new ContentVisibilityEntry(
                "game",
                game.Name,
                game.LaunchTarget,
                game.IconPath,
                game.ExePath,
                Localization["Games"]))
            .Concat(app.QuickLinks.Select(link => new ContentVisibilityEntry(
                "link",
                link.Name,
                link.Url,
                GetQuickLinkIconPath(link),
                string.Empty,
                "↗")))
            .ToArray();

        var dialog = new Window
        {
            Title = Localization["ContentSelectionTitle"],
            Width = 540,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent
        };

        var root = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 20, 22, 18)
        };
        root.SetResourceReference(Border.BackgroundProperty, "K.Bg");
        root.SetResourceReference(Border.BorderBrushProperty, "K.OutlineStrong");

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = Localization["ContentSelectionTitle"],
            Foreground = new SolidColorBrush(ThemeAccentColor),
            FontWeight = FontWeights.Bold,
            FontSize = 16
        });
        heading.Children.Add(new TextBlock
        {
            Text = Localization["ContentSelectionHint"],
            Foreground = Brush("#777780"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        Grid.SetRow(heading, 0);
        layout.Children.Add(heading);

        var separator = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(ThemeOutlineColor),
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(separator, 1);
        layout.Children.Add(separator);

        var contentPanel = new StackPanel();
        var selectionRows = new List<(ContentVisibilityEntry Entry, CheckBox CheckBox)>();
        foreach (var entry in entries)
        {
            var checkBox = new CheckBox
            {
                IsChecked = HiddenContentStore.IsItemHidden(
                    app.Name,
                    app.LaunchTarget,
                    entry.Kind,
                    entry.Name,
                    entry.Target),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Foreground = new SolidColorBrush(ThemeAccentColor),
                Focusable = true
            };

            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            details.Children.Add(new TextBlock
            {
                Text = entry.Name,
                Foreground = Brush("#F1F1F1"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            });
            details.Children.Add(new TextBlock
            {
                Text = entry.Kind.Equals("game", StringComparison.OrdinalIgnoreCase)
                    ? Localization["Games"]
                    : Localization["QuickLinks"],
                Foreground = Brush("#777780"),
                FontSize = 10,
                Margin = new Thickness(0, 3, 0, 0)
            });

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(checkBox, 0);
            Grid.SetColumn(details, 1);
            row.Children.Add(checkBox);
            row.Children.Add(details);

            contentPanel.Children.Add(new Border
            {
                Background = Brush("#17171E"),
                BorderBrush = new SolidColorBrush(ThemeOutlineColor),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 9, 10, 9),
                Margin = new Thickness(0, 0, 0, 7),
                Child = row
            });
            selectionRows.Add((entry, checkBox));
        }

        if (entries.Length == 0)
        {
            contentPanel.Children.Add(new TextBlock
            {
                Text = Localization["NoContentsToSelect"],
                Foreground = Brush("#777780"),
                Margin = new Thickness(2, 8, 0, 0)
            });
        }

        var contentScroll = new ScrollViewer
        {
            Content = contentPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly
        };
        ConfigureSmoothVerticalScroll(contentScroll);
        Grid.SetRow(contentScroll, 2);
        layout.Children.Add(contentScroll);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button
        {
            Content = Localization["Cancel"],
            Width = 105,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("K.Button.Secondary")
        };
        cancel.Click += (_, _) => dialog.Close();

        var save = new Button
        {
            Content = Localization["Save"],
            Width = 105,
            Height = 36,
            Style = (Style)FindResource("K.Button.Primary"),
            IsDefault = true
        };
        save.Click += (_, _) =>
        {
            foreach (var row in selectionRows)
            {
                HiddenContentStore.SetItemHidden(
                    app.Name,
                    app.LaunchTarget,
                    row.Entry.Kind,
                    row.Entry.Name,
                    row.Entry.Target,
                    row.CheckBox.IsChecked == true);
            }

            dialog.Close();
            ShowListDetails(app);
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetRow(buttons, 3);
        layout.Children.Add(buttons);

        root.Child = layout;
        dialog.Content = root;
        DialogChrome.Attach(dialog);
        dialog.ShowDialog();
    }

    private void ListDetailsActionsScrollViewer_OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || viewer.ScrollableWidth <= 0)
            return;

        viewer.ScrollToHorizontalOffset(
            Math.Clamp(viewer.HorizontalOffset - e.Delta * 0.85, 0, viewer.ScrollableWidth));
        e.Handled = true;
    }

    private static Button CreateListActionButton(
        string text,
        string glyph,
        string foreground,
        Action action)
    {
        // Hover sheen, press dip and focus ring come from the shared button
        // style; the tone only picks which themed variant to use.
        var tone = ActionToneFromLegacyColor(foreground);
        var button = new Button
        {
            Height = 32,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(10, 0, 12, 0),
            ToolTip = text,
            Focusable = true
        };
        button.SetResourceReference(
            FrameworkElement.StyleProperty,
            tone switch
            {
                ActionTone.Primary => "K.Button.Primary",
                ActionTone.Danger => "K.Button.Danger",
                _ => "K.Button.Secondary"
            });

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = GlyphFont,
            FontWeight = FontWeights.Normal,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
            IsHitTestVisible = false
        };
        if (tone == ActionTone.Neutral)
            icon.SetResourceReference(TextBlock.ForegroundProperty, "K.TextMuted");
        content.Children.Add(icon);
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });
        button.Content = content;
        button.Click += (_, _) => action();
        return button;
    }

    private static string GetQuickLinkIconPath(QuickLink link)
    {
        if (QuickLinkIconStore.IsUsableIconPath(link.IconPath))
            return link.IconPath;

        return QuickLinkIconStore.GetCachedIconPath(link.Url) ?? string.Empty;
    }

    private static void AnimateListDetailsCards(Panel host)
    {
        var cards = host.Children
            .OfType<FrameworkElement>()
            .ToArray();

        for (var index = 0; index < cards.Length; index++)
        {
            var card = cards[index];
            var targetMargin = card.Margin;
            var startMargin = new Thickness(
                targetMargin.Left - 28,
                targetMargin.Top,
                targetMargin.Right + 28,
                targetMargin.Bottom);
            var delay = MotionSettings.Ms(index * 55);

            card.BeginAnimation(UIElement.OpacityProperty, null);
            card.BeginAnimation(FrameworkElement.MarginProperty, null);
            card.Opacity = 0;
            card.Margin = startMargin;
            card.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, MotionSettings.Ms(190))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            card.BeginAnimation(
                FrameworkElement.MarginProperty,
                new ThicknessAnimation(startMargin, targetMargin, MotionSettings.Ms(260))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }
    }

    private async void Card_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (_isContextMenuOpen)
            return;

        if (sender is not Border border)
            return;

        if (border.DataContext is not AppModel app)
            return;

        // Games and quick links are not part of the main screen data. Resolve
        // them only when the user actually opens an app's context menu.
        await _viewModel.LoadDetailsAsync(app);

        // Every app opens the showcase: its games and links, then its actions.
        if (_interfaceMode == LauncherInterfaceMode.Overview)
        {
            ShowOverviewPanel(app, border);
            return;
        }

        ShowContextMenu(app, border);
    }

    private void AnimateCardPressReverse(Border border)
    {
        var transform = GetMutableCardTransform(border);
        var animation = new DoubleAnimation
        {
            From = Math.Max(transform.ScaleX, 0.001),
            To = 1,
            Duration = MotionSettings.Ms(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation.Clone(), HandoffBehavior.SnapshotAndReplace);

        var brush = GetMutableCardBrush(border, isBackground: false);
        var styled = border.Style is not null;
        var endColor = !styled && border.IsMouseOver ? CardHoverBorderColor : CardBorderColor;
        var reverse = new ColorAnimation
        {
            From = brush.Color,
            To = endColor,
            Duration = MotionSettings.Ms(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        if (styled)
        {
            reverse.Completed += (_, _) =>
            {
                if (ReferenceEquals(border.BorderBrush, brush))
                    border.ClearValue(Border.BorderBrushProperty);
            };
        }
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

    private void ShowOverviewPanel(AppModel app, FrameworkElement target)
    {
        if (_isContextMenuOpen)
            return;

        var displayName = string.IsNullOrWhiteSpace(app.LocalizedName)
            ? Localization.GetAppDisplayName(app.Name)
            : app.LocalizedName;

        var entries = new List<OverviewEntry>();
        foreach (var game in app.Games)
        {
            if (HiddenContentStore.IsHidden(app.Name, app.LaunchTarget) ||
                HiddenContentStore.IsItemHidden(
                    app.Name,
                    app.LaunchTarget,
                    "game",
                    game.Name,
                    game.LaunchTarget))
                continue;

            entries.Add(new OverviewEntry(
                game.Name,
                game.IconPath,
                game.ExePath,
                Localization["Games"],
                BuildBatchSelectionKey(app, "game", game.LaunchTarget),
                    () => Launch(game.LaunchTarget, game.LaunchArguments, game.Name),
                    game));
        }

        foreach (var link in app.QuickLinks)
        {
            if (HiddenContentStore.IsHidden(app.Name, app.LaunchTarget) ||
                HiddenContentStore.IsItemHidden(
                    app.Name,
                    app.LaunchTarget,
                    "link",
                    link.Name,
                    link.Url))
                continue;

            entries.Add(new OverviewEntry(
                link.Name,
                GetQuickLinkIconPath(link),
                string.Empty,
                "↗",
                BuildBatchSelectionKey(app, "link", link.Url),
                () =>
                {
                    if (IsBrowserApplication(app.Name) && File.Exists(app.ExePath))
                        Launch(app.ExePath, link.Url, app.Name);
                    else
                        Launch(link.Url, string.Empty);
                }));
        }

        // Actions open dialogs or change the app list, so they run once the
        // panel has closed.
        Action? afterClose = null;
        void Act(Action action) => afterClose = action;
        void AddAction(string name, string glyph, string color, Action action) =>
            entries.Add(new OverviewEntry(
                name,
                string.Empty,
                string.Empty,
                string.Empty,
                $"action|{name}",
                () => Act(action),
                ActionGlyph: glyph,
                ActionColor: color));

        AddAction(
            Localization["Launch"],
            Glyphs.Play,
            "#00FF66",
            () => Launch(app.LaunchTarget, app.LaunchArguments, app.Name));

        if (IsStoreLauncherApp(app))
        {
            AddAction(
                Localization["StoreInstallByName"],
                Glyphs.Download,
                "#FFBE40",
                () => ShowStoreGameNameDialog(app));
            AddAction(
                Localization["StoreBatchInstall"],
                Glyphs.Batch,
                "#00C8FF",
                () => ShowStoreBatchInstallDialog(app));
        }

        if (File.Exists(app.LaunchTarget) &&
            Path.GetExtension(app.LaunchTarget).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            AddAction(
                Localization["RunAsAdministrator"],
                Glyphs.Admin,
                "#FFBE40",
                () => Launch(app.LaunchTarget, app.LaunchArguments, app.Name, runAsAdministrator: true));
        }

        AddAction(
            Localization["HideApp"],
            Glyphs.Hide,
            "#B7BEC3",
            () => _viewModel.HideApplication(app));

        var contentsHidden = HiddenContentStore.IsHidden(app.Name, app.LaunchTarget);
        AddAction(
            contentsHidden ? Localization["ShowContents"] : Localization["SelectContents"],
            contentsHidden ? Glyphs.View : Glyphs.Checklist,
            contentsHidden ? "#00FF66" : "#B7BEC3",
            () =>
            {
                if (contentsHidden)
                {
                    HiddenContentStore.SetHidden(app.Name, app.LaunchTarget, false);
                    HiddenContentStore.ClearItems(app.Name, app.LaunchTarget);
                }
                else
                {
                    ShowContentVisibilityDialog(app);
                }
            });

        if (app.IsUserAdded)
        {
            AddAction(
                Localization["RemoveApp"],
                Glyphs.Delete,
                "#FF6B75",
                () => _viewModel.RemoveUserApplication(app));
        }

        const double panelWidth = 780;
        const double panelHeight = 470;
        var workArea = SystemParameters.WorkArea;
        var targetPoint = target.PointToScreen(
            new Point(target.ActualWidth, target.ActualHeight / 2));
        var panel = new Window
        {
            Title = $"K-LAUNCHER // {displayName}",
            Width = panelWidth,
            Height = panelHeight,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = Math.Clamp(targetPoint.X - panelWidth / 2, workArea.Left, workArea.Right - panelWidth),
            Top = Math.Clamp(targetPoint.Y - panelHeight / 2, workArea.Top, workArea.Bottom - panelHeight),
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Opacity = 0
        };

        var panelTransform = new TranslateTransform(0, 22);
        var root = new Border
        {
            Tag = "OverviewPanel",
            Background = Brush("#101014"),
            BorderBrush = new SolidColorBrush(ThemeAccentColor),
            BorderThickness = new Thickness(1),
            RenderTransform = panelTransform
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Border
        {
            Background = Brush("#111116"),
            BorderBrush = Brush("#12311F"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 0, 12, 0)
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        headerText.Children.Add(new TextBlock
        {
            Text = $"{displayName.ToUpper()}  //  {Localization["InterfaceOverviewHeader"]}",
            Foreground = Brush("#F1F1F1"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        });
        headerText.Children.Add(new TextBlock
        {
            Text = Localization["InterfaceSwipeHint"],
            Foreground = new SolidColorBrush(ThemeAccentColor),
            FontSize = 9,
            Margin = new Thickness(0, 4, 0, 0)
        });
        headerGrid.Children.Add(headerText);

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

        var cards = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(24, 18, 24, 24)
        };
        var scroll = new ScrollViewer
        {
            Content = cards,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.HorizontalOnly,
            ClipToBounds = true
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);

        var closing = false;
        var dragStartPoint = new Point();
        var lastDragPoint = new Point();
        var lastDragSample = Stopwatch.GetTimestamp();
        var dragStartOffset = 0d;
        var dragVelocity = 0d;
        var dragCaptured = false;
        var dragMoved = false;

        void ClosePanel()
        {
            if (closing)
                return;

            closing = true;
            closeButton.IsEnabled = false;
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, MotionSettings.Ms(170)));
            panelTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 22, MotionSettings.Ms(170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
            var closeTimer = new DispatcherTimer { Interval = MotionSettings.Ms(180) };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                panel.Close();
            };
            closeTimer.Start();
        }

        scroll.PreviewMouseLeftButtonDown += (_, args) =>
        {
            StopSmoothScroll(scroll);
            dragStartPoint = args.GetPosition(scroll);
            lastDragPoint = dragStartPoint;
            lastDragSample = Stopwatch.GetTimestamp();
            dragStartOffset = scroll.HorizontalOffset;
            dragVelocity = 0;
            dragCaptured = false;
            dragMoved = false;
        };
        scroll.PreviewMouseMove += (_, args) =>
        {
            if (args.LeftButton != MouseButtonState.Pressed)
                return;

            var currentPoint = args.GetPosition(scroll);
            var deltaX = currentPoint.X - dragStartPoint.X;
            if (!dragCaptured && Math.Abs(deltaX) < 6)
                return;

            if (!dragCaptured)
            {
                dragCaptured = true;
                dragMoved = true;
                scroll.CaptureMouse();
                lastDragPoint = currentPoint;
                lastDragSample = Stopwatch.GetTimestamp();
            }
            else
            {
                var now = Stopwatch.GetTimestamp();
                var elapsedMs = Math.Max(
                    1,
                    (now - lastDragSample) * 1000d / Stopwatch.Frequency);
                var offsetDelta = -(currentPoint.X - lastDragPoint.X);
                var sampleVelocity = offsetDelta / elapsedMs;
                dragVelocity = (dragVelocity * 0.72) + (sampleVelocity * 0.28);
                lastDragPoint = currentPoint;
                lastDragSample = now;
            }

            scroll.ScrollToHorizontalOffset(dragStartOffset - deltaX);
            args.Handled = true;
        };
        scroll.PreviewMouseLeftButtonUp += (_, args) =>
        {
            if (!dragCaptured)
                return;

            scroll.ReleaseMouseCapture();
            dragCaptured = false;
            if (Math.Abs(dragVelocity) > 0.05)
            {
                var inertiaDistance = Math.Clamp(dragVelocity * 180, -260, 260);
                SmoothScrollTo(
                    scroll,
                    scroll.HorizontalOffset + inertiaDistance,
                    horizontal: true,
                    duration: MotionSettings.Ms(260));
            }
            args.Handled = true;
        };
        // Quick wheel notches add up instead of each restarting from the
        // offset reached so far.
        var wheelTarget = 0d;
        scroll.PreviewMouseWheel += (_, args) =>
        {
            args.Handled = true;
            var from = _smoothScrollHandlers.ContainsKey(scroll) ? wheelTarget : scroll.HorizontalOffset;
            wheelTarget = Math.Clamp(from - (args.Delta * 0.9), 0, scroll.ScrollableWidth);
            SmoothScrollTo(
                scroll,
                wheelTarget,
                horizontal: true,
                duration: MotionSettings.Ms(220));
        };
        // Touchpad side swipes and tilt wheels arrive as WM_MOUSEHWHEEL,
        // which WPF does not surface as an event.
        panel.SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(panel) is not HwndSource source)
                return;

            source.AddHook((IntPtr _, int message, IntPtr wParam, IntPtr _, ref bool handled) =>
            {
                const int WM_MOUSEHWHEEL = 0x020E;
                if (message != WM_MOUSEHWHEEL)
                    return IntPtr.Zero;

                var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                // The wheel reports physical direction; a mirrored row scrolls the other way.
                if (scroll.FlowDirection == FlowDirection.RightToLeft)
                    delta = (short)-delta;
                StopSmoothScroll(scroll);
                scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + delta);
                handled = true;
                return new IntPtr(1);
            });
        };

        foreach (var entry in entries)
            cards.Children.Add(CreateOverviewEntryCard(entry, ClosePanel, () => dragMoved));

        root.Child = layout;
        panel.Content = root;
        ApplyAccentPaletteToElement(root);

        closeButton.Click += (_, _) => ClosePanel();
        panel.Deactivated += (_, _) => ClosePanel();
        panel.Closed += (_, _) =>
        {
            StopSmoothScroll(scroll);
            var action = afterClose;
            afterClose = null;
            action?.Invoke();
        };
        panel.Closing += (_, _) => _isContextMenuOpen = false;
        panel.Loaded += (_, _) =>
        {
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, MotionSettings.Ms(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            panelTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(22, 0, MotionSettings.Ms(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        };

        _isContextMenuOpen = true;
        DialogChrome.Attach(panel, animateOpen: false, animateClose: false);
        panel.Show();
    }

    private Border CreateOverviewEntryCard(
        OverviewEntry entry,
        Action closePanel,
        Func<bool> wasDragged)
    {
        var card = new Border
        {
            Width = 220,
            Height = 300,
            Margin = new Thickness(8, 0, 8, 0),
            Tag = "AppCard",
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        card.SetResourceReference(Border.BackgroundProperty, "K.Card");
        card.SetResourceReference(Border.BorderBrushProperty, "K.Outline");
        var transform = new ScaleTransform(1, 1);
        card.RenderTransform = transform;

        var layout = new Grid { Margin = new Thickness(12) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });

        var iconFrame = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(20),
            Child = CreateOverviewEntryIcon(entry)
        };
        iconFrame.SetResourceReference(Border.BackgroundProperty, "K.Bg");
        Grid.SetRow(iconFrame, 0);
        layout.Children.Add(iconFrame);

        var title = new TextBlock
        {
            Text = entry.Name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 190,
            MaxHeight = 40
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "K.Text");
        Grid.SetRow(title, 1);
        layout.Children.Add(title);

        var hint = new TextBlock
        {
            // Actions only take a left click; the launch/menu hint would mislead.
            Text = entry.IsAction ? string.Empty : Localization["InterfaceLaunchHint"],
            FontSize = 10,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "K.TextFaint");
        Grid.SetRow(hint, 2);
        layout.Children.Add(hint);

        var hover = new Border
        {
            Tag = "CardHover",
            Margin = new Thickness(-1),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Opacity = 0,
            IsHitTestVisible = false
        };
        hover.SetResourceReference(Border.BackgroundProperty, "K.AccentSoft");
        hover.SetResourceReference(Border.BorderBrushProperty, "K.AccentLine");
        var cardContent = new Grid();
        cardContent.Children.Add(hover);
        cardContent.Children.Add(layout);
        card.Child = cardContent;
        if (_batchSelectedEntries.ContainsKey(entry.SelectionKey))
        {
            _batchSelectionEntryCards[entry.SelectionKey] = card;
            ApplyListAppSelectionVisual(card, selected: true);
        }
        card.Loaded += AppCard_Loaded;
        card.MouseEnter += Card_MouseEnter;
        card.MouseLeave += Card_MouseLeave;
        card.MouseLeftButtonUp += (_, args) =>
        {
            if (wasDragged())
            {
                args.Handled = true;
                return;
            }

            args.Handled = true;
            if (_isBatchSelectionMode && !entry.IsAction)
            {
                ToggleBatchSelection(entry, card);
                return;
            }

            closePanel();
            entry.Launch();
        };
        card.MouseRightButtonUp += (_, args) =>
        {
            args.Handled = true;
            if (wasDragged() || entry.StoreGame is null)
                return;

            ShowGameStoreMenu(entry.StoreGame, card);
        };
        return card;
    }

    private void ShowGameStoreMenu(GameItem game, Border target)
    {
        if (_isContextMenuOpen || !IsStoreGame(game))
            return;

        var menu = new ContextMenu
        {
            Background = Brush("#D215151B"),
            Foreground = Brush("#F1F1F1"),
            BorderBrush = new SolidColorBrush(ThemeAccentColor),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            PlacementTarget = target,
            StaysOpen = false,
            HasDropShadow = false
        };
        menu.Style = (Style)FindResource("LauncherContextMenuStyle");
        menu.Resources[typeof(MenuItem)] = (Style)FindResource("LauncherContextMenuItemStyle");

        menu.Items.Add(new MenuItem
        {
            Header = game.Name,
            IsEnabled = false,
            Foreground = Brush("#74747D"),
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(10, 8, 14, 8)
        });
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem(
            Localization["StoreMenuSelect"],
            Glyphs.Store,
            "#B7BEC3",
            async (_, _) => await RunStoreAutomationAsync(game, StoreLauncherAction.Select)));
        menu.Items.Add(MakeItem(
            Localization["StoreMenuInstall"],
            Glyphs.Download,
            "#FFBE40",
            async (_, _) => await RunStoreAutomationAsync(game, StoreLauncherAction.Install)));
        menu.Items.Add(MakeItem(
            Localization["StoreMenuUninstall"],
            Glyphs.Delete,
            "#FF6B75",
            async (_, _) => await RunStoreAutomationAsync(game, StoreLauncherAction.Uninstall)));

        ConfigureAnimatedContextMenu(menu, target);
    }

    private static FrameworkElement CreateOverviewEntryIcon(OverviewEntry entry)
    {
        if (entry.IsAction)
        {
            return new TextBlock
            {
                Text = entry.ActionGlyph,
                FontFamily = new FontFamily(Glyphs.FontFamily),
                Foreground = Brush(entry.ActionColor ?? "#B7BEC3"),
                FontSize = 64,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        ImageSource? source = null;
        var converter = new ShellIconConverter();
        if (!string.IsNullOrWhiteSpace(entry.IconPath))
        {
            source = converter.Convert(
                entry.IconPath,
                typeof(ImageSource),
                null,
                CultureInfo.InvariantCulture) as ImageSource;
        }

        if (source is null && !string.IsNullOrWhiteSpace(entry.FallbackPath))
        {
            source = converter.Convert(
                entry.FallbackPath,
                typeof(ImageSource),
                null,
                CultureInfo.InvariantCulture) as ImageSource;
        }

        if (source is not null)
        {
            return new Image
            {
                Source = source,
                Width = 112,
                Height = 112,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        return new TextBlock
        {
            Text = entry.FallbackGlyph,
            Foreground = new SolidColorBrush(ThemeAccentColor),
            FontSize = entry.FallbackGlyph == "↗" ? 64 : 22,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
    }

    private void ShowContextMenu(AppModel app, FrameworkElement target)
    {
        if (_isContextMenuOpen)
            return;

        var displayName = string.IsNullOrWhiteSpace(app.LocalizedName)
            ? Localization.GetAppDisplayName(app.Name)
            : app.LocalizedName;

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
            Header = $"{displayName.ToUpper()}  //  {Localization["QuickMenu"]}",
            IsEnabled = false,
            Foreground = Brush("#74747D"),
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(10, 8, 14, 8)
        };
        menu.Items.Add(header);
        menu.Items.Add(new Separator());

        menu.Items.Add(MakeItem(
            Localization["Launch"],
            Glyphs.Play,
            "#00FF66",
            (_, _) => Launch(app.LaunchTarget, app.LaunchArguments, app.Name)));

        if (IsStoreLauncherApp(app))
        {
            menu.Items.Add(MakeItem(
                Localization["StoreInstallByName"],
                Glyphs.Download,
                "#FFBE40",
                (_, _) => ShowStoreGameNameDialog(app)));

            menu.Items.Add(MakeItem(
                Localization["StoreBatchInstall"],
                Glyphs.Batch,
                "#00C8FF",
                (_, _) => ShowStoreBatchInstallDialog(app)));
        }

        if (File.Exists(app.LaunchTarget) &&
            Path.GetExtension(app.LaunchTarget).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            menu.Items.Add(MakeItem(
                Localization["RunAsAdministrator"],
                Glyphs.Admin,
                "#FFBE40",
                (_, _) => Launch(app.LaunchTarget, app.LaunchArguments, app.Name, runAsAdministrator: true)));
        }

        menu.Items.Add(MakeItem(
            Localization["HideApp"],
            Glyphs.Hide,
            "#B7BEC3",
            (_, _) => _viewModel.HideApplication(app)));

        var contentsHidden = HiddenContentStore.IsHidden(app.Name, app.LaunchTarget);
        menu.Items.Add(MakeItem(
            contentsHidden
                ? Localization["ShowContents"]
                : Localization["SelectContents"],
            contentsHidden ? Glyphs.View : Glyphs.Checklist,
            contentsHidden ? "#00FF66" : "#B7BEC3",
            (_, _) =>
            {
                if (contentsHidden)
                {
                    HiddenContentStore.SetHidden(app.Name, app.LaunchTarget, false);
                    HiddenContentStore.ClearItems(app.Name, app.LaunchTarget);
                }
                else
                {
                    ShowContentVisibilityDialog(app);
                }
            }));

        if (app.IsUserAdded)
        {
            menu.Items.Add(MakeItem(
                Localization["RemoveApp"],
                Glyphs.Delete,
                "#FF6B75",
                (_, _) => _viewModel.RemoveUserApplication(app)));
        }

        var visibleGames = app.Games
            .Where(game => !HiddenContentStore.IsItemHidden(
                app.Name,
                app.LaunchTarget,
                "game",
                game.Name,
                game.LaunchTarget))
            .ToArray();
        var visibleLinks = app.QuickLinks
            .Where(link => !HiddenContentStore.IsItemHidden(
                app.Name,
                app.LaunchTarget,
                "link",
                link.Name,
                link.Url))
            .ToArray();

        if (!contentsHidden && visibleGames.Length > 0)
        {
            menu.Items.Add(new Separator());

            var gamesHeader = new MenuItem
            {
                Header = $"{Localization["Games"]}  [{visibleGames.Length}]",
                IsEnabled = false,
                Foreground = Brush("#00FF66"),
                FontWeight = FontWeights.Bold
            };
            menu.Items.Add(gamesHeader);

            foreach (var game in visibleGames)
            {
                menu.Items.Add(CreateGameMenuItem(game));
            }
        }

        if (!contentsHidden && visibleLinks.Length > 0)
        {
            menu.Items.Add(new Separator());

            var linksHeader = new MenuItem
            {
                Header = $"{Localization["QuickLinks"]}  [{visibleLinks.Length}]",
                IsEnabled = false,
                Foreground = Brush("#00FF66"),
                FontWeight = FontWeights.Bold
            };
            menu.Items.Add(linksHeader);

            foreach (var link in visibleLinks)
            {
                var iconPath = GetQuickLinkIconPath(link);
                var item = new MenuItem
                {
                    Header = link.Name,
                    Tag = link,
                    Icon = string.IsNullOrWhiteSpace(iconPath)
                        ? MakeGlyph(Glyphs.OpenLink)
                        : MakeExeIcon(iconPath, string.Empty)
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
            Glyphs.Link,
            "#00FF66",
            (_, _) => AddQuickLink(app)));

        if (app.QuickLinks.Count > 0)
        {
            menu.Items.Add(MakeItem(
                Localization["RemoveQuickLink"],
                Glyphs.Remove,
                "#FF6B75",
                (_, _) => RemoveQuickLinks(app)));
        }

        if (!string.IsNullOrWhiteSpace(app.ExePath) &&
            Path.GetExtension(app.ExePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(app.ExePath))
        {
            menu.Items.Add(MakeItem(
                Localization["OpenInstallFolder"],
                Glyphs.Folder,
                "#B7BEC3",
                (_, _) => OpenInstallFolder(app.ExePath)));
        }

        menu.Items.Add(MakeItem(
            Localization["Rescan"],
            Glyphs.Refresh,
            "#B7BEC3",
            async (_, _) =>
            {
                await _viewModel.RefreshAsync(forceScan: true);
                UpdateLocalizedAppNames();
                _ = WarmQuickLinkIconsAsync();
            }));

        ConfigureAnimatedContextMenu(menu, target as Border);
    }

    private MenuItem CreateGameMenuItem(GameItem game)
    {
        var item = new MenuItem
        {
            Header = game.Name,
            Tag = game,
            Icon = MakeExeIcon(game.IconPath, game.ExePath)
        };

        if (!IsStoreGame(game))
        {
            item.Click += Game_Click;
            return item;
        }

        // Keep the normal game launch action, while exposing store actions that
        // operate the official Steam/Epic window by the visible game name.
        item.Items.Add(MakeItem(
            Localization["Launch"],
            Glyphs.Play,
            "#00FF66",
            (_, _) => Game_Click(item, new RoutedEventArgs())));
        item.Items.Add(MakeItem(
            Localization["StoreMenuSelect"],
            Glyphs.Store,
            "#B7BEC3",
            async (_, _) => await RunStoreAutomationAsync(game, StoreLauncherAction.Select)));
        item.Items.Add(MakeItem(
            Localization["StoreMenuInstall"],
            Glyphs.Download,
            "#FFBE40",
            async (_, _) => await RunStoreAutomationAsync(game, StoreLauncherAction.Install)));
        item.Items.Add(MakeItem(
            Localization["StoreMenuUninstall"],
            Glyphs.Delete,
            "#FF6B75",
            async (_, _) => await RunStoreAutomationAsync(game, StoreLauncherAction.Uninstall)));
        return item;
    }

    private static bool IsStoreLauncherApp(AppModel app) =>
        app.Name.Contains("Steam", StringComparison.OrdinalIgnoreCase) ||
        app.Name.Contains("Epic", StringComparison.OrdinalIgnoreCase) ||
        app.LaunchTarget.Contains("steam.exe", StringComparison.OrdinalIgnoreCase) ||
        app.LaunchTarget.StartsWith("com.epicgames.launcher://", StringComparison.OrdinalIgnoreCase) ||
        app.ExePath.Contains("EpicGamesLauncher.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsStoreGame(GameItem game) =>
        game.StorePlatform.Equals("Steam", StringComparison.OrdinalIgnoreCase) ||
        game.StorePlatform.Equals("Epic", StringComparison.OrdinalIgnoreCase) ||
        game.LaunchTarget.Contains("steam.exe", StringComparison.OrdinalIgnoreCase) ||
        game.LaunchTarget.StartsWith("com.epicgames.launcher://", StringComparison.OrdinalIgnoreCase);

    private static string GetStorePlatform(AppModel app) =>
        app.Name.Contains("Epic", StringComparison.OrdinalIgnoreCase) ||
        app.LaunchTarget.StartsWith("com.epicgames.launcher://", StringComparison.OrdinalIgnoreCase) ||
        app.ExePath.Contains("EpicGamesLauncher.exe", StringComparison.OrdinalIgnoreCase)
            ? "Epic"
            : "Steam";

    private static GameItem CreateStoreLookupGame(AppModel app, string gameName) => new()
    {
        Name = gameName,
        StorePlatform = GetStorePlatform(app),
        LaunchTarget = app.LaunchTarget,
        StoreLauncherPath = app.ExePath
    };

    private static GameItem CreateStoreLookupGame(string platform, string gameName) => new()
    {
        Name = gameName,
        StorePlatform = platform
    };

    private void ShowStoreGameNameDialog(AppModel app)
    {
        if (!IsStoreLauncherApp(app))
            return;

        var platform = GetStorePlatform(app);
        var dialog = new Window
        {
            Title = Localization["StoreInstallByName"],
            Width = 520,
            Height = 270,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent
        };

        var root = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 20, 22, 18)
        };
        root.SetResourceReference(Border.BackgroundProperty, "K.Bg");
        root.SetResourceReference(Border.BorderBrushProperty, "K.OutlineStrong");

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = Localization["StoreInstallByName"],
            Foreground = new SolidColorBrush(ThemeAccentColor),
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 7)
        };
        Grid.SetRow(title, 0);
        layout.Children.Add(title);

        var hint = new TextBlock
        {
            Text = $"{platform} • {Localization["StoreGameNameHint"]}",
            Foreground = Brush("#80808A"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(hint, 1);
        layout.Children.Add(hint);

        var nameBox = new TextBox
        {
            Height = 38,
            Background = Brush("#18181F"),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(ThemeAccentColor),
            // The theme's TextBox template puts Padding around the text host,
            // so vertical padding squeezes the line and can clip it. With
            // VerticalContentAlignment=Center the template centres the host.
            Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 13,
            ToolTip = Localization["StoreGameNamePlaceholder"]
        };
        Grid.SetRow(nameBox, 2);
        layout.Children.Add(nameBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var cancel = new Button
        {
            Content = Localization["Cancel"],
            Width = 105,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("K.Button.Secondary")
        };
        cancel.Click += (_, _) => dialog.Close();

        var install = new Button
        {
            Content = platform.Equals("Epic", StringComparison.OrdinalIgnoreCase)
                ? Localization["StoreInstallActionEpic"]
                : Localization["StoreInstallAction"],
            Width = 150,
            Height = 36,
            Style = (Style)FindResource("K.Button.Primary"),
            IsDefault = true
        };
        install.Click += async (_, _) =>
        {
            var gameName = nameBox.Text.Trim();
            if (gameName.Length == 0)
            {
                LauncherMessageBox.Show(
                    dialog,
                    Localization["StoreGameNameRequired"],
                    "K-LAUNCHER",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                nameBox.Focus();
                return;
            }

            install.IsEnabled = false;
            dialog.Close();
            await RunStoreAutomationAsync(
                CreateStoreLookupGame(app, gameName),
                StoreLauncherAction.Install);
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(install);
        Grid.SetRow(buttons, 3);
        layout.Children.Add(buttons);

        root.Child = layout;
        dialog.Content = root;
        dialog.Loaded += (_, _) => nameBox.Focus();
        DialogChrome.Attach(dialog);
        dialog.ShowDialog();
    }

    private void ShowStoreBatchInstallDialog(AppModel app)
    {
        if (!IsStoreLauncherApp(app))
            return;

        // The old text-only dialog is now part of the Ninite window, which
        // installs Steam/Epic games and regular applications in one queue.
        ShowNiniteWindow(GetStorePlatform(app));
    }

    private void NiniteInstall_Click(object sender, RoutedEventArgs e) =>
        ShowNiniteWindow("Steam");

    private void ShowNiniteWindow(string defaultGamePlatform)
    {
        var window = new NiniteWindow(
            ThemeAccentColor,
            ThemeBackgroundColor,
            key => Localization[key],
            (platform, name, token, progress) => ExecuteStoreAutomationAsync(
                CreateStoreLookupGame(platform, name),
                StoreLauncherAction.Install,
                token,
                progress),
            defaultGamePlatform)
        {
            Owner = this
        };
        // Newly installed catalog apps are picked up by the scan and appear
        // in the launcher as soon as a job finishes, without adding them by hand.
        window.ChangesApplied += () =>
        {
            if (!_isSuspendedForBackground)
                _ = RefreshAfterBatchInstallAsync();
        };
        window.ShowDialog();
    }

    private async Task RefreshAfterBatchInstallAsync()
    {
        try
        {
            await _viewModel.RefreshAsync(forceScan: true);
            UpdateLocalizedAppNames();
            _ = WarmQuickLinkIconsAsync();
        }
        catch
        {
            // The next normal refresh will pick the apps up.
        }
    }

    private static List<(string Platform, string Name)> ParseStoreBatchEntries(
        string text,
        string defaultPlatform)
    {
        var entries = new List<(string Platform, string Name)>();
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var platform = defaultPlatform;
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
            {
                entries.Add((platform, name));
            }
        }

        return entries;
    }

    private async Task RunStoreAutomationAsync(GameItem game, StoreLauncherAction action)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var result = await ExecuteStoreAutomationAsync(game, action);

            if (result is not { Success: true })
            {
                LauncherMessageBox.Show(
                    result?.Message ?? Localization["StoreActionFailed"],
                    "K-LAUNCHER",
                    MessageBoxButton.OK,
                    result?.RequiresUserAction == true
                        ? MessageBoxImage.Warning
                        : MessageBoxImage.Error);
            }
        }
        catch (OperationCanceledException)
        {
            // The launcher action was interrupted without showing an error.
        }
        catch (Exception ex)
        {
            var platform = GetStorePlatformForGame(game);
            LauncherMessageBox.Show(
                string.Format(Localization["StoreActionStartFailed"], platform, ex.Message),
                "K-LAUNCHER",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async Task RunStoreBatchAutomationAsync(
        IReadOnlyList<(string Platform, string Name)> jobs)
    {
        var succeeded = new List<string>();
        var failed = new List<string>();
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            // Official launchers share foreground UI state, therefore the
            // queue is deliberately sequential like Ninite: one item is
            // searched and handed to its launcher before the next begins.
            foreach (var job in jobs)
            {
                var result = await ExecuteStoreAutomationAsync(
                    CreateStoreLookupGame(job.Platform, job.Name),
                    StoreLauncherAction.Install);

                if (result is { Success: true })
                    succeeded.Add($"{job.Platform}: {job.Name}");
                else
                    failed.Add($"{job.Platform}: {job.Name} — {result?.Message ?? Localization["UnknownError"]}");
            }
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        var summary = new List<string>
        {
            Localization["StoreBatchInstallSummary"],
            string.Empty,
            string.Format(Localization["BatchSucceeded"], succeeded.Count),
            string.Format(Localization["BatchFailed"], failed.Count)
        };

        if (succeeded.Count > 0)
        {
            summary.Add(string.Empty);
            summary.AddRange(succeeded.Select(item => $"✓ {item}"));
        }

        if (failed.Count > 0)
        {
            summary.Add(string.Empty);
            summary.AddRange(failed.Select(item => $"× {item}"));
        }

        LauncherMessageBox.Show(
            this,
            string.Join(Environment.NewLine, summary),
            "K-LAUNCHER",
            MessageBoxButton.OK,
            failed.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async Task<StoreLauncherAutomationResult?> ExecuteStoreAutomationAsync(
        GameItem game,
        StoreLauncherAction action,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        var platform = GetStorePlatformForGame(game);
        var launcherPath = game.StoreLauncherPath;
        if (string.IsNullOrWhiteSpace(launcherPath))
        {
            launcherPath = platform.Equals("Steam", StringComparison.OrdinalIgnoreCase)
                ? FindLauncherExecutable("steam.exe", [
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steam.exe")])
                : FindLauncherExecutable("EpicGamesLauncher.exe", [
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Epic Games", "Launcher", "EpicGamesLauncher.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe")]);
        }

        try
        {
            return await StoreLauncherAutomation.RunAsync(
                platform,
                game.Name,
                launcherPath,
                action,
                cancellationToken,
                progress);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            return new(
                false,
                string.Format(Localization["StoreActionStartFailed"], platform, ex.Message),
                RequiresUserAction: false);
        }
    }

    private static string GetStorePlatformForGame(GameItem game)
    {
        if (!string.IsNullOrWhiteSpace(game.StorePlatform))
            return game.StorePlatform;

        return game.LaunchTarget.Contains("steam.exe", StringComparison.OrdinalIgnoreCase)
            ? "Steam"
            : "Epic";
    }

    private static string? FindLauncherExecutable(string exeName, IEnumerable<string> knownPaths)
    {
        var direct = knownPaths.FirstOrDefault(File.Exists);
        if (direct is not null)
            return direct;

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
            var value = key?.GetValue("") as string;
            return !string.IsNullOrWhiteSpace(value) && File.Exists(value)
                ? value
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void AnimateContextMenuWithDirectSlide(ContextMenu menu)
    {
        var animationTarget = GetContextMenuAnimationTarget(menu);
        AnimateContextMenuActions(menu);

        var translate = new TranslateTransform(-48, 0);
        animationTarget.RenderTransformOrigin = new Point(0, 0);
        animationTarget.RenderTransform = translate;
        animationTarget.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, MotionSettings.Ms(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        }, HandoffBehavior.SnapshotAndReplace);
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-48, 0, MotionSettings.Ms(320))
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
        // Opening: the frame rises 6px and settles from 96% while it fades
        // in, then the rows follow with a short stagger. Transform and
        // opacity only, so nothing is re-laid out or captured as a bitmap.
        var animationTarget = GetContextMenuAnimationTarget(menu);
        if (animationTarget.RenderTransform is not TransformGroup transform)
            return;

        var scale = transform.Children.OfType<ScaleTransform>().FirstOrDefault();
        var translate = transform.Children.OfType<TranslateTransform>().FirstOrDefault();
        if (scale is null || translate is null)
            return;

        animationTarget.Clip = null;
        animationTarget.RenderTransformOrigin = new Point(0.5, 0);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var settle = MotionSettings.Ms(180);

        animationTarget.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, MotionSettings.Ms(130)) { EasingFunction = ease },
            HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, settle) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, settle) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, settle) { EasingFunction = ease });

        AnimateContextMenuFrame(menu, TimeSpan.Zero);
        AnimateContextMenuActions(menu);
    }

    private static FrameworkElement GetContextMenuAnimationTarget(ContextMenu menu)
    {
        menu.ApplyTemplate();
        return menu.Template?.FindName("MenuRoot", menu) as FrameworkElement ?? menu;
    }

    private void AnimateContextMenuClose(ContextMenu menu, Action completed)
    {
        // Closing: a quick fade with a slight shrink toward the anchor.
        var animationTarget = GetContextMenuAnimationTarget(menu);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var duration = MotionSettings.Ms(110);

        if (animationTarget.RenderTransform is TransformGroup transform &&
            transform.Children.OfType<ScaleTransform>().FirstOrDefault() is { } scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, duration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, duration) { EasingFunction = ease });
        }

        var fade = new DoubleAnimation(0, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        var finished = false;
        fade.Completed += (_, _) =>
        {
            if (finished)
                return;

            finished = true;
            completed();
        };
        animationTarget.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateMenuFrameClose(UIElement? element)
    {
        if (element is null)
            return;

        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = element.Opacity,
            To = 0,
            BeginTime = MotionSettings.Ms(60),
            Duration = MotionSettings.Ms(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateContextMenuActions(ContextMenu menu)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var index = 0;
        foreach (var item in menu.Items.OfType<UIElement>())
        {
            // Only the first rows are staggered; long menus stay snappy.
            var delay = MotionSettings.Ms(30 + (Math.Min(index, 10) * 14));
            var translate = new TranslateTransform();
            item.RenderTransform = translate;
            translate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(-8, 0, MotionSettings.Ms(200))
                {
                    BeginTime = delay,
                    EasingFunction = ease,
                    FillBehavior = FillBehavior.Stop
                });
            item.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, MotionSettings.Ms(150))
                {
                    BeginTime = delay,
                    EasingFunction = ease
                },
                HandoffBehavior.SnapshotAndReplace);
            index++;
        }
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
            Duration = MotionSettings.Ms(130),
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
                    LauncherMessageBox.Show(
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
                LauncherMessageBox.Show(
                    string.Format(Localization["ValorantLaunchFailed"], ex.Message),
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
            Width = 560,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent
        };

        var root = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 20, 22, 18)
        };
        root.SetResourceReference(Border.BackgroundProperty, "K.Bg");
        root.SetResourceReference(Border.BorderBrushProperty, "K.OutlineStrong");

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
        ConfigureSmoothVerticalScroll(scrollViewer);
        Grid.SetRow(scrollViewer, 1);
        layout.Children.Add(scrollViewer);

        var close = new Button
        {
            Content = Localization["Close"],
            Width = 110,
            Height = 36,
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)FindResource("K.Button.Secondary")
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
                    Style = (Style)FindResource("K.Button.Danger"),
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
                        LauncherMessageBox.Show(
                            dialog,
                            ex.Message,
                            "K-LAUNCHER",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                };
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);

                var linkCard = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(12, 9, 10, 9),
                    Margin = new Thickness(0, 0, 0, 7),
                    Child = row
                };
                linkCard.SetResourceReference(Border.BackgroundProperty, "K.Card");
                linkCard.SetResourceReference(Border.BorderBrushProperty, "K.Outline");
                linksPanel.Children.Add(linkCard);
            }
        }

        RefreshLinks();
        root.Child = layout;
        dialog.Content = root;
        DialogChrome.Attach(dialog);
        dialog.ShowDialog();
    }

    private void AddQuickLink(AppModel app)
    {
        var dialog = new Window
        {
            Title = Localization["LinkAddTitle"],
            Width = 500,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent
        };

        var root = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 20, 22, 18)
        };
        root.SetResourceReference(Border.BackgroundProperty, "K.Bg");
        root.SetResourceReference(Border.BorderBrushProperty, "K.OutlineStrong");

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
            Height = 36,
            Margin = new Thickness(0, 0, 0, 8),
            Background = Brush("#18181F"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#303039"),
            Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = Localization["LinkNamePlaceholder"]
        };
        Grid.SetRow(nameBox, 1);
        grid.Children.Add(nameBox);

        var urlBox = new TextBox
        {
            Height = 36,
            Margin = new Thickness(0, 0, 0, 14),
            Background = Brush("#18181F"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#303039"),
            Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = string.Empty
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
            Width = 105,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("K.Button.Secondary")
        };
        cancel.Click += (_, _) => dialog.Close();

        var save = new Button
        {
            Content = Localization["Save"],
            Width = 105,
            Height = 36,
            Style = (Style)FindResource("K.Button.Primary"),
            IsDefault = true
        };

        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                // The user enters the address freely. Web lookups and storage
                // normalize a missing scheme to https:// automatically.
                var normalizedUrl = QuickLinkStore.NormalizeUrl(urlBox.Text);
                var iconPath = await QuickLinkIconStore.TryFetchAndSaveAsync(normalizedUrl);
                QuickLinkStore.Add(
                    app.Name,
                    nameBox.Text,
                    normalizedUrl,
                    iconPath);

                QuickLinkStore.Apply(app);
                dialog.Close();
            }
            catch (Exception ex)
            {
                save.IsEnabled = true;
                LauncherMessageBox.Show(
                    dialog,
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
        DialogChrome.Attach(dialog);
        dialog.ShowDialog();
    }

    private static MenuItem MakeItem(
        string text,
        string glyph,
        string color,
        RoutedEventHandler handler)
    {
        var tone = ActionToneFromLegacyColor(color);
        var item = new MenuItem
        {
            Header = text,
            Icon = MakeGlyph(glyph, tone),
            Padding = new Thickness(9, 7, 14, 7)
        };
        if (tone == ActionTone.Danger)
            item.SetResourceReference(Control.ForegroundProperty, "K.Danger");

        item.Click += handler;
        return item;
    }

    private enum ActionTone
    {
        Neutral,
        Primary,
        Danger
    }

    // Older call sites still describe an action by its colour. Map those to a
    // role so every menu and button reads from the live theme tokens.
    private static ActionTone ActionToneFromLegacyColor(string color) =>
        color.ToUpperInvariant() switch
        {
            "#FF6B75" => ActionTone.Danger,
            "#00FF66" or "#FF00FF66" => ActionTone.Primary,
            _ => ActionTone.Neutral
        };

    private static FontFamily? _glyphFont;
    private static FontFamily GlyphFont => _glyphFont ??= new FontFamily(Glyphs.FontFamily);

    private static FrameworkElement MakeGlyph(string glyph, ActionTone tone = ActionTone.Neutral)
    {
        var text = new TextBlock
        {
            Text = glyph,
            FontFamily = GlyphFont,
            FontSize = 13,
            Width = 18,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.SetResourceReference(
            TextBlock.ForegroundProperty,
            tone switch
            {
                ActionTone.Primary => "K.Accent",
                ActionTone.Danger => "K.Danger",
                _ => "K.TextMuted"
            });
        return text;
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
            bitmap.DecodePixelWidth = 96;
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
            LauncherMessageBox.Show(
                string.Format(Localization["LaunchFailed"], ex.Message),
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
