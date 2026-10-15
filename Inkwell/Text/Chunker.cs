using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Inkwell.Text;

/// <param name="ColorKey">Theme entry holding this channel's color, or null for no color.</param>
public readonly record struct ChatHeader(string Name, string Command, string? ColorKey);

public static class ChatHeaders
{
    public const int TellIndex = 1;

    // Commands typed at the start of the text, mapped to their entry in All.
    private static readonly Dictionary<string, int> CommandAliases = BuildAliases();

    // "/em " etc. The trailing whitespace means the command has been fully typed.
    private static readonly Regex CommandRegex =
        new(@"^\s*/([a-z0-9]+)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "/t Name Surname@World " or "/t <t> ".
    private static readonly Regex TellRegex = new(
        @"^\s*/(?:t|tell)\s+(<[a-z0-9]+>|[\p{L}'\-]+\s+[\p{L}'\-]+(?:@[\p{L}]+)?)\s+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static Dictionary<string, int> BuildAliases()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["s"] = 0, ["say"] = 0,
            ["em"] = 2, ["emote"] = 2,
            ["p"] = 3, ["party"] = 3,
            ["fc"] = 4, ["freecompany"] = 4,
            ["a"] = 5, ["alliance"] = 5,
            ["y"] = 6, ["yell"] = 6,
            ["sh"] = 7, ["shout"] = 7,
        };
        for (int i = 1; i <= 8; i++)
        {
            map[$"l{i}"] = 7 + i;
            map[$"linkshell{i}"] = 7 + i;
            map[$"cwl{i}"] = 15 + i;
            map[$"cwlinkshell{i}"] = 15 + i;
        }
        return map;
    }

    /// <summary>
    /// If <paramref name="text"/> starts with a chat command like "/em " or "/t Name Surname@World ",
    /// returns which channel it means and how many characters to remove from the text.
    /// </summary>
    public static bool TryParseCommand(string text, out int headerIndex, out string? tellTarget, out int length)
    {
        headerIndex = 0;
        tellTarget = null;
        length = 0;

        var tell = TellRegex.Match(text);
        if (tell.Success)
        {
            headerIndex = TellIndex;
            tellTarget = Regex.Replace(tell.Groups[1].Value, @"\s+", " ");
            length = tell.Length;
            return true;
        }

        var m = CommandRegex.Match(text);
        if (!m.Success || !CommandAliases.TryGetValue(m.Groups[1].Value, out headerIndex))
            return false;

        length = m.Length;
        return true;
    }

    public static readonly ChatHeader[] All = BuildList();

    private static ChatHeader[] BuildList()
    {
        var list = new List<ChatHeader>
        {
            new("Say", "/s", "channel.say"),
            new("Tell", "/t", "channel.tell"),
            new("Emote", "/em", "channel.emote"),
            new("Party", "/p", "channel.party"),
            new("Free Company", "/fc", "channel.fc"),
            new("Alliance", "/a", "channel.alliance"),
            new("Yell", "/y", "channel.yell"),
            new("Shout", "/sh", "channel.shout"),
        };
        for (int i = 1; i <= 8; i++) list.Add(new($"Linkshell {i}", $"/l{i}", "channel.ls"));
        for (int i = 1; i <= 8; i++) list.Add(new($"Cross-world LS {i}", $"/cwl{i}", "channel.cwls"));
        list.Add(new("None (no header)", "", null));
        return list.ToArray();
    }

    public static string Resolve(int headerIndex, string tellTarget)
    {
        var idx = Math.Clamp(headerIndex, 0, All.Length - 1);
        if (idx == TellIndex)
            return string.IsNullOrWhiteSpace(tellTarget) ? "/t" : $"/t {tellTarget.Trim()}";
        return All[idx].Command;
    }
}

public static class Chunker
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static List<string> Build(string text, string header, bool ooc, Configuration cfg)
    {
        var result = new List<string>();
        text = Whitespace.Replace(text, " ").Trim();
        if (text.Length == 0) return result;

        var prefix = string.IsNullOrWhiteSpace(header) ? "" : header.Trim() + " ";
        var open = ooc ? "((" : "";
        var close = ooc ? "))" : "";
        var max = Math.Clamp(cfg.MaxChunkBytes, 50, 500);
        var fixedBytes = Utf8(prefix) + Utf8(open) + Utf8(close);

        // Fits in one message: no continuation marker needed.
        if (fixedBytes + Utf8(text) <= max)
        {
            result.Add(prefix + open + text + close);
            return result;
        }

        var useMarker = !string.IsNullOrWhiteSpace(cfg.ContinuationMarker);
        var worstMarker = useMarker ? " " + FormatMarker(cfg.ContinuationMarker, 99, 99) : "";
        var budget = Math.Max(20, max - fixedBytes - Utf8(worstMarker));

        var bodies = new List<string>();
        int pos = 0;
        while (pos < text.Length)
        {
            int end = CutIndex(text, pos, budget);
            if (end >= text.Length)
            {
                bodies.Add(text[pos..].Trim());
                break;
            }

            int split = FindSplit(text, pos, end, cfg.SplitOnSentences);
            bodies.Add(text[pos..split].Trim());
            pos = split;
            while (pos < text.Length && text[pos] == ' ') pos++;
        }

        for (int i = 0; i < bodies.Count; i++)
        {
            var last = i == bodies.Count - 1;
            var marker = useMarker && (!last || cfg.MarkerOnLastChunk)
                ? " " + FormatMarker(cfg.ContinuationMarker, i + 1, bodies.Count)
                : "";
            result.Add(prefix + open + bodies[i] + close + marker);
        }
        return result;
    }

    public static string FormatMarker(string marker, int current, int total) =>
        marker.Replace("#c", current.ToString()).Replace("#m", total.ToString());

    private static int Utf8(string s) => Encoding.UTF8.GetByteCount(s);

    /// <summary>End index (exclusive) of the longest run from <paramref name="start"/> that fits in <paramref name="maxBytes"/>.</summary>
    private static int CutIndex(string s, int start, int maxBytes)
    {
        int bytes = 0, i = start;
        while (i < s.Length)
        {
            int b, len;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { b = 4; len = 2; }
            else { b = s[i] < 0x80 ? 1 : s[i] < 0x800 ? 2 : 3; len = 1; }
            if (bytes + b > maxBytes) break;
            bytes += b;
            i += len;
        }
        return i;
    }

    private static int FindSplit(string text, int start, int end, bool preferSentences)
    {
        // Don't create a tiny chunk just to land on a sentence boundary.
        int minSplit = start + (end - start) / 2;

        if (preferSentences)
        {
            for (int i = end; i > minSplit; i--)
            {
                if (text[i] != ' ') continue;
                char p = text[i - 1];
                char pp = i >= 2 ? text[i - 2] : ' ';
                if (p is '.' or '!' or '?' || (p is '"' or '”' or ')' && pp is '.' or '!' or '?'))
                    return i;
            }
        }

        for (int i = end; i > start; i--)
            if (text[i] == ' ') return i;

        return end; // One enormous "word": hard cut.
    }
}
