using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Inkwell.Spelling;
using Inkwell.Text;

namespace Inkwell.Windows;

/// <summary>
/// A multiline editor that word-wraps and paints misspelled words directly on top of the text.
///
/// How it works (ImGui can't color individual words inside an input box):
///   1. The input lives inside a scrolling child window and is sized to fit ALL its lines, so the
///      input itself never scrolls. That means we always know exactly where each line is on screen.
///   2. We do our own word wrap by turning spaces into '\n' (see WordWrapper). Same length, so
///      character offsets from the spell checker map straight onto lines.
///   3. After ImGui draws the input, we draw over it: the misspelled word in red, a squiggle,
///      and/or a translucent box, using the same font metrics ImGui used.
/// </summary>
public sealed unsafe class SpellTextEditor
{
    // Size of the text box's buffer. 4,000 used to cap a pad at about 9 chat messages; this allows
    // roughly 200+ messages, far more than anyone will write in one go, while keeping memory use
    // small. Raise it if you ever need more.
    private const int MaxBytes = 100_000;

    private readonly SpellChecker spell;
    private readonly Configuration config;

    private string buffer = string.Empty;   // What ImGui edits. Contains soft-wrap '\n's.
    private string? pendingWrap;            // Re-wrapped text waiting to be pushed into ImGui's live buffer.
    private string? pendingPrefixRemoval;   // Text to cut from the start of ImGui's live buffer.
    private float lastWrapWidth = -1f;
    private float lastFontSize = -1f;
    private bool dirty = true;
    private int cursorLine;
    private int lastCursorLine = -1;
    private List<Misspelling> misspellings = new();

    public SpellTextEditor(SpellChecker spell, Configuration config)
    {
        this.spell = spell;
        this.config = config;
    }

    /// <summary>The text as it will be sent: no soft wraps.</summary>
    public string Text { get; private set; } = string.Empty;

    public IReadOnlyList<Misspelling> Misspellings => misspellings;
    public bool IsActive { get; private set; }
    public event Action? TextChanged;

    public void SetText(string text)
    {
        Text = text.Replace('\n', ' ').Replace('\r', ' ');
        buffer = Text;
        pendingWrap = null;
        dirty = true;
        TextChanged?.Invoke();
    }

    public void Replace(Misspelling m, string replacement)
    {
        if (m.Start + m.Length > Text.Length || Text.Substring(m.Start, m.Length) != m.Word) return;
        SetText(Text.Remove(m.Start, m.Length).Insert(m.Start, replacement));
    }

    /// <summary>
    /// Removes <paramref name="charCount"/> characters from the start of the text. Works while the
    /// box has focus: ImGui keeps its own copy of the text then, so the cut is applied in the
    /// input callback on the next frame.
    /// </summary>
    public void RemovePrefix(int charCount)
    {
        if (charCount <= 0 || charCount > Text.Length) return;

        if (IsActive)
            pendingPrefixRemoval = Text[..charCount];
        else
            SetText(Text[charCount..]);
    }

    /// <summary>Call after the dictionary or spell settings change.</summary>
    public void Recheck() => dirty = true;

    public void Draw(Vector2 size)
    {
        // AlwaysVerticalScrollbar keeps the content width stable; otherwise the scrollbar appearing
        // would change the wrap width, which could change the line count, which could hide the
        // scrollbar again, and so on.
        // While Ctrl is held the mouse wheel changes text size, so don't also scroll.
        var childFlags = ImGuiWindowFlags.AlwaysVerticalScrollbar;
        if (ImGui.GetIO().KeyCtrl) childFlags |= ImGuiWindowFlags.NoScrollWithMouse;

        using var child = ImRaii.Child("##inkwell-editor", size, true, childFlags);
        if (!child) return;

        var style = ImGui.GetStyle();
        var lineHeight = ImGui.GetTextLineHeight();
        var avail = ImGui.GetContentRegionAvail();
        var wrapWidth = MathF.Max(50f, avail.X - style.FramePadding.X * 2f - 4f);

        // A different text size changes every line's width, so re-wrap.
        var fontSize = ImGui.GetFontSize();
        if (dirty || MathF.Abs(wrapWidth - lastWrapWidth) > 0.5f || MathF.Abs(fontSize - lastFontSize) > 0.01f)
        {
            lastFontSize = fontSize;
            Reflow(wrapWidth);
        }

        int lineCount = 1;
        foreach (var c in buffer) if (c == '\n') lineCount++;

        // Tall enough for every line plus one spare, so the input never scrolls internally.
        var inputHeight = MathF.Max(avail.Y, (lineCount + 1) * lineHeight + style.FramePadding.Y * 2f);
        var inputTop = ImGui.GetCursorPosY();

        var flags = ImGuiInputTextFlags.NoHorizontalScroll
                  | ImGuiInputTextFlags.CallbackAlways
                  | ImGuiInputTextFlags.CallbackCharFilter;

        var changed = ImGui.InputTextMultiline("##inkwell-text", ref buffer, MaxBytes,
            new Vector2(-1, inputHeight), flags, OnInputCallback);

        IsActive = ImGui.IsItemActive();
        var textOrigin = ImGui.GetItemRectMin() + style.FramePadding;

        if (changed)
        {
            Text = buffer.Replace('\n', ' ');
            dirty = true;              // Re-wrap and re-check next frame.
            TextChanged?.Invoke();
        }
        else if (!dirty)
        {
            DrawHighlights(textOrigin, lineHeight);
        }

        if (IsActive)
        {
            if (cursorLine != lastCursorLine)
            {
                KeepVisible(inputTop + style.FramePadding.Y + cursorLine * lineHeight, lineHeight);
                lastCursorLine = cursorLine;
            }
        }
        else
        {
            pendingWrap = null;
            lastCursorLine = -1;
        }
    }

