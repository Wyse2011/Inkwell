using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Inkwell.Chat;
using Inkwell.Spelling;
using Inkwell.Text;

namespace Inkwell.Windows;

public sealed class ScratchPadWindow : Window
{
    private const float MinEditorHeight = 60f;
    private const float MinPreviewHeight = 60f;
    private const float SplitterHeight = 6f;
    private const int MaxWordButtons = 25;

    private readonly Plugin plugin;
    private readonly Configuration config;
    private readonly SpellTextEditor editor;

    private ThemePush windowTheme;

    private List<string> chunks = new();
    private string chunkKey = string.Empty;
    /// <summary>Which chunk Copy and Send act on next. Shared, so both buttons stay in sync.</summary>
    private int nextChunk;

    private DateTime lastSend = DateTime.MinValue;
    private DateTime confirmSendUntil = DateTime.MinValue;
    private bool sending;
    private string statusText = string.Empty;
    private bool statusIsError;
    private DateTime statusUntil = DateTime.MinValue;

    private Misspelling selected;
    private List<string> suggestions = new();
    private string replaceText = string.Empty;

    public ScratchPadWindow(Plugin plugin) : base("Inkwell Scratch Pad###inkwell-main")
    {
        this.plugin = plugin;
        config = plugin.Configuration;
        editor = new SpellTextEditor(plugin.Spell, config);
        editor.TextChanged += () =>
        {
            nextChunk = 0;
            confirmSendUntil = DateTime.MinValue;
        };

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(560, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    private Vector4 Red => Theme.Accent(config, "status.error");
    private Vector4 Green => Theme.Accent(config, "status.ok");
    private Vector4 Yellow => Theme.Accent(config, "status.warn");
    private Vector4 Dim => Theme.Accent(config, "status.muted");

    // Window-wide colors (title bar, background...) must be pushed before the window begins,
    // which is what PreDraw is for. PostDraw runs after the window ends.
    public override void PreDraw() => windowTheme = Theme.Push(config, ThemeScope.Window);
    public override void PostDraw() => windowTheme.Dispose();

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
        DrawStatus();
        DrawSpellBar();

        var avail = ImGui.GetContentRegionAvail();
        var spacing = ImGui.GetStyle().ItemSpacing.Y;

        // Clamp the saved editor height to what fits in the window right now.
        var maxEditor = MathF.Max(MinEditorHeight, avail.Y - MinPreviewHeight - SplitterHeight - spacing * 2);
        var editorHeight = Math.Clamp(config.EditorHeight, MinEditorHeight, maxEditor);
        var previewHeight = MathF.Max(MinPreviewHeight, avail.Y - editorHeight - SplitterHeight - spacing * 2);

        using (Theme.Push(config, ThemeScope.Preview))
            DrawPreview(new Vector2(0, previewHeight));

        DrawSplitter(avail.X, editorHeight, maxEditor);

        using (Theme.Push(config, ThemeScope.Editor))
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
        if (ImGui.Button("Clear"))
            editor.SetText(string.Empty);

        ImGui.SameLine();
        if (ImGui.Button("Settings"))
            plugin.ConfigWindow.Toggle();

        DrawChunkButtons();
    }

    // ------------------------------------------------------------------ copy / send

    private void DrawChunkButtons()
    {
        if (nextChunk >= chunks.Count) nextChunk = 0;

        var hasChunks = chunks.Count > 0;
        var counter = hasChunks ? $" {nextChunk + 1}/{chunks.Count}" : "";
        var errors = editor.Misspellings.Count;
        var warn = errors > 0;
        var confirming = DateTime.Now < confirmSendUntil;

        var copyLabel = $"Copy{counter}###copy";
        var sendLabel = (confirming ? "Send anyway?" : "Send to chat") + $"{counter}###send";

        // Right-align both buttons on the toolbar row. If the window is too narrow they simply
        // follow the other controls.
        var style = ImGui.GetStyle();
        var copyWidth = ImGui.CalcTextSize(copyLabel.Split("###")[0]).X + style.FramePadding.X * 2;
        var sendWidth = ImGui.CalcTextSize(sendLabel.Split("###")[0]).X + style.FramePadding.X * 2;
        var total = copyWidth + style.ItemSpacing.X + sendWidth;

        ImGui.SameLine();
        var free = ImGui.GetContentRegionAvail().X;
        if (free > total)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + free - total);

        // ---- Copy ----
        using (ImRaii.Disabled(!hasChunks))
        using (ImRaii.PushColor(ImGuiCol.Button, Theme.Accent(config, "copy.warn"), warn))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, Theme.Accent(config, "copy.warn.hover"), warn))
        {
            if (ImGui.Button(copyLabel))
            {
                ImGui.SetClipboardText(chunks[nextChunk]);
                SetStatus($"Copied chunk {nextChunk + 1}/{chunks.Count} to the clipboard.", false);
                AdvanceChunk();
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(warn
                ? $"{errors} possible spelling error(s). You can still copy."
                : "Copy the next chunk to your clipboard.");

        // ---- Send ----
        ImGui.SameLine();
        var cooling = (DateTime.Now - lastSend).TotalSeconds < 1.0;
        var sendColor = warn ? Theme.Accent(config, "copy.warn") : Theme.Accent(config, "send");
        var sendHover = warn ? Theme.Accent(config, "copy.warn.hover") : Theme.Accent(config, "send.hover");

        using (ImRaii.Disabled(!hasChunks || sending || cooling))
        using (ImRaii.PushColor(ImGuiCol.Button, sendColor))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, sendHover))
        {
            if (ImGui.Button(sendLabel))
                OnSendClicked(warn, confirming);
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(warn
                ? $"{errors} possible spelling error(s).\nSends the next chunk to chat. One click = one message."
                : "Sends the next chunk to chat. One click = one message.");
    }

    private void OnSendClicked(bool hasErrors, bool confirming)
    {
        if (hasErrors && config.ConfirmSendWithErrors && !confirming)
        {
            confirmSendUntil = DateTime.Now.AddSeconds(3);
            SetStatus("There are spelling errors. Click \"Send anyway?\" within 3 seconds to send.", true);
            return;
        }
        confirmSendUntil = DateTime.MinValue;

        if (!Plugin.ClientState.IsLoggedIn)
        {
            SetStatus("You need to be logged in to a character to send chat.", true);
            return;
        }

        var index = nextChunk;
        var total = chunks.Count;
        var message = chunks[index];

        // With no channel header, a chunk starting with "/" would be run by the game as a command.
        if (string.IsNullOrWhiteSpace(ChatHeaders.Resolve(config)) && message.TrimStart().StartsWith('/'))
        {
            SetStatus("This chunk starts with \"/\", which the game would run as a command. Pick a chat channel first.", true);
            return;
        }

        sending = true;
        lastSend = DateTime.Now;
        var expectedText = editor.Text;

        Plugin.Framework.RunOnFrameworkThread(() =>
        {
            string? error;
            try
            {
                error = ChatSender.TrySend(message);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Inkwell: failed to send chat message");
                error = "Something went wrong sending the message. Check /xllog for details.";
            }

            sending = false;
            if (error != null)
            {
                SetStatus(error, true);
                return;
            }

            SetStatus($"Sent chunk {index + 1}/{total}.", false);
            // Only advance if the text didn't change while we were sending.
            if (editor.Text == expectedText && nextChunk == index)
                AdvanceChunk();
        });
    }

    /// <summary>Moves both buttons to the next chunk; wraps (and optionally clears) after the last.</summary>
    private void AdvanceChunk()
    {
        nextChunk++;
        if (nextChunk < chunks.Count) return;

        nextChunk = 0;
        if (config.ClearAfterLastCopy)
            editor.SetText(string.Empty);
    }

    private void SetStatus(string text, bool isError)
    {
        statusText = text;
        statusIsError = isError;
        statusUntil = DateTime.Now.AddSeconds(isError ? 6 : 3);
    }

    private void DrawStatus()
    {
        if (DateTime.Now > statusUntil || statusText.Length == 0) return;
        ImGui.TextColored(statusIsError ? Yellow : Green, statusText);
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
        if (nextChunk >= chunks.Count) nextChunk = 0;
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
            var label = i == nextChunk ? $"Chunk {i + 1} (next)" : $"Chunk {i + 1}";
            ImGui.TextColored(i == nextChunk ? Theme.Accent(config, "preview.next") : Dim, label);
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
        var col = ImGui.GetColorU32(Theme.Accent(config, active || hovered ? "splitter.hover" : "splitter"));
        ImGui.GetWindowDrawList().AddRectFilled(new Vector2(min.X, mid - 1), new Vector2(max.X, mid + 1), col);
    }
}
