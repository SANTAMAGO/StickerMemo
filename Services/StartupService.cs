using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace StickerMemo.Services
{
    public static class StartupService
    {
        private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "StickerMemo";

        public static bool IsRunAtStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: false);
                if (key == null) return false;

                var val = key.GetValue(AppName) as string;
                return !string.IsNullOrEmpty(val);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupService] Failed to check startup registry: {ex.Message}");
                return false;
            }
        }

        public static bool SetRunAtStartup(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: true);
                if (key == null) return false;

                if (enable)
                {
                    string? exePath = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    {
                        exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    }

                    if (string.IsNullOrEmpty(exePath))
                    {
                        return false;
                    }

                    // Wrap in quotes to handle paths with spaces safely
                    string command = $"\"{exePath}\" --autostart";
                    key.SetValue(AppName, command);
                }
                else
                {
                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, throwOnMissingValue: false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupService] Failed to modify startup registry: {ex.Message}");
                return false;
            }
        }
    }
}
