using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace KLauncher.Services;

internal sealed class SystemTrayIcon : IDisposable
{
    private const uint NotifyIconAdd = 0;
    private const uint NotifyIconDelete = 2;
    private const uint NotifyIconMessage = 1;
    private const uint NotifyIconIcon = 2;
    private const uint NotifyIconTip = 4;
    private const int TrayCallbackMessage = 0x8001;
    private const int LeftButtonUp = 0x0202;
    private const int RightButtonUp = 0x0205;

    private readonly Window _owner;
    private readonly ContextMenu _menu;
    private readonly Action _restore;
    private readonly Icon? _icon;
    private HwndSource? _source;
    private NotifyIconData _data;
    private bool _isVisible;
    private bool _isDisposed;

    public SystemTrayIcon(Window owner, ContextMenu menu, Action restore)
    {
        _owner = owner;
        _menu = menu;
        _restore = restore;
        _icon = Environment.ProcessPath is { } executablePath
            ? Icon.ExtractAssociatedIcon(executablePath)
            : null;

        _owner.SourceInitialized += Owner_SourceInitialized;
        _owner.Closed += Owner_Closed;
    }

    public void Show()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SystemTrayIcon));
        if (_isVisible)
            return;

        var windowHandle = new WindowInteropHelper(_owner).Handle;
        if (windowHandle == IntPtr.Zero)
            throw new InvalidOperationException("The K-Launcher window handle is not ready for the system tray.");

        _data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = windowHandle,
            uID = 1,
            uFlags = NotifyIconMessage | NotifyIconIcon | NotifyIconTip,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _icon?.Handle ?? IntPtr.Zero,
            szTip = "K-Launcher"
        };

        if (_data.hIcon == IntPtr.Zero || !Shell_NotifyIcon(NotifyIconAdd, ref _data))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not add K-Launcher to the system tray.");

        _isVisible = true;
    }

    public void Hide()
    {
        if (!_isVisible)
            return;

        Shell_NotifyIcon(NotifyIconDelete, ref _data);
        _isVisible = false;
    }

    private void Owner_SourceInitialized(object? sender, EventArgs e)
    {
        _source = PresentationSource.FromVisual(_owner) as HwndSource
            ?? throw new InvalidOperationException("Could not attach the K-Launcher tray message handler.");
        _source.AddHook(WindowMessageHook);
    }

    private void Owner_Closed(object? sender, EventArgs e) => Dispose();

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != TrayCallbackMessage)
            return IntPtr.Zero;

        handled = true;
        switch (unchecked((int)lParam.ToInt64()))
        {
            case LeftButtonUp:
                Hide();
                _restore();
                break;
            case RightButtonUp:
                _menu.PlacementTarget = _owner;
                _menu.Placement = PlacementMode.MousePoint;
                _menu.IsOpen = true;
                SetForegroundWindow(hwnd);
                break;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        Hide();
        if (_source is not null)
            _source.RemoveHook(WindowMessageHook);
        _icon?.Dispose();
        _isDisposed = true;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string? szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string? szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }
}
