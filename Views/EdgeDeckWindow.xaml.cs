using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StickerMemo.Models;
using StickerMemo.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfCursors = System.Windows.Input.Cursors;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfPanel = System.Windows.Controls.Panel;
using WpfDock = System.Windows.Controls.Dock;

namespace StickerMemo.Views
{
    public enum DeckState
    {
        Dormant,
        Fan
    }

    public partial class EdgeDeckWindow : Window
    {
        private DeckState _currentState = DeckState.Dormant;
        private readonly List<NoteModel> _allNotes = new();
        private Border? _currentlyHoveredTab;
        private WpfButton? _moreNotesButton;
        private readonly NoteStorageService _storage = WindowManager.Instance.Storage;
        private readonly DispatcherTimer _hoverCloseTimer;
        private readonly DispatcherTimer _tabLeaveTimer;
        private readonly DispatcherTimer _searchDebounceTimer;
        private bool _isAllNotesOpen = false;
        private bool _isArchivedTabSelected = false;

        private const double CompactTabWidth = 135.0;
        private const double CompactTabHeight = 34.0;
        private const double HoverPreviewWidth = 250.0;
        private const double HoverPreviewHeight = 80.0;
        private const double StripeTabWidth = 16.0;

        public EdgeDeckWindow()
        {
            InitializeComponent();

            _hoverCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _hoverCloseTimer.Tick += (s, e) =>
            {
                _hoverCloseTimer.Stop();
                if (DeckEdgeContainer.IsMouseOver) return;

                if (_currentState == DeckState.Fan)
                {
                    CollapseHoveredTabPreviewImmediate();
                    TransitionToState(DeckState.Dormant);
                }
            };

            _tabLeaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _tabLeaveTimer.Tick += (s, e) =>
            {
                _tabLeaveTimer.Stop();
                if (_currentlyHoveredTab != null && _currentState == DeckState.Fan)
                {
                    CollapseTabPreview(_currentlyHoveredTab);
                    _currentlyHoveredTab = null;
                }
            };

            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                RenderAllNotesList(AllNotesSearchTextBox.Text);
            };

