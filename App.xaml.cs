using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using StickerMemo.Services;
using StickerMemo.Views;
using WpfApplication = System.Windows.Application;

namespace StickerMemo
{
    public partial class App : WpfApplication
    {
        private static Mutex? _singleInstanceMutex;
        private const string MutexName = @"Global\StickerMemo_SingleInstance_Mutex_2026";
        private static readonly string StartupLogPath = Path.Combine(Path.GetTempPath(), "StickerMemo-startup.log");
        private static readonly object StartupLogLock = new();
        private const long MaxStartupLogBytes = 1024 * 1024;

        public static void LogStartup(string message)
        {
            try
            {
                lock (StartupLogLock)
                {
                    if (File.Exists(StartupLogPath) && new FileInfo(StartupLogPath).Length >= MaxStartupLogBytes)
                    {
                        File.Move(StartupLogPath, StartupLogPath + ".old", overwrite: true);
                    }

                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [PID:{Environment.ProcessId}] {message}";
                    File.AppendAllText(StartupLogPath, line + Environment.NewLine);
                    Console.WriteLine(line);
                    Debug.WriteLine(line);
                }
            }
            catch { }
        }

        public App()
        {
            // Global Unhandled Exception Handlers
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogStartup($"[CRITICAL] AppDomain.UnhandledException: {e.ExceptionObject}");
            };

            DispatcherUnhandledException += (s, e) =>
            {
                LogStartup($"[CRITICAL] DispatcherUnhandledException: {e.Exception}");
                e.Handled = false;
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogStartup($"[CRITICAL] UnobservedTaskException: {e.Exception}");
            };

            LogStartup("=== StickerMemo App Constructor Started ===");
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            LogStartup($"OnStartup triggered. ProcessPath: '{Environment.ProcessPath}', BaseDirectory: '{AppDomain.CurrentDomain.BaseDirectory}'");

            try
            {
                // Ensure single-instance application
                _singleInstanceMutex = new Mutex(true, MutexName, out bool isNewInstance);
                LogStartup($"Single-Instance Mutex acquired: isNewInstance={isNewInstance}");

                if (!isNewInstance)
                {
                    LogStartup("Another instance is already running; terminating silently.");
                    _singleInstanceMutex.Dispose();
                    _singleInstanceMutex = null;
                    Current.Shutdown(0);
                    return;
                }

                // Hook process exit to measure actual termination
                AppDomain.CurrentDomain.ProcessExit += (s, ev) =>
                {
                    LogStartup("AppDomain.ProcessExit fired.");
                };

                LogStartup("Calling WindowManager.Instance.Initialize()...");
                WindowManager.Instance.Initialize();
                LogStartup("WindowManager.Instance.Initialize() completed successfully.");
            }
            catch (Exception ex)
            {
                LogStartup($"[EXCEPTION in OnStartup] {ex}");
                try
                {
                    AppConfirmDialog.ShowAlert(
                        owner: null,
                        title: "StickerMemo Startup Error",
                        message: $"앱을 시작하지 못했습니다.\n\n{ex.Message}\n\n진단 로그: {StartupLogPath}",
                        confirmText: "OK");
                }
                catch (Exception dialogException)
                {
                    // Startup may have failed before WPF resources are usable. Keep
                    // the bounded diagnostic log as the safe final fallback.
                    LogStartup($"[EXCEPTION showing startup dialog] {dialogException}");
                }
                Current.Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            var sw = Stopwatch.StartNew();
            LogStartup("App.OnExit entered.");

            if (_singleInstanceMutex != null)
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                try { _singleInstanceMutex.Dispose(); } catch { }
                _singleInstanceMutex = null;
            }

            LogStartup($"App.OnExit completed in {sw.ElapsedMilliseconds}ms.");
            base.OnExit(e);
        }
    }
}
