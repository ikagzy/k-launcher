using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KLauncher.Services;

namespace KLauncher;

/// <summary>
/// Shared behaviour for every code-built dialog: rounded corners, a short
/// open animation, a short close animation and Esc to close. Animations only
/// touch Opacity and a render transform, so they cost nothing once finished.
/// </summary>
internal static class DialogChrome
{
    private const double Radius = 12;

    public static void Attach(
        Window window,
        bool animateOpen = true,
        bool animateClose = true,
        bool escapeCloses = true)
    {
        AttachToMainWindow(window);
        AttachFlowDirection(window);
        BeepWatch.Attach(window);

        if (escapeCloses)
        {
            window.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape || e.Handled)
                    return;

                e.Handled = true;
                window.Close();
            };
        }

        // Hide the content before the first frame so it never flashes at full opacity.
        if (animateOpen)
        {
            window.SourceInitialized += (_, _) =>
            {
                if (MotionSettings.Enabled && window.Content is UIElement root)
                    root.Opacity = 0;
            };
        }

        window.Loaded += (_, _) =>
        {
            ApplyRoundedCorners(window);
            if (animateOpen && MotionSettings.Enabled)
                PlayOpen(window);
        };

        if (animateClose)
            AttachCloseAnimation(window);
    }

    // Right-to-left languages mirror the whole dialog, and follow a language
    // change made while the dialog is open.
    private static void AttachFlowDirection(Window window)
    {
        if (LocalizationService.Current is not { } localization)
            return;

        window.FlowDirection = localization.FlowDirection;
        System.ComponentModel.PropertyChangedEventHandler changed = (_, args) =>
        {
            if (args.PropertyName == nameof(LocalizationService.FlowDirection))
                window.FlowDirection = localization.FlowDirection;
        };
        localization.PropertyChanged += changed;
        window.Closed += (_, _) => localization.PropertyChanged -= changed;
    }

    // Secondary windows belong to the launcher: owned by the main window and
    // kept out of the taskbar, so Alt+Tab shows only K-LAUNCHER. A window
    // without an owner (main window minimized or hidden) stays reachable.
    private static void AttachToMainWindow(Window window)
    {
        if (window.Owner is null &&
            Application.Current?.MainWindow is { IsVisible: true, WindowState: not WindowState.Minimized } main &&
            !ReferenceEquals(main, window))
        {
            try
            {
                window.Owner = main;
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (window.Owner is not null)
            window.ShowInTaskbar = false;
    }

    private static void ApplyRoundedCorners(Window window)
    {
        if (window.Content is not Border root)
            return;

        if (root.CornerRadius == default)
            root.CornerRadius = new CornerRadius(Radius);

        if (root.Child is not FrameworkElement child)
            return;

        void UpdateClip()
        {
            if (child.ActualWidth <= 0 || child.ActualHeight <= 0)
                return;

            var clip = new RectangleGeometry(
                new Rect(0, 0, child.ActualWidth, child.ActualHeight),
                Radius - 1,
                Radius - 1);
            clip.Freeze();
            child.Clip = clip;
        }

        child.SizeChanged += (_, _) => UpdateClip();
        UpdateClip();
    }

    private static void PlayOpen(Window window)
    {
        if (window.Content is not UIElement root)
            return;

        var scale = new ScaleTransform(0.97, 0.97);
        var translate = new TranslateTransform(0, 8);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(translate);
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        root.RenderTransform = group;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = MotionSettings.Ms(190);

        var fade = new DoubleAnimation(0, 1, MotionSettings.Ms(150)) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            root.BeginAnimation(UIElement.OpacityProperty, null);
            root.Opacity = 1;
            root.RenderTransform = Transform.Identity;
        };

        root.BeginAnimation(UIElement.OpacityProperty, fade);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, duration) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration) { EasingFunction = ease });
    }

    private static void AttachCloseAnimation(Window window)
    {
        var closing = false;

        window.Closing += (_, e) =>
        {
            // Second pass (after the animation), another handler vetoed the close,
            // or the app is shutting down: nothing to animate.
            if (closing || e.Cancel || Application.Current.Dispatcher.HasShutdownStarted ||
                !MotionSettings.Enabled)
                return;

            if (window.Content is not UIElement root || !window.IsVisible)
                return;

            closing = true;
            e.Cancel = true;

            // Cancelling Closing clears DialogResult, so remember it and set it again.
            bool? result = null;
            try { result = window.DialogResult; }
            catch (InvalidOperationException) { }

            var fade = new DoubleAnimation(root.Opacity, 0, MotionSettings.Ms(110))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (_, _) =>
            {
                try
                {
                    if (result.HasValue)
                        window.DialogResult = result;
                    else
                        window.Close();
                }
                catch (InvalidOperationException)
                {
                    window.Close();
                }
            };
            root.BeginAnimation(UIElement.OpacityProperty, fade);
        };
    }
}

/// <summary>
/// Themed replacement for System.Windows.MessageBox with the same call shape.
/// Colours come from the K.* tokens, so it follows the active palette.
/// </summary>
internal static class LauncherMessageBox
{
    public static MessageBoxResult Show(
        string text,
        string caption,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None) =>
        Show(null, text, caption, buttons, image);

