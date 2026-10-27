# Inkwell

A roleplay scratch pad for FINAL FANTASY XIV, built on Dalamud. Write long posts in a
comfortable editor, catch typos before they go out, and send them to chat in correctly-headed,
correctly-sized chunks.

## Credit where it's due: Inkwell is built on Wordsmith

Inkwell is **not an original idea.** Its framework (the scratch pad, the chunk preview above
the text box, chat headers, continuation markers, the OOC toggle, spell check, and the
one-click "copy the next chunk" workflow) comes straight from
**[Wordsmith](https://github.com/MythicPalette/Wordsmith-DalamudPlugin)** by
**[MythicPalette](https://github.com/MythicPalette)**.

A huge shoutout to MythicPalette for all the work he has put into Wordsmith. It has made
in-game roleplay easier for a lot of people, and Inkwell exists only because Wordsmith showed
how good this kind of tool can be. If you like Inkwell, please go star Wordsmith too.

Inkwell's code was written from scratch rather than forked, so any bugs in Inkwell are Inkwell's
own and should be reported here, not to Wordsmith.

**Wordsmith repository:** https://github.com/MythicPalette/Wordsmith-DalamudPlugin

### And on Snooper

Inkwell's chat log features (following what specific people say, per-window watch lists,
and blink/sound notifications) are modeled on
**[Snooper](https://codeberg.org/Linneris/dalamud-snooper)** by **Linneris**, a plugin built to
help you keep track of individual speakers at busy RP events. Thank you to Linneris and
Snooper's contributors for the idea and the inspiration. As with Wordsmith, Inkwell's code is
written from scratch, so please report Inkwell bugs here rather than to Snooper.

**Snooper repository:** https://codeberg.org/Linneris/dalamud-snooper

## What Inkwell changes and adds

Quality-of-life changes on top of the Wordsmith-style workflow:

- **Chat log for the people you're writing with (from Snooper).** Add people to a pad with
  **+ Target** (or by name), or turn on **Follow target**, and everything they say (say, emotes,
  yells, party, FC, linkshells, tells) appears above your text, colored by channel, with your
  own lines included if you like. Each pad has its own list of people, so one pad can follow
  your scene partner while another follows your party. Each person gets their own color
  (chosen automatically, or pick one with the swatch on their tag), and a small gap separates
  different speakers, so busy scenes are easy to follow.
  Earlier messages are pulled in from the game's own chat log (everything since you logged
  in), and you can optionally have Inkwell remember history between sessions.
  Export any pad's chat log to a Markdown (.md) or plain text (.txt) file with the
  **Export...** button above the log.
- **New-message alerts.** Per pad, the tab or window title blinks and/or a game sound
  (`<se.1>` to `<se.16>`) plays when someone in that pad posts, optionally only when you're not
  already looking at it.
- **Tabs and pop-out windows.** Keep several pads open as tabs (click **+** to add one), and
  pop any pad out into its own window so you can see several at once. Drag a tab away from
  the tab row to tear it off into a window, and drag a window by its title bar back over the
  main Inkwell window to turn it into a tab again. Each pad has its own
  text, chat channel, /tell target, OOC setting and chunk counter. Closing a popped-out window
  puts the pad back in the tabs instead of deleting it, and all pads are saved between sessions.
- **Type chat commands to switch channels.** Start a pad with `/em `, `/p `, `/fc `, `/l1 `,
  `/t Name Surname@World `, `/r ` and so on, and Inkwell switches that pad's channel and removes the
  command from your text, so it never shows up in the post itself. The switch only lasts for
  that post: once it's sent, copied or cleared, the pad goes back to its previous channel.
- **Color-coded channels like the game's chat log.** Channel names in the dropdown are shown in
  their chat colors (Party blue, Tell pink, Emote teal and so on, using FFXIV's defaults). You can
  optionally tint the text box or color your text by channel too, and every channel's color is
  adjustable in Appearance.
- **Reply channel.** Pick **Reply (last tell)** in the channel dropdown, or type `/r `, to send
  your post with `/r` to whoever last sent you a tell.
- **Works in GPose.** Inkwell stays open in GPose, notices when you're in it, greys out
  channels that don't work there (like Tell and Emote) and pauses Send for them, while
  channels that still work (like Party) send as normal. Which channels count as working is
  adjustable in settings.
- **Resizable text box.** Drag the bar between the chunk preview and the text box to make the
  editor as large as you like. The size is remembered between sessions.
- **Misspellings highlighted where you type.** Misspelled words are marked directly in the
  text box at the bottom (red text, a squiggly underline and/or a background highlight, your
  choice), not only in the preview, so they're easy to find before you copy.
- **Adjustable text size.** Make the text in the text box (and optionally the preview) as large
  or small as you like in settings, or hold Ctrl and scroll over the text box. Text stays sharp
  at every size.
- **Click-to-fix spelling bar.** Every misspelled word is listed as a button above the preview.
  Click one to pick a suggestion, type a replacement, or add the word to your personal
  dictionary.
- **Send to chat.** A **Send to chat** button sits next to **Copy** at the top right. Each click
  posts exactly one chunk to chat; click again for the next. Text is checked against the game's
  own chat filter and the 500-byte limit first, and nothing is ever sent without a click.
- **Copy and Send stay in sync.** Both buttons share one counter ("Copy 2/3", "Send to chat
  2/3"), so using either one moves both to the next chunk.
- **Safety checks before sending.** Optional "Send anyway?" confirmation when there are spelling
  errors, a short cooldown so a double-click can't send two chunks, and a block on chunks that
  would be run as a game command when no chat channel is selected.
- **Spelling warning on the buttons.** Copy and Send turn red while there are possible spelling
  errors, with a tooltip showing how many.
- **Full color and transparency customization.** Under Settings, then Appearance, you can recolor
  the title bar, window background, border, text, toolbar buttons and fields, preview panel, text
  box, resize bar, scrollbars and status messages, each with its own transparency.
- **Status line.** A short message confirms each copy or send, or explains why a send was
  blocked.

### Wordsmith features Inkwell doesn't have (yet)

To be upfront: Wordsmith still does more in some areas. Inkwell currently has no thesaurus and
no custom chat aliases. If you rely on those, Wordsmith is the better
choice for now.

## Install (for players)

Copy this custom repository URL:

```
https://raw.githubusercontent.com/Wyse2011/Inkwell/main/repo.json
```

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste the URL into the empty box, click the **+**
   button, make sure the checkbox next to it is ticked, then click **Save and Close**.
3. Type `/xlplugins`, search for **Inkwell**, and click **Install**.
4. Type `/inkwell` to open Inkwell, `/inkwell new` to add a pad, or `/inkwell config` for
   settings.

Inkwell is a third-party plugin and is not affiliated with Square Enix, Wordsmith or
MythicPalette.

---

# Building from source (for developers)

## Requirements

- XIVLauncher with Dalamud, and the game launched at least once with Dalamud enabled
  (the SDK finds Dalamud's files from that install).
- The .NET SDK version your Dalamud API level uses. If it's wrong, the build error will name the
  version it wants.
- Visual Studio 2022+, Rider, or VS Code with the C# extension.

## 1. Add a dictionary

Spell check needs a word list at `Inkwell/Dictionary/en_US.txt`, one word per line. Any of these
work:

- `words_alpha.txt` from github.com/dwyl/english-words (public domain, large, includes some
  junk words so it will miss some typos)
- A SCOWL list from wordlist.aspell.net (size 60 or 70 is a good balance)
- A Hunspell `.dic` file (the flags after `/` are ignored)

Rename it to `en_US.txt` and put it in `Inkwell/Dictionary/`. You can also drop a list into the
plugin's config folder (`%AppData%\XIVLauncher\pluginConfigs\Inkwell\`) and it will be used
instead, which survives plugin updates.

## 2. Build

```
cd Inkwell
dotnet build -c Debug
```

The DLL ends up in `Inkwell/bin/x64/Debug/Inkwell.dll` (the exact path is printed by the build).

## 3. Load it in game

1. Type `/xlsettings`, open **Experimental**, add the full path to `Inkwell.dll` under
   **Dev Plugin Locations**, and save.
2. Type `/xlplugins`, go to **Dev Tools > Installed Dev Plugins**, and enable Inkwell.
3. Type `/inkwell` to open the scratch pad, `/inkwell config` for settings.

After rebuilding, disable and re-enable the plugin in `/xlplugins` to reload it.

## 4. Publish a release (makes the custom repository work)

The repository URL above reads `repo.json`, which points Dalamud at a file named `latest.zip`
attached to the newest GitHub release. Until a release exists, the plugin will show up in
`/xlplugins` but fail to install.

1. Make sure `Inkwell/Dictionary/en_US.txt` exists on your machine so it gets packed into the zip.
2. Build in Release mode:
   ```
   cd Inkwell
   dotnet build -c Release
   ```
   The SDK's packager creates `Inkwell/bin/x64/Release/Inkwell/latest.zip` (the build output
   shows the exact path).
3. On GitHub, go to **Releases > Draft a new release**, create a tag such as `v0.1.0`, attach
   `latest.zip` (keep that exact file name), and click **Publish release**.

To ship an update: bump `<Version>` in `Inkwell.csproj` and `AssemblyVersion` in `repo.json`
to the same new number, commit, build in Release, and publish a new release with the new
`latest.zip`. Dalamud compares those version numbers to decide when to offer an update. If you
move to a newer Dalamud SDK, update `DalamudApiLevel` in `repo.json` to match.

## If it doesn't compile

Dalamud switched to new ImGui bindings in API 13, and small signature details can shift between
versions. Everything binding-sensitive is in `SpellTextEditor.OnInputCallback`. If that's where
the error is, hover `ImGui.InputTextMultiline` in your IDE to see which callback type it expects
(a `ref ImGuiInputTextCallbackData` or an `ImGuiInputTextCallbackDataPtr`) and whether
`BufDirty` is a `byte` or a `bool`. The logic doesn't change, just the types.

## How the in-editor highlighting works

ImGui can't color individual words inside a text box, so Inkwell draws over it:

- The text box sits inside a scrolling panel and is sized to fit every line, so the box itself
  never scrolls and line positions are always known. The panel scrolls instead, and follows your
  cursor as you type.
- Word wrap is done by turning spaces into newlines. Chat is single-line, so every newline in
  the box is a wrapped space (Enter is blocked). The text length never changes, so spell-check
  positions map exactly onto what's on screen, and they're turned back into spaces before copying.
- After the text box draws, misspelled words are redrawn on top in your highlight color.

## Ideas for next steps

- Right-click a highlighted word in the editor itself to get suggestions.
- Custom chat aliases (e.g. `/friendo` for a frequent /tell target).

## Acknowledgements

- [Wordsmith](https://github.com/MythicPalette/Wordsmith-DalamudPlugin) by MythicPalette, the
  plugin whose design and workflow Inkwell is built on.
- [Dalamud](https://github.com/goatcorp/Dalamud) and the goatcorp team.
- [Snooper](https://codeberg.org/Linneris/dalamud-snooper) by Linneris, the plugin whose
  per-character chat log Inkwell's chat log is modeled on.
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs), used for sending chat and
  playing notification sounds.
- [dwyl/english-words](https://github.com/dwyl/english-words) (public domain), the default
  spell-check word list.
