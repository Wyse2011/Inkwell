using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Inkwell.Snoop;

/// <summary>One chat line from a player.</summary>
/// <param name="Name">Speaker's character name ("First Last"). Your own name for your lines.</param>
/// <param name="ColorKey">Theme key of the channel color.</param>
/// <param name="IsEmote">Emote lines read "Name does a thing" rather than "Name: text".</param>
/// <param name="NameInText">Standard emotes ("Bob waves to you.") already contain the name.</param>
public sealed record LogEntry(
    DateTime Time, string Name, string Text, string ColorKey, bool IsEmote, bool NameInText, bool IsSelf);

/// <summary>
/// Listens to the game's chat and keeps recent lines from players, so a pad can show everything
/// the people it watches have said, including lines from before they were added.
///
/// History comes from three places: lines that arrive while Inkwell is running, lines imported
/// from the game's own chat log (everything since you logged in), and, if you turn it on, a file
/// Inkwell saves so history survives restarting the game.
/// </summary>
public sealed unsafe class ChatLog : IDisposable
{
    private readonly Configuration config;
    private readonly List<LogEntry> entries = new();
    private readonly object gate = new();
    private readonly string historyFile;
    private bool unsaved;
    private DateTime lastSave = DateTime.Now;

    public ChatLog(Configuration config)
    {
        this.config = config;
        historyFile = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "chat-history.json");

        if (config.LogRememberHistory) LoadHistory();
        Plugin.ChatGui.ChatMessage += OnChatMessage;
        Plugin.Framework.Update += OnFrameworkUpdate;

