using System;
using System.Collections.Generic;

namespace Inkwell.Text;

/// <summary>
/// Soft word wrap that never changes the text length: it only turns existing spaces into '\n'.
///
/// Why: ImGui's multiline input has no word wrap of its own. FFXIV chat is single-line, so the
/// editor never needs a "real" newline, which means every '\n' in the edit buffer can be treated
/// as a wrapped space. Because the length (and UTF-8 byte length) never changes, character
/// offsets in the wrapped text are identical to the logical text, so the cursor never jumps and
/// spell-check positions line up with what's on screen.
/// </summary>
public static class WordWrapper
{
    public static string Wrap(string text, float maxWidth, Func<string, float> measure)
    {
        if (text.Length == 0 || maxWidth <= 0) return text;

        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] is '\n' or '\r') chars[i] = ' ';

        int lineStart = 0;
        int lastSpace = -1;

        for (int i = 0; i <= chars.Length; i++)
        {
            bool atEnd = i == chars.Length;
            if (!atEnd && chars[i] != ' ') continue;

            // [lineStart, i) is the current line if we don't break.
            var width = measure(new string(chars, lineStart, i - lineStart));
            if (width > maxWidth && lastSpace >= lineStart)
            {
                chars[lastSpace] = '\n';
                lineStart = lastSpace + 1;
            }

            if (!atEnd) lastSpace = i;
        }

        return new string(chars);
    }

    /// <summary>Character index at which each visual line starts.</summary>
    public static List<int> LineStarts(string wrapped)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < wrapped.Length; i++)
            if (wrapped[i] == '\n') starts.Add(i + 1);
        return starts;
    }

    /// <summary>Which line (index into <paramref name="starts"/>) contains character <paramref name="index"/>.</summary>
    public static int LineOf(List<int> starts, int index)
    {
        int lo = 0, hi = starts.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (starts[mid] <= index) lo = mid; else hi = mid - 1;
        }
        return lo;
    }
}
