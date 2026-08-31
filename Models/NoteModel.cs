using System;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StickerMemo.Models
{
    public partial class NoteModel : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string? _remoteId = null; // Windows Sticky Notes ID for deduplication

        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _text = string.Empty;

        [ObservableProperty]
        private double _x = double.NaN;

        [ObservableProperty]
        private double _y = double.NaN;

        [ObservableProperty]
        private double _width = 340;

        [ObservableProperty]
        private double _height = 340;

        [ObservableProperty]
        private double _expandedHeight = 340;

        [ObservableProperty]
        private bool _isCollapsed = false;

        [ObservableProperty]
        private bool _isArchived = false;

        [ObservableProperty]
        private bool _isFloating = false;

        [ObservableProperty]
        private int _sortOrder = 0;

        [ObservableProperty]
        private string _themeId = "Yellow";

        [ObservableProperty]
        private bool _isTopmost = true;

        [ObservableProperty]
        private double _tapeAngle = -0.5;

        // Font settings per note
        [ObservableProperty]
        private string _fontFamily = "Malgun Gothic";

        [ObservableProperty]
        private double _fontSize = 13.5;

        [ObservableProperty]
        private bool _isBold = false;

        [ObservableProperty]
        private DateTime _createdAt = DateTime.Now;

        [ObservableProperty]
        private DateTime _updatedAt = DateTime.Now;

        // Cached preview properties to avoid expensive Regex parsing on every render
        private string _cachedMainTitle = "새 메모";
        private string? _cachedSubPreview = null;
        private string _cachedHoverBody = "(내용이 비어 있습니다)";

        public string DisplayMainTitle => _cachedMainTitle;
        public string? DisplaySubPreview => _cachedSubPreview;
        public string DisplayHoverBodyPreview => _cachedHoverBody;

        public NoteModel()
        {
            RecomputeCachedPreviews();
        }

        public void RecomputeCachedPreviews()
        {
            // 1. Main Title
            if (!string.IsNullOrWhiteSpace(Title))
            {
                var cleanTitle = CleanPreviewString(Title);
                _cachedMainTitle = cleanTitle.Length > 18 ? cleanTitle.Substring(0, 16) + "…" : cleanTitle;
            }
            else if (!string.IsNullOrWhiteSpace(Text))
            {
                var firstLine = CleanPreviewString(Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
                if (!string.IsNullOrWhiteSpace(firstLine))
                {
                    _cachedMainTitle = firstLine.Length > 20 ? firstLine.Substring(0, 18) + "…" : firstLine;
                }
                else
                {
                    _cachedMainTitle = "새 메모";
                }
            }
            else
            {
                _cachedMainTitle = "새 메모";
            }

            // 2. Sub Preview
            if (!string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(Text))
            {
                var clean = CleanPreviewString(Text);
                _cachedSubPreview = !string.IsNullOrWhiteSpace(clean) 
                    ? (clean.Length > 22 ? clean.Substring(0, 20) + "…" : clean)
                    : null;
            }
            else
            {
                _cachedSubPreview = null;
            }

            // 3. Hover Body Preview
            if (string.IsNullOrWhiteSpace(Text))
            {
                _cachedHoverBody = "(내용이 비어 있습니다)";
            }
            else
            {
                var clean = CleanPreviewString(Text);
                _cachedHoverBody = clean.Length > 85 ? clean.Substring(0, 80) + "…" : clean;
            }

            OnPropertyChanged(nameof(DisplayMainTitle));
            OnPropertyChanged(nameof(DisplaySubPreview));
            OnPropertyChanged(nameof(DisplayHoverBodyPreview));
        }

        private static readonly Regex WhitespaceRegex = new(@"[\r\n\t]+", RegexOptions.Compiled);
        private static readonly Regex MultiSpaceRegex = new(@"\s{2,}", RegexOptions.Compiled);

        private static string CleanPreviewString(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var clean = WhitespaceRegex.Replace(input, " ");
            clean = MultiSpaceRegex.Replace(clean, " ");
            return clean.Trim();
        }

        partial void OnTextChanged(string value)
        {
            RecomputeCachedPreviews();
        }

        partial void OnTitleChanged(string value)
        {
            RecomputeCachedPreviews();
        }
    }
}
