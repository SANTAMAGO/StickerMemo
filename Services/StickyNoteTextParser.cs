using System;
using System.Text.RegularExpressions;

namespace StickerMemo.Services
{
    /// <summary>
    /// Parses and cleans raw content from Windows Sticky Notes (plum.sqlite) into standard plain text.
    /// </summary>
    public static class StickyNoteTextParser
    {
        public static string Parse(string? rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return string.Empty;
            }

            try
            {
                var text = rawText;

                // 1. Check if wrapped in RTF format
                if (text.StartsWith("{\\rtf", StringComparison.OrdinalIgnoreCase))
                {
                    text = CleanRtf(text);
                }

                // 2. Strip internal Sticky Notes metadata markup (e.g. \id=... \note=... \document...)
                text = Regex.Replace(text, @"\\id=[a-zA-Z0-9_\-]+", "", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"\\metadata\{[^}]*\}", "", RegexOptions.IgnoreCase);

                // 3. Normalize line breaks (\r\n -> \n)
                text = text.Replace("\r\n", "\n").Replace("\r", "\n");

                // 4. Strip excess trailing and leading null/whitespace characters
                text = text.Trim('\0', ' ', '\t', '\n');

                return text;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StickyNoteTextParser error: {ex}");
                // Fallback to raw text to guarantee note content is never lost
                return rawText.Trim();
            }
        }

        private static string CleanRtf(string rtf)
        {
            try
            {
                // Replace paragraph and line breaks
                var result = Regex.Replace(rtf, @"\\par[d]?", "\n", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"\\tab", "\t", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"\\line", "\n", RegexOptions.IgnoreCase);

                // Handle unicode characters formatted like \u2612?
                result = Regex.Replace(result, @"\\u(-?\d+)\??", match =>
                {
                    if (short.TryParse(match.Groups[1].Value, out var charCode))
                    {
                        return ((char)charCode).ToString();
                    }
                    return "";
                });

                // Remove all other RTF tags \keyword
                result = Regex.Replace(result, @"\\[a-zA-Z]+(-?\d+)? ?", "");

                // Remove braces
                result = Regex.Replace(result, @"[{}]", "");

                return result;
            }
            catch
            {
                return rtf;
            }
        }
    }
}
