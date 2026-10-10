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

        if (ImGui.CollapsingHeader("Chunks", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.SetNextItemWidth(200);
            changed |= ImGui.SliderInt("Max bytes per message", ref config.MaxChunkBytes, 100, 500);
            ImGui.SetNextItemWidth(200);
            changed |= ImGui.InputText("Continuation marker", ref config.ContinuationMarker, 32);
            ImGui.TextDisabled("#c = chunk number, #m = total chunks. Leave blank for none.");
            changed |= ImGui.Checkbox("Put marker on the last chunk too", ref config.MarkerOnLastChunk);
            changed |= ImGui.Checkbox("Prefer splitting at the end of a sentence", ref config.SplitOnSentences);
            changed |= ImGui.Checkbox("Clear text after the last chunk is copied", ref config.ClearAfterLastCopy);
        }

        DrawAppearance();

        if (changed)
        {
            config.Save();
            plugin.ScratchPad.OnSettingsChanged();
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
