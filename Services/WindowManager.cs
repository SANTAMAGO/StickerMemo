using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using StickerMemo.Models;
using StickerMemo.Views;
using Application = System.Windows.Application;
using WinFormsApp = System.Windows.Forms.Application;

namespace StickerMemo.Services
{
    public class WindowManager : IDisposable
    {
        public static WindowManager Instance { get; } = new();

        public EdgeDeckWindow? DeckWindow { get; private set; }
        public NoteStorageService Storage { get; } = new();
        private readonly Dictionary<string, FloatingNoteWindow> _openFloatingNotes = new();
        private readonly HashSet<string> _openingFloatingNotes = new();
        private NotifyIcon? _trayIcon;

        public bool IsShuttingDown { get; private set; } = false;

        public void Initialize()
        {
            App.LogStartup("WindowManager: Setting up Tray Icon...");
            SetupTrayIcon();

            App.LogStartup("WindowManager: Creating EdgeDeckWindow...");
            DeckWindow = new EdgeDeckWindow();
            DeckWindow.Show();
            App.LogStartup("WindowManager: EdgeDeckWindow shown.");

            App.LogStartup("WindowManager: Restoring Floating Notes...");
            RestoreFloatingNotes();
            App.LogStartup("WindowManager: RestoreFloatingNotes completed.");
        }

        private void RestoreFloatingNotes()
        {
            var activeNotes = Storage.GetAllActiveNotes();
            foreach (var note in activeNotes.Where(n => n.IsFloating))
            {
                ShowFloatingNote(note);
            }
        }

        private void SetupTrayIcon()
        {
            try
            {
                System.Drawing.Icon appIcon = SystemIcons.Application;
                try
                {
                    var resStream = Application.GetResourceStream(new Uri("pack://application:,,,/StickerMemo;component/Assets/app_icon.ico", UriKind.Absolute));
                    if (resStream != null)
                    {
                        using var s = resStream.Stream;
                        appIcon = new System.Drawing.Icon(s);
                    }
                    else
                    {
                        string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app_icon.ico");
                        if (System.IO.File.Exists(iconPath))
                        {
                            appIcon = new System.Drawing.Icon(iconPath);
                        }
                    }
                }
                catch { }

                _trayIcon = new NotifyIcon
                {
                    Text = "StickerMemo",
                    Icon = appIcon,
                    Visible = true
                };

                // Open custom WPF Paper-Card Tray Menu on Right Click
                _trayIcon.MouseClick += (s, e) =>
                {
                    if (e.Button == MouseButtons.Right)
                    {
                        TrayMenuWindow.ShowMenu();
                    }
                    else if (e.Button == MouseButtons.Left)
                    {
                        ShowDeck();
                    }
                };

                _trayIcon.DoubleClick += (s, e) => ShowDeck();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create tray icon: {ex}");
            }
        }

        public async Task OpenFloatingNoteAsync(NoteModel note)
        {
            if (IsShuttingDown) return;

            if (_openFloatingNotes.TryGetValue(note.Id, out var existingWindow))
            {
                if (existingWindow.WindowState == WindowState.Minimized)
                {
                    existingWindow.WindowState = WindowState.Normal;
                }
                existingWindow.Activate();
                existingWindow.Topmost = true;
                return;
            }

            if (!_openingFloatingNotes.Add(note.Id)) return;

            note.IsFloating = true;
            try
            {
                await Storage.SaveOrUpdateNoteAsync(note);
            }
            catch (Exception ex)
            {
                note.IsFloating = false;
                App.LogStartup($"[WindowManager] Failed to persist floating state for {note.Id}: {ex}");
                AppConfirmDialog.ShowAlert(DeckWindow, "Save Error", "메모 창 상태를 저장하지 못했습니다. 잠시 후 다시 시도해 주세요.", "OK", note.ThemeId);
                return;
            }
            finally
            {
                _openingFloatingNotes.Remove(note.Id);
            }

            ShowFloatingNote(note);
        }

        private void ShowFloatingNote(NoteModel note)
        {
            if (IsShuttingDown || _openFloatingNotes.ContainsKey(note.Id)) return;

            var win = new FloatingNoteWindow(note);
            _openFloatingNotes[note.Id] = win;
            win.Show();
            win.Activate();

            DeckWindow?.UpdateNoteTabFloatingState(note.Id, true);
        }

        public bool IsNoteFloating(string noteId)
        {
            return _openFloatingNotes.ContainsKey(noteId);
        }

        public void OnFloatingNoteClosed(string noteId)
        {
            if (IsShuttingDown) return;
            _openFloatingNotes.Remove(noteId);
            DeckWindow?.UpdateNoteTabFloatingState(noteId, false);
        }

