using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace KLauncher.Services;

/// <summary>
/// Makes the launcher as light as Windows allows while it is minimized or in
/// the tray, without ever blocking the UI thread: the message loop keeps
/// running (so Windows never reports "not responding"), it just gets less CPU
/// and gives its memory back first.
/// </summary>
public static class BackgroundEfficiency
{
    private const int ProcessMemoryPriority = 0;
    private const int ProcessPowerThrottling = 4;
    private const uint MemoryPriorityLow = 2;
    private const uint MemoryPriorityNormal = 5;
    private const uint PowerThrottlingCurrentVersion = 1;
    private const uint PowerThrottlingExecutionSpeed = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(
        IntPtr process, int informationClass, ref PowerThrottlingState information, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(
        IntPtr process, int informationClass, ref uint information, int size);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    private static DispatcherTimer? _trimTimer;
    private static int _trimTicks;

    public static bool IsActive { get; private set; }

    public static void Enter()
    {
        if (IsActive)
            return;

        IsActive = true;
        using (var process = Process.GetCurrentProcess())
        {
            TrySetPriority(process, ProcessPriorityClass.BelowNormal);
            SetEfficiencyMode(process.Handle, enabled: true);
            SetMemoryPriority(process.Handle, MemoryPriorityLow);
        }

        DeepTrim();

        // Work done right after minimizing (closing menus, saving caches) touches
        // pages again, so trim once more shortly after, then only now and then.
        _trimTicks = 0;
        _trimTimer ??= new DispatcherTimer(DispatcherPriority.Background);
        _trimTimer.Interval = TimeSpan.FromSeconds(8);
        _trimTimer.Tick -= OnTrimTick;
        _trimTimer.Tick += OnTrimTick;
        _trimTimer.Start();
    }

    public static void Exit()
    {
        if (!IsActive)
            return;

        IsActive = false;
        _trimTimer?.Stop();
        using var process = Process.GetCurrentProcess();
        SetEfficiencyMode(process.Handle, enabled: false);
        SetMemoryPriority(process.Handle, MemoryPriorityNormal);
        TrySetPriority(process, ProcessPriorityClass.Normal);
    }

    /// <summary>
    /// One GC + working-set trim while the window stays in the foreground,
    /// e.g. right after a feature is switched off. Pages the UI still uses
    /// come back on their own as it draws.
    /// </summary>
    public static void TrimOnce() => DeepTrim();

    private static void OnTrimTick(object? sender, EventArgs e)
    {
        if (!IsActive)
        {
            _trimTimer?.Stop();
            return;
        }

        _trimTicks++;
        if (_trimTicks == 1)
        {
            DeepTrim();
            _trimTimer!.Interval = TimeSpan.FromSeconds(60);
            return;
        }

        // Later ticks only hand untouched pages back; no GC, nothing to wait for.
        TryEmptyWorkingSet();
    }

    private static void DeepTrim()
    {
        try
        {
            // Aggressive also decommits the freed heap, so private memory drops too.
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        catch
        {
            // Trimming is optional.
        }

        TryEmptyWorkingSet();
    }

    private static void TryEmptyWorkingSet()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            EmptyWorkingSet(process.Handle);
        }
        catch
        {
            // Trimming is optional.
        }
    }

    // EcoQoS: Windows runs the process on efficient cores at low clock speed.
    private static void SetEfficiencyMode(IntPtr process, bool enabled)
    {
        try
        {
            var state = new PowerThrottlingState
            {
                Version = PowerThrottlingCurrentVersion,
                ControlMask = PowerThrottlingExecutionSpeed,
                StateMask = enabled ? PowerThrottlingExecutionSpeed : 0
            };
            SetProcessInformation(process, ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
        }
        catch
        {
            // Not available on older Windows builds.
        }
    }

    // Low memory priority: under pressure Windows reuses these pages first.
    private static void SetMemoryPriority(IntPtr process, uint priority)
    {
        try
        {
            SetProcessInformation(process, ProcessMemoryPriority, ref priority, sizeof(uint));
        }
        catch
        {
            // Optional.
        }
    }

    private static void TrySetPriority(Process process, ProcessPriorityClass priority)
    {
        try
        {
            process.PriorityClass = priority;
        }
        catch
        {
            // Priority can be denied by policy.
        }
    }
}
