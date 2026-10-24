using System;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Inkwell.Snoop;

/// <summary>Plays the game's chat sound effects, the same ones as &lt;se.1&gt; to &lt;se.16&gt;.</summary>
public static class ChatSound
{
    public const int Min = 1;
    public const int Max = 16;

    /// <summary>Must be called on the game thread (chat events and UI drawing both are).</summary>
    public static void Play(int effect)
    {
        try
        {
            UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(effect, Min, Max));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Inkwell: couldn't play notification sound");
        }
    }
}
