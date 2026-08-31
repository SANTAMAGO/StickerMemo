using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using StickerMemo.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;

namespace StickerMemo.Views
{
    public partial class TrayMenuWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        private const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        public TrayMenuWindow()
        {
            InitializeComponent();
        }

        public static void ShowMenu()
        {
            var menu = new TrayMenuWindow();
            menu.UpdateMenuState();
            menu.Show();
            menu.PositionNearCursor();
            menu.Activate();
        }

        private void UpdateMenuState()
        {
            // 1. Deck Show / Hide toggle state
            bool isDeckVisible = WindowManager.Instance.DeckWindow != null && WindowManager.Instance.DeckWindow.IsVisible;
            if (isDeckVisible)
            {
                ToggleDeckIcon.Text = "🙈";
                ToggleDeckText.Text = "Hide Deck";
            }
            else
            {
                ToggleDeckIcon.Text = "🗂️";
                ToggleDeckText.Text = "Show Deck";
            }

            // 2. Start with Windows badge state
            bool isAutostart = StartupService.IsRunAtStartup();
            if (isAutostart)
            {
                StartupCheckBadge.Background = new SolidColorBrush(WpfColor.FromArgb(32, 16, 185, 129));
                StartupCheckText.Text = "✓ ON";
                StartupCheckText.Foreground = new SolidColorBrush(WpfColor.FromRgb(5, 150, 105));
            }
            else
            {
                StartupCheckBadge.Background = new SolidColorBrush(WpfColor.FromArgb(20, 107, 114, 128));
                StartupCheckText.Text = "OFF";
                StartupCheckText.Foreground = new SolidColorBrush(WpfColor.FromRgb(107, 114, 128));
            }
        }

        private void PositionNearCursor()
        {
            double menuWidth = Width > 0 ? Width : 220;
            double menuHeight = 230; // Estimated height with margins

            if (GetCursorPos(out POINT pt))
            {
                double dpiScale = GetDpiScaleForPoint(pt);
                var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y));
                var physicalWorkArea = screen.WorkingArea;
                var workLeft = physicalWorkArea.Left / dpiScale;
                var workTop = physicalWorkArea.Top / dpiScale;
                var workRight = physicalWorkArea.Right / dpiScale;
                var workBottom = physicalWorkArea.Bottom / dpiScale;
                var cursorX = pt.X / dpiScale;
                var cursorY = pt.Y / dpiScale;

                double targetX = cursorX - (menuWidth / 2);
                double targetY = cursorY - menuHeight;

                // Screen Boundary Protection
                if (targetX + menuWidth > workRight)
                {
                    targetX = workRight - menuWidth - 8;
                }
                if (targetX < workLeft)
                {
                    targetX = workLeft + 8;
                }

                if (targetY < workTop)
                {
                    targetY = cursorY + 8;
                }
                if (targetY + menuHeight > workBottom)
                {
                    targetY = workBottom - menuHeight - 8;
                }

                Left = targetX;
                Top = targetY;
            }
            else
            {
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Right - menuWidth - 12;
                Top = workArea.Bottom - menuHeight - 12;
            }
        }

        private double GetDpiScaleForPoint(POINT point)
        {
            try
            {
                var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
                if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0)
                {
                    return dpiX / 96.0;
                }
            }
            catch (DllNotFoundException)
            {
                // Fall back to WPF's current visual DPI on older Windows versions.
            }
            catch (EntryPointNotFoundException)
            {
                // Fall back to WPF's current visual DPI on older Windows versions.
            }

            return VisualTreeHelper.GetDpi(this).DpiScaleX;
        }

        private async void OnNewNoteClick(object sender, RoutedEventArgs e)
        {
            Close();
            WindowManager.Instance.ShowDeck();
            if (WindowManager.Instance.DeckWindow != null)
            {
                await WindowManager.Instance.DeckWindow.CreateNewNoteAndOpenAsync();
            }
        }

        private void OnToggleDeckClick(object sender, RoutedEventArgs e)
        {
            Close();
            bool isDeckVisible = WindowManager.Instance.DeckWindow != null && WindowManager.Instance.DeckWindow.IsVisible;
            if (isDeckVisible)
            {
                WindowManager.Instance.HideDeck();
            }
            else
            {
                WindowManager.Instance.ShowDeck();
            }
        }

        private void OnStartWithWindowsClick(object sender, RoutedEventArgs e)
        {
            bool current = StartupService.IsRunAtStartup();
            bool ok = StartupService.SetRunAtStartup(!current);
            if (!ok)
            {
                AppConfirmDialog.ShowAlert(this, "Settings Error", "Failed to update Windows startup settings.", "OK");
            }
            UpdateMenuState();
        }

        private async void OnImportStickyNotesClick(object sender, RoutedEventArgs e)
        {
            Close();
            await WindowManager.Instance.ImportStickyNotesAsync();
        }

        private void OnQuitAppClick(object sender, RoutedEventArgs e)
        {
            Hide();
            WindowManager.Instance.ExitApplication();
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            Close();
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }
    }
}
