using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Inkwell.Chat;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Inkwell.Fonts;
using Inkwell.Snoop;
using Inkwell.Spelling;
using Inkwell.Text;
using Inkwell.Windows;

namespace Inkwell.Pads;

/// <summary>
/// The full scratch pad UI (toolbar, spelling bar, chunk preview, resize bar, text box) for one
/// pad. Drawn inside a tab of the main window, or inside its own popped-out window.
/// </summary>
public sealed class PadView
{
    private const float MinEditorHeight = 60f;
    private const float MinPreviewHeight = 60f;
    private const float SplitterHeight = 6f;
    private const int MaxWordButtons = 25;

    private readonly Plugin plugin;
    private readonly Configuration config;
    private readonly PadManager manager;
    private readonly PadData pad;
    private readonly SpellTextEditor editor;

    private List<string> chunks = new();
    private string chunkKey = string.Empty;
    /// <summary>Which chunk Copy and Send act on next. Shared, so both buttons stay in sync.</summary>
    private int nextChunk;

    private DateTime lastSend = DateTime.MinValue;
    private bool postStartedAfterSwitch;
    private DateTime confirmSendUntil = DateTime.MinValue;
    private bool sending;
    private string statusText = string.Empty;
    private bool statusIsError;
    private DateTime statusUntil = DateTime.MinValue;

    // Chat log / notifications.
    private string addName = string.Empty;
    private LogEntry? lastLogEntry;
    private bool logAtBottom = true;
    private DateTime lastLookedAt = DateTime.MinValue;
    private DateTime lastSound = DateTime.MinValue;

    /// <summary>Someone this pad watches posted since you last looked at it.</summary>
    public bool HasUnread { get; private set; }

    private Misspelling selected;
    private List<string> suggestions = new();
    private string replaceText = string.Empty;

    public PadView(Plugin plugin, PadManager manager, PadData pad)
    {
        this.plugin = plugin;
        this.manager = manager;
        this.pad = pad;
        config = plugin.Configuration;

        editor = new SpellTextEditor(plugin.Spell, config);
        editor.SetText(pad.Text);
        editor.TextChanged += () =>
        {
            nextChunk = 0;
            confirmSendUntil = DateTime.MinValue;
            pad.Text = editor.Text;
            manager.QueueSave();
            var switched = CheckForChatCommand();

            // Deleting everything you wrote after a "/p " switch counts as finishing the post.
            // (Skipped while the command itself is still being removed from the box.)
            if (!switched && pad.RevertHeaderIndex != null)
            {
                if (editor.Text.Length > 0) postStartedAfterSwitch = true;
                else if (postStartedAfterSwitch) RevertTemporaryChannel();
            }
        };
    }

    public PadData Data => pad;
    public bool HasText => editor.Text.Length > 0;

    private Vector4 Red => Theme.Accent(config, "status.error");
    private Vector4 Green => Theme.Accent(config, "status.ok");
    private Vector4 Yellow => Theme.Accent(config, "status.warn");
    private Vector4 Dim => Theme.Accent(config, "status.muted");

    /// <summary>Called by the settings window whenever something changes.</summary>
    public void OnSettingsChanged()
    {
        editor.Recheck();
        chunkKey = string.Empty;
    }