    private void Reflow(float wrapWidth)
    {
        var wrapped = WordWrapper.Wrap(buffer, wrapWidth, s => ImGui.CalcTextSize(s).X);
        if (wrapped != buffer)
        {
            buffer = wrapped;
            // While the box has focus, ImGui ignores our string and edits its own copy,
            // so the new wrap has to be patched in from the callback.
            pendingWrap = IsActive ? wrapped : null;
        }

        Text = buffer.Replace('\n', ' ');
        misspellings = spell.Check(Text);
        lastWrapWidth = wrapWidth;
        dirty = false;
    }

    // NOTE ON BINDINGS: this signature matches Dalamud's ImGui bindings (API 13+). If your
    // Dalamud version complains here, check the InputTextMultiline overloads in your IDE:
    // the parameter may be ImGuiInputTextCallbackDataPtr instead of a ref struct, and
    // BufDirty may be a bool instead of a byte. The logic stays the same.
    private int OnInputCallback(scoped ref ImGuiInputTextCallbackData data)
    {
        if (data.EventFlag == ImGuiInputTextFlags.CallbackCharFilter)
        {
            // Chat is single-line; block Enter so the only newlines are our soft wraps.
            return data.EventChar is '\n' or '\r' ? 1 : 0;
        }

        if (pendingPrefixRemoval != null)
        {
            ApplyPrefixRemoval(ref data, pendingPrefixRemoval);
            pendingPrefixRemoval = null;
            pendingWrap = null;   // Lengths changed; the wrap is recomputed next frame anyway.
        }

        if (pendingWrap != null)
        {
            var bytes = Encoding.UTF8.GetBytes(pendingWrap);
            // Only patch if nothing was typed since we computed the wrap.
            if (bytes.Length == data.BufTextLen)
            {
                bool any = false;
                for (int i = 0; i < bytes.Length; i++)
                {
                    var current = data.Buf[i];
                    if (current != bytes[i] && IsSpaceOrNl(current) && IsSpaceOrNl(bytes[i]))
                    {
                        data.Buf[i] = bytes[i];
                        any = true;
                    }
                }
                if (any) data.BufDirty = 1;
            }
            pendingWrap = null;
        }

        // Which visual line is the caret on? (CursorPos is a byte offset; '\n' is one byte.)
        int line = 0;
        int limit = Math.Min(data.CursorPos, data.BufTextLen);
        for (int i = 0; i < limit; i++)
            if (data.Buf[i] == (byte)'\n') line++;
        cursorLine = line;

        return 0;
    }

    private static bool IsSpaceOrNl(byte b) => b == (byte)' ' || b == (byte)'\n';

