using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

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
/// Nothing is saved to disk; history starts fresh each session.
/// </summary>
public sealed class ChatLog : IDisposable
{
    private readonly Configuration config;
    private readonly List<LogEntry> entries = new();
    private readonly object gate = new();

    public ChatLog(Configuration config)
    {
        this.config = config;
        Plugin.ChatGui.ChatMessage += OnChatMessage;
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

    public void Clear()
    {
        lock (gate) entries.Clear();
    }

    // Dalamud API 15+: chat events pass a single message object (type, sender, text).
    private void OnChatMessage(IHandleableChatMessage chat)
    {
        try
        {
            var type = chat.LogKind;
            var sender = chat.Sender;
            var message = chat.Message;

            var channel = Classify(type);
            if (channel == null || !IsChannelLogged(channel.Value.Group)) return;

            // Players are identified by the link payload on their name. Your own lines have none.
            var player = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
            var isStandardEmote = type == XivChatType.StandardEmote;
            if (player == null && isStandardEmote)
                player = message.Payloads.OfType<PlayerPayload>().FirstOrDefault();

            var isSelf = player == null;
            var name = player?.PlayerName ?? CleanName(sender.TextValue);
            if (string.IsNullOrWhiteSpace(name) && !isStandardEmote) return;

            var text = message.TextValue.Trim();
            if (text.Length == 0) return;

            var entry = new LogEntry(DateTime.Now, name.Trim(), text, channel.Value.ColorKey,
                channel.Value.Group == ChannelGroup.Emote, isStandardEmote, isSelf);

            lock (gate)
            {
                entries.Add(entry);
                var overflow = entries.Count - Math.Max(500, config.LogMaxEntries);
                if (overflow > 0) entries.RemoveRange(0, overflow);
            }

            EntryAdded?.Invoke(entry);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Inkwell: failed to read a chat message");
        }
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

    public void Dispose() => Plugin.ChatGui.ChatMessage -= OnChatMessage;
}
