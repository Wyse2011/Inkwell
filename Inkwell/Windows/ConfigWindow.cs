using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Inkwell.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private readonly Configuration config;

    public ConfigWindow(Plugin plugin) : base("Inkwell Settings###inkwell-config")
    {
        this.plugin = plugin;
        config = plugin.Configuration;
        Size = new Vector2(460, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var changed = false;

        if (ImGui.CollapsingHeader("Editor highlighting", ImGuiTreeNodeFlags.DefaultOpen))
        {
            changed |= ImGui.Checkbox("Color misspelled words", ref config.HighlightRecolor);
            changed |= ImGui.Checkbox("Squiggly underline", ref config.HighlightUnderline);
            changed |= ImGui.Checkbox("Background highlight", ref config.HighlightBackground);
            changed |= ImGui.ColorEdit4("Highlight color", ref config.MisspellColor);
        }

        if (ImGui.CollapsingHeader("Spell check", ImGuiTreeNodeFlags.DefaultOpen))
        {
            changed |= ImGui.Checkbox("Ignore ALL-CAPS words", ref config.IgnoreAllCaps);
            changed |= ImGui.Checkbox("Ignore Capitalized words (names, places)", ref config.IgnoreCapitalized);

            ImGui.SetNextItemWidth(200);
            if (ImGui.InputText("Dictionary file", ref config.DictionaryFile, 128))
                config.Save();
            if (ImGui.Button("Reload dictionary"))
            {
                plugin.LoadDictionary();
                changed = true;
            }

            ImGui.Spacing();
            ImGui.TextUnformatted($"Custom words ({config.CustomWords.Count})");
            foreach (var word in config.CustomWords.ToList())
            {
                if (ImGui.SmallButton($"x##rm{word}"))
                {
                    plugin.Spell.RemoveCustom(word);
                    changed = true;
                }
                ImGui.SameLine();
                ImGui.TextUnformatted(word);
            }
        }

        if (ImGui.CollapsingHeader("Chat log (Snooper features)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Add people to a pad with + Target to see what they say above your text.");
            ImGui.TextUnformatted("Channels to show:");
            changed |= ImGui.Checkbox("Say", ref config.LogSay);
            ImGui.SameLine();
            changed |= ImGui.Checkbox("Emotes", ref config.LogEmotes);
            ImGui.SameLine();
            changed |= ImGui.Checkbox("Yell / Shout", ref config.LogYellShout);
            changed |= ImGui.Checkbox("Party / Alliance", ref config.LogParty);
            ImGui.SameLine();
            changed |= ImGui.Checkbox("Free Company", ref config.LogFreeCompany);
            ImGui.SameLine();
            changed |= ImGui.Checkbox("Linkshells", ref config.LogLinkshells);
            changed |= ImGui.Checkbox("Incoming tells", ref config.LogTells);

            ImGui.Spacing();
            changed |= ImGui.Checkbox("Include my own messages (to follow the whole conversation)", ref config.LogIncludeSelf);
            changed |= ImGui.Checkbox("Show times", ref config.LogTimestamps);
            changed |= ImGui.Checkbox("Show the chunk preview under the chat log", ref config.ShowChunkPreview);
            changed |= ImGui.Checkbox("Only alert when I'm not looking at that pad", ref config.NotifyOnlyWhenUnfocused);
            ImGui.TextDisabled("Blink and sound are set per pad with the Alerts button.");

            if (ImGui.Button("Clear chat history"))
                plugin.ChatLog.Clear();
            ImGui.SameLine();
            ImGui.TextDisabled("History is kept only until you close the game.");
        }

        if (ImGui.CollapsingHeader("Text size", ImGuiTreeNodeFlags.DefaultOpen))
        {
            changed |= ImGui.Checkbox("Use a custom text size", ref config.CustomTextSize);
            using (ImRaii.Disabled(!config.CustomTextSize))
            {
                ImGui.SetNextItemWidth(200);
                changed |= ImGui.SliderFloat("Text size", ref config.TextSize,
                    Inkwell.Fonts.TextFont.MinSize, Inkwell.Fonts.TextFont.MaxSize, "%.0f");
                changed |= ImGui.Checkbox("Also use it in the chunk preview and chat log", ref config.TextSizeInPreview);
            }
            ImGui.TextDisabled("Tip: hold Ctrl and scroll the mouse wheel over the text box.");
        }

        if (ImGui.CollapsingHeader("Channel colors", ImGuiTreeNodeFlags.DefaultOpen))
        {
            changed |= ImGui.Checkbox("Tint the text box and preview with the channel's color", ref config.ChannelTintBackground);
            using (ImRaii.Disabled(!config.ChannelTintBackground))
            {
                ImGui.SetNextItemWidth(200);
                var percent = config.ChannelTintStrength * 100f;
                if (ImGui.SliderFloat("Tint strength", ref percent, 5f, 60f, "%.0f%%"))
                {
                    config.ChannelTintStrength = percent / 100f;
                    changed = true;
                }
            }
            changed |= ImGui.Checkbox("Color the text with the channel's color (like the game's chat log)", ref config.ChannelColorText);
            ImGui.TextDisabled("Change each channel's color under Appearance > Chat channel colors.");
        }

        if (ImGui.CollapsingHeader("Chunks", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.SetNextItemWidth(200);
            changed |= ImGui.SliderInt("Max bytes per message", ref config.MaxChunkBytes, 100, 500);
            ImGui.SetNextItemWidth(200);
            changed |= ImGui.InputText("Continuation marker", ref config.ContinuationMarker, 32);
            ImGui.TextDisabled("#c = chunk number, #m = total chunks. Leave blank for none.");
            changed |= ImGui.Checkbox("Put marker on the last chunk too", ref config.MarkerOnLastChunk);
            changed |= ImGui.Checkbox("Prefer splitting at the end of a sentence", ref config.SplitOnSentences);
            changed |= ImGui.Checkbox("Clear text after the last chunk is copied or sent", ref config.ClearAfterLastCopy);
            changed |= ImGui.Checkbox("Ask before sending to chat when there are spelling errors", ref config.ConfirmSendWithErrors);
            changed |= ImGui.Checkbox("Typing /em, /p, /t Name@World etc. at the start switches the channel", ref config.ChatCommandsSwitchChannel);
            ImGui.TextDisabled("The command is removed from your text, so it never shows up in chat.");
            using (ImRaii.Disabled(!config.ChatCommandsSwitchChannel))
                changed |= ImGui.Checkbox("Go back to the previous channel when that post is finished", ref config.ChatCommandsTemporary);
        }

        DrawAppearance();

        if (changed)
        {
            config.Save();
            plugin.Pads.NotifySettingsChanged();
        }
    }

    private void DrawAppearance()
    {
        if (!ImGui.CollapsingHeader("Appearance (colors and transparency)")) return;

        ImGui.TextWrapped("Tick a box to use your own color for that part of the scratch pad. " +
                          "The A value (or the strip under the color picker) controls transparency: " +
                          "255 is solid, 0 is invisible. Unticked items keep the default look.");

        if (ImGui.Button("Reset all colors"))
        {
            config.ThemeOverrides.Clear();
            config.Save();
        }

        var flags = ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf;
        var groupColor = new Vector4(0.6f, 0.75f, 1f, 1f);
        string? group = null;
        var save = false;

        foreach (var entry in Theme.Entries)
        {
            if (entry.Group != group)
            {
                group = entry.Group;
                ImGui.Spacing();
                ImGui.TextColored(groupColor, group);
                ImGui.Separator();
            }

            using var id = ImRaii.PushId(entry.Key);

            var on = config.ThemeOverrides.TryGetValue(entry.Key, out var color);
            if (!on) color = Theme.Default(entry);

            if (ImGui.Checkbox("##on", ref on))
            {
                if (on) config.ThemeOverrides[entry.Key] = color;
                else config.ThemeOverrides.Remove(entry.Key);
                save = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(on ? "Using your color. Untick to go back to the default." : "Tick to customize.");

            ImGui.SameLine();
            using (ImRaii.Disabled(!on))
            {
                ImGui.SetNextItemWidth(220);
                if (ImGui.ColorEdit4(entry.Label, ref color, flags))
                    config.ThemeOverrides[entry.Key] = color;   // Live preview while dragging.
                if (ImGui.IsItemDeactivatedAfterEdit())
                    save = true;                               // Save once you let go.
            }
        }

        if (save) config.Save();
    }
}
