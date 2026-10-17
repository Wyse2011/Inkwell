using System;
using Dalamud.Interface.ManagedFontAtlas;

namespace Inkwell.Fonts;

/// <summary>
/// The font used for pad text when a custom text size is set. Built through Dalamud's font atlas
/// so text stays sharp at any size (instead of stretching a small font).
/// </summary>
public sealed class TextFont : IDisposable
{
    public const float MinSize = 10f;
    public const float MaxSize = 48f;

    private readonly Configuration config;
    private IFontHandle? handle;
    private float builtSize = -1f;

    // While the size is being changed quickly (Ctrl + wheel), wait until it settles before
    // building a new font, so we don't build one for every step.
    private float wantedSize = -1f;
    private DateTime wantedSince;

    public TextFont(Configuration config) => this.config = config;

    /// <summary>
    /// Pushes the custom-size font if one is set, otherwise does nothing. Use with "using":
    /// a null result is fine there.
    /// </summary>
    public IDisposable? Push()
    {
        if (!config.CustomTextSize) return null;

        var size = Math.Clamp(config.TextSize, MinSize, MaxSize);
        if (MathF.Abs(size - builtSize) > 0.01f)
        {
            if (MathF.Abs(size - wantedSize) > 0.01f)
            {
                wantedSize = size;
                wantedSince = DateTime.Now;
            }
            if (handle == null || (DateTime.Now - wantedSince).TotalMilliseconds > 200)
                Rebuild(size);
        }

        // Until the new font finishes building, Dalamud pushes the default font instead.
        return handle?.Push();
    }

    private void Rebuild(float size)
    {
        handle?.Dispose();
        builtSize = size;
        handle = Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
            e.OnPreBuild(toolkit => toolkit.AddDalamudDefaultFont(size)));
    }

    public void Dispose()
    {
        handle?.Dispose();
        handle = null;
    }
}
