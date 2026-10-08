using Application = System.Windows.Application;
using System;
using System.Windows;
using System.Windows.Threading;
using KLauncher.Services;

namespace KLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        try
        {
            if (AppInstallationBootstrapper.InstallAndRelaunchIfNeeded(e.Args))
            {
                Shutdown();
                return;
            }

            var window = new MainWindow();
            MainWindow = window;
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

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatal(e.Exception);
        e.Handled = true;
        Shutdown(-1);
    }

    private static void ShowFatal(Exception ex)
    {
        MessageBox.Show(
            $"K-Launcher baÅŸlatÄ±lamadÄ±.\n\n{ex}",
            "K-Launcher",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}

