using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Inkwell.Spelling;
using Inkwell.Text;

namespace Inkwell.Windows;

public sealed class ScratchPadWindow : Window
{
    private const float MinEditorHeight = 60f;
    private const float MinPreviewHeight = 60f;
    private const float SplitterHeight = 6f;
    private const int MaxWordButtons = 25;

    private static readonly Vector4 Red = new(1f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Green = new(0.5f, 0.9f, 0.5f, 1f);
    private static readonly Vector4 Yellow = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 Dim = new(0.6f, 0.6f, 0.6f, 1f);

    private readonly Plugin plugin;
    private readonly Configuration config;
    private readonly SpellTextEditor editor;

    private List<string> chunks = new();
    private string chunkKey = string.Empty;
    private int copyIndex;

    private Misspelling selected;
    private List<string> suggestions = new();
    private string replaceText = string.Empty;

    public ScratchPadWindow(Plugin plugin) : base("Inkwell Scratch Pad###inkwell-main")
    {
        this.plugin = plugin;
        config = plugin.Configuration;
        editor = new SpellTextEditor(plugin.Spell, config);
        editor.TextChanged += () => copyIndex = 0;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(560, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>Called by the settings window whenever something changes.</summary>
    public void OnSettingsChanged()
    {
        editor.Recheck();
        chunkKey = string.Empty;
    }

    public override void Draw()
    {
        RebuildChunksIfNeeded();
        DrawToolbar();
        DrawSpellBar();

        var avail = ImGui.GetContentRegionAvail();
        var spacing = ImGui.GetStyle().ItemSpacing.Y;

        // Clamp the saved editor height to what fits in the window right now.
        var maxEditor = MathF.Max(MinEditorHeight, avail.Y - MinPreviewHeight - SplitterHeight - spacing * 2);
        var editorHeight = Math.Clamp(config.EditorHeight, MinEditorHeight, maxEditor);
        var previewHeight = MathF.Max(MinPreviewHeight, avail.Y - editorHeight - SplitterHeight - spacing * 2);

        DrawPreview(new Vector2(0, previewHeight));
        DrawSplitter(avail.X, editorHeight, maxEditor);
        editor.Draw(new Vector2(0, editorHeight));
    }

    // ------------------------------------------------------------------ toolbar

    private void DrawToolbar()
    {
        var headers = ChatHeaders.All;
        var idx = Math.Clamp(config.HeaderIndex, 0, headers.Length - 1);

        ImGui.SetNextItemWidth(150);
        using (var combo = ImRaii.Combo("##header", headers[idx].Name))
        {
            if (combo)
            {
                for (int i = 0; i < headers.Length; i++)
                {
                    if (ImGui.Selectable(headers[i].Name, i == idx))
                    {
                        config.HeaderIndex = i;
                        config.Save();
                    }
                }
            }
        }

        if (config.HeaderIndex == ChatHeaders.TellIndex)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(170);
            if (ImGui.InputTextWithHint("##tell", "Name Surname@World", ref config.TellTarget, 64))
                config.Save();
        }

        ImGui.SameLine();
        if (ImGui.Checkbox("OOC", ref config.Ooc))
            config.Save();

        ImGui.SameLine();
        DrawCopyButton();

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
            editor.SetText(string.Empty);

        ImGui.SameLine();
        if (ImGui.Button("Settings"))
            plugin.ConfigWindow.Toggle();
    }

    private void DrawCopyButton()
    {
        var hasChunks = chunks.Count > 0;
        if (copyIndex >= chunks.Count) copyIndex = 0;

        var label = hasChunks ? $"Copy {copyIndex + 1}/{chunks.Count}###copy" : "Copy###copy";
        var warn = editor.Misspellings.Count > 0;

        using (ImRaii.Disabled(!hasChunks))
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.55f, 0.2f, 0.2f, 1f), warn))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.25f, 0.25f, 1f), warn))
        {
            if (ImGui.Button(label))
            {
                ImGui.SetClipboardText(chunks[copyIndex]);
                copyIndex++;
                if (copyIndex >= chunks.Count)
                {
                    copyIndex = 0;
                    if (config.ClearAfterLastCopy) editor.SetText(string.Empty);
                }
            }
        }

        if (warn && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip($"{editor.Misspellings.Count} possible spelling error(s). You can still copy.");
    }

    // ------------------------------------------------------------------ spell bar

    private void DrawSpellBar()
    {
        if (!plugin.Spell.IsLoaded)
        {
            ImGui.TextColored(Yellow, "No dictionary loaded. Spell check is off (see README).");
            return;
        }

        var list = editor.Misspellings;
        if (list.Count == 0)
        {
            ImGui.TextColored(editor.Text.Length == 0 ? Dim : Green,
                editor.Text.Length == 0 ? "Start typing below." : "No spelling errors found.");
            return;
        }

        ImGui.TextColored(Red, $"{list.Count} misspelled:");

        // Buttons for each distinct word, flowing onto new lines as needed.
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var padX = ImGui.GetStyle().FramePadding.X * 2;
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;

        for (int i = 0; i < list.Count && count < MaxWordButtons; i++)
        {
            var m = list[i];
            if (!shown.Add(m.Word)) continue;
            count++;

            var width = ImGui.CalcTextSize(m.Word).X + padX;
            ImGui.SameLine();
            if (ImGui.GetCursorScreenPos().X + width > right)
                ImGui.NewLine();

            if (ImGui.SmallButton($"{m.Word}##ms{i}"))
            {
                selected = m;
                suggestions = plugin.Spell.Suggest(m.Word);
                replaceText = m.Word;
                ImGui.OpenPopup("##fixword");
            }
        }

        DrawFixPopup();
    }

    private void DrawFixPopup()
    {
        using var popup = ImRaii.Popup("##fixword");
        if (!popup) return;

        ImGui.TextColored(Red, selected.Word);
        ImGui.Separator();

        if (suggestions.Count == 0)
            ImGui.TextColored(Dim, "No suggestions");

        foreach (var s in suggestions)
        {
            if (ImGui.Selectable(s))
            {
                editor.Replace(selected, s);
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.Separator();
        ImGui.SetNextItemWidth(180);
        if (ImGui.InputText("##replace", ref replaceText, 64, ImGuiInputTextFlags.EnterReturnsTrue)
            && replaceText.Length > 0)
        {
            editor.Replace(selected, replaceText);
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        ImGui.TextColored(Dim, "Enter to replace");

        if (ImGui.Selectable("Add to dictionary"))
        {
            plugin.Spell.AddCustom(selected.Word);
            editor.Recheck();
            chunkKey = string.Empty;
            ImGui.CloseCurrentPopup();
        }
    }

    // ------------------------------------------------------------------ preview

    private void RebuildChunksIfNeeded()
    {
        var header = ChatHeaders.Resolve(config);
        var key = string.Join('\u0001', header, config.Ooc, config.MaxChunkBytes, config.ContinuationMarker,
            config.MarkerOnLastChunk, config.SplitOnSentences, editor.Text);
        if (key == chunkKey) return;

        chunkKey = key;
        chunks = Chunker.Build(editor.Text, header, config.Ooc, config);
        if (copyIndex >= chunks.Count) copyIndex = 0;
    }

    private void DrawPreview(Vector2 size)
    {
        using var child = ImRaii.Child("##inkwell-preview", size, true);
        if (!child) return;

        if (chunks.Count == 0)
        {
            ImGui.TextColored(Dim, "Your chunks will appear here.");
            return;
        }

        for (int i = 0; i < chunks.Count; i++)
        {
            var label = i == copyIndex ? $"Chunk {i + 1} (next to copy)" : $"Chunk {i + 1}";
            ImGui.TextColored(i == copyIndex ? Green : Dim, label);
            DrawFlowText(chunks[i]);
            if (i < chunks.Count - 1) ImGui.Separator();
        }
    }

    /// <summary>Word-wrapped text where misspelled words are drawn in red.</summary>
    private void DrawFlowText(string text)
    {
        var spaceWidth = ImGui.CalcTextSize(" ").X;
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var first = true;

        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var width = ImGui.CalcTextSize(token).X;
            if (!first)
            {
                ImGui.SameLine(0, spaceWidth);
                if (ImGui.GetCursorScreenPos().X + width > right)
                    ImGui.NewLine();
            }
            first = false;

            if (plugin.Spell.IsTokenMisspelled(token))
                ImGui.TextColored(config.MisspellColor, token);
            else
                ImGui.TextUnformatted(token);
        }
    }

    // ------------------------------------------------------------------ splitter

    private void DrawSplitter(float width, float editorHeight, float maxEditor)
    {
        ImGui.InvisibleButton("##splitter", new Vector2(MathF.Max(1, width), SplitterHeight));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        if (hovered || active)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);

        if (active)
        {
            // Dragging up makes the editor bigger.
            var newHeight = editorHeight - ImGui.GetIO().MouseDelta.Y;
            config.EditorHeight = Math.Clamp(newHeight, MinEditorHeight, maxEditor);
        }

        if (ImGui.IsItemDeactivated())
            config.Save();

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var mid = (min.Y + max.Y) / 2f;
        var col = ImGui.GetColorU32(active ? ImGuiCol.SeparatorActive : hovered ? ImGuiCol.SeparatorHovered : ImGuiCol.Separator);
        ImGui.GetWindowDrawList().AddRectFilled(new Vector2(min.X, mid - 1), new Vector2(max.X, mid + 1), col);
    }
}
