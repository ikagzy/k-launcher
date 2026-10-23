using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace KLauncher.Services;

public sealed record SerialPortInfo(string Name, string Description);

/// <summary>
/// Minimal read-only serial port access through Win32, so the launcher needs
/// no extra package. DTR and RTS stay off: on ESP32 boards those lines drive
/// reset and boot mode, and the finder sketch repeats its result anyway.
/// </summary>
public static class SerialPortReader
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct Dcb
    {
        public uint DCBlength;
        public uint BaudRate;
        public uint Flags;
        public ushort wReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public sbyte XonChar;
        public sbyte XoffChar;
        public sbyte ErrorChar;
        public sbyte EofChar;
        public sbyte EvtChar;
        public ushort wReserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommTimeouts
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetCommState(SafeFileHandle handle, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommState(SafeFileHandle handle, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommTimeouts(SafeFileHandle handle, ref CommTimeouts timeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle handle, byte[] buffer, int count, out int read, IntPtr overlapped);

    /// <summary>COM ports from the registry, with the USB chip name when Windows knows it.</summary>
    public static IReadOnlyList<SerialPortInfo> ListPorts()
    {
        var names = new List<string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key is not null)
            {
                foreach (var valueName in key.GetValueNames())
                {
                    if (key.GetValue(valueName) is string port && port.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                        names.Add(port);
                }
            }
        }
        catch
        {
            // No ports, or the key cannot be read.
        }

        var descriptions = FriendlyNames();
        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => int.TryParse(name[3..], out var number) ? number : int.MaxValue)
            .Select(name => new SerialPortInfo(name, descriptions.GetValueOrDefault(name, string.Empty)))
            .ToList();
    }

    // "Silicon Labs CP210x USB to UART Bridge (COM5)" → CP210x etc.
    private static Dictionary<string, string> FriendlyNames()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usb is null)
                return result;

            foreach (var deviceName in usb.GetSubKeyNames())
            {
                using var device = usb.OpenSubKey(deviceName);
                if (device is null)
                    continue;
                foreach (var instanceName in device.GetSubKeyNames())
                {
                    using var instance = device.OpenSubKey(instanceName);
                    using var parameters = instance?.OpenSubKey("Device Parameters");
                    if (parameters?.GetValue("PortName") is not string port)
                        continue;
                    var friendly = instance?.GetValue("FriendlyName") as string ?? instance?.GetValue("DeviceDesc") as string;
                    if (string.IsNullOrWhiteSpace(friendly))
                        continue;
                    // DeviceDesc can be "@oem12.inf,%desc%;Readable name".
                    var semicolon = friendly.LastIndexOf(';');
                    if (semicolon >= 0)
                        friendly = friendly[(semicolon + 1)..];
                    result[port] = friendly.Replace($"({port})", string.Empty).Trim();
                }
            }
        }
        catch
        {
            // Descriptions are only a convenience.
        }

        return result;
    }

    /// <summary>
    /// Reads text lines from the port until <paramref name="onLine"/> returns
    /// true, the timeout passes or the token is cancelled.
    /// </summary>
    public static Task<bool> ReadLinesAsync(
        string portName,
        int baudRate,
        Func<string, bool> onLine,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var handle = CreateFile(@"\\.\" + portName, GenericRead | GenericWrite, 0, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var dcb = new Dcb { DCBlength = (uint)Marshal.SizeOf<Dcb>() };
            if (!GetCommState(handle, ref dcb))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            dcb.BaudRate = (uint)baudRate;
            dcb.ByteSize = 8;
            dcb.Parity = 0;
            dcb.StopBits = 0;
            dcb.Flags = 1; // binary mode; no flow control; DTR and RTS off
            if (!SetCommState(handle, ref dcb))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var timeouts = new CommTimeouts { ReadIntervalTimeout = 50, ReadTotalTimeoutConstant = 200 };
            SetCommTimeouts(handle, ref timeouts);

            var buffer = new byte[256];
            var pending = new StringBuilder();
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                if (!ReadFile(handle, buffer, buffer.Length, out var read, IntPtr.Zero))
                    throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
                if (read == 0)
                    continue;

                pending.Append(Encoding.UTF8.GetString(buffer, 0, read));
                var text = pending.ToString();
                int newline;
                while ((newline = text.IndexOf('\n')) >= 0)
                {
                    var line = text[..newline].TrimEnd('\r').Trim();
                    text = text[(newline + 1)..];
                    if (line.Length > 0 && onLine(line))
                        return true;
                }

                pending.Clear().Append(text);
            }

            return false;
        }, cancellationToken);
}
