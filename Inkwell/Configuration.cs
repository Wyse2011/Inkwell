using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace Inkwell;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ---- Layout ----
    /// <summary>Height of the bottom editor in pixels. Changed by dragging the splitter.</summary>
    public float EditorHeight = 180f;

    // ---- Chat header ----
    public int HeaderIndex = 0;
    public string TellTarget = string.Empty;
    public bool Ooc = false;

    // ---- Chunking ----
    public int MaxChunkBytes = 500;
    public string ContinuationMarker = "(#c/#m)";
    public bool MarkerOnLastChunk = true;
    public bool SplitOnSentences = true;
    public bool ClearAfterLastCopy = false;

    // ---- Spell check ----
    public string DictionaryFile = "en_US.txt";
    public List<string> CustomWords = new();
    public bool IgnoreCapitalized = false;
    public bool IgnoreAllCaps = true;

    // ---- Highlighting in the editor ----
    public bool HighlightRecolor = true;
    public bool HighlightUnderline = true;
    public bool HighlightBackground = false;
    public Vector4 MisspellColor = new(1f, 0.35f, 0.35f, 1f);

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
