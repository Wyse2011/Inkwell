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
    private int blinkPushes;

    private int? openRenameFor;
    private int? openCloseFor;
    private int renameId;
    private string renameText = string.Empty;
    private int closeId;
    private string closeName = string.Empty;

    // Tab tear-off state.
    private int? tabDragId;
    private string tabDragName = string.Empty;
    private Vector2 tabRowMin, tabRowMax;

    // Where this window was last drawn, so popped-out pads know where to drop.
    private Vector2 windowMin, windowMax;
    private int lastDrawFrame = -10;

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

    public override void PreDraw()
    {
        windowTheme = Theme.Push(config, ThemeScope.Window);

        // Blink the title bar while any tab here has unread messages.
        var unread = Pads.Pads.Any(p => !p.Detached && Pads.View(p).HasUnread);
        blinkPushes = unread && Theme.BlinkOn ? PushTitleBlink(config) : 0;
    }

    public override void PostDraw()
    {
        if (blinkPushes > 0) ImGui.PopStyleColor(blinkPushes);
        windowTheme.Dispose();
    }

    /// <summary>Pushes the blink color onto the title bar. Returns how many colors were pushed.</summary>
    internal static int PushTitleBlink(Configuration config)
    {
        var c = Theme.Accent(config, "notify.blink");
        ImGui.PushStyleColor(ImGuiCol.TitleBg, c);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, c);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, c);
        return 3;
    }


    public override void Draw()
    {
        var docked = Pads.Pads.Where(p => !p.Detached).ToList();
        var select = Pads.SelectRequest;

        windowMin = ImGui.GetWindowPos();
        windowMax = windowMin + ImGui.GetWindowSize();
        lastDrawFrame = ImGui.GetFrameCount();
        tabRowMin = ImGui.GetCursorScreenPos();
        tabRowMax = new Vector2(windowMax.X, tabRowMin.Y + ImGui.GetFrameHeight());

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

        HandleTabTearOff();

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

        // Unread tabs blink.
        var blink = Pads.View(pad).HasUnread && Theme.BlinkOn;
        var blinkColor = Theme.Accent(config, "notify.blink");
        bool visible;
        using (ImRaii.PushColor(ImGuiCol.Tab, blinkColor, blink))
        using (ImRaii.PushColor(ImGuiCol.TabHovered, blinkColor, blink))
            visible = ImGui.BeginTabItem($"{pad.Name}###inkwell-tab-{pad.Id}", ref open, flags);

        // Remember which tab is being dragged; HandleTabTearOff decides what happens on release.
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            tabDragId = pad.Id;
            tabDragName = pad.Name;
        }

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

    // ------------------------------------------------------------------ drag and drop

    /// <summary>Dragging a tab well away from the tab row and releasing pops it out there.</summary>
    private void HandleTabTearOff()
    {
        if (tabDragId is not int id) return;

        var mouse = ImGui.GetMousePos();
        var tornOff = IsAwayFromTabRow(mouse);

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (tornOff)
                DrawLabel(mouse + new Vector2(14, 14), $"Release to pop out \"{tabDragName}\"");
            return;
        }

        if (tornOff)
            Pads.Detach(id, mouse - new Vector2(40, 10));
        tabDragId = null;
    }

    private bool IsAwayFromTabRow(Vector2 mouse)
    {
        var margin = ImGui.GetFrameHeight() * 1.5f;
        return mouse.Y > tabRowMax.Y + margin || mouse.Y < tabRowMin.Y - margin
            || mouse.X < windowMin.X - margin || mouse.X > windowMax.X + margin;
    }

    /// <summary>True if a popped-out pad released at <paramref name="mouse"/> should dock here.</summary>
    public bool IsDropTarget(Vector2 mouse)
    {
        if (!IsOpen || ImGui.GetFrameCount() - lastDrawFrame > 1) return false;
        return mouse.X >= windowMin.X && mouse.X <= windowMax.X
            && mouse.Y >= windowMin.Y && mouse.Y <= windowMax.Y;
    }

    /// <summary>Highlights this window while a popped-out pad is dragged over it.</summary>
    public void DrawDropHint(string padName)
    {
        var drawList = ImGui.GetForegroundDrawList();
        var accent = Theme.Accent(config, "splitter.hover");

        drawList.AddRectFilled(windowMin, windowMax, ImGui.GetColorU32(accent with { W = 0.18f }), 6f);
        drawList.AddRect(windowMin, windowMax, ImGui.GetColorU32(accent), 6f, ImDrawFlags.None, 3f);

        var text = $"Release to dock \"{padName}\" as a tab";
        var center = (windowMin + windowMax) / 2f;
        DrawLabel(center - ImGui.CalcTextSize(text) / 2f, text);
    }

    private static void DrawLabel(Vector2 pos, string text)
    {
        var drawList = ImGui.GetForegroundDrawList();
        var pad = new Vector2(8, 5);
        var size = ImGui.CalcTextSize(text) + pad * 2;

        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(ImGuiCol.PopupBg), 4f);
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(ImGuiCol.Border), 4f);
        drawList.AddText(pos + pad, ImGui.GetColorU32(ImGuiCol.Text), text);
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
