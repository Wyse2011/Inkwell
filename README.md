# Inkwell

A Wordsmith-style roleplay scratch pad for Dalamud, with two changes:

1. **Resizable editor.** Drag the bar between the chunk preview and the text box to make the
   editor as tall as you want. The height is saved.
2. **Misspellings highlighted where you type.** Misspelled words are drawn in red (plus an
   optional squiggle or background) directly in the bottom text box, not only in the preview.

It also keeps the core Wordsmith flow: chat header picker (including /tell with a target), OOC
toggle, automatic chunking at 500 bytes with `(#c/#m)` continuation markers, one-click "copy next
chunk", and click-a-word to see suggestions or add it to your dictionary.

**Send to chat:** the **Send to chat** button (top right, next to Copy) posts the next chunk
straight into chat. One click sends one chunk; Copy and Send share the same counter, so they
always point at the same chunk. Text is run through the game's own chat filter and the 500-byte
limit before sending, and nothing is ever sent without a click.

**Custom colors:** `/inkwell config` → **Appearance** lets you recolor the title bar, window
background, toolbar, preview panel, text box, resize bar and status text, each with its own
transparency.

## Install (for players)

Copy this custom repository URL:

```
https://raw.githubusercontent.com/Wyse2011/Inkwell/main/repo.json
```

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste the URL into the empty box, click the **+**
   button, make sure the checkbox next to it is ticked, then click **Save and Close**.
3. Type `/xlplugins`, search for **Inkwell**, and click **Install**.
4. Type `/inkwell` to open the scratch pad, or `/inkwell config` for settings.

Inkwell is a third-party plugin and is not affiliated with or reviewed by the Dalamud team.

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
- Multiple scratch pads.
- Typing `/p` or `/em` at the start of the text to switch the header automatically.