    /// <summary>Cuts <paramref name="prefix"/> off the start of ImGui's buffer, if it's still there.</summary>
    private static void ApplyPrefixRemoval(scoped ref ImGuiInputTextCallbackData data, string prefix)
    {
        var bytes = Encoding.UTF8.GetBytes(prefix);
        int n = bytes.Length;
        if (n == 0 || n > data.BufTextLen) return;

        // Make sure the buffer still starts with the prefix (a soft wrap may have turned a space
        // into a newline, so treat those as equal).
        for (int i = 0; i < n; i++)
        {
            var b = data.Buf[i];
            if (b == bytes[i]) continue;
            if (IsSpaceOrNl(b) && IsSpaceOrNl(bytes[i])) continue;
            return;
        }

        // Shift the rest of the text left, including the terminating zero.
        int remaining = data.BufTextLen - n;
        for (int i = 0; i <= remaining; i++)
            data.Buf[i] = data.Buf[i + n];

        data.BufTextLen = remaining;
        data.CursorPos = Math.Max(0, data.CursorPos - n);
        data.SelectionStart = Math.Max(0, data.SelectionStart - n);
        data.SelectionEnd = Math.Max(0, data.SelectionEnd - n);
        data.BufDirty = 1;
    }

    private void DrawHighlights(Vector2 origin, float lineHeight)
    {
        var drawList = ImGui.GetWindowDrawList();
        var starts = WordWrapper.LineStarts(buffer);
        var mouse = ImGui.GetMousePos();
        var canHover = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && !ImGui.IsMouseDown(ImGuiMouseButton.Left);
        string? tooltip = null;

        if (misspellings.Count > 0 &&
            (config.HighlightRecolor || config.HighlightUnderline || config.HighlightBackground))
        {
            var color = ImGui.GetColorU32(config.MisspellColor);
            var fill = ImGui.GetColorU32(config.MisspellColor with { W = config.MisspellColor.W * 0.25f });

            foreach (var m in misspellings)
            {
                foreach (var (pos, width) in Segments(starts, m.Start, m.Length, origin, lineHeight))
                {
                    if (config.HighlightBackground)
                        drawList.AddRectFilled(pos, pos + new Vector2(width, lineHeight), fill, 2f);
                    if (config.HighlightRecolor)
                        drawList.AddText(pos, color, buffer.Substring(m.Start, m.Length));
                    if (config.HighlightUnderline)
                        DrawSquiggle(drawList, pos.X, pos.X + width, pos.Y + lineHeight - 1f, color);

                    if (canHover && Inside(mouse, pos, width, lineHeight))
                        tooltip = $"\"{m.Word}\" isn't in the dictionary.\nClick it in the spelling bar for suggestions.";
                }
            }
        }

        if (tooltip != null)
            ImGui.SetTooltip(tooltip);
    }

    /// <summary>
    /// Screen position and width of each visual line a text range covers (a run-on sentence can
    /// span several wrapped lines).
    /// </summary>
    private List<(Vector2 Pos, float Width)> Segments(List<int> starts, int start, int length, Vector2 origin, float lineHeight)
    {
        var result = new List<(Vector2, float)>();
        int end = Math.Min(start + length, buffer.Length);
        int pos = start;

        while (pos < end)
        {
            int line = WordWrapper.LineOf(starts, pos);
            int lineStart = starts[line];
            int lineEnd = line + 1 < starts.Count ? starts[line + 1] - 1 : buffer.Length; // index of the '\n'
            int segEnd = Math.Min(end, lineEnd);

            if (segEnd > pos)
            {
                float x = ImGui.CalcTextSize(buffer.Substring(lineStart, pos - lineStart)).X;
                float w = ImGui.CalcTextSize(buffer.Substring(pos, segEnd - pos)).X;
                result.Add((origin + new Vector2(x, line * lineHeight), w));
            }

            pos = segEnd + 1; // Skip the soft-wrap newline.
        }
        return result;
    }

    private static bool Inside(Vector2 p, Vector2 min, float width, float height) =>
        p.X >= min.X && p.X <= min.X + width && p.Y >= min.Y && p.Y <= min.Y + height;

    private static void DrawSquiggle(ImDrawListPtr drawList, float x0, float x1, float y, uint color)
    {
        const float step = 2f;
        bool up = true;
        for (float x = x0; x < x1; x += step)
        {
            var nx = MathF.Min(x + step, x1);
            drawList.AddLine(new Vector2(x, up ? y : y - 2f), new Vector2(nx, up ? y - 2f : y), color, 1f);
            up = !up;
        }
    }

    private static void KeepVisible(float y, float lineHeight)
    {
        var scrollY = ImGui.GetScrollY();
        var viewHeight = ImGui.GetWindowHeight();
        var margin = lineHeight * 0.5f;

        if (y - margin < scrollY)
            ImGui.SetScrollY(MathF.Max(0, y - margin));
        else if (y + lineHeight + margin > scrollY + viewHeight)
            ImGui.SetScrollY(y + lineHeight + margin - viewHeight);
    }
}
