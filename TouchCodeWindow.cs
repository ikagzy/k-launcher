using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KLauncher.Services;

namespace KLauncher;

/// <summary>
/// Pick a board, a touch chip and the pins, and get a ready Arduino sketch
/// that adds touch input to the display. The code updates as you change
/// anything and can be copied, saved as .ino or opened in the Arduino IDE.
/// </summary>
public sealed class TouchCodeWindow : Window
{
    private const string ArduinoIdePackageId = "ArduinoSA.IDE.stable";

    private readonly Func<string, string> _text;
    private readonly bool _turkish;
    private TouchConfig _config = TouchCodeGenerator.Presets[0].Config;
    private bool _updating;
    private bool _showingFinder;
    private string? _selectedPort;
    private CancellationTokenSource? _listenCancellation;
    private readonly WrapPanel _portWrap = new();
    private readonly Button _listenButton;

    private readonly WrapPanel _boardWrap = new();
    private readonly WrapPanel _chipWrap = new();
    private readonly WrapPanel _outputWrap = new();
    private readonly WrapPanel _rotationWrap = new();
    private readonly WrapPanel _presetWrap = new();
    private readonly TextBlock _chipHint;
    private readonly TextBox _widthBox;
    private readonly TextBox _heightBox;
    private readonly Grid _pinGrid = new();
    private readonly CheckBox _separateSpiBox;
    private readonly CheckBox _swapBox;
    private readonly CheckBox _invertXBox;
    private readonly CheckBox _invertYBox;
    private readonly TextBlock _librariesText;
    private readonly TextBox _codeBox;
    private readonly TextBlock _statusText;
    private readonly Button _ideButton;

    public TouchCodeWindow(Func<string, string> text, bool turkish)
    {
        _text = text;
        _turkish = turkish;

        Title = _text("TouchTitle");
        Width = 1040;
        Height = 720;
        MinWidth = 860;
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
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- Title
        var titleRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = _text("TouchTitle"),
            Foreground = T("K.Accent"),
            FontFamily = (FontFamily)Application.Current.FindResource("K.Font.Display"),
            FontWeight = FontWeights.Bold,
            FontSize = 19
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = _text("TouchSubtitle"),
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
        layout.Children.Add(titleRow);

        // ---- Body: settings on the left, code on the right
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1);
        layout.Children.Add(body);

