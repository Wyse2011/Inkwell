using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Inkwell.Windows;

/// <summary>Where a color is applied.</summary>
public enum ThemeScope
{
    /// <summary>The whole scratch pad window, including the title bar. Pushed before the window opens.</summary>
    Window,
    /// <summary>Only the chunk preview panel at the top.</summary>
    Preview,
    /// <summary>Only the text box at the bottom.</summary>
    Editor,
    /// <summary>Colors Inkwell draws itself (status text, splitter, copy warning). Always have a value.</summary>
    Accent,
}

/// <param name="Key">Saved name in the config file. Never change once released.</param>
/// <param name="Targets">ImGui colors this entry overrides (empty for accents).</param>
/// <param name="AccentDefault">Built-in value for accent colors.</param>
public sealed record ThemeEntry(
    string Key, string Label, string Group, ThemeScope Scope, ImGuiCol[] Targets, Vector4? AccentDefault = null);

public static class Theme
{
    public static readonly ThemeEntry[] Entries =
    {
        // ---- Title bar ----
        new("title", "Title bar", "Title bar", ThemeScope.Window, new[] { ImGuiCol.TitleBg }),
        new("title.active", "Title bar (focused)", "Title bar", ThemeScope.Window, new[] { ImGuiCol.TitleBgActive }),
        new("title.collapsed", "Title bar (collapsed)", "Title bar", ThemeScope.Window, new[] { ImGuiCol.TitleBgCollapsed }),

        // ---- Window ----
        new("window.bg", "Background", "Window", ThemeScope.Window, new[] { ImGuiCol.WindowBg }),
        new("window.border", "Border", "Window", ThemeScope.Window, new[] { ImGuiCol.Border }),
        new("window.text", "Text", "Window", ThemeScope.Window, new[] { ImGuiCol.Text }),
        new("window.popup", "Popups and dropdowns", "Window", ThemeScope.Window, new[] { ImGuiCol.PopupBg }),
        new("window.scrolltrack", "Scrollbar track", "Window", ThemeScope.Window, new[] { ImGuiCol.ScrollbarBg }),
        new("window.scrollgrab", "Scrollbar handle", "Window", ThemeScope.Window,
            new[] { ImGuiCol.ScrollbarGrab, ImGuiCol.ScrollbarGrabHovered, ImGuiCol.ScrollbarGrabActive }),
        new("window.grip", "Resize corner", "Window", ThemeScope.Window,
            new[] { ImGuiCol.ResizeGrip, ImGuiCol.ResizeGripHovered, ImGuiCol.ResizeGripActive }),

        // ---- Toolbar ----
        new("btn", "Buttons", "Toolbar", ThemeScope.Window, new[] { ImGuiCol.Button }),
        new("btn.hover", "Button (hovered)", "Toolbar", ThemeScope.Window, new[] { ImGuiCol.ButtonHovered }),
        new("btn.active", "Button (pressed)", "Toolbar", ThemeScope.Window, new[] { ImGuiCol.ButtonActive }),
        new("field", "Dropdown, checkbox and text fields", "Toolbar", ThemeScope.Window, new[] { ImGuiCol.FrameBg }),
        new("field.hover", "Fields (hovered)", "Toolbar", ThemeScope.Window,
            new[] { ImGuiCol.FrameBgHovered, ImGuiCol.FrameBgActive }),
        new("check", "Checkmark", "Toolbar", ThemeScope.Window, new[] { ImGuiCol.CheckMark }),
        new("list.highlight", "Dropdown highlight", "Toolbar", ThemeScope.Window,
            new[] { ImGuiCol.Header, ImGuiCol.HeaderHovered, ImGuiCol.HeaderActive }),
        new("copy.warn", "Copy button (spelling errors)", "Toolbar", ThemeScope.Accent, Array.Empty<ImGuiCol>(),
            new Vector4(0.55f, 0.2f, 0.2f, 1f)),
        new("copy.warn.hover", "Copy button (errors, hovered)", "Toolbar", ThemeScope.Accent, Array.Empty<ImGuiCol>(),
            new Vector4(0.7f, 0.25f, 0.25f, 1f)),

        // ---- Preview panel ----
        new("preview.bg", "Background", "Chunk preview (top)", ThemeScope.Preview, new[] { ImGuiCol.ChildBg }),
        new("preview.text", "Text", "Chunk preview (top)", ThemeScope.Preview, new[] { ImGuiCol.Text }),
        new("preview.sep", "Divider lines", "Chunk preview (top)", ThemeScope.Preview, new[] { ImGuiCol.Separator }),
        new("preview.next", "\"Next to copy\" label", "Chunk preview (top)", ThemeScope.Accent, Array.Empty<ImGuiCol>(),
            new Vector4(0.5f, 0.9f, 0.5f, 1f)),

        // ---- Editor ----
        new("editor.bg", "Text box background", "Text box (bottom)", ThemeScope.Editor,
            new[] { ImGuiCol.FrameBg, ImGuiCol.ChildBg }),
        new("editor.text", "Text", "Text box (bottom)", ThemeScope.Editor, new[] { ImGuiCol.Text }),
        new("editor.select", "Selected text highlight", "Text box (bottom)", ThemeScope.Editor,
            new[] { ImGuiCol.TextSelectedBg }),
        new("splitter", "Resize bar", "Text box (bottom)", ThemeScope.Accent, Array.Empty<ImGuiCol>(),
            new Vector4(0.43f, 0.43f, 0.5f, 0.5f)),
        new("splitter.hover", "Resize bar (hovered / dragging)", "Text box (bottom)", ThemeScope.Accent,
            Array.Empty<ImGuiCol>(), new Vector4(0.26f, 0.59f, 0.98f, 1f)),

        // ---- Status messages ----
        new("status.error", "Error / misspelled count", "Status messages", ThemeScope.Accent, Array.Empty<ImGuiCol>(),
            new Vector4(1f, 0.4f, 0.4f, 1f)),
        new("status.ok", "No errors", "Status messages", ThemeScope.Accent, Array.Empty<ImGuiCol>(),
            new Vector4(0.5f, 0.9f, 0.5f, 1f)),
        new("status.warn", "Warnings", "Status messages", ThemeScope.Accent, Array.Empty<ImGuiCol>(),
            new Vector4(1f, 0.85f, 0.3f, 1f)),
        new("status.muted", "Hints and muted text", "Status messages", ThemeScope.Accent, Array.Empty<ImGuiCol>(),
            new Vector4(0.6f, 0.6f, 0.6f, 1f)),
    };