    public static MessageBoxResult Show(
        Window? owner,
        string text,
        string caption,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        var app = Application.Current;
        if (app is null)
            return MessageBoxResult.None;

        if (!app.Dispatcher.CheckAccess())
        {
            return (MessageBoxResult)app.Dispatcher.Invoke(
                () => Show(owner, text, caption, buttons, image));
        }

        owner ??= app.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive) ?? app.MainWindow;
        var result = DefaultResult(buttons);

        var dialog = new Window
        {
            Width = 440,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 560,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Title = caption
        };

        if (owner is { IsVisible: true, WindowState: not WindowState.Minimized })
        {
            try
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            catch (InvalidOperationException)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Topmost = true;
        }

        var root = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 20, 22, 18)
        };
        root.SetResourceReference(Border.BackgroundProperty, "K.Bg");
        root.SetResourceReference(Border.BorderBrushProperty, "K.OutlineStrong");
        root.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Border or Grid or TextBlock)
            {
                try { dialog.DragMove(); }
                catch (InvalidOperationException) { }
            }
        };

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- icon badge
        var (glyph, tone) = Describe(image);
        var badge = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(19),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 16, 0)
        };
        var badgeGlyph = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily(Glyphs.FontFamily),
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = badgeGlyph;
        if (tone == Tone.Danger)
        {
            badge.SetResourceReference(Border.BackgroundProperty, "K.DangerSoft");
            badgeGlyph.SetResourceReference(TextBlock.ForegroundProperty, "K.Danger");
        }
        else if (tone == Tone.Warning)
        {
            badge.Background = Frozen(Color.FromArgb(0x2A, 0xFF, 0xBE, 0x40));
            badgeGlyph.Foreground = Frozen(Color.FromRgb(0xFF, 0xBE, 0x40));
        }
        else
        {
            badge.SetResourceReference(Border.BackgroundProperty, "K.AccentSoft");
            badgeGlyph.SetResourceReference(TextBlock.ForegroundProperty, "K.Accent");
        }

        if (image != MessageBoxImage.None)
            layout.Children.Add(badge);

        // ---- text
        var textStack = new StackPanel();
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var title = new TextBlock
            {
                Text = caption,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "K.TextMuted");
            title.SetResourceReference(TextBlock.FontFamilyProperty, "K.Font.Display");
            textStack.Children.Add(title);
        }

        var message = new TextBlock
        {
            Text = text,
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        };
        message.SetResourceReference(TextBlock.ForegroundProperty, "K.Text");
        message.SetResourceReference(TextBlock.FontFamilyProperty, "K.Font.UI");
        textStack.Children.Add(message);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 340,
            Content = textStack
        };
        Grid.SetColumn(scroll, image == MessageBoxImage.None ? 0 : 1);
        Grid.SetColumnSpan(scroll, image == MessageBoxImage.None ? 2 : 1);
        layout.Children.Add(scroll);

        // ---- buttons
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        Grid.SetRow(buttonRow, 1);
        Grid.SetColumnSpan(buttonRow, 2);
        layout.Children.Add(buttonRow);

        void AddButton(string label, MessageBoxResult value, bool primary, bool isCancel)
        {
            var button = new Button
            {
                Content = label,
                MinWidth = 92,
                Height = 34,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = primary,
                IsCancel = isCancel
            };
            button.SetResourceReference(
                FrameworkElement.StyleProperty,
                primary ? "K.Button.Primary" : "K.Button.Secondary");
            button.Click += (_, _) =>
            {
                result = value;
                dialog.Close();
            };
            buttonRow.Children.Add(button);
        }

        var localization = (LocalizationService)app.Resources["Localization"];
        switch (buttons)
        {
            case MessageBoxButton.OK:
                AddButton(localization["Ok"], MessageBoxResult.OK, primary: true, isCancel: true);
                break;
            case MessageBoxButton.OKCancel:
                AddButton(localization["Cancel"], MessageBoxResult.Cancel, primary: false, isCancel: true);
                AddButton(localization["Ok"], MessageBoxResult.OK, primary: true, isCancel: false);
                break;
            case MessageBoxButton.YesNo:
                AddButton(No(localization), MessageBoxResult.No, primary: false, isCancel: true);
                AddButton(Yes(localization), MessageBoxResult.Yes, primary: true, isCancel: false);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton(localization["Cancel"], MessageBoxResult.Cancel, primary: false, isCancel: true);
                AddButton(No(localization), MessageBoxResult.No, primary: false, isCancel: false);
                AddButton(Yes(localization), MessageBoxResult.Yes, primary: true, isCancel: false);
                break;
        }

        root.Child = layout;
        dialog.Content = root;

        // Closing through Esc or the window itself counts as the cancel answer.
        DialogChrome.Attach(dialog);
        dialog.ShowDialog();
        return result;
    }

    private enum Tone
    {
        Info,
        Warning,
        Danger
    }

    private static (string Glyph, Tone Tone) Describe(MessageBoxImage image) => image switch
    {
        MessageBoxImage.Error => (Glyphs.Error, Tone.Danger),
        MessageBoxImage.Warning => (Glyphs.Warning, Tone.Warning),
        MessageBoxImage.Question => ("", Tone.Info),
        _ => (Glyphs.Info, Tone.Info)
    };

    private static MessageBoxResult DefaultResult(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OK => MessageBoxResult.OK,
        MessageBoxButton.YesNo => MessageBoxResult.No,
        _ => MessageBoxResult.Cancel
    };

    private static string Yes(LocalizationService localization) => localization["DialogYes"];

    private static string No(LocalizationService localization) => localization["DialogNo"];

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