    public void Draw()
    {
        using var id = ImRaii.PushId(pad.Id);

        // Looking at (or hovering) this pad counts as having seen its new messages.
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) ||
            ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows))
        {
            HasUnread = false;
            lastLookedAt = DateTime.Now;
        }

        RebuildChunksIfNeeded();
        DrawToolbar();
        DrawPeopleBar();
        DrawStatus();
        DrawSpellBar();

        var avail = ImGui.GetContentRegionAvail();
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        var gap = SplitterHeight + spacing * 2;

        // Top area: chat log and/or chunk preview. Bottom: the text box.
        var showLog = pad.FollowTarget || pad.Watched.Count > 0;
        var showPreview = config.ShowChunkPreview || !showLog;
        var topMin = showLog && showPreview ? MinPreviewHeight * 2 + gap : MinPreviewHeight;

        var maxEditor = MathF.Max(MinEditorHeight, avail.Y - topMin - gap);
        var editorHeight = Math.Clamp(pad.EditorHeight, MinEditorHeight, maxEditor);
        var topHeight = MathF.Max(topMin, avail.Y - editorHeight - gap);

        if (showLog && showPreview)
        {
            var maxLog = MathF.Max(MinPreviewHeight, topHeight - MinPreviewHeight - gap);
            var logHeight = Math.Clamp(pad.LogHeight, MinPreviewHeight, maxLog);
            DrawLogPanel(logHeight);
            DrawSplitter("##logsplitter", avail.X, logHeight, MinPreviewHeight, maxLog, true, v => pad.LogHeight = v);
            DrawPreviewPanel(MathF.Max(MinPreviewHeight, topHeight - logHeight - gap));
        }
        else if (showLog)
        {
            DrawLogPanel(topHeight);
        }
        else
        {
            DrawPreviewPanel(topHeight);
        }

        DrawSplitter("##splitter", avail.X, editorHeight, MinEditorHeight, maxEditor, false, v => pad.EditorHeight = v);

        var editorTopLeft = ImGui.GetCursorScreenPos();

        using (Theme.Push(config, ThemeScope.Editor))
        using (PushChannelColors(ImGuiCol.FrameBg, ImGuiCol.ChildBg))
        using (plugin.TextFont.Push())
            editor.Draw(new Vector2(0, editorHeight));

        HandleZoom(editorTopLeft, new Vector2(avail.X, editorHeight));
    }

    // ------------------------------------------------------------------ toolbar

    private void DrawToolbar()
    {
        var headers = ChatHeaders.All;
        var idx = Math.Clamp(pad.HeaderIndex, 0, headers.Length - 1);

        ImGui.SetNextItemWidth(150);
        var temporary = pad.RevertHeaderIndex != null;
        var preview = temporary ? $"{headers[idx].Name} (this post)" : headers[idx].Name;

        using (ImRaii.PushColor(ImGuiCol.Text, ChannelColor(idx) ?? Vector4.Zero, ChannelColor(idx) != null))
        using (var combo = ImRaii.Combo("##header", preview))
        {
            if (combo)
            {
                foreach (var i in ChatHeaders.DisplayOrder)
                {
                    var color = ChannelColor(i);
                    using (ImRaii.PushColor(ImGuiCol.Text, color ?? Vector4.Zero, color != null))
                    {
                        if (ImGui.Selectable(headers[i].Name, i == idx))
                        {
                            pad.HeaderIndex = i;
                            pad.RevertHeaderIndex = null;   // A manual pick is the new normal.
                            manager.QueueSave();
                        }
                    }
                }
            }
        }
        if (temporary && ImGui.IsItemHovered())
        {
            var back = ChatHeaders.All[Math.Clamp(pad.RevertHeaderIndex!.Value, 0, headers.Length - 1)].Name;
            ImGui.SetTooltip($"Switched by a typed command. Goes back to {back} when this post is finished.\n" +
                             "Pick a channel here to keep it instead.");
        }

        if (pad.HeaderIndex == ChatHeaders.TellIndex)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(170);
            if (ImGui.InputTextWithHint("##tell", "Name Surname@World", ref pad.TellTarget, 64))
                manager.QueueSave();
        }

        ImGui.SameLine();
        if (ImGui.Checkbox("OOC", ref pad.Ooc))
            manager.QueueSave();

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            editor.SetText(string.Empty);
            RevertTemporaryChannel();
        }

        ImGui.SameLine();
        if (ImGui.Button("Settings"))
            plugin.ConfigWindow.Toggle();

        ImGui.SameLine();
        if (pad.Detached)
        {
            if (ImGui.Button("Dock"))
                manager.Dock(pad.Id);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Put this pad back into the tabs of the main window.");
        }
        else
        {
            if (ImGui.Button("Pop out"))
                manager.Detach(pad.Id);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Open this pad in its own window.");
        }

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
        if (string.IsNullOrWhiteSpace(ChatHeaders.Resolve(pad.HeaderIndex, pad.TellTarget)) && message.TrimStart().StartsWith('/'))
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

        // The whole post has gone out.
        nextChunk = 0;
        if (config.ClearAfterLastCopy)
            editor.SetText(string.Empty);
        RevertTemporaryChannel();
    }

    /// <summary>Undo a "/p "-style switch once its post is finished.</summary>
    private void RevertTemporaryChannel()
    {
        if (pad.RevertHeaderIndex is not int previous) return;

        pad.HeaderIndex = previous;
        pad.TellTarget = pad.RevertTellTarget;
        pad.RevertHeaderIndex = null;
        postStartedAfterSwitch = false;
        manager.QueueSave();
        SetStatus($"Channel back to {ChatHeaders.All[Math.Clamp(previous, 0, ChatHeaders.All.Length - 1)].Name}.", false);
    }

    /// <summary>Ctrl + mouse wheel over the text box makes the text bigger or smaller.</summary>
    private void HandleZoom(Vector2 min, Vector2 size)
    {
        var io = ImGui.GetIO();
        if (!io.KeyCtrl || io.MouseWheel == 0) return;

        var mouse = ImGui.GetMousePos();
        if (mouse.X < min.X || mouse.Y < min.Y || mouse.X > min.X + size.X || mouse.Y > min.Y + size.Y) return;
        if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows)) return;

        var current = config.CustomTextSize ? config.TextSize : ImGui.GetFontSize();
        config.CustomTextSize = true;
        config.TextSize = Math.Clamp(MathF.Round(current + io.MouseWheel), TextFont.MinSize, TextFont.MaxSize);
        manager.QueueSave();
        SetStatus($"Text size {config.TextSize:0}", false);
    }

    /// <summary>The channel's color from Appearance, or null for "None".</summary>
    private Vector4? ChannelColor(int headerIndex)
    {
        var key = ChatHeaders.All[Math.Clamp(headerIndex, 0, ChatHeaders.All.Length - 1)].ColorKey;
        return key == null ? null : Theme.Accent(config, key);
    }

    /// <summary>
    /// Tints the given background colors toward the pad's channel color (and optionally colors the
    /// text), on top of whatever Appearance colors are already in effect.
    /// </summary>
    private ThemePush PushChannelColors(params ImGuiCol[] backgrounds)
    {
        var channel = ChannelColor(pad.HeaderIndex);
        if (channel is not Vector4 c) return new ThemePush(0);

        int count = 0;
        if (config.ChannelTintBackground)
        {
            var t = Math.Clamp(config.ChannelTintStrength, 0f, 1f);
            foreach (var target in backgrounds)
            {
                var bg = ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(target));
                var mixed = new Vector4(
                    bg.X + (c.X - bg.X) * t,
                    bg.Y + (c.Y - bg.Y) * t,
                    bg.Z + (c.Z - bg.Z) * t,
                    MathF.Max(bg.W, t));
                ImGui.PushStyleColor(target, mixed);
                count++;
            }
        }

        if (config.ChannelColorText)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, c);
            count++;
        }

        return new ThemePush(count);
    }

    /// <summary>"/em waves" becomes channel Emote + text "waves".</summary>
    private bool CheckForChatCommand()
    {
        if (!config.ChatCommandsSwitchChannel) return false;
        if (!ChatHeaders.TryParseCommand(editor.Text, out var headerIndex, out var tellTarget, out var length))
            return false;

        // Remember where to go back to (only the first time, so "/p " then "/fc " still returns
        // to the original channel).
        if (config.ChatCommandsTemporary && pad.RevertHeaderIndex == null && headerIndex != pad.HeaderIndex)
        {
            pad.RevertHeaderIndex = pad.HeaderIndex;
            pad.RevertTellTarget = pad.TellTarget;
        }
        postStartedAfterSwitch = false;

        pad.HeaderIndex = headerIndex;
        if (tellTarget != null) pad.TellTarget = tellTarget;
        editor.RemovePrefix(length);
        manager.QueueSave();

        var name = ChatHeaders.All[headerIndex].Name;
        SetStatus(tellTarget != null
            ? $"Channel switched to Tell ({tellTarget})."
            : $"Channel switched to {name}.", false);
        return true;
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
            manager.NotifySettingsChanged();
            ImGui.CloseCurrentPopup();
        }
    }

    // ------------------------------------------------------------------ preview

    private void RebuildChunksIfNeeded()
    {
        var header = ChatHeaders.Resolve(pad.HeaderIndex, pad.TellTarget);
        var key = string.Join('\u0001', header, pad.Ooc, config.MaxChunkBytes, config.ContinuationMarker,
            config.MarkerOnLastChunk, config.SplitOnSentences, editor.Text);
        if (key == chunkKey) return;

        chunkKey = key;
        chunks = Chunker.Build(editor.Text, header, pad.Ooc, config);
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

    /// <param name="growsDown">True if dragging down makes the panel above bigger.</param>
    private void DrawSplitter(string id, float width, float current, float min, float max, bool growsDown, Action<float> set)
    {
        ImGui.InvisibleButton(id, new Vector2(MathF.Max(1, width), SplitterHeight));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        if (hovered || active)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);

        if (active)
        {
            var delta = ImGui.GetIO().MouseDelta.Y;
            set(Math.Clamp(current + (growsDown ? delta : -delta), min, max));
        }

        if (ImGui.IsItemDeactivated())
            manager.QueueSave();

        var rmin = ImGui.GetItemRectMin();
        var rmax = ImGui.GetItemRectMax();
        var mid = (rmin.Y + rmax.Y) / 2f;
        var col = ImGui.GetColorU32(Theme.Accent(config, active || hovered ? "splitter.hover" : "splitter"));
        ImGui.GetWindowDrawList().AddRectFilled(new Vector2(rmin.X, mid - 1), new Vector2(rmax.X, mid + 1), col);
    }

    private void DrawPreviewPanel(float height)
    {
        using (Theme.Push(config, ThemeScope.Preview))
        using (PushChannelColors(ImGuiCol.ChildBg))
        using (config.TextSizeInPreview ? plugin.TextFont.Push() : null)
            DrawPreview(new Vector2(0, height));
    }

    // ------------------------------------------------------------------ chat log (Snooper)

    /// <summary>Everyone whose lines this pad shows right now.</summary>
    private HashSet<string> WatchedNames()
    {
        var names = new HashSet<string>(pad.Watched, StringComparer.OrdinalIgnoreCase);
        if (pad.FollowTarget && CurrentTargetName() is { } target)
            names.Add(target);
        return names;
    }

    private static string? CurrentTargetName() =>
        Plugin.TargetManager.Target is IPlayerCharacter player ? player.Name.TextValue : null;

    private void DrawPeopleBar()
    {
        if (ImGui.SmallButton("+ Target"))
        {
            if (CurrentTargetName() is { } name) AddWatched(name);
            else SetStatus("Target a player first, then click + Target.", true);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show what your current target says in this pad.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputTextWithHint("##addname", "Add by name...", ref addName, 40, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            AddWatched(addName);
            addName = string.Empty;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Type a character name (First Last) and press Enter.");

        ImGui.SameLine();
        if (ImGui.Checkbox("Follow target", ref pad.FollowTarget))
            manager.QueueSave();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Also show whoever you currently have targeted.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Alerts"))
            ImGui.OpenPopup("##alerts");
        DrawAlertsPopup();

        // Name tags, flowing onto new lines as needed.
        var following = pad.FollowTarget ? CurrentTargetName() : null;
        if (pad.Watched.Count == 0 && following == null) return;

        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var padX = ImGui.GetStyle().FramePadding.X * 2;
        var first = true;

        for (int i = 0; i < pad.Watched.Count; i++)
        {
            var label = $"{pad.Watched[i]}  x";
            FlowSameLine(ref first, ImGui.CalcTextSize(label).X + padX, right);
            if (ImGui.SmallButton($"{label}##watch{i}"))
            {
                SetStatus($"Stopped showing {pad.Watched[i]}.", false);
                pad.Watched.RemoveAt(i);
                manager.QueueSave();
                break;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Click to remove from this pad.");
        }

        if (following != null && !pad.Watched.Contains(following, StringComparer.OrdinalIgnoreCase))
        {
            var label = $"Target: {following}";
            FlowSameLine(ref first, ImGui.CalcTextSize(label).X, right);
            ImGui.TextColored(Dim, label);
        }
    }

    private static void FlowSameLine(ref bool first, float width, float right)
    {
        if (first) { first = false; return; }
        ImGui.SameLine();
        if (ImGui.GetCursorScreenPos().X + width > right)
            ImGui.NewLine();
    }

    private void AddWatched(string name)
    {
        name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0) return;

        if (pad.Watched.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            SetStatus($"{name} is already in this pad.", false);
            return;
        }

        pad.Watched.Add(name);
        manager.QueueSave();
        SetStatus($"Now showing {name} in \"{pad.Name}\".", false);
    }

    private void DrawAlertsPopup()
    {
        using var popup = ImRaii.Popup("##alerts");
        if (!popup) return;

        ImGui.TextUnformatted($"When someone in \"{pad.Name}\" posts:");
        if (ImGui.Checkbox("Blink the tab / window title", ref pad.NotifyBlink)) manager.QueueSave();
        if (ImGui.Checkbox("Play a sound", ref pad.NotifySound)) manager.QueueSave();

        using (ImRaii.Disabled(!pad.NotifySound))
        {
            ImGui.SetNextItemWidth(140);
            if (ImGui.SliderInt("##sound", ref pad.NotifySoundId, ChatSound.Min, ChatSound.Max, "Sound <se.%d>"))
                manager.QueueSave();
            ImGui.SameLine();
            if (ImGui.SmallButton("Test"))
                ChatSound.Play(pad.NotifySoundId);
        }

        ImGui.TextDisabled(config.NotifyOnlyWhenUnfocused
            ? "Only alerts while you're not looking at this pad."
            : "Alerts even while you're looking at this pad.");
    }

    private void DrawLogPanel(float height)
    {
        using var child = ImRaii.Child("##chatlog", new Vector2(0, height), true);
        if (!child) return;
        using var font = config.TextSizeInPreview ? plugin.TextFont.Push() : null;

        var names = WatchedNames();
        if (names.Count == 0)
        {
            ImGui.TextColored(Dim, "Target a player to see what they say here.");
            return;
        }

        var lines = plugin.ChatLog.For(names, config.LogIncludeSelf);
        if (lines.Count == 0)
            ImGui.TextColored(Dim, $"Nothing yet from {string.Join(", ", names)}.");

        foreach (var e in lines)
        {
            var time = config.LogTimestamps ? $"[{e.Time:HH:mm}] " : "";
            var body = e.NameInText ? e.Text : e.IsEmote ? $"{e.Name} {e.Text}" : $"{e.Name}: {e.Text}";
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Accent(config, e.ColorKey)))
                ImGui.TextWrapped(time + body);
        }

        // Follow new lines, unless you've scrolled up to read something older.
        var newest = lines.Count > 0 ? lines[^1] : null;
        if (!ReferenceEquals(newest, lastLogEntry))
        {
            if (logAtBottom) ImGui.SetScrollHereY(1f);
            lastLogEntry = newest;
        }
        logAtBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f;
    }

    /// <summary>Called for every new chat line; blinks and/or beeps if it's from someone here.</summary>
    public void OnLogEntry(LogEntry entry)
    {
        if (entry.IsSelf || !WatchedNames().Contains(entry.Name)) return;

        var looking = (DateTime.Now - lastLookedAt).TotalMilliseconds < 300;
        if (looking && config.NotifyOnlyWhenUnfocused) return;

        if (pad.NotifyBlink && !looking)
            HasUnread = true;

        if (pad.NotifySound && (DateTime.Now - lastSound).TotalSeconds > 1.5)
        {
            lastSound = DateTime.Now;
            ChatSound.Play(pad.NotifySoundId);
        }
    }
}
