using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Inkwell.Windows;

namespace Inkwell.Pads;

/// <summary>
/// Owns every scratch pad: its view (UI state) and, when popped out, its window.
///
/// Adding or removing windows while Dalamud is drawing them isn't safe, so every structural
/// change (new, close, pop out, dock) is queued and applied by <see cref="Update"/> right before
/// the next frame is drawn.
/// </summary>
public sealed class PadManager : IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration config;
    private readonly WindowSystem windowSystem;

    private readonly Dictionary<int, PadView> views = new();
    private readonly Dictionary<int, PadWindow> windows = new();
    private readonly Queue<Action> pending = new();

    private bool saveQueued;
    private DateTime saveAt;

    public PadManager(Plugin plugin, WindowSystem windowSystem)
    {
        this.plugin = plugin;
        this.windowSystem = windowSystem;
        config = plugin.Configuration;

        // First run on v0.4+: turn the old single-pad settings into Pad 1.
        if (config.Pads.Count == 0)
        {
            config.Pads.Add(new PadData
            {
                Id = config.NextPadId++,
                Name = "Pad 1",
                HeaderIndex = config.HeaderIndex,
                TellTarget = config.TellTarget,
                Ooc = config.Ooc,
                EditorHeight = config.EditorHeight,
            });
            config.Save();
        }

        foreach (var pad in config.Pads)
        {
            views[pad.Id] = new PadView(plugin, this, pad);
            if (pad.Detached) OpenWindow(pad);
        }
    }

    /// <summary>Pad to select in the tab bar on the next frame (after creating or docking one).</summary>
    public int? SelectRequest { get; set; }

    public IReadOnlyList<PadData> Pads => config.Pads;

    public PadView View(PadData pad) => views[pad.Id];

    // ------------------------------------------------------------------ actions (queued)

    public void NewPad() => pending.Enqueue(() =>
    {
        var pad = CreatePad();
        SelectRequest = pad.Id;
    });

    public void Close(int id) => pending.Enqueue(() =>
    {
        var pad = Find(id);
        if (pad == null) return;

        CloseWindow(id);
        views.Remove(id);
        config.Pads.Remove(pad);

        // Always keep at least one pad around.
        if (config.Pads.Count == 0)
            SelectRequest = CreatePad().Id;

        QueueSave();
    });

    /// <param name="at">Screen position for the new window (e.g. where a dragged tab was dropped).</param>
    public void Detach(int id, Vector2? at = null) => pending.Enqueue(() =>
    {
        var pad = Find(id);
        if (pad == null || pad.Detached) return;

        pad.Detached = true;
        OpenWindow(pad, at);
        QueueSave();
    });

    public void Dock(int id) => pending.Enqueue(() =>
    {
        var pad = Find(id);
        if (pad == null) return;

        pad.Detached = false;
        CloseWindow(id);
        SelectRequest = id;
        plugin.MainWindow.IsOpen = true;
        QueueSave();
    });

    public void Rename(int id, string name) => pending.Enqueue(() =>
    {
        var pad = Find(id);
        if (pad == null) return;

        name = name.Trim();
        pad.Name = name.Length == 0 ? NextName() : name;
        QueueSave();
    });

    // ------------------------------------------------------------------ per frame

    /// <summary>Call once per frame, before WindowSystem.Draw.</summary>
    public void Update()
    {
        while (pending.Count > 0)
            pending.Dequeue().Invoke();

        if (saveQueued && DateTime.Now >= saveAt)
            SaveNow();
    }

    /// <summary>Saves a moment later, so typing doesn't write the config file on every key.</summary>
    public void QueueSave()
    {
        saveQueued = true;
        saveAt = DateTime.Now.AddSeconds(1.5);
    }

    public void NotifySettingsChanged()
    {
        foreach (var view in views.Values)
            view.OnSettingsChanged();
    }

    public void Dispose()
    {
        if (saveQueued) SaveNow();
        foreach (var w in windows.Values)
            windowSystem.RemoveWindow(w);
        windows.Clear();
    }

    // ------------------------------------------------------------------ helpers

    private PadData? Find(int id) => config.Pads.FirstOrDefault(p => p.Id == id);

    private PadData CreatePad()
    {
        var pad = new PadData { Id = config.NextPadId++, Name = NextName() };
        config.Pads.Add(pad);
        views[pad.Id] = new PadView(plugin, this, pad);
        QueueSave();
        return pad;
    }

    private string NextName()
    {
        for (int n = 1; ; n++)
        {
            var name = $"Pad {n}";
            if (config.Pads.All(p => p.Name != name)) return name;
        }
    }

    private void OpenWindow(PadData pad, Vector2? at = null)
    {
        if (windows.ContainsKey(pad.Id)) return;
        var window = new PadWindow(plugin, this, views[pad.Id]) { IsOpen = true };
        if (at != null) window.PlaceAt(at.Value);
        windows[pad.Id] = window;
        windowSystem.AddWindow(window);
    }

    private void CloseWindow(int id)
    {
        if (windows.Remove(id, out var window))
            windowSystem.RemoveWindow(window);
    }

    private void SaveNow()
    {
        saveQueued = false;
        config.Save();
    }
}