            Loaded += OnLoaded;
            Closed += OnClosed;
            KeyDown += OnWindowKeyDown;
            SourceInitialized += OnSourceInitialized;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var source = System.Windows.Interop.HwndSource.FromHwnd(handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_CLOSE = 0x0010;
            if (msg == WM_CLOSE && !WindowManager.Instance.IsShuttingDown)
            {
                handled = true;
                WindowManager.Instance.ExitApplication();
                return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _hoverCloseTimer.Stop();
            _tabLeaveTimer.Stop();
            _searchDebounceTimer.Stop();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AppIconHelper.ApplyAppIcon(this);
            PositionOnScreenEdge();
            LoadNotesFromStorage();
            TransitionToState(DeckState.Dormant, animate: false);
        }

        public void PositionOnScreenEdge()
        {
            var workingArea = SystemParameters.WorkArea;
            Width = 460;
            Height = Math.Min(640, workingArea.Height * 0.75);

            Left = workingArea.Right - Width;
            Top = workingArea.Top + (workingArea.Height - Height) / 2;
        }

        public void LoadNotesFromStorage()
        {
            _allNotes.Clear();
            var activeNotes = _storage.GetAllActiveNotes();
            var archivedNotes = _storage.GetAllArchivedNotes();

            _allNotes.AddRange(activeNotes);
            _allNotes.AddRange(archivedNotes);

            if (_allNotes.Count == 0)
            {
                // Create Initial Welcome Note
                var welcome = new NoteModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "반가워요!",
                    Text = "📌 StickerMemo Edge Deck입니다.\n\n• 평소에는 화면 오른쪽 끝에 얇은 띠(Stripe)로 숨어 있습니다.\n• 마우스를 탭 위로 올리면 본문 3줄 미리보기가 펼쳐집니다.\n• 메모 탭을 클릭하면 화면 어디든 자유롭게 드래그 배치할 수 있는 Floating Note로 열립니다.\n• Floating Note 상단 🔤 버튼으로 글꼴/크기/굵기를 변경할 수 있습니다.",
                    ThemeId = "Yellow",
                    SortOrder = 0,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                _allNotes.Add(welcome);
                ObserveBackgroundMutation(_storage.SaveOrUpdateNoteAsync(welcome), "create welcome note");
            }

            RenderNoteTabs();
            if (_isAllNotesOpen)
            {
                RenderAllNotesList(AllNotesSearchTextBox.Text);
            }
        }

        private List<NoteModel> GetActiveNotes()
        {
            return _allNotes.Where(n => !n.IsArchived).OrderBy(n => n.SortOrder).ThenByDescending(n => n.UpdatedAt).ToList();
        }

        public void RenderNoteTabs()
        {
            TabsStackPanel.Children.Clear();

            var activeNotes = GetActiveNotes();
            var visibleNotes = activeNotes.Take(8).ToList();

            for (int i = 0; i < visibleNotes.Count; i++)
            {
                var note = visibleNotes[i];
                var tabElement = CreateTabElement(note, i);
                TabsStackPanel.Children.Add(tabElement);
            }

            // +N More button if there are more than 8 active notes
            if (activeNotes.Count > 8)
            {
                int hiddenCount = activeNotes.Count - 8;
                _moreNotesButton = CreateMoreNotesButton(hiddenCount);
                TabsStackPanel.Children.Add(_moreNotesButton);
            }
            else
            {
                _moreNotesButton = null;
            }

            // Add button
            TabsStackPanel.Children.Add(AddNewNoteDeckButton);
        }

        public void NotifyNoteTextChanged(NoteModel note)
        {
            note.RecomputeCachedPreviews();

            // Find matching tab in UI without reconstructing entire Deck
            foreach (UIElement child in TabsStackPanel.Children)
            {
                if (child is Border tabBorder && tabBorder.Tag is NoteModel n && n.Id == note.Id)
                {
                    if (tabBorder.Child is Grid grid)
                    {
                        // 1. Compact View Title
                        if (grid.Children.Count > 0 && grid.Children[0] is StackPanel compact && compact.Children.Count > 1 && compact.Children[1] is TextBlock cTitle)
                        {
                            cTitle.Text = note.DisplayMainTitle;
                        }

                        // 2. Hover Preview View Title & Body
                        if (grid.Children.Count > 1 && grid.Children[1] is StackPanel hover && hover.Children.Count > 1)
                        {
                            if (hover.Children[0] is DockPanel header && header.Children.Count > 1 && header.Children[1] is TextBlock hTitle)
                            {
                                hTitle.Text = note.DisplayMainTitle;
                            }
                            if (hover.Children[1] is TextBlock hBody)
                            {
                                hBody.Text = note.DisplayHoverBodyPreview;
                            }
                        }
                    }
                    break;
                }
            }
        }

        public void NotifyNoteThemeChanged(NoteModel note)
        {
            var theme = ThemeColors.GetTheme(note.ThemeId);
            bool isFloating = WindowManager.Instance.IsNoteFloating(note.Id);

            // 1. Update matching tab in Edge Index
            foreach (UIElement child in TabsStackPanel.Children)
            {
                if (child is Border tabBorder && tabBorder.Tag is NoteModel n && n.Id == note.Id)
                {
                    tabBorder.Background = theme.BackgroundBrush;
                    tabBorder.BorderBrush = isFloating ? theme.AccentBrush : theme.BorderBrush;

                    if (tabBorder.Child is Grid grid)
                    {
                        // Compact Tape
                        if (grid.Children.Count > 0 && grid.Children[0] is StackPanel compact && compact.Children.Count > 0 && compact.Children[0] is Border tape)
                        {
                            tape.Background = theme.AccentBrush;
                        }

                        // Hover Preview Header Tape
                        if (grid.Children.Count > 1 && grid.Children[1] is StackPanel hover && hover.Children.Count > 0 && hover.Children[0] is DockPanel header && header.Children.Count > 0 && header.Children[0] is Border hTape)
                        {
                            hTape.Background = theme.AccentBrush;
                        }
                    }
                    break;
                }
            }

            // 2. If All Notes Drawer is open, re-render the list items
            if (_isAllNotesOpen)
            {
                RenderAllNotesList(AllNotesSearchTextBox.Text);
            }
        }

        public void UpdateNoteTabFloatingState(string noteId, bool isFloating)
        {
            foreach (UIElement child in TabsStackPanel.Children)
            {
                if (child is Border tabBorder && tabBorder.Tag is NoteModel note && note.Id == noteId)
                {
                    var theme = ThemeColors.GetTheme(note.ThemeId);
                    tabBorder.BorderBrush = isFloating ? theme.AccentBrush : theme.BorderBrush;
                    tabBorder.BorderThickness = isFloating ? new Thickness(2, 2, 0, 2) : new Thickness(1, 1, 0, 1);

                    if (tabBorder.Child is Grid grid && grid.Children.Count > 0 && grid.Children[0] is StackPanel compact)
                    {
                        // Check if pin icon exists
                        bool hasPin = compact.Children.OfType<TextBlock>().Any(tb => tb.Text == "📌");
                        if (isFloating && !hasPin)
                        {
                            compact.Children.Add(new TextBlock
                            {
                                Text = "📌",
                                FontSize = 9.5,
                                Margin = new Thickness(4, 0, 0, 0),
                                VerticalAlignment = VerticalAlignment.Center
                            });
                        }
                        else if (!isFloating && hasPin)
                        {
                            var pin = compact.Children.OfType<TextBlock>().FirstOrDefault(tb => tb.Text == "📌");
                            if (pin != null) compact.Children.Remove(pin);
                        }
                    }
                    break;
                }
            }
        }

        private WpfButton CreateMoreNotesButton(int hiddenCount)
        {
            var btn = new WpfButton
            {
                Style = (Style)FindResource("MoreNotesButtonStyle"),
                HorizontalAlignment = WpfHorizontalAlignment.Right,
                Margin = new Thickness(0, 3, 0, 2),
                ToolTip = $"전체 메모 및 보관함 열기 (+{hiddenCount} more)"
            };

            var stack = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var icon = new TextBlock
            {
                Text = "📂",
                FontSize = 10,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var label = new TextBlock
            {
                Text = $"+{hiddenCount} more",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(60, 60, 60)),
                VerticalAlignment = VerticalAlignment.Center
            };

            stack.Children.Add(icon);
            stack.Children.Add(label);
            btn.Content = stack;

            btn.Click += (s, e) =>
            {
                OpenAllNotesDrawer();
                e.Handled = true;
            };

            return btn;
        }

        private UIElement CreateTabElement(NoteModel note, int index)
        {
            var theme = ThemeColors.GetTheme(note.ThemeId);
            bool isFloating = WindowManager.Instance.IsNoteFloating(note.Id);

            var border = new Border
            {
                Tag = note,
                Width = StripeTabWidth,
                Height = CompactTabHeight,
                CornerRadius = new CornerRadius(8, 0, 0, 8),
                Background = theme.BackgroundBrush,
                BorderBrush = isFloating ? theme.AccentBrush : theme.BorderBrush,
                BorderThickness = isFloating ? new Thickness(2, 2, 0, 2) : new Thickness(1, 1, 0, 1),
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(6, 4, 6, 4),
                Cursor = WpfCursors.Hand,
                HorizontalAlignment = WpfHorizontalAlignment.Right,
                ClipToBounds = true,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = isFloating ? 10 : 6,
                    ShadowDepth = 2,
                    Direction = 180,
                    Opacity = isFloating ? 0.22 : 0.10
                }
            };

            // Main Grid inside Border
            var grid = new Grid();

            // 1. Compact View (1 line)
            var compactStack = new StackPanel
            {
                Name = "CompactStack",
                Orientation = WpfOrientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var tapeIndicator = new Border
            {
                Width = 4,
                Height = 18,
                CornerRadius = new CornerRadius(2),
                Background = theme.AccentBrush,
                Opacity = isFloating ? 1.0 : 0.85,
                Margin = new Thickness(0, 0, 6, 0)
            };

            var titleBlock = new TextBlock
            {
                Text = note.DisplayMainTitle,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(35, 35, 35)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 95,
                VerticalAlignment = VerticalAlignment.Center
            };

            compactStack.Children.Add(tapeIndicator);
            compactStack.Children.Add(titleBlock);

            if (isFloating)
            {
                var pinIcon = new TextBlock
                {
                    Text = "📌",
                    FontSize = 9.5,
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                compactStack.Children.Add(pinIcon);
            }

            // 2. Hover Preview View (Multi-line 2~3 lines preview, Width: 250px)
            var hoverStack = new StackPanel
            {
                Name = "HoverPreviewStack",
                Orientation = WpfOrientation.Vertical,
                Visibility = Visibility.Collapsed,
                Width = 230
            };

            var hoverHeader = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(0, 0, 0, 3)
            };

            var hoverTape = new Border
            {
                Width = 4,
                Height = 14,
                CornerRadius = new CornerRadius(2),
                Background = theme.AccentBrush,
                Margin = new Thickness(0, 0, 6, 0)
            };
            DockPanel.SetDock(hoverTape, WpfDock.Left);

            var hoverTitle = new TextBlock
            {
                Text = note.DisplayMainTitle,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(30, 30, 30)),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            hoverHeader.Children.Add(hoverTape);
            hoverHeader.Children.Add(hoverTitle);

            var hoverBody = new TextBlock
            {
                Text = note.DisplayHoverBodyPreview,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(65, 65, 65)),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 44,
                TextTrimming = TextTrimming.CharacterEllipsis,
                LineHeight = 14.5
            };

            hoverStack.Children.Add(hoverHeader);
            hoverStack.Children.Add(hoverBody);

            grid.Children.Add(compactStack);
            grid.Children.Add(hoverStack);
            border.Child = grid;

            // Tab Hover Events for Individual Hover Preview
            border.MouseEnter += (s, e) =>
            {
                if (_currentState == DeckState.Fan)
                {
                    _tabLeaveTimer.Stop();
                    if (_currentlyHoveredTab != null && _currentlyHoveredTab != border)
                    {
                        CollapseTabPreview(_currentlyHoveredTab);
                    }
                    _currentlyHoveredTab = border;
                    ExpandTabPreview(border);
                }
            };

            border.MouseLeave += (s, e) =>
            {
                if (_currentState == DeckState.Fan)
                {
                    _tabLeaveTimer.Stop();
                    _tabLeaveTimer.Start();
                }
            };

            border.MouseLeftButtonDown += async (s, e) =>
            {
                CollapseHoveredTabPreviewImmediate();
                await WindowManager.Instance.OpenFloatingNoteAsync(note);
                e.Handled = true;
            };

            return border;
        }

