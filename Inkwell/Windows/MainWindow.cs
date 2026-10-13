using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Inkwell.Pads;

namespace Inkwell.Windows;

/// <summary>The main Inkwell window: one tab per docked scratch pad.</summary>
public sealed class MainWindow : Window
{
    private const string RenamePopup = "Rename pad###inkwell-rename";
    private const string ClosePopup = "Close pad?###inkwell-close";

    private readonly Plugin plugin;
    private readonly Configuration config;
    private ThemePush windowTheme;

    private int? openRenameFor;
    private int? openCloseFor;
    private int renameId;
    private string renameText = string.Empty;
    private int closeId;
    private string closeName = string.Empty;

    public MainWindow(Plugin plugin) : base("Inkwell###inkwell-main")
    {
        this.plugin = plugin;
        config = plugin.Configuration;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 340),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(560, 540);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    private PadManager Pads => plugin.Pads;

    public override void PreDraw() => windowTheme = Theme.Push(config, ThemeScope.Window);
    public override void PostDraw() => windowTheme.Dispose();

    public override void Draw()
    {
        var docked = Pads.Pads.Where(p => !p.Detached).ToList();
        var select = Pads.SelectRequest;

        using (var tabBar = ImRaii.TabBar("##inkwell-pads", ImGuiTabBarFlags.Reorderable))
        {
            if (tabBar)
            {
                foreach (var pad in docked)
                    DrawTab(pad, select == pad.Id);

                if (ImGui.TabItemButton("+", ImGuiTabItemFlags.Trailing | ImGuiTabItemFlags.NoTooltip))
                    Pads.NewPad();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("New pad");
            }
        }

        if (select != null) Pads.SelectRequest = null;

        if (docked.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.Accent(config, "status.muted"),
                "All of your pads are popped out into their own windows.");
            ImGui.TextColored(Theme.Accent(config, "status.muted"),
                "Click + to start a new one, or Dock in a pad's window to bring it back here.");
        }

        // Popups are opened here, outside the tab bar, so their IDs are stable.
        if (openRenameFor != null)
        {
            openRenameFor = null;
            ImGui.OpenPopup(RenamePopup);
        }
        if (openCloseFor != null)
        {
            openCloseFor = null;
            ImGui.OpenPopup(ClosePopup);
        }

        DrawRenamePopup();
        DrawClosePopup();
    }

    private void DrawTab(PadData pad, bool select)
    {
        var open = true;
        var flags = select ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;

        var visible = ImGui.BeginTabItem($"{pad.Name}###inkwell-tab-{pad.Id}", ref open, flags);

        // Double-click a tab to rename it.
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            StartRename(pad);

        // Right-click menu.
        if (ImGui.BeginPopupContextItem($"##inkwell-tabmenu-{pad.Id}"))
        {
            if (ImGui.Selectable("Rename")) StartRename(pad);
            if (ImGui.Selectable("Pop out to its own window")) Pads.Detach(pad.Id);
            if (ImGui.Selectable("New pad")) Pads.NewPad();
            ImGui.Separator();
            if (ImGui.Selectable("Close")) RequestClose(pad);
            ImGui.EndPopup();
        }

        if (visible)
        {
            Pads.View(pad).Draw();
            ImGui.EndTabItem();
        }

        if (!open)
            RequestClose(pad);
    }

    private void StartRename(PadData pad)
    {
        renameId = pad.Id;
        renameText = pad.Name;
        openRenameFor = pad.Id;
    }

    private void RequestClose(PadData pad)
    {
        // Empty pads close straight away; pads with text ask first.
        if (!Pads.View(pad).HasText)
        {
            Pads.Close(pad.Id);
            return;
        }

        closeId = pad.Id;
        closeName = pad.Name;
        openCloseFor = pad.Id;
    }

    private void DrawRenamePopup()
    {
        using var popup = ImRaii.Popup(RenamePopup);
        if (!popup) return;

        ImGui.TextUnformatted("Pad name");
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();

        ImGui.SetNextItemWidth(220);
        var enter = ImGui.InputText("##name", ref renameText, 40, ImGuiInputTextFlags.EnterReturnsTrue);

        if (ImGui.Button("Save") || enter)
        {
            Pads.Rename(renameId, renameText);
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawClosePopup()
    {
        using var popup = ImRaii.Popup(ClosePopup);
        if (!popup) return;

        ImGui.TextUnformatted($"\"{closeName}\" still has text in it.");
        ImGui.TextUnformatted("Close it and delete the text?");
        ImGui.Spacing();

        using (ImRaii.PushColor(ImGuiCol.Button, Theme.Accent(config, "copy.warn")))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, Theme.Accent(config, "copy.warn.hover")))
        {
            if (ImGui.Button("Close pad"))
            {
                Pads.Close(closeId);
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Keep it"))
            ImGui.CloseCurrentPopup();
    }
}
