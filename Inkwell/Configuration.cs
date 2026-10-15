using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using Inkwell.Pads;

namespace Inkwell;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ---- Scratch pads ----
    /// <summary>All open pads, in tab order. Each has its own text, channel and settings.</summary>
    public List<PadData> Pads = new();
    public int NextPadId = 1;

    // ---- Pre-tabs settings (v0.3 and earlier). Only read once, to build the first pad. ----
    public float EditorHeight = 180f;
    public int HeaderIndex = 0;
    public string TellTarget = string.Empty;
    public bool Ooc = false;

    // ---- Chunking ----
    public int MaxChunkBytes = 500;
    public string ContinuationMarker = "(#c/#m)";
    public bool MarkerOnLastChunk = true;
    public bool SplitOnSentences = true;
    public bool ClearAfterLastCopy = false;
    public bool ConfirmSendWithErrors = true;
    public bool ChatCommandsSwitchChannel = true;
    public bool ChatCommandsTemporary = true;

    // ---- Channel colors ----
    public bool ChannelTintBackground = false;
    public float ChannelTintStrength = 0.15f;
    public bool ChannelColorText = false;

    // ---- Spell check ----
    public string DictionaryFile = "en_US.txt";
    public List<string> CustomWords = new();
    public bool IgnoreCapitalized = false;
    public bool IgnoreAllCaps = true;

    // ---- Grammar and style ----
    public bool GrammarEnabled = true;
    public bool GrammarDoubledWords = true;
    public bool GrammarArticles = true;
    public bool GrammarLowercaseI = true;
    public bool GrammarCapitalization = true;
    public bool GrammarSpacing = true;
    public bool GrammarCommonMistakes = true;
    public bool StyleRunOns = true;
    public int RunOnWordLimit = 35;
    public bool StyleOverusedWords = true;
    public int OveruseLimit = 3;
    public bool StyleCloseRepeats = true;
    public bool StyleRepeatedStarts = true;

    // ---- Highlighting in the editor ----
    public bool HighlightRecolor = true;
    public bool HighlightUnderline = true;
    public bool HighlightBackground = false;
    public Vector4 MisspellColor = new(1f, 0.35f, 0.35f, 1f);

    // ---- Appearance ----
    /// <summary>Custom colors keyed by Theme entry. Missing key = use the default look.</summary>
    public Dictionary<string, Vector4> ThemeOverrides = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