        private void ExpandTabPreview(Border tab)
        {
            WpfPanel.SetZIndex(tab, 100);

            if (tab.Child is Grid grid)
            {
                if (grid.Children.Count > 0) grid.Children[0].Visibility = Visibility.Collapsed;
                if (grid.Children.Count > 1) grid.Children[1].Visibility = Visibility.Visible;
            }

            var widthAnim = new DoubleAnimation
            {
                From = tab.ActualWidth > 0 ? tab.ActualWidth : CompactTabWidth,
                To = HoverPreviewWidth,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var heightAnim = new DoubleAnimation
            {
                From = tab.ActualHeight > 0 ? tab.ActualHeight : CompactTabHeight,
                To = HoverPreviewHeight,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            tab.BeginAnimation(WidthProperty, widthAnim);
            tab.BeginAnimation(HeightProperty, heightAnim);
        }

        private void CollapseTabPreview(Border tab)
        {
            WpfPanel.SetZIndex(tab, 1);

            if (tab.Child is Grid grid)
            {
                if (grid.Children.Count > 0) grid.Children[0].Visibility = Visibility.Visible;
                if (grid.Children.Count > 1) grid.Children[1].Visibility = Visibility.Collapsed;
            }

            var widthAnim = new DoubleAnimation
            {
                From = tab.ActualWidth,
                To = CompactTabWidth,
                Duration = TimeSpan.FromMilliseconds(130),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var heightAnim = new DoubleAnimation
            {
                From = tab.ActualHeight,
                To = CompactTabHeight,
                Duration = TimeSpan.FromMilliseconds(130),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            tab.BeginAnimation(WidthProperty, widthAnim);
            tab.BeginAnimation(HeightProperty, heightAnim);
        }

        private void CollapseHoveredTabPreviewImmediate()
        {
            if (_currentlyHoveredTab != null)
            {
                _tabLeaveTimer.Stop();
                CollapseTabPreview(_currentlyHoveredTab);
                _currentlyHoveredTab = null;
            }
        }

        public void TransitionToState(DeckState state, bool animate = true)
        {
            _currentState = state;

            switch (state)
            {
                case DeckState.Dormant:
                    ApplyDormantState(animate);
                    break;

                case DeckState.Fan:
                    ApplyFanState(animate);
                    break;
            }
        }

        private void ApplyDormantState(bool animate)
        {
            CollapseHoveredTabPreviewImmediate();

            int idx = 0;
            foreach (UIElement child in TabsStackPanel.Children)
            {
                if (child is Border tabBorder && tabBorder.Tag is NoteModel)
                {
                    AnimateTab(tabBorder, targetWidth: StripeTabWidth, targetHeight: CompactTabHeight, delayMs: animate ? idx * 20 : 0);
                    if (tabBorder.Child is Grid grid)
                    {
                        if (grid.Children.Count > 0) grid.Children[0].Visibility = Visibility.Collapsed;
                        if (grid.Children.Count > 1) grid.Children[1].Visibility = Visibility.Collapsed;
                    }
                    idx++;
                }
            }

            if (_moreNotesButton != null)
            {
                _moreNotesButton.Visibility = Visibility.Collapsed;
            }

            AddNewNoteDeckButton.Visibility = Visibility.Collapsed;
        }

        private void ApplyFanState(bool animate)
        {
            int idx = 0;
            foreach (UIElement child in TabsStackPanel.Children)
            {
                if (child is Border tabBorder && tabBorder.Tag is NoteModel note)
                {
                    if (tabBorder.Child is Grid grid)
                    {
                        if (grid.Children.Count > 0) grid.Children[0].Visibility = Visibility.Visible;
                        if (grid.Children.Count > 1) grid.Children[1].Visibility = Visibility.Collapsed;
                    }
                    AnimateTab(tabBorder, targetWidth: CompactTabWidth, targetHeight: CompactTabHeight, delayMs: animate ? idx * 20 : 0);
                    idx++;
                }
            }

            if (_moreNotesButton != null)
            {
                _moreNotesButton.Visibility = Visibility.Visible;
            }

            AddNewNoteDeckButton.Visibility = Visibility.Visible;
        }

        private void AnimateTab(Border tab, double targetWidth, double targetHeight, int delayMs)
        {
            double fromWidth = double.IsNaN(tab.Width) || tab.Width <= 0 
                ? (tab.ActualWidth > 0 ? tab.ActualWidth : StripeTabWidth) 
                : tab.Width;

            double fromHeight = double.IsNaN(tab.Height) || tab.Height <= 0
                ? (tab.ActualHeight > 0 ? tab.ActualHeight : CompactTabHeight)
                : tab.Height;

            var widthAnim = new DoubleAnimation
            {
                From = fromWidth,
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(140),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                BeginTime = TimeSpan.FromMilliseconds(delayMs)
            };

            var heightAnim = new DoubleAnimation
            {
                From = fromHeight,
                To = targetHeight,
                Duration = TimeSpan.FromMilliseconds(140),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                BeginTime = TimeSpan.FromMilliseconds(delayMs)
            };

            tab.BeginAnimation(WidthProperty, widthAnim);
            tab.BeginAnimation(HeightProperty, heightAnim);
        }

        // ==============================================================
        // ALL NOTES & ARCHIVE DRAWER / SEARCH LOGIC (PERSISTENT PANEL)
        // ==============================================================
        public void OpenAllNotesDrawer()
        {
            _isAllNotesOpen = true;

            _isArchivedTabSelected = false;
            UpdateSegmentedTabUi();

            AllNotesSearchTextBox.Text = string.Empty;
            RenderAllNotesList(null);

            AllNotesContainer.Visibility = Visibility.Visible;
            var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
            var slideAnim = new DoubleAnimation(25.0, 0.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase() };

            AllNotesContainer.BeginAnimation(OpacityProperty, fadeAnim);
            AllNotesTranslateTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);
        }

        public void CloseAllNotesDrawer()
        {
            _isAllNotesOpen = false;

            if (AllNotesContainer.Visibility == Visibility.Visible)
            {
                var fadeAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(120));
                fadeAnim.Completed += (s, e) => AllNotesContainer.Visibility = Visibility.Collapsed;
                AllNotesContainer.BeginAnimation(OpacityProperty, fadeAnim);
            }
        }

        private void OnCloseAllNotesClick(object sender, RoutedEventArgs e)
        {
            CloseAllNotesDrawer();
        }

        private void OnActiveTabClick(object sender, RoutedEventArgs e)
        {
            if (!_isArchivedTabSelected) return;
            _isArchivedTabSelected = false;
            UpdateSegmentedTabUi();
            RenderAllNotesList(AllNotesSearchTextBox.Text);
        }

        private void OnArchivedTabClick(object sender, RoutedEventArgs e)
        {
            if (_isArchivedTabSelected) return;
            _isArchivedTabSelected = true;
            UpdateSegmentedTabUi();
            RenderAllNotesList(AllNotesSearchTextBox.Text);
        }

        private void UpdateSegmentedTabUi()
        {
            if (!_isArchivedTabSelected)
            {
                ActiveTabButton.Background = WpfBrushes.White;
                ActiveTabLabel.FontWeight = FontWeights.Bold;
                ActiveTabLabel.Foreground = new SolidColorBrush(WpfColor.FromRgb(31, 41, 55));

                ArchivedTabButton.Background = WpfBrushes.Transparent;
                ArchivedTabLabel.FontWeight = FontWeights.Medium;
                ArchivedTabLabel.Foreground = new SolidColorBrush(WpfColor.FromRgb(107, 114, 128));
            }
            else
            {
                ActiveTabButton.Background = WpfBrushes.Transparent;
                ActiveTabLabel.FontWeight = FontWeights.Medium;
                ActiveTabLabel.Foreground = new SolidColorBrush(WpfColor.FromRgb(107, 114, 128));

                ArchivedTabButton.Background = WpfBrushes.White;
                ArchivedTabLabel.FontWeight = FontWeights.Bold;
                ArchivedTabLabel.Foreground = new SolidColorBrush(WpfColor.FromRgb(31, 41, 55));
            }
        }

        private void OnAllNotesSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void RenderAllNotesList(string? query)
        {
            AllNotesListStackPanel.Children.Clear();

            // Filter by Active vs Archived
            var baseList = _isArchivedTabSelected 
                ? _allNotes.Where(n => n.IsArchived) 
                : _allNotes.Where(n => !n.IsArchived);

            var filtered = baseList.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = query.Trim();
                filtered = filtered.Where(n => (n.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                               (n.Text?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var list = filtered.ToList();
            int totalTabCount = baseList.Count();
            AllNotesFooterText.Text = _isArchivedTabSelected 
                ? $"총 {totalTabCount}개의 보관된 메모 중 {list.Count}개 표시"
                : $"총 {totalTabCount}개의 활성 메모 중 {list.Count}개 표시";

            if (list.Count == 0)
            {
                var emptyBlock = new TextBlock
                {
                    Text = _isArchivedTabSelected ? "보관된 메모가 없습니다." : "표시할 활성 메모가 없습니다.",
                    FontSize = 11,
                    Foreground = WpfBrushes.Gray,
                    HorizontalAlignment = WpfHorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 20)
                };
                AllNotesListStackPanel.Children.Add(emptyBlock);
                return;
            }

            foreach (var note in list)
            {
                var theme = ThemeColors.GetTheme(note.ThemeId);
                bool isFloating = WindowManager.Instance.IsNoteFloating(note.Id);

                var itemBorder = new Border
                {
                    Background = new SolidColorBrush(WpfColor.FromArgb(240, 255, 255, 255)),
                    BorderBrush = isFloating ? theme.AccentBrush : theme.BorderBrush,
                    BorderThickness = isFloating ? new Thickness(1.5) : new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 6, 6),
                    Margin = new Thickness(0, 0, 0, 6),
                    Cursor = WpfCursors.Hand
                };

                var itemDock = new DockPanel { LastChildFill = true };

                // Theme color circle dot
                var dot = new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = theme.AccentBrush,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(dot, WpfDock.Left);

                // Right Action Buttons / Date
                var rightStack = new StackPanel { Orientation = WpfOrientation.Horizontal };

                if (_isArchivedTabSelected)
                {
                    // Restore Button
                    var restoreBtn = new WpfButton
                    {
                        Content = "↩",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(WpfColor.FromRgb(22, 101, 52)),
                        Background = new SolidColorBrush(WpfColor.FromArgb(30, 34, 197, 94)),
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(4, 1, 4, 1),
                        Margin = new Thickness(0, 0, 4, 0),
                        Cursor = WpfCursors.Hand,
                        ToolTip = "보관 해제 및 활성 메모로 복원 (Restore)"
                    };
                    restoreBtn.Click += async (s, e) =>
                    {
                        note.IsArchived = false;
                        try
                        {
                            await _storage.SaveOrUpdateNoteAsync(note);
                            RenderNoteTabs();
                            RenderAllNotesList(AllNotesSearchTextBox.Text);
                        }
                        catch (Exception ex)
                        {
                            note.IsArchived = true;
                            App.LogStartup($"[EdgeDeck] Restore failed for {note.Id}: {ex}");
                            AppConfirmDialog.ShowAlert(this, "Restore Error", "메모를 복원하지 못했습니다. 잠시 후 다시 시도해 주세요.", "OK", note.ThemeId);
                        }
                        e.Handled = true;
                    };
                    rightStack.Children.Add(restoreBtn);

                    // Delete Button
                    var deleteBtn = new WpfButton
                    {
                        Content = "🗑️",
                        FontSize = 10,
                        Background = WpfBrushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(2),
                        Margin = new Thickness(0, 0, 4, 0),
                        Cursor = WpfCursors.Hand,
                        ToolTip = "메모 영구 삭제"
                    };
                    deleteBtn.Click += async (s, e) =>
                    {
                        bool confirmed = AppConfirmDialog.Show(
                            owner: this,
                            title: "Delete this note?",
                            message: "This action cannot be undone.",
                            confirmText: "Delete",
                            cancelText: "Cancel",
                            isDestructive: true,
                            themeId: note.ThemeId);

                        if (confirmed)
                        {
                            try
                            {
                                await _storage.DeleteNoteAsync(note.Id);
                                _allNotes.Remove(note);
                                WindowManager.Instance.OnNoteDeleted(note.Id);
                                RenderAllNotesList(AllNotesSearchTextBox.Text);
                            }
                            catch (Exception ex)
                            {
                                App.LogStartup($"[EdgeDeck] Delete failed for {note.Id}: {ex}");
                                AppConfirmDialog.ShowAlert(this, "Delete Error", "메모를 삭제하지 못했습니다. 잠시 후 다시 시도해 주세요.", "OK", note.ThemeId);
                            }
                        }
                        e.Handled = true;
                    };
                    rightStack.Children.Add(deleteBtn);
                }
                else if (isFloating)
                {
                    rightStack.Children.Add(new TextBlock { Text = "📌", FontSize = 9, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                }

                // Date label
                rightStack.Children.Add(new TextBlock
                {
                    Text = $"{note.UpdatedAt:MM/dd}",
                    FontSize = 9.5,
                    Foreground = WpfBrushes.Gray,
                    VerticalAlignment = VerticalAlignment.Center
                });

                DockPanel.SetDock(rightStack, WpfDock.Right);

                // Main Title & snippet
                var textBlock = new TextBlock
                {
                    Text = note.DisplayMainTitle,
                    FontSize = 11.5,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(40, 40, 40)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };

                itemDock.Children.Add(dot);
                itemDock.Children.Add(rightStack);
                itemDock.Children.Add(textBlock);
                itemBorder.Child = itemDock;

                // Item click opens Floating Note (Drawer stays open unless user closes it)
                itemBorder.MouseLeftButtonDown += async (s, e) =>
                {
                    await WindowManager.Instance.OpenFloatingNoteAsync(note);
                    e.Handled = true;
                };

                // Hover highlight
                itemBorder.MouseEnter += (s, e) => itemBorder.Background = theme.BackgroundBrush;
                itemBorder.MouseLeave += (s, e) => itemBorder.Background = new SolidColorBrush(WpfColor.FromArgb(240, 255, 255, 255));

                AllNotesListStackPanel.Children.Add(itemBorder);
            }
        }

        private void OnDeckMouseEnter(object sender, MouseEventArgs e)
        {
            _hoverCloseTimer.Stop();

            if (_currentState == DeckState.Dormant)
            {
                TransitionToState(DeckState.Fan);
            }
        }

        private void OnDeckMouseLeave(object sender, MouseEventArgs e)
        {
            if (_currentState == DeckState.Fan)
            {
                _hoverCloseTimer.Stop();
                _hoverCloseTimer.Start();
            }
        }

        private async void OnAddNewNoteClick(object sender, RoutedEventArgs e)
        {
            await CreateNewNoteAndOpenAsync();
        }

        public async Task CreateNewNoteAndOpenAsync()
        {
            var newNote = new NoteModel
            {
                Id = Guid.NewGuid().ToString(),
                Title = string.Empty,
                Text = string.Empty,
                ThemeId = "Yellow",
                SortOrder = _allNotes.Count,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            try
            {
                await _storage.SaveOrUpdateNoteAsync(newNote);
            }
            catch (Exception ex)
            {
                App.LogStartup($"[EdgeDeck] Failed to create note {newNote.Id}: {ex}");
                AppConfirmDialog.ShowAlert(this, "Save Error", "새 메모를 저장하지 못했습니다. 잠시 후 다시 시도해 주세요.", "OK", newNote.ThemeId);
                return;
            }

            _allNotes.Insert(0, newNote);

            RenderNoteTabs();
            if (_isAllNotesOpen)
            {
                RenderAllNotesList(AllNotesSearchTextBox.Text);
            }
            await WindowManager.Instance.OpenFloatingNoteAsync(newNote);
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_isAllNotesOpen)
                {
                    CloseAllNotesDrawer();
                    e.Handled = true;
                }
            }
        }

        public void RefreshNotes()
        {
            LoadNotesFromStorage();
        }

        private static void ObserveBackgroundMutation(Task mutation, string operation)
        {
            _ = mutation.ContinueWith(
                task => App.LogStartup($"[EdgeDeck] Failed to {operation}: {task.Exception?.Flatten()}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
