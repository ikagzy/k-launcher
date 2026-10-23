using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;

namespace KLauncher.Services;

/// <summary>
/// Logs the window messages for which Windows plays its error beep in the
/// default window procedure, so a beep with nothing on screen can be traced:
/// a click on a disabled/blocked window (WM_SETCURSOR with HTERROR), an
/// Alt+key with no matching menu (WM_MENUCHAR / WM_SYSCHAR), plus minimize and
/// restore commands for the timeline. Costs one cheap check per message.
/// </summary>
internal static class BeepWatch
{
    private const int WmSetCursor = 0x0020;
    private const int WmSysChar = 0x0106;
    private const int WmSysCommand = 0x0112;
    private const int WmMenuChar = 0x0120;
    private const short HtError = -2;

    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(window) is HwndSource source)
                source.AddHook((IntPtr _, int message, IntPtr wParam, IntPtr lParam, ref bool _) =>
                {
                    Inspect(window, message, wParam, lParam);
                    return IntPtr.Zero;
                });
        };
    }

    // Detailed message trace, only while Data\diag.on exists (checked once at start).
    private static readonly bool Diagnostics = File.Exists(LauncherStorage.GetFilePath("diag.on"));

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetMessageTime();

    private static void Trace(Window window, int message, IntPtr wParam, IntPtr lParam)
    {
        string? name = message switch
        {
            0x0005 => "WM_SIZE",
            0x0006 => "WM_ACTIVATE",
            0x0007 => "WM_SETFOCUS",
            0x0008 => "WM_KILLFOCUS",
            0x000A => "WM_ENABLE",
            0x0018 => "WM_SHOWWINDOW",
            0x001C => "WM_ACTIVATEAPP",
            0x001F => "WM_CANCELMODE",
            0x0020 => "WM_SETCURSOR",
            0x0086 => "WM_NCACTIVATE",
            0x00A1 => "WM_NCLBUTTONDOWN",
            0x00A5 => "WM_NCRBUTTONUP",
            0x0104 => "WM_SYSKEYDOWN",
            0x0106 => "WM_SYSCHAR",
            0x0112 => "WM_SYSCOMMAND",
            0x0120 => "WM_MENUCHAR",
            0x0313 => "WM_TASKBARMENU",
            _ => null
        };
        if (name is null)
            return;

        // WM_SETCURSOR is constant while the mouse moves; keep only button presses.
        if (message == 0x0020 && ((lParam.ToInt64() >> 16) & 0xFFFF) == 0x0200)
            return;

        var lag = Environment.TickCount - GetMessageTime();
        LauncherLog.Write("DIAG",
            $"{name} w={wParam.ToInt64():X} l={lParam.ToInt64():X} gecikme={lag}ms '{window.Title}' durum={window.WindowState} etkin={window.IsActive}");
    }

    private static void Inspect(Window window, int message, IntPtr wParam, IntPtr lParam)
    {
        if (Diagnostics)
            Trace(window, message, wParam, lParam);

        switch (message)
        {
            case WmSetCursor:
                var hitTest = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                var mouseMessage = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                // Only a button press on a disabled window beeps; mouse moves do not.
                if (hitTest == HtError && mouseMessage is 0x0201 or 0x0204 or 0x0207 or 0x00A1 or 0x00A4)
                    LauncherLog.Write("BIP", $"Tıklama engellenen pencereye geldi: {Describe(window)}");
                break;
            case WmMenuChar:
                LauncherLog.Write("BIP", $"Alt+tuş karşılıksız (WM_MENUCHAR '{(char)(wParam.ToInt64() & 0xFFFF)}'): {Describe(window)}");
                break;
            case WmSysChar:
                LauncherLog.Write("BIP?", $"Alt+tuş (WM_SYSCHAR '{(char)wParam.ToInt64()}'): {Describe(window)}");
                break;
            case WmSysCommand:
                var command = wParam.ToInt64() & 0xFFF0;
                if (command is 0xF020 or 0xF120)
                    LauncherLog.Write("PENCERE", $"{(command == 0xF020 ? "küçült" : "geri aç")} komutu: {Describe(window)}");
                break;
        }
    }

    private static string Describe(Window window)
    {
        try
        {
            var owned = window.OwnedWindows.Cast<Window>()
                .Select(w => $"'{w.Title}' görünür={w.IsVisible} durum={w.WindowState} etkin={w.IsActive}");
            return $"'{window.Title}' durum={window.WindowState} etkin={window.IsActive} açılabilir={window.IsEnabled} " +
                   $"sahipli=[{string.Join("; ", owned)}]";
        }
        catch (Exception ex)
        {
            return $"'{window.Title}' ({ex.GetType().Name})";
        }
    }
}