        if (config.LogImportOnStart)
            Plugin.Framework.RunOnFrameworkThread(() => ImportFromGame());
    }

    /// <summary>Raised (on the game thread) for every line that was stored.</summary>
    public event Action<LogEntry>? EntryAdded;

    /// <summary>Lines spoken by any of <paramref name="names"/> (plus your own, if wanted), oldest first.</summary>
    public List<LogEntry> For(ICollection<string> names, bool includeSelf, int max = 400)
    {
        lock (gate)
        {
            var result = new List<LogEntry>();
            for (int i = entries.Count - 1; i >= 0 && result.Count < max; i--)
            {
                var e = entries[i];
                if (e.IsSelf ? includeSelf : names.Contains(e.Name))
                    result.Add(e);
            }
            result.Reverse();
            return result;
        }
    }

    public int Count
    {
        get { lock (gate) return entries.Count; }
    }

    /// <summary>Forgets all history, including the saved file.</summary>
    public void Clear()
    {
        lock (gate) entries.Clear();
        unsaved = false;
        try
        {
            if (File.Exists(historyFile)) File.Delete(historyFile);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Inkwell: couldn't delete saved chat history");
        }
    }

    // ------------------------------------------------------------------ import from the game

    /// <summary>
    /// Reads everything in the game's chat log (all lines since you logged in) and adds any lines
    /// Inkwell doesn't already have. Must run on the game thread. Returns how many were added.
    /// </summary>
    public int ImportFromGame()
    {
        var added = new List<LogEntry>();
        try
        {
            var module = RaptureLogModule.Instance();
            if (module == null) return 0;

            // The game's log is a list we can read by index; it says "false" past the end.
            for (int i = 0; i < 100_000; i++)
            {
                if (!module->GetLogMessageDetail(i, out var senderBytes, out var messageBytes,
                        out var logKind, out _, out _, out var time))
                    break;

                var when = DateTimeOffset.FromUnixTimeSeconds(time).LocalDateTime;
                if (TryMakeEntry((XivChatType)logKind, SeString.Parse(senderBytes), SeString.Parse(messageBytes), when, out var entry))
                    added.Add(entry);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Inkwell: couldn't read the game's chat log");
        }

        return Merge(added);
    }

    /// <summary>Adds entries that aren't already known (same person, same text, within a few seconds).</summary>
    private int Merge(List<LogEntry> incoming)
    {
        if (incoming.Count == 0) return 0;

        lock (gate)
        {
            var known = new Dictionary<string, List<DateTime>>();
            foreach (var e in entries)
            {
                var key = Key(e);
                if (!known.TryGetValue(key, out var times)) known[key] = times = new List<DateTime>();
                times.Add(e.Time);
            }

            int count = 0;
            foreach (var e in incoming)
            {
                var key = Key(e);
                if (known.TryGetValue(key, out var times) && times.Any(t => Math.Abs((t - e.Time).TotalSeconds) <= 5))
                    continue;
                entries.Add(e);
                if (!known.TryGetValue(key, out times)) known[key] = times = new List<DateTime>();
                times.Add(e.Time);
                count++;
            }

            if (count > 0)
            {
                entries.Sort((a, b) => a.Time.CompareTo(b.Time));
                Trim();
                unsaved = true;
            }
            return count;
        }
    }

    private static string Key(LogEntry e) => $"{e.Name.ToLowerInvariant()}\u0001{e.Text}";

    private void Trim()
    {
        var overflow = entries.Count - Math.Max(500, config.LogMaxEntries);
        if (overflow > 0) entries.RemoveRange(0, overflow);
    }

    // ------------------------------------------------------------------ saved history

    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(historyFile)) return;
            var saved = JsonSerializer.Deserialize<List<LogEntry>>(File.ReadAllText(historyFile));
            if (saved == null) return;
            lock (gate)
            {
                entries.AddRange(saved);
                entries.Sort((a, b) => a.Time.CompareTo(b.Time));
                Trim();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Inkwell: couldn't load saved chat history");
        }
    }

    public void SaveHistory()
    {
        if (!config.LogRememberHistory || !unsaved) return;
        try
        {
            string json;
            lock (gate) json = JsonSerializer.Serialize(entries);
            File.WriteAllText(historyFile, json);
            unsaved = false;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Inkwell: couldn't save chat history");
        }
    }

    private void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        // Save at most once a minute while there's something new.
        if (unsaved && config.LogRememberHistory && (DateTime.Now - lastSave).TotalSeconds > 60)
        {
            lastSave = DateTime.Now;
            SaveHistory();
        }
    }

    // Dalamud API 15+: chat events pass a single message object (type, sender, text).
    private void OnChatMessage(IHandleableChatMessage chat)
    {
        try
        {
            if (!TryMakeEntry(chat.LogKind, chat.Sender, chat.Message, DateTime.Now, out var entry))
                return;

            lock (gate)
            {
                entries.Add(entry);
                Trim();
                unsaved = true;
            }

            EntryAdded?.Invoke(entry);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Inkwell: failed to read a chat message");
        }
    }

    /// <summary>Turns a chat line into a log entry, if it's a player line on a channel we keep.</summary>
    private bool TryMakeEntry(XivChatType type, SeString sender, SeString message, DateTime time, out LogEntry entry)
    {
        entry = null!;
        var channel = Classify(type);
        if (channel == null || !IsChannelLogged(channel.Value.Group)) return false;

        // Players are identified by the link payload on their name. Your own lines have none.
        var player = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        var isStandardEmote = type == XivChatType.StandardEmote;
        if (player == null && isStandardEmote)
            player = message.Payloads.OfType<PlayerPayload>().FirstOrDefault();

        var isSelf = player == null;
        var name = player?.PlayerName ?? CleanName(sender.TextValue);
        if (string.IsNullOrWhiteSpace(name) && !isStandardEmote) return false;

        var text = message.TextValue.Trim();
        if (text.Length == 0) return false;

        entry = new LogEntry(time, name.Trim(), text, channel.Value.ColorKey,
            channel.Value.Group == ChannelGroup.Emote, isStandardEmote, isSelf);
        return true;
    }

    private enum ChannelGroup { Say, Emote, YellShout, Party, FreeCompany, Linkshell, Tell }

    private bool IsChannelLogged(ChannelGroup group) => group switch
    {
        ChannelGroup.Say => config.LogSay,
        ChannelGroup.Emote => config.LogEmotes,
        ChannelGroup.YellShout => config.LogYellShout,
        ChannelGroup.Party => config.LogParty,
        ChannelGroup.FreeCompany => config.LogFreeCompany,
        ChannelGroup.Linkshell => config.LogLinkshells,
        ChannelGroup.Tell => config.LogTells,
        _ => false,
    };

    private static (ChannelGroup Group, string ColorKey)? Classify(XivChatType type) => type switch
    {
        XivChatType.Say => (ChannelGroup.Say, "channel.say"),
        XivChatType.CustomEmote or XivChatType.StandardEmote => (ChannelGroup.Emote, "channel.emote"),
        XivChatType.Yell => (ChannelGroup.YellShout, "channel.yell"),
        XivChatType.Shout => (ChannelGroup.YellShout, "channel.shout"),
        XivChatType.Party or XivChatType.CrossParty => (ChannelGroup.Party, "channel.party"),
        XivChatType.Alliance => (ChannelGroup.Party, "channel.alliance"),
        XivChatType.FreeCompany => (ChannelGroup.FreeCompany, "channel.fc"),
        XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4 or
        XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8 => (ChannelGroup.Linkshell, "channel.ls"),
        XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2 or XivChatType.CrossLinkShell3 or
        XivChatType.CrossLinkShell4 or XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6 or
        XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8 => (ChannelGroup.Linkshell, "channel.cwls"),
        XivChatType.TellIncoming => (ChannelGroup.Tell, "channel.tell"),
        _ => null,
    };

    /// <summary>Removes the game's icon glyphs (party numbers, world icons) from a sender name.</summary>
    public static string CleanName(string raw)
    {
        var chars = raw.Where(c => c < '\uE000' || c > '\uF8FF').ToArray();
        return new string(chars).Trim();
    }

    public void Dispose()
    {
        Plugin.ChatGui.ChatMessage -= OnChatMessage;
        Plugin.Framework.Update -= OnFrameworkUpdate;
        SaveHistory();
    }
}