    private static readonly Dictionary<string, ThemeEntry> ByKey = Entries.ToDictionary(e => e.Key);

    /// <summary>The color used when the user hasn't overridden this entry.</summary>
    public static Vector4 Default(ThemeEntry e) =>
        e.AccentDefault ?? ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(e.Targets[0]));

    /// <summary>Current value of an accent color (override or built-in default).</summary>
    public static Vector4 Accent(Configuration cfg, string key) =>
        cfg.ThemeOverrides.TryGetValue(key, out var v) ? v : ByKey[key].AccentDefault!.Value;

    /// <summary>Pushes every overridden color for a scope. Dispose (or call Pop) to undo.</summary>
    public static ThemePush Push(Configuration cfg, ThemeScope scope)
    {
        int count = 0;
        foreach (var e in Entries)
        {
            if (e.Scope != scope || !cfg.ThemeOverrides.TryGetValue(e.Key, out var color)) continue;
            foreach (var target in e.Targets)
            {
                ImGui.PushStyleColor(target, color);
                count++;
            }
        }
        return new ThemePush(count);
    }
}

public readonly struct ThemePush : IDisposable
{
    private readonly int count;
    public ThemePush(int count) => this.count = count;
    public void Dispose()
    {
        if (count > 0) ImGui.PopStyleColor(count);
    }
}
