using Application = System.Windows.Application;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using KLauncher.Services;

namespace KLauncher;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\KLauncher.SingleInstance";
    private const string ActivateEventName = @"Local\KLauncher.Activate";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateEvent;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LauncherLog.Write("HATA", $"Unhandled (terminating={args.IsTerminating}): {args.ExceptionObject}");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LauncherLog.Error("Unobserved task", args.Exception);
            args.SetObserved();
        };
        // Elevated helper for batch installs: no window, no install check, no
        // single-instance lock (the normal launcher is already running).
        if (e.Args.Length == 2 && e.Args[0] == WingetBatchHost.HelperArgument)
        {
            RunWingetBatchHelper(e.Args[1]);
            return;
        }

        try
        {
            // First start after a portable install: remove the portable EXE;
            // the desktop shortcut created on startup replaces it.
            var arguments = AppInstallationBootstrapper.RemovePortableCopy(e.Args);

            if (AppInstallationBootstrapper.InstallAndRelaunchIfNeeded(arguments))
            {
                Shutdown();
                return;
            }

            LauncherStorage.Initialize();
            MotionSettings.ApplyThemeTokens();

            if (!TryAcquireSingleInstance())
            {
                // Hand the start over to the running launcher. The message box
                // stays only as a fallback: started from the background it
                // often opened behind other windows and was heard but not seen.
                if (SignalRunningInstance())
                {
                    Shutdown();
                    return;
                }

                MessageBox.Show(
                    Text("AlreadyRunning", "K-Launcher zaten açık. Lütfen önce açık olan uygulamayı kapatın."),
                    "K-Launcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            var window = new MainWindow();
            MainWindow = window;
            ListenForSecondStart(window);
            window.Show();
            window.Activate();
            window.Focus();
        }
        catch (Exception ex)
        {
            ShowFatal(ex);
            Shutdown(-1);
        }
    }

    private async void RunWingetBatchHelper(string folder)
    {
        try
        {
            await WingetBatchHost.RunHelperAsync(folder);
        }
        catch
        {
            // The launcher reports unfinished items itself.
        }
        finally
        {
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
            _singleInstanceMutex?.ReleaseMutex();

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private bool TryAcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: false, SingleInstanceMutexName);
        try
        {
            _ownsSingleInstanceMutex = _singleInstanceMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsSingleInstanceMutex = true;
        }

        return _ownsSingleInstanceMutex;
    }

    // A wait registered on the thread pool: no thread or timer of its own.
    private void ListenForSecondStart(MainWindow window)
    {
        try
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            ThreadPool.RegisterWaitForSingleObject(
                _activateEvent,
                (_, _) => window.Dispatcher.BeginInvoke(window.BringToFront),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch
        {
            // Without the signal a second start falls back to the message box.
        }
    }

    private static bool SignalRunningInstance()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(ActivateEventName, out var activate))
                return false;

            using (activate)
            {
                // Let the running launcher take the foreground from this process.
                AllowSetForegroundWindow(-1);
                return activate.Set();
            }
        }
        catch
        {
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatal(e.Exception);
        e.Handled = true;
        Shutdown(-1);
    }

    // Startup messages may appear before the window exists; fall back to the
    // built-in Turkish text if the localization resource is not ready.
    private static string Text(string key, string fallback)
    {
        try
        {
            if (Current?.Resources["Localization"] is LocalizationService localization)
            {
                var value = localization[key];
                if (!string.IsNullOrEmpty(value) && value != key)
                    return value;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private static void ShowFatal(Exception ex)
    {
        LauncherLog.Error("Startup/UI", ex);
        MessageBox.Show(
            string.Format(Text("StartupFailed", "K-Launcher başlatılamadı.\n\n{0}"), ex),
            "K-Launcher",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
