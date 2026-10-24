using System;
using System.Collections.Generic;

namespace Inkwell.Pads;

/// <summary>Everything saved about one scratch pad.</summary>
[Serializable]
public class PadData
{
    public int Id;
    public string Name = "Pad";
    public string Text = string.Empty;

    public int HeaderIndex;
    public string TellTarget = string.Empty;
    public bool Ooc;

    /// <summary>True when the pad is popped out into its own window instead of being a tab.</summary>
    public bool Detached;

    public float EditorHeight = 180f;

    // ---- Chat log (Snooper features) ----
    /// <summary>Characters whose chat this pad shows ("First Last").</summary>
    public List<string> Watched = new();
    /// <summary>Also show whoever is currently targeted.</summary>
    public bool FollowTarget;
    public float LogHeight = 180f;
    public bool NotifyBlink = true;
    public bool NotifySound;
    public int NotifySoundId = 1;

    /// <summary>
    /// Set when a typed command (like "/p ") switched the channel for one post. The channel goes
    /// back to this once the post is finished. Null when no temporary switch is active.
    /// </summary>
    public int? RevertHeaderIndex;
    public string RevertTellTarget = string.Empty;
}
