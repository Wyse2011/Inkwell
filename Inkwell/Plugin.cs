using System.IO;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Inkwell.Pads;
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

    private const string CommandName = "/inkwell";

    public Configuration Configuration { get; }
    public SpellChecker Spell { get; }
    public WindowSystem WindowSystem { get; } = new("Inkwell");
    public MainWindow MainWindow { get; }
    public PadManager Pads { get; }
    public ConfigWindow ConfigWindow { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Spell = new SpellChecker(Configuration);
        LoadDictionary();

        MainWindow = new MainWindow(this);
        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(ConfigWindow);
        Pads = new PadManager(this, WindowSystem);

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
        Pads.Dispose();
        PluginInterface.UiBuilder.OpenConfigUi -= ConfigWindow.Toggle;
        WindowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
    }
}
