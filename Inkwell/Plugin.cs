using System.IO;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Inkwell.Fonts;
using Inkwell.Pads;
using Inkwell.Snoop;
using Inkwell.Spelling;
using Inkwell.Windows;

namespace Inkwell;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;

    private const string CommandName = "/inkwell";

    public Configuration Configuration { get; }
    public SpellChecker Spell { get; }
    public TextFont TextFont { get; }
    public ChatLog ChatLog { get; }
    public WindowSystem WindowSystem { get; } = new("Inkwell");
    public FileDialogManager FileDialogs { get; } = new();
    public MainWindow MainWindow { get; }
    public PadManager Pads { get; }
    public ConfigWindow ConfigWindow { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Spell = new SpellChecker(Configuration);
        TextFont = new TextFont(Configuration);
        ChatLog = new ChatLog(Configuration);
        LoadDictionary();

        MainWindow = new MainWindow(this);
        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(ConfigWindow);
        Pads = new PadManager(this, WindowSystem);
        ChatLog.EntryAdded += Pads.OnLogEntry;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Inkwell. \"/inkwell new\" adds a pad, \"/inkwell config\" opens settings.",
        });

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += MainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += ConfigWindow.Toggle;
    }

    /// <summary>
    /// Looks for the word list first in the plugin's config folder (survives updates, lets users
    /// drop in their own list), then next to the DLL (the copy shipped with the build).
    /// </summary>
    public void LoadDictionary()
    {
        var name = Configuration.DictionaryFile;
        var candidates = new[]
        {
            Path.Combine(PluginInterface.ConfigDirectory.FullName, name),
            Path.Combine(PluginInterface.AssemblyLocation.Directory!.FullName, "Dictionary", name),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            var count = Spell.LoadFile(path);
            Log.Information($"Inkwell: loaded {count} words from {path}");
            return;
        }

        Log.Warning($"Inkwell: no dictionary found. Looked in: {string.Join(", ", candidates)}");
    }

    private void DrawUi()
    {
        Pads.Update();          // Apply queued tab/window changes before anything draws.
        WindowSystem.Draw();
        FileDialogs.Draw();     // Save dialogs (chat log export).
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "config":
                ConfigWindow.Toggle();
                break;
            case "new":
                Pads.NewPad();
                MainWindow.IsOpen = true;
                break;
            default:
                MainWindow.Toggle();
                break;
        }
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenMainUi -= MainWindow.Toggle;
        ChatLog.EntryAdded -= Pads.OnLogEntry;
        ChatLog.Dispose();
        Pads.Dispose();
        TextFont.Dispose();
        PluginInterface.UiBuilder.OpenConfigUi -= ConfigWindow.Toggle;
        WindowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
    }
}