        var settings = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        var settingsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = settings
        };
        body.Children.Add(settingsScroll);

        settings.Children.Add(Heading(_text("TouchPresets"), first: true));
        foreach (var preset in TouchCodeGenerator.Presets)
        {
            var presetButton = SmallButton(preset.Name);
            presetButton.Click += (_, _) =>
            {
                _showingFinder = false;
                _config = preset.Config with { Output = _config.Output };
                SyncControls();
                Regenerate();
            };
            _presetWrap.Children.Add(presetButton);
        }
        settings.Children.Add(_presetWrap);

        // ---- Automatic finder
        settings.Children.Add(Heading(_text("TouchFinder")));
        settings.Children.Add(new TextBlock
        {
            Text = _text("TouchFinderHint"),
            Foreground = T("K.TextMuted"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 0, 4)
        });
        var showFinderButton = SmallButton(_text("TouchFinderShow"));
        showFinderButton.Click += (_, _) =>
        {
            _showingFinder = true;
            Regenerate();
            _statusText.Text = _text("TouchFinderShown");
        };
        settings.Children.Add(showFinderButton);
        var portRow = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var refreshPortsButton = SmallButton("↻");
        refreshPortsButton.Width = 34;
        refreshPortsButton.ToolTip = _text("TouchFinderRefresh");
        refreshPortsButton.Click += (_, _) => RefreshPorts();
        DockPanel.SetDock(refreshPortsButton, Dock.Right);
        portRow.Children.Add(refreshPortsButton);
        portRow.Children.Add(_portWrap);
        settings.Children.Add(portRow);
        _listenButton = SmallButton(_text("TouchFinderListen"));
        _listenButton.SetResourceReference(FrameworkElement.StyleProperty, "K.Button.Primary");
        _listenButton.Click += async (_, _) => await ListenAsync();
        settings.Children.Add(_listenButton);

        settings.Children.Add(Heading(_text("TouchBoard")));
        foreach (var board in Enum.GetValues<TouchBoard>())
            _boardWrap.Children.Add(ChoiceButton(TouchCodeGenerator.BoardName(board), board,
                () => _config = _config with { Board = board }));
        settings.Children.Add(_boardWrap);

        settings.Children.Add(Heading(_text("TouchChip")));
        foreach (var chip in Enum.GetValues<TouchChip>())
            _chipWrap.Children.Add(ChoiceButton(TouchCodeGenerator.ChipName(chip), chip,
                () => { _showingFinder = false; _config = _config with { Chip = chip }; }));
        settings.Children.Add(_chipWrap);
        _chipHint = new TextBlock
        {
            Foreground = T("K.TextMuted"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 2, 0, 0)
        };
        settings.Children.Add(_chipHint);

        settings.Children.Add(Heading(_text("TouchOutput")));
        foreach (var output in Enum.GetValues<TouchOutput>())
            _outputWrap.Children.Add(ChoiceButton(OutputName(output), output,
                () => { _showingFinder = false; _config = _config with { Output = output }; }));
        settings.Children.Add(_outputWrap);

        settings.Children.Add(Heading(_text("TouchScreen")));
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
        _widthBox = NumberBox();
        _heightBox = NumberBox();
        sizeRow.Children.Add(Label(_text("TouchWidth")));
        sizeRow.Children.Add(_widthBox);
        sizeRow.Children.Add(Label(_text("TouchHeight"), leftMargin: 12));
        sizeRow.Children.Add(_heightBox);
        settings.Children.Add(sizeRow);
        _widthBox.TextChanged += (_, _) => ApplyNumber(_widthBox, value => _config = _config with { Width = value }, min: 16);
        _heightBox.TextChanged += (_, _) => ApplyNumber(_heightBox, value => _config = _config with { Height = value }, min: 16);

        var rotationRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        rotationRow.Children.Add(Label(_text("TouchRotation")));
        for (var rotation = 0; rotation < 4; rotation++)
        {
            var value = rotation;
            _rotationWrap.Children.Add(ChoiceButton(value.ToString(), value,
                () => _config = _config with { Rotation = value }, width: 34));
        }
        rotationRow.Children.Add(_rotationWrap);
        settings.Children.Add(rotationRow);

        settings.Children.Add(Heading(_text("TouchPins")));
        _pinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        _pinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        _pinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        _pinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        settings.Children.Add(_pinGrid);
        settings.Children.Add(new TextBlock
        {
            Text = _text("TouchPinNotUsed"),
            Foreground = T("K.TextFaint"),
            FontSize = 10,
            Margin = new Thickness(2, 2, 0, 0)
        });

        _separateSpiBox = Check(_text("TouchSeparateSpi"), value => _config = _config with { SeparateSpiBus = value });
        _swapBox = Check(_text("TouchSwap"), value => _config = _config with { SwapXY = value });
        _invertXBox = Check(_text("TouchInvertX"), value => _config = _config with { InvertX = value });
        _invertYBox = Check(_text("TouchInvertY"), value => _config = _config with { InvertY = value });
        settings.Children.Add(Heading(_text("TouchOrientation")));
        settings.Children.Add(_separateSpiBox);
        settings.Children.Add(_swapBox);
        settings.Children.Add(_invertXBox);
        settings.Children.Add(_invertYBox);
        settings.Children.Add(new TextBlock
        {
            Text = _text("TouchOrientationHint"),
            Foreground = T("K.TextMuted"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 4, 0, 6)
        });

        // ---- Code preview
        var codePanel = new Grid();
        codePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        codePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _librariesText = new TextBlock
        {
            Foreground = T("K.TextMuted"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 0, 8)
        };
        codePanel.Children.Add(_librariesText);
        _codeBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 12,
            // Generated Arduino code reads left to right in every language.
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Thickness(10),
            VerticalContentAlignment = VerticalAlignment.Top
        };
        Grid.SetRow(_codeBox, 1);
        codePanel.Children.Add(_codeBox);
        Grid.SetColumn(codePanel, 2);
        body.Children.Add(codePanel);

        // ---- Buttons
        var buttonRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _statusText = new TextBlock
        {
            Foreground = T("K.TextMuted"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        buttonRow.Children.Add(_statusText);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var copyButton = BigButton(_text("TouchCopy"), primary: false);
        copyButton.Click += (_, _) => CopyCode();
        var saveButton = BigButton(_text("TouchSave"), primary: false);
        saveButton.Click += (_, _) => SaveCode();
        _ideButton = BigButton(_text("TouchOpenIde"), primary: true);
        _ideButton.Click += async (_, _) => await OpenOrInstallIdeAsync();
        buttons.Children.Add(copyButton);
        buttons.Children.Add(saveButton);
        buttons.Children.Add(_ideButton);
        Grid.SetColumn(buttons, 1);
        buttonRow.Children.Add(buttons);
        Grid.SetRow(buttonRow, 2);
        layout.Children.Add(buttonRow);

        root.Child = layout;
        Content = root;

        SyncControls();
        Regenerate();
        UpdateIdeButton();
        RefreshPorts();
        Closed += (_, _) => _listenCancellation?.Cancel();
        DialogChrome.Attach(this, animateOpen: true, animateClose: true, escapeCloses: true);
    }

    // ------------------------------------------------------------------ State

    private void SyncControls()
    {
        _updating = true;
        SetSelected(_boardWrap, _config.Board);
        SetSelected(_chipWrap, _config.Chip);
        SetSelected(_outputWrap, _config.Output);
        SetSelected(_rotationWrap, _config.Rotation);
        _widthBox.Text = _config.Width.ToString();
        _heightBox.Text = _config.Height.ToString();
        _separateSpiBox.IsChecked = _config.SeparateSpiBus;
        _swapBox.IsChecked = _config.SwapXY;
        _invertXBox.IsChecked = _config.InvertX;
        _invertYBox.IsChecked = _config.InvertY;
        _separateSpiBox.Visibility = TouchConfig.UsesSpi(_config.Chip) && TouchConfig.IsEsp32Family(_config.Board)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _chipHint.Text = _text(_config.Chip switch
        {
            TouchChip.Ft6206 => "TouchChipFt",
            TouchChip.Gt911 => "TouchChipGt",
            TouchChip.Cst816s => "TouchChipCst",
            _ => "TouchChipXpt"
        });
        BuildPinGrid();
        _updating = false;
    }

    private void BuildPinGrid()
    {
        _pinGrid.Children.Clear();
        _pinGrid.RowDefinitions.Clear();

        var pins = new List<(string Name, int Value, Func<int, TouchConfig> Apply)>();
        if (TouchConfig.UsesSpi(_config.Chip))
        {
            pins.Add(("SCK", _config.Sck, value => _config with { Sck = value }));
            pins.Add(("MISO", _config.Miso, value => _config with { Miso = value }));
            pins.Add(("MOSI", _config.Mosi, value => _config with { Mosi = value }));
            pins.Add(("CS", _config.Cs, value => _config with { Cs = value }));
            pins.Add(("IRQ", _config.Irq, value => _config with { Irq = value }));
        }
        else
        {
            pins.Add(("SDA", _config.Sda, value => _config with { Sda = value }));
            pins.Add(("SCL", _config.Scl, value => _config with { Scl = value }));
            if (_config.Chip == TouchChip.Gt911)
            {
                pins.Add(("INT", _config.Int, value => _config with { Int = value }));
                pins.Add(("RST", _config.Rst, value => _config with { Rst = value }));
            }
            else if (_config.Chip == TouchChip.Cst816s)
            {
                pins.Add(("RST", _config.Rst, value => _config with { Rst = value }));
                pins.Add(("IRQ", _config.Irq, value => _config with { Irq = value }));
            }
        }

        for (var index = 0; index < pins.Count; index++)
        {
            var (name, value, apply) = pins[index];
            var row = index / 2;
            if (row >= _pinGrid.RowDefinitions.Count)
                _pinGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = Label(name);
            label.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(label, row);
            Grid.SetColumn(label, (index % 2) * 2);
            _pinGrid.Children.Add(label);

            var box = NumberBox();
            box.Text = value.ToString();
            box.Margin = new Thickness(0, 3, 10, 3);
            box.TextChanged += (_, _) => ApplyNumber(box, parsed => _config = apply(parsed), min: -1);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, (index % 2) * 2 + 1);
            _pinGrid.Children.Add(box);
        }
    }

    private void ApplyNumber(TextBox box, Action<int> apply, int min)
    {
        if (_updating)
            return;
        if (int.TryParse(box.Text.Trim(), out var value) && value >= min && value <= 4096)
        {
            box.ClearValue(Control.BorderBrushProperty);
            apply(value);
            Regenerate();
        }
        else
        {
            box.BorderBrush = T("K.Danger");
        }
    }

    private void Regenerate()
    {
        if (_showingFinder)
        {
            _codeBox.Text = TouchCodeGenerator.GenerateFinder(_config.Board, _turkish);
            _librariesText.Text = _text("TouchFinderNoLibraries");
            return;
        }

        _codeBox.Text = TouchCodeGenerator.Generate(_config, _turkish);
        _librariesText.Text = string.Format(_text("TouchLibraries"),
            string.Join(", ", TouchCodeGenerator.RequiredLibraries(_config)));
    }

    private string CurrentSketchName() => _showingFinder
        ? $"KTouch_Finder_{_config.Board}"
        : TouchCodeGenerator.SketchName(_config);

    // ------------------------------------------------------------ Finder

    private void RefreshPorts()
    {
        _portWrap.Children.Clear();
        var ports = SerialPortReader.ListPorts();
        if (ports.Count == 0)
        {
            _selectedPort = null;
            _portWrap.Children.Add(new TextBlock
            {
                Text = _text("TouchFinderNoPorts"),
                Foreground = T("K.TextMuted"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 4, 6, 4)
            });
            return;
        }

        if (_selectedPort is null || ports.All(port => port.Name != _selectedPort))
            _selectedPort = ports[^1].Name;

        foreach (var port in ports)
        {
            var label = string.IsNullOrWhiteSpace(port.Description) ? port.Name : $"{port.Name} • {port.Description}";
            var button = SmallButton(label);
            button.Tag = port.Name;
            button.ToolTip = label;
            button.MaxWidth = 300;
            button.Click += (_, _) =>
            {
                _selectedPort = port.Name;
                SetSelected(_portWrap, _selectedPort);
            };
            _portWrap.Children.Add(button);
        }

        SetSelected(_portWrap, _selectedPort);
    }

    private async Task ListenAsync()
    {
        if (_listenCancellation is not null)
        {
            _listenCancellation.Cancel();
            return;
        }

        if (_selectedPort is null)
        {
            RefreshPorts();
            if (_selectedPort is null)
            {
                _statusText.Text = _text("TouchFinderPickPort");
                return;
            }
        }

        var port = _selectedPort;
        using var cancellation = new CancellationTokenSource();
        _listenCancellation = cancellation;
        _listenButton.Content = _text("TouchFinderStop");
        _statusText.Text = string.Format(_text("TouchFinderListening"), port);

        string? outcome = null;
        try
        {
            var found = await SerialPortReader.ReadLinesAsync(port, 115200, line =>
            {
                if (!line.StartsWith("KTOUCH:", StringComparison.Ordinal))
                    return false;

                var payload = line[7..];
                if (payload == "PRESS")
                {
                    Dispatcher.BeginInvoke(() => _statusText.Text = _text("TouchFinderPress"));
                    return false;
                }

                outcome = payload;
                return true;
            }, TimeSpan.FromSeconds(60), cancellation.Token);

            if (!found)
                _statusText.Text = cancellation.IsCancellationRequested ? string.Empty : _text("TouchFinderTimeout");
            else
                ApplyFinderResult(outcome!);
        }
        catch (OperationCanceledException)
        {
            _statusText.Text = string.Empty;
        }
        catch (Exception ex)
        {
            _statusText.Text = string.Format(_text("TouchFinderPortError"), ex.Message);
        }
        finally
        {
            _listenCancellation = null;
            _listenButton.Content = _text("TouchFinderListen");
        }
    }

    // "CHIP=Gt911;SDA=19;SCL=20", "UNSUPPORTED=STMPE610;SDA=..", "NONE"
    private void ApplyFinderResult(string payload)
    {
        var fields = payload
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2)
            .ToDictionary(part => part[0].Trim(), part => part[1].Trim(), StringComparer.OrdinalIgnoreCase);

        if (fields.TryGetValue("UNSUPPORTED", out var unsupported))
        {
            _statusText.Text = string.Format(_text("TouchFinderUnsupported"), unsupported);
            return;
        }

        if (!fields.TryGetValue("CHIP", out var chipName) || !Enum.TryParse<TouchChip>(chipName, true, out var chip))
        {
            _statusText.Text = _text("TouchFinderNone");
            return;
        }

        int Pin(string name, int fallback) =>
            fields.TryGetValue(name, out var value) && int.TryParse(value, out var pin) ? pin : fallback;

        _config = _config with
        {
            Chip = chip,
            Output = TouchOutput.SerialTest,
            Sda = Pin("SDA", _config.Sda),
            Scl = Pin("SCL", _config.Scl),
            Sck = Pin("SCK", _config.Sck),
            Miso = Pin("MISO", _config.Miso),
            Mosi = Pin("MOSI", _config.Mosi),
            Cs = Pin("CS", _config.Cs)
        };
        _showingFinder = false;
        SyncControls();
        Regenerate();

        var pins = string.Join(", ", fields
            .Where(field => !field.Key.Equals("CHIP", StringComparison.OrdinalIgnoreCase))
            .Select(field => $"{field.Key} {field.Value}"));
        _statusText.Text = string.Format(_text("TouchFinderFound"), TouchCodeGenerator.ChipName(chip), pins);
    }

    // ---------------------------------------------------------------- Actions

    private void CopyCode()
    {
        try
        {
            Clipboard.SetText(_codeBox.Text);
            _statusText.Text = _text("TouchCopied");
        }
        catch
        {
            _statusText.Text = _text("TouchCopyFailed");
        }
    }

    private void SaveCode()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = CurrentSketchName() + ".ino",
            Filter = "Arduino sketch (*.ino)|*.ino",
            DefaultExt = ".ino",
            InitialDirectory = ArduinoSketchbook()
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var path = WriteSketch(dialog.FileName);
            _statusText.Text = string.Format(_text("TouchSaved"), path);
        }
        catch (Exception ex)
        {
            _statusText.Text = string.Format(_text("TouchSaveFailed"), ex.Message);
        }
    }

    // The Arduino IDE only opens a sketch whose folder has the same name.
    private string WriteSketch(string chosenPath)
    {
        var name = Path.GetFileNameWithoutExtension(chosenPath);
        var folder = Path.GetDirectoryName(chosenPath) ?? ArduinoSketchbook();
        if (!string.Equals(Path.GetFileName(folder), name, StringComparison.OrdinalIgnoreCase))
            folder = Path.Combine(folder, name);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".ino");
        File.WriteAllText(path, _codeBox.Text);
        return path;
    }

    private async Task OpenOrInstallIdeAsync()
    {
        var ide = FindArduinoIde();
        if (ide is not null)
        {
            try
            {
                var name = CurrentSketchName();
                var path = WriteSketch(Path.Combine(ArduinoSketchbook(), name, name + ".ino"));
                Process.Start(new ProcessStartInfo(ide) { UseShellExecute = true, ArgumentList = { path } });
                _statusText.Text = string.Format(_text("TouchSaved"), path);
            }
            catch (Exception ex)
            {
                _statusText.Text = string.Format(_text("TouchSaveFailed"), ex.Message);
            }
            return;
        }

        _ideButton.IsEnabled = false;
        _statusText.Text = _text("TouchInstallingIde");
        var result = await WingetService.InstallAsync(ArduinoIdePackageId, null, CancellationToken.None);
        _ideButton.IsEnabled = true;
        _statusText.Text = result.Status is WingetInstallStatus.Installed or WingetInstallStatus.AlreadyInstalled
            ? _text("TouchIdeInstalled")
            : string.Format(_text("TouchIdeFailed"), _text(result.Message));
        UpdateIdeButton();
    }

    private void UpdateIdeButton() =>
        _ideButton.Content = FindArduinoIde() is null ? _text("TouchInstallIde") : _text("TouchOpenIde");

    private static string? FindArduinoIde()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Arduino IDE", "Arduino IDE.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Arduino IDE", "Arduino IDE.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Arduino", "arduino.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Arduino", "arduino.exe")
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string ArduinoSketchbook() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Arduino");

    // ---------------------------------------------------------------- Helpers

    private string OutputName(TouchOutput output) => _text(output switch
    {
        TouchOutput.TftEspiDemo => "TouchOutputTft",
        TouchOutput.LvglDriver => "TouchOutputLvgl",
        _ => "TouchOutputSerial"
    });

    private Button ChoiceButton<TValue>(string content, TValue value, Action apply, double width = double.NaN)
    {
        var button = SmallButton(content);
        button.Width = width;
        button.Tag = value;
        button.Click += (_, _) =>
        {
            apply();
            SyncControls();
            Regenerate();
        };
        return button;
    }

    private static void SetSelected<TValue>(Panel panel, TValue value)
    {
        foreach (var button in panel.Children.OfType<Button>())
        {
            var selected = Equals(button.Tag, value);
            button.BorderThickness = new Thickness(selected ? 2 : 1);
            if (selected)
            {
                button.BorderBrush = T("K.Accent");
                button.Foreground = T("K.Accent");
            }
            else
            {
                button.ClearValue(Control.BorderBrushProperty);
                button.ClearValue(Control.ForegroundProperty);
            }
        }
    }

    private static Button SmallButton(string content)
    {
        var button = new Button
        {
            Content = content,
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 3, 6, 3),
            FontSize = 11
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "K.Button.Secondary");
        return button;
    }

    private static Button BigButton(string content, bool primary)
    {
        var button = new Button
        {
            Content = content,
            Height = 34,
            MinWidth = 120,
            Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(8, 0, 0, 0)
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, primary ? "K.Button.Primary" : "K.Button.Secondary");
        return button;
    }

    private static TextBlock Heading(string text, bool first = false) => new()
    {
        Text = text,
        Foreground = T("K.Accent"),
        FontWeight = FontWeights.Bold,
        FontSize = 11,
        Margin = new Thickness(0, first ? 0 : 14, 0, 4)
    };

    private static TextBlock Label(string text, double leftMargin = 0) => new()
    {
        Text = text,
        Foreground = T("K.Text"),
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(leftMargin, 0, 8, 0)
    };

    private static TextBox NumberBox() => new()
    {
        Width = 58,
        Height = 28,
        FontSize = 12,
        Padding = new Thickness(6, 3, 6, 0),
        Margin = new Thickness(0, 3, 0, 3)
    };

    private CheckBox Check(string content, Action<bool> apply)
    {
        var box = new CheckBox
        {
            Content = content,
            Foreground = T("K.Text"),
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 2),
            Cursor = Cursors.Hand
        };
        box.Checked += (_, _) => { if (!_updating) { apply(true); Regenerate(); } };
        box.Unchecked += (_, _) => { if (!_updating) { apply(false); Regenerate(); } };
        return box;
    }

    private static Brush T(string key) => (Brush)Application.Current.FindResource(key);
}
