using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Inkwell.Pads;

namespace Inkwell.Windows;

/// <summary>A scratch pad popped out of the tabs into its own window.</summary>
public sealed class PadWindow : Window
{
    private readonly Configuration config;
    private readonly PadManager manager;
    private readonly PadView view;
    private ThemePush windowTheme;

    public PadWindow(Plugin plugin, PadManager manager, PadView view)
        : base(Title(view.Data))
    {
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
    }

    public override void PostDraw() => windowTheme.Dispose();

    public override void Draw() => view.Draw();

    /// <summary>Closing a popped-out pad puts it back in the tabs. It never deletes it.</summary>
    public override void OnClose() => manager.Dock(view.Data.Id);
}
