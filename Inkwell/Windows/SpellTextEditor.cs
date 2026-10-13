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
    private const int MaxBytes = 4000;

    private readonly SpellChecker spell;
    private readonly Configuration config;

    private string buffer = string.Empty;   // What ImGui edits. Contains soft-wrap '\n's.
    private string? pendingWrap;            // Re-wrapped text waiting to be pushed into ImGui's live buffer.
    private float lastWrapWidth = -1f;
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

    /// <summary>Call after the dictionary or spell settings change.</summary>
    public void Recheck() => dirty = true;

    public void Draw(Vector2 size)
    {
        // AlwaysVerticalScrollbar keeps the content width stable; otherwise the scrollbar appearing
        // would change the wrap width, which could change the line count, which could hide the
        // scrollbar again, and so on.
        using var child = ImRaii.Child("##inkwell-editor", size, true, ImGuiWindowFlags.AlwaysVerticalScrollbar);
        if (!child) return;

        var style = ImGui.GetStyle();
        var lineHeight = ImGui.GetTextLineHeight();
        var avail = ImGui.GetContentRegionAvail();
        var wrapWidth = MathF.Max(50f, avail.X - style.FramePadding.X * 2f - 4f);

        if (dirty || MathF.Abs(wrapWidth - lastWrapWidth) > 0.5f)
            Reflow(wrapWidth);

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

    private void DrawHighlights(Vector2 origin, float lineHeight)
    {
        if (misspellings.Count == 0) return;
        if (!config.HighlightRecolor && !config.HighlightUnderline && !config.HighlightBackground) return;

        var drawList = ImGui.GetWindowDrawList();
        var color = ImGui.GetColorU32(config.MisspellColor);
        var fill = ImGui.GetColorU32(config.MisspellColor with { W = config.MisspellColor.W * 0.25f });
        var starts = WordWrapper.LineStarts(buffer);

        foreach (var m in misspellings)
        {
            if (m.Start + m.Length > buffer.Length) continue;

            int line = WordWrapper.LineOf(starts, m.Start);
            int lineStart = starts[line];
            float x = ImGui.CalcTextSize(buffer.Substring(lineStart, m.Start - lineStart)).X;
            var word = buffer.Substring(m.Start, m.Length);
            float w = ImGui.CalcTextSize(word).X;

            var pos = origin + new Vector2(x, line * lineHeight);

            if (config.HighlightBackground)
                drawList.AddRectFilled(pos, pos + new Vector2(w, lineHeight), fill, 2f);

            if (config.HighlightRecolor)
                drawList.AddText(pos, color, word);

            if (config.HighlightUnderline)
                DrawSquiggle(drawList, pos.X, pos.X + w, pos.Y + lineHeight - 1f, color);
        }
    }

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
