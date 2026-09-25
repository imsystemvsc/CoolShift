using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace CoolShift;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("DispatcherUnhandledException", e.Exception);

        // Always mark handled so non-fatal UI / Tray / Binding / Shell exceptions do not crash the app
        e.Handled = true;
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException($"AppDomain.UnhandledException (IsTerminating={e.IsTerminating})", ex);
        }
        else
        {
            LogMessage($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERROR] [AppDomain.UnhandledException (IsTerminating={e.IsTerminating})] {e.ExceptionObject}");
        }
    }

    private static void LogException(string source, Exception ex)
    {
        var message = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERROR] [{source}] {ex}";
        LogMessage(message);
    }

    private static void LogMessage(string message)
    {
        try
        {
            Trace.WriteLine(message);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDirectory = Path.Combine(appData, "CoolShift", "Logs");
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(logDirectory, "CoolShift.log");
            File.AppendAllText(logPath, message + Environment.NewLine);

            var crashPath = Path.Combine(logDirectory, "CoolShift_Crash.log");
            File.AppendAllText(crashPath, message + Environment.NewLine);
        }
        catch
        {
            // Ignore logging failures
        }
    }
}