        public void OnNoteDeleted(string noteId)
        {
            if (IsShuttingDown) return;
            if (_openFloatingNotes.TryGetValue(noteId, out var win))
            {
                _openFloatingNotes.Remove(noteId);
            }
            DeckWindow?.RefreshNotes();
        }

        public void ShowDeck()
        {
            if (IsShuttingDown) return;

            if (DeckWindow == null)
            {
                DeckWindow = new EdgeDeckWindow();
            }

            DeckWindow.PositionOnScreenEdge();
            DeckWindow.Show();
            DeckWindow.Activate();
            DeckWindow.TransitionToState(DeckState.Fan);
        }

        public void HideDeck()
        {
            DeckWindow?.Hide();
        }

        public async Task ImportStickyNotesAsync()
        {
            var existingIds = Storage.GetExistingRemoteIds();
            var result = StickyNotesImporter.ImportNotes(existingIds);

            if (!result.Success)
            {
                AppConfirmDialog.ShowAlert(DeckWindow, "Sticky Notes Import", result.Message, "OK");
                return;
            }

            if (result.ImportedCount == 0)
            {
                if (result.SkippedCount > 0)
                {
                    AppConfirmDialog.ShowAlert(DeckWindow, "Sticky Notes Import", $"가져올 새로운 메모가 없습니다. ({result.SkippedCount}개 기존 메모 건너뜀)", "OK");
                }
                else
                {
                    AppConfirmDialog.ShowAlert(DeckWindow, "Sticky Notes Import", "불러올 수 있는 활성 Windows 스티커 메모가 없습니다.", "OK");
                }
                return;
            }

            int order = 0;
            foreach (var note in result.Notes)
            {
                note.SortOrder = order++;
            }

            await Storage.SaveOrUpdateNotesBatchAsync(result.Notes);

            ShowDeck();
            DeckWindow?.RefreshNotes();

            AppConfirmDialog.ShowAlert(DeckWindow, "Import Complete", result.Message, "OK");
        }

        public void ExitApplication()
        {
            if (IsShuttingDown) return;

            // 1. Set IsShuttingDown flag immediately
            IsShuttingDown = true;

            // 2. Give in-flight saves a bounded chance to finish, then persist one
            // final snapshot of every open note through the process-wide write lock.
            var shutdownSaveBudget = TimeSpan.FromMilliseconds(1500);
            var saveDeadline = Stopwatch.StartNew();
            var openWindows = _openFloatingNotes.Values.ToList();
            var inFlightSaves = new List<Task>();
            foreach (var win in openWindows)
            {
                inFlightSaves.Add(win.PrepareForShutdownSave());
            }

            bool flushSucceeded = true;
            if (inFlightSaves.Count > 0)
            {
                var remaining = shutdownSaveBudget - saveDeadline.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    try
                    {
                        Task.WhenAll(inFlightSaves).Wait(remaining);
                    }
                    catch (AggregateException ex)
                    {
                        App.LogStartup($"[Shutdown] An in-flight save failed: {ex.Flatten()}");
                    }
                }

                remaining = shutdownSaveBudget - saveDeadline.Elapsed;
                try
                {
                    flushSucceeded = Storage.TrySaveOrUpdateNotesBatchSync(
                        openWindows.Select(w => w.Note),
                        remaining);
                }
                catch (Exception ex)
                {
                    flushSucceeded = false;
                    App.LogStartup($"[Shutdown] Final note flush failed: {ex}");
                }

                if (flushSucceeded)
                {
                    foreach (var win in openWindows)
                    {
                        win.MarkShutdownSaveSucceeded();
                    }
                    App.LogStartup($"[Shutdown] Final note flush completed in {saveDeadline.ElapsedMilliseconds}ms.");
                }
                else
                {
                    App.LogStartup($"[CRITICAL] Shutdown save budget expired or final note flush failed after {saveDeadline.ElapsedMilliseconds}ms.");
                }
            }

            // 3. Clear SQLite Connection Pools
            NoteStorageService.ClearConnectionPools();

            // 4. Dispose TrayIcon & WinForms Message Loop
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            try { WinFormsApp.ExitThread(); } catch { }
            try { WinFormsApp.Exit(); } catch { }

            // 5. Hide all active windows immediately (do not block on focus events)
            try
            {
                if (DeckWindow != null)
                {
                    DeckWindow.Visibility = Visibility.Collapsed;
                    DeckWindow.Hide();
                }
            }
            catch { }

            foreach (var win in _openFloatingNotes.Values.ToList())
            {
                try
                {
                    win.Visibility = Visibility.Collapsed;
                    win.Hide();
                }
                catch { }
            }
            _openFloatingNotes.Clear();

            // 6. Application Shutdown
            Application.Current?.Shutdown(0);
        }

        public void Dispose()
        {
            if (!IsShuttingDown)
            {
                ExitApplication();
            }
        }
    }
}
