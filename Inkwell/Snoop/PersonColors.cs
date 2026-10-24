using System.Numerics;
using Inkwell.Windows;

namespace Inkwell.Snoop;

/// <summary>
/// A color for each person in the chat log. Chosen automatically from a readable palette (the
/// same name always gets the same color), unless you've picked one yourself.
/// </summary>
public static class PersonColors
{
    private static readonly Vector4[] Palette =
    {
        new(0.95f, 0.55f, 0.55f, 1f), // rose
        new(0.55f, 0.80f, 1.00f, 1f), // sky
        new(0.60f, 0.95f, 0.60f, 1f), // green
        new(1.00f, 0.80f, 0.45f, 1f), // amber
        new(0.80f, 0.60f, 1.00f, 1f), // lavender
        new(0.45f, 0.95f, 0.90f, 1f), // teal
        new(1.00f, 0.60f, 0.85f, 1f), // pink
        new(0.85f, 0.95f, 0.50f, 1f), // lime
        new(1.00f, 0.70f, 0.55f, 1f), // peach
        new(0.60f, 0.70f, 1.00f, 1f), // periwinkle
        new(0.95f, 0.95f, 0.60f, 1f), // butter
        new(0.70f, 0.90f, 0.75f, 1f), // mint
    };

    public static Vector4 For(Configuration config, string name)
    {
        var key = name.Trim().ToLowerInvariant();
        if (config.PersonColors.TryGetValue(key, out var custom)) return custom;
        return Palette[StableHash(key) % (uint)Palette.Length];
    }

    public static Vector4 For(Configuration config, LogEntry entry) =>
        entry.IsSelf ? Theme.Accent(config, "log.self") : For(config, entry.Name);

    public static void Set(Configuration config, string name, Vector4 color) =>
        config.PersonColors[name.Trim().ToLowerInvariant()] = color;

    // string.GetHashCode() changes every time the game starts; this doesn't, so colors stay put.
    private static uint StableHash(string s)
    {
        uint hash = 2166136261;
        foreach (var c in s)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash;
    }
}
