using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using WpfApp = System.Windows.Application;

namespace StickerMemo.Services
{
    public static class AppIconHelper
    {
        private static BitmapFrame? _cachedIconFrame;

        public static void ApplyAppIcon(Window window)
        {
            try
            {
                if (_cachedIconFrame != null)
                {
                    window.Icon = _cachedIconFrame;
                    return;
                }

                var uri = new Uri("pack://application:,,,/StickerMemo;component/Assets/app_icon.ico", UriKind.Absolute);
                var streamInfo = WpfApp.GetResourceStream(uri);
                if (streamInfo != null)
                {
                    using var stream = streamInfo.Stream;
                    _cachedIconFrame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    window.Icon = _cachedIconFrame;
                }
            }
            catch (Exception ex)
            {
                App.LogStartup($"[AppIconHelper] Warning: Failed to apply icon to window: {ex.Message}");
            }
        }
    }
}
