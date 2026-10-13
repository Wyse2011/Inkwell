using System;

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
}
