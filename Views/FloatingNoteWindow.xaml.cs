using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using StickerMemo.Models;
using StickerMemo.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;

namespace StickerMemo.Views
{
    public partial class FloatingNoteWindow : Window
    {
        public NoteModel Note { get; }
        private readonly NoteStorageService _storage = WindowManager.Instance.Storage;
        private readonly DispatcherTimer _autoSaveTimer;
        private readonly DispatcherTimer _windowMoveSaveTimer;
        private readonly object _saveStateLock = new();
        private Task _inFlightSaveTask = Task.CompletedTask;
        private HwndSource? _hwndSource;
        private int _changeVersion;
        private bool _isInitializing = true;
        private bool _isFontListLoaded = false;
        private bool _isDirty = false;
        private bool _allowClose;
        private bool _isClosePending;

        // Static cached font families list to avoid re-enumerating on every window open
        private static List<string>? _cachedFontNames;

        #region Win32 API for Resize and Snap prevention
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MAXIMIZE = 0xF030;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTBOTTOMRIGHT = 17;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        #endregion

        public FloatingNoteWindow(NoteModel note)
        {
            Note = note;
            InitializeComponent();

            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _autoSaveTimer.Tick += OnAutoSaveTimerTick;

            _windowMoveSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _windowMoveSaveTimer.Tick += OnWindowMoveSaveTimerTick;

            Loaded += OnLoaded;
            Closed += OnClosed;
            Closing += OnClosing;
            SourceInitialized += OnSourceInitialized;
            LocationChanged += OnLocationOrSizeChanged;
            SizeChanged += OnLocationOrSizeChanged;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_MAXIMIZE)
            {
                handled = true; // Block Maximize & Snap
                return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AppIconHelper.ApplyAppIcon(this);
            InitPositionAndSize();
            ApplyNoteDataToUi();
            _isInitializing = false;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _autoSaveTimer.Stop();
            _windowMoveSaveTimer.Stop();
            _autoSaveTimer.Tick -= OnAutoSaveTimerTick;
            _windowMoveSaveTimer.Tick -= OnWindowMoveSaveTimerTick;
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
            Loaded -= OnLoaded;
            Closed -= OnClosed;
            Closing -= OnClosing;
            SourceInitialized -= OnSourceInitialized;
            LocationChanged -= OnLocationOrSizeChanged;
            SizeChanged -= OnLocationOrSizeChanged;
            WindowManager.Instance.OnFloatingNoteClosed(Note.Id);
        }

        private async void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_allowClose || WindowManager.Instance.IsShuttingDown) return;

