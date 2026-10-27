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

    // ---- Chat log (Snooper features) ----
    public bool ShowChunkPreview = true;
    public bool LogIncludeSelf = true;
    public bool LogTimestamps = true;
    public int LogMaxEntries = 5000;
    public bool LogImportOnStart = true;
    public bool LogRememberHistory = false;
    public bool LogSay = true;
    public bool LogEmotes = true;
    public bool LogYellShout = true;
    public bool LogParty = true;
    public bool LogFreeCompany = true;
    public bool LogLinkshells = false;
    public bool LogTells = true;
    public bool NotifyOnlyWhenUnfocused = true;
    public bool LogColorByPerson = true;
    public bool LogWholeLineByPerson = false;
    public bool LogSpaceBetweenSpeakers = true;
    /// <summary>Colors you've picked for people (lowercase name). Others are chosen automatically.</summary>
    public Dictionary<string, Vector4> PersonColors = new();

    // ---- GPose ----
    public bool ShowInGpose = true;
    // Which channels can still send while in GPose. Separate true/false settings (rather than a
    // list) so saving and loading the config can't duplicate entries.
    public bool GposeAllowSay = false;
    public bool GposeAllowEmote = false;
    public bool GposeAllowTell = false;
    public bool GposeAllowParty = true;
    public bool GposeAllowFreeCompany = true;
    public bool GposeAllowLinkshells = true;
    public bool GposeAllowYellShout = false;
    public bool GposeAllowNoHeader = true;

    // ---- Text size ----
    public bool CustomTextSize = false;
    public float TextSize = 20f;
    public bool TextSizeInPreview = true;

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
