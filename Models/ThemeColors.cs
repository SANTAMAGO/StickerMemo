using System.Collections.Generic;
using System.Windows.Media;

namespace StickerMemo.Models
{
    public class MemoTheme
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BackgroundHex { get; set; } = string.Empty;
        public string AccentHex { get; set; } = string.Empty;
        public string BorderHex { get; set; } = string.Empty;
        public string TapeHex { get; set; } = string.Empty;

        public SolidColorBrush BackgroundBrush => (SolidColorBrush)new BrushConverter().ConvertFromString(BackgroundHex)!;
        public SolidColorBrush AccentBrush => (SolidColorBrush)new BrushConverter().ConvertFromString(AccentHex)!;
        public SolidColorBrush BorderBrush => (SolidColorBrush)new BrushConverter().ConvertFromString(BorderHex)!;
        public SolidColorBrush TapeBrush => (SolidColorBrush)new BrushConverter().ConvertFromString(TapeHex)!;
    }

    public static class ThemeColors
    {
        public static readonly Dictionary<string, MemoTheme> Themes = new()
        {
            ["Yellow"] = new MemoTheme
            {
                Id = "Yellow",
                Name = "Yellow",
                BackgroundHex = "#FEF08A",   // Clean, bright warm lemon sticky-note yellow
                AccentHex = "#A16207",       // Warm golden amber accent
                BorderHex = "#FACC15",       // Defined yellow border
                TapeHex = "#80FFFFFF"
            },
            ["Pink"] = new MemoTheme
            {
                Id = "Pink",
                Name = "Pink",
                BackgroundHex = "#FBCFE8",   // Soft blush pink, clearly pink
                AccentHex = "#BE185D",       // Deep rose berry accent
                BorderHex = "#F472B6",       // Defined pink border
                TapeHex = "#80FFFFFF"
            },
            ["Mint"] = new MemoTheme
            {
                Id = "Mint",
                Name = "Mint",
                BackgroundHex = "#A7F3D0",   // Fresh pastel mint with recognizable green hue
                AccentHex = "#047857",       // Forest mint accent
                BorderHex = "#34D399",       // Defined emerald-mint border
                TapeHex = "#80FFFFFF"
            },
            ["Sky"] = new MemoTheme
            {
                Id = "Sky",
                Name = "Blue",
                BackgroundHex = "#BAE6FD",   // Clear soft sky blue
                AccentHex = "#0369A1",       // Ocean blue accent
                BorderHex = "#38BDF8",       // Defined sky blue border
                TapeHex = "#80FFFFFF"
            },
            ["Blue"] = new MemoTheme
            {
                Id = "Blue",
                Name = "Blue",
                BackgroundHex = "#BAE6FD",   // Clear soft sky blue
                AccentHex = "#0369A1",       // Ocean blue accent
                BorderHex = "#38BDF8",       // Defined sky blue border
                TapeHex = "#80FFFFFF"
            },
            ["Purple"] = new MemoTheme
            {
                Id = "Purple",
                Name = "Purple",
                BackgroundHex = "#DDD6FE",   // Recognizable soft lavender purple
                AccentHex = "#6D28D9",       // Deep violet purple accent
                BorderHex = "#A78BFA",       // Defined lavender border
                TapeHex = "#80FFFFFF"
            },
            ["Beige"] = new MemoTheme
            {
                Id = "Beige",
                Name = "Kraft",
                BackgroundHex = "#E6D5B8",   // Warm tan / light brown recycled-paper tone
                AccentHex = "#78350F",       // Deep kraft brown accent
                BorderHex = "#C7A77D",       // Defined tan brown border
                TapeHex = "#80FFFFFF"
            },
            ["Kraft"] = new MemoTheme
            {
                Id = "Kraft",
                Name = "Kraft",
                BackgroundHex = "#E6D5B8",   // Warm tan / light brown recycled-paper tone
                AccentHex = "#78350F",       // Deep kraft brown accent
                BorderHex = "#C7A77D",       // Defined tan brown border
                TapeHex = "#80FFFFFF"
            }
        };

        public static MemoTheme GetTheme(string? themeId)
        {
            if (!string.IsNullOrEmpty(themeId) && Themes.TryGetValue(themeId, out var theme))
            {
                return theme;
            }
            return Themes["Yellow"];
        }

        public static string MapWindowsStickyTheme(string? winTheme)
        {
            if (string.IsNullOrEmpty(winTheme)) return "Yellow";
            return winTheme.ToLower() switch
            {
                "yellow" => "Yellow",
                "green" => "Mint",
                "pink" => "Pink",
                "purple" => "Purple",
                "blue" => "Blue",
                "charcoal" => "Kraft",
                "grey" or "gray" => "Kraft",
                _ => "Yellow"
            };
        }
    }
}