            e.Cancel = true;
            await CloseAndDockAsync();
        }

        private void InitPositionAndSize()
        {
            var workArea = SystemParameters.WorkArea;

            Width = Note.Width >= 280 ? Note.Width : 350;
            Height = Note.Height >= 180 ? Note.Height : 350;

            if (!double.IsNaN(Note.X) && !double.IsNaN(Note.Y) && Note.X >= 0 && Note.Y >= 0)
            {
                Left = Math.Min(Note.X, workArea.Right - Width);
                Top = Math.Min(Note.Y, workArea.Bottom - Height);
            }
            else
            {
                // Default center position
                Left = workArea.Left + (workArea.Width - Width) / 2;
                Top = workArea.Top + (workArea.Height - Height) / 2;
                Note.X = Left;
                Note.Y = Top;
            }
        }

        private void LazyLoadFontComboBoxes()
        {
            if (_isFontListLoaded) return;
            _isFontListLoaded = true;

            if (_cachedFontNames == null)
            {
                var fontNames = new List<string> { "Malgun Gothic", "Pretendard", "Segoe UI", "Gowun Dodum", "Consolas", "Gulim", "Batang" };
                foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
                {
                    if (!fontNames.Contains(font.Source))
                    {
                        fontNames.Add(font.Source);
                    }
                }
                _cachedFontNames = fontNames;
            }

            FontFamilyComboBox.ItemsSource = _cachedFontNames;
            FontFamilyComboBox.SelectedItem = Note.FontFamily ?? "Malgun Gothic";
            UpdateFontControlsUi();
        }

        private void ApplyNoteDataToUi()
        {
            var theme = ThemeColors.GetTheme(Note.ThemeId);
            PaperBorder.Background = theme.BackgroundBrush;
            PaperBorder.BorderBrush = theme.BorderBrush;
            TapeThemeLabel.Text = theme.Name.ToUpper();
            TapeThemeLabel.Foreground = theme.AccentBrush;

            TitleTextBox.Text = Note.Title ?? string.Empty;
            ContentTextBox.Text = Note.Text ?? string.Empty;
            TimestampText.Text = $"수정: {Note.UpdatedAt:MM/dd HH:mm}";

            UpdateFontControlsUi();
            ApplyFontToEditor();
        }

        private void UpdateFontControlsUi()
        {
            FontSizeDisplayText.Text = $"{Note.FontSize:0.#} pt";

            var theme = ThemeColors.GetTheme(Note.ThemeId);
            if (Note.IsBold)
            {
                BoldChipBorder.Background = theme.AccentBrush;
                BoldChipBorder.BorderBrush = theme.BorderBrush;
                if (BoldChipBorder.Child is StackPanel sp)
                {
                    if (sp.Children.Count > 0 && sp.Children[0] is TextBlock tb1) tb1.Foreground = WpfBrushes.White;
                    if (sp.Children.Count > 1 && sp.Children[1] is TextBlock tb2) tb2.Foreground = WpfBrushes.White;
                }
            }
            else
            {
                BoldChipBorder.Background = new SolidColorBrush(WpfColor.FromArgb(18, 0, 0, 0));
                BoldChipBorder.BorderBrush = new SolidColorBrush(WpfColor.FromArgb(32, 0, 0, 0));
                if (BoldChipBorder.Child is StackPanel sp)
                {
                    if (sp.Children.Count > 0 && sp.Children[0] is TextBlock tb1) tb1.Foreground = new SolidColorBrush(WpfColor.FromRgb(50, 50, 50));
                    if (sp.Children.Count > 1 && sp.Children[1] is TextBlock tb2) tb2.Foreground = new SolidColorBrush(WpfColor.FromRgb(85, 85, 85));
                }
            }
        }

        private void ApplyFontToEditor()
        {
            try
            {
                if (!string.IsNullOrEmpty(Note.FontFamily))
                {
                    ContentTextBox.FontFamily = new WpfFontFamily(Note.FontFamily);
                    TitleTextBox.FontFamily = new WpfFontFamily(Note.FontFamily);
                }
                ContentTextBox.FontSize = Note.FontSize > 0 ? Note.FontSize : 13.5;
                ContentTextBox.FontWeight = Note.IsBold ? FontWeights.Bold : FontWeights.Normal;
            }
            catch
            {
                // Fallback safe font
                ContentTextBox.FontFamily = new WpfFontFamily("Segoe UI");
            }
        }

        private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void OnResizeThumbDragDelta(object sender, DragDeltaEventArgs e)
        {
            var workArea = SystemParameters.WorkArea;

            double newWidth = Width + e.HorizontalChange;
            double newHeight = Height + e.VerticalChange;

            newWidth = Math.Max(MinWidth, Math.Min(newWidth, workArea.Width));
            newHeight = Math.Max(MinHeight, Math.Min(newHeight, workArea.Height));

            Width = newWidth;
            Height = newHeight;

            Note.Width = Width;
            Note.Height = Height;
        }

        private void OnResizeThumbDragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (WindowManager.Instance.IsShuttingDown) return;

            Note.Width = Width;
            Note.Height = Height;
            _windowMoveSaveTimer.Stop();
            _windowMoveSaveTimer.Start();
        }

        private void OnLocationOrSizeChanged(object? sender, EventArgs e)
        {
            if (_isInitializing || WindowManager.Instance.IsShuttingDown) return;

            Note.X = Left;
            Note.Y = Top;
            Note.Width = Width;
            Note.Height = Height;

            // Debounce position/size saving
            _windowMoveSaveTimer.Stop();
            _windowMoveSaveTimer.Start();
        }

        private async void OnWindowMoveSaveTimerTick(object? sender, EventArgs e)
        {
            _windowMoveSaveTimer.Stop();
            if (WindowManager.Instance.IsShuttingDown) return;
            await StartTrackedSaveAsync(clearDirtyOnSuccess: false);
        }

        private void OnTitleTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing || WindowManager.Instance.IsShuttingDown) return;

            Note.Title = TitleTextBox.Text;
            MarkDirty();
            TriggerAutoSave();
            WindowManager.Instance.DeckWindow?.NotifyNoteTextChanged(Note);
        }

        private void OnContentTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing || WindowManager.Instance.IsShuttingDown) return;

            Note.Text = ContentTextBox.Text;
            MarkDirty();
            TriggerAutoSave();
            WindowManager.Instance.DeckWindow?.NotifyNoteTextChanged(Note);
        }

        private void OnFontMenuClick(object sender, RoutedEventArgs e)
        {
            LazyLoadFontComboBoxes();
            FontPopup.IsOpen = !FontPopup.IsOpen;
        }

        private void OnFontFamilySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || !_isFontListLoaded || WindowManager.Instance.IsShuttingDown) return;
            if (FontFamilyComboBox.SelectedItem is string fontName)
            {
                Note.FontFamily = fontName;
                ApplyFontToEditor();
                MarkDirty();
                TriggerAutoSave();
            }
        }

        private void OnDecreaseFontSizeClick(object sender, RoutedEventArgs e)
        {
            if (Note.FontSize > 8)
            {
                Note.FontSize = Math.Max(8, Math.Round(Note.FontSize - 1.0, 1));
                UpdateFontControlsUi();
                ApplyFontToEditor();
                MarkDirty();
                TriggerAutoSave();
            }
        }

        private void OnIncreaseFontSizeClick(object sender, RoutedEventArgs e)
        {
            if (Note.FontSize < 36)
            {
                Note.FontSize = Math.Min(36, Math.Round(Note.FontSize + 1.0, 1));
                UpdateFontControlsUi();
                ApplyFontToEditor();
                MarkDirty();
                TriggerAutoSave();
            }
        }

        private void OnBoldChipClick(object sender, MouseButtonEventArgs e)
        {
            Note.IsBold = !Note.IsBold;
            UpdateFontControlsUi();
            ApplyFontToEditor();
            MarkDirty();
            TriggerAutoSave();
        }

        private void OnColorPaletteClick(object sender, RoutedEventArgs e)
        {
            ColorPopup.IsOpen = !ColorPopup.IsOpen;
        }

        private void OnSelectThemeClick(object sender, RoutedEventArgs e)
        {
            if (sender is WpfButton btn && btn.Tag is string themeId)
            {
                Note.ThemeId = themeId;
                var theme = ThemeColors.GetTheme(themeId);

                PaperBorder.Background = theme.BackgroundBrush;
                PaperBorder.BorderBrush = theme.BorderBrush;
                TapeThemeLabel.Text = theme.Name.ToUpper();
                TapeThemeLabel.Foreground = theme.AccentBrush;

                UpdateFontControlsUi();

                ColorPopup.IsOpen = false;
                MarkDirty();
                TriggerAutoSave();
                WindowManager.Instance.DeckWindow?.NotifyNoteThemeChanged(Note);
            }
        }

        private async void OnArchiveClick(object sender, RoutedEventArgs e)
        {
            if (_isClosePending) return;
            _isClosePending = true;
            bool previousArchived = Note.IsArchived;
            bool previousFloating = Note.IsFloating;
            Note.IsArchived = true;
            Note.IsFloating = false;
            try
            {
                await _storage.SaveOrUpdateNoteAsync(Note);
                MarkSaved();
                WindowManager.Instance.OnFloatingNoteClosed(Note.Id);
                WindowManager.Instance.DeckWindow?.RefreshNotes();
                _allowClose = true;
                Close();
            }
            catch (Exception ex)
            {
                _isClosePending = false;
                Note.IsArchived = previousArchived;
                Note.IsFloating = previousFloating;
                App.LogStartup($"[FloatingNote] Archive save failed for {Note.Id}: {ex}");
                AppConfirmDialog.ShowAlert(this, "Save Error", "메모를 보관하지 못했습니다. 잠시 후 다시 시도해 주세요.", "OK", Note.ThemeId);
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_isClosePending) return;
            bool confirmed = AppConfirmDialog.Show(
                owner: this,
                title: "Delete this note?",
                message: "This action cannot be undone.",
                confirmText: "Delete",
                cancelText: "Cancel",
                isDestructive: true,
                themeId: Note.ThemeId);

            if (confirmed)
            {
                _isClosePending = true;
                try
                {
                    await _storage.DeleteNoteAsync(Note.Id);
                    WindowManager.Instance.OnNoteDeleted(Note.Id);
                    _allowClose = true;
                    Close();
                }
                catch (Exception ex)
                {
                    _isClosePending = false;
                    App.LogStartup($"[FloatingNote] Delete failed for {Note.Id}: {ex}");
                    AppConfirmDialog.ShowAlert(this, "Delete Error", "메모를 삭제하지 못했습니다. 잠시 후 다시 시도해 주세요.", "OK", Note.ThemeId);
                }
            }
        }

        private async void OnCloseDockBackClick(object sender, RoutedEventArgs e)
        {
            await CloseAndDockAsync();
        }

        private async Task CloseAndDockAsync()
        {
            if (_allowClose || _isClosePending || WindowManager.Instance.IsShuttingDown) return;

            _isClosePending = true;
            // Close Floating Window and Dock back to Edge Deck (Not deleting!)
            Note.IsFloating = false;
            try
            {
                await _storage.SaveOrUpdateNoteAsync(Note);
                MarkSaved();
                WindowManager.Instance.OnFloatingNoteClosed(Note.Id);
                _allowClose = true;
                Close();
            }
            catch (Exception ex)
            {
                _isClosePending = false;
                Note.IsFloating = true;
                App.LogStartup($"[FloatingNote] Dock save failed for {Note.Id}: {ex}");
                AppConfirmDialog.ShowAlert(this, "Save Error", "메모 상태를 저장하지 못했습니다. 잠시 후 다시 시도해 주세요.", "OK", Note.ThemeId);
            }
        }

        private void TriggerAutoSave()
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        private async void OnAutoSaveTimerTick(object? sender, EventArgs e)
        {
            _autoSaveTimer.Stop();
            if (WindowManager.Instance.IsShuttingDown) return;

            Note.UpdatedAt = DateTime.Now;
            TimestampText.Text = $"수정: {Note.UpdatedAt:MM/dd HH:mm}";
            await StartTrackedSaveAsync(clearDirtyOnSuccess: true);
        }

        /// <summary>
        /// Flushes any unsaved changes immediately during application shutdown
        /// </summary>
        public Task PrepareForShutdownSave()
        {
            _autoSaveTimer.Stop();
            _windowMoveSaveTimer.Stop();

            Note.Title = TitleTextBox.Text;
            Note.Text = ContentTextBox.Text;
            Note.X = Left;
            Note.Y = Top;
            Note.Width = Width;
            Note.Height = Height;
            Note.UpdatedAt = DateTime.Now;
            lock (_saveStateLock)
            {
                if (!_isDirty)
                {
                    _isDirty = true;
                }
                _changeVersion++;
                return _inFlightSaveTask;
            }
        }

        public void MarkShutdownSaveSucceeded()
        {
            MarkSaved();
        }

        private void MarkDirty()
        {
            lock (_saveStateLock)
            {
                _isDirty = true;
                _changeVersion++;
            }
        }

        private void MarkSaved()
        {
            lock (_saveStateLock)
            {
                _isDirty = false;
            }
        }

        private Task StartTrackedSaveAsync(bool clearDirtyOnSuccess)
        {
            int version;
            lock (_saveStateLock)
            {
                version = _changeVersion;
                _inFlightSaveTask = SaveTrackedAsync(version, clearDirtyOnSuccess);
                return _inFlightSaveTask;
            }
        }

        private async Task SaveTrackedAsync(int version, bool clearDirtyOnSuccess)
        {
            try
            {
                await _storage.SaveOrUpdateNoteAsync(Note).ConfigureAwait(false);
                if (clearDirtyOnSuccess)
                {
                    lock (_saveStateLock)
                    {
                        if (_changeVersion == version)
                        {
                            _isDirty = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogStartup($"[FloatingNote] Autosave failed for {Note.Id}: {ex}");
            }
        }
    }
}
