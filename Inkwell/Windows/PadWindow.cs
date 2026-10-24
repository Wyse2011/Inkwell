using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Inkwell.Pads;

namespace Inkwell.Windows;

/// <summary>A scratch pad popped out of the tabs into its own window.</summary>
public sealed class PadWindow : Window
{
    private readonly Plugin plugin;
    private readonly Configuration config;
    private readonly PadManager manager;
    private readonly PadView view;
    private ThemePush windowTheme;
    private int blinkPushes;

    private bool placePending;
    private bool titlePressed;   // Mouse went down on this window's title bar.
    private bool titleDragged;   // ...and has actually moved since.

    public PadWindow(Plugin plugin, PadManager manager, PadView view)
        : base(Title(view.Data))
    {
        this.plugin = plugin;
        config = plugin.Configuration;
        this.manager = manager;
        this.view = view;

        // Escape shouldn't dock the pad while you're typing in it.
        RespectCloseHotkey = false;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(520, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    // The ### part keeps the window's identity (position, size) stable when the pad is renamed.
    private static string Title(PadData pad) => $"Inkwell - {pad.Name}###inkwell-pad-{pad.Id}";

    public override void PreDraw()
    {
        WindowName = Title(view.Data);
        windowTheme = Theme.Push(config, ThemeScope.Window);
        blinkPushes = view.HasUnread && Theme.BlinkOn ? MainWindow.PushTitleBlink(config) : 0;
    }

    public override void PostDraw()
    {
        if (blinkPushes > 0) ImGui.PopStyleColor(blinkPushes);
        windowTheme.Dispose();

        // Position is only forced for the first frame, then the window moves freely.
        if (placePending)
        {
            placePending = false;
            Position = null;
        }
    }

    /// <summary>Opens the window with its title bar under <paramref name="screenPos"/>.</summary>
    public void PlaceAt(Vector2 screenPos)
    {
        Position = screenPos;
        PositionCondition = ImGuiCond.Always;
        placePending = true;
    }

    public override void Draw()
    {
        TrackTitleDrag();
        view.Draw();
    }

    /// <summary>
    /// Dragging this window by its title bar over the main Inkwell window docks it back into the
    /// tabs when the mouse is released.
    /// </summary>
    private void TrackTitleDrag()
    {
        var mouse = ImGui.GetMousePos();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var titleHeight = ImGui.GetFrameHeight();

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsWindowHovered())
        {
            // Title bar, minus the close button on the right.
            var onTitle = mouse.Y >= pos.Y && mouse.Y < pos.Y + titleHeight
                       && mouse.X >= pos.X && mouse.X < pos.X + size.X - titleHeight;
            titlePressed = onTitle;
            titleDragged = false;
        }

        if (!titlePressed) return;

        var main = plugin.MainWindow;
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Left)) titleDragged = true;
            if (titleDragged && main.IsDropTarget(mouse))
                main.DrawDropHint(view.Data.Name);
            return;
        }

        // Released.
        if (titleDragged && main.IsDropTarget(mouse))
            manager.Dock(view.Data.Id);
        titlePressed = false;
        titleDragged = false;
    }

    /// <summary>Closing a popped-out pad puts it back in the tabs. It never deletes it.</summary>
    public override void OnClose() => manager.Dock(view.Data.Id);
}
