using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Inkwell.Snoop;

/// <summary>Turns a pad's chat log into a .md or .txt file.</summary>
public static class LogExporter
{
    public static string Build(IReadOnlyList<LogEntry> lines, string padName, bool markdown)
    {
        var sb = new StringBuilder();
        var people = lines.Where(l => !l.IsSelf).Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var exported = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        if (markdown)
        {
            sb.AppendLine($"# Chat log: {Escape(padName)}");
            sb.AppendLine();
            sb.AppendLine($"*Exported from Inkwell on {exported}*");
            if (people.Count > 0) sb.AppendLine($"*With: {Escape(string.Join(", ", people))}*");
            sb.AppendLine();

            DateTime? day = null;
            foreach (var e in lines)
            {
                if (day != e.Time.Date)
                {
                    day = e.Time.Date;
                    sb.AppendLine($"## {e.Time:dddd, MMMM d, yyyy}");
                    sb.AppendLine();
                }

                var time = $"`{e.Time:HH:mm}`";
                if (e.NameInText)
                    sb.AppendLine($"{time} *{Escape(e.Text)}*");
                else if (e.IsEmote)
                    sb.AppendLine($"{time} ***{Escape(e.Name)}*** *{Escape(e.Text)}*");
                else
                    sb.AppendLine($"{time} **{Escape(e.Name)}:** {Escape(e.Text)}");
                sb.AppendLine(); // Blank line so each message is its own paragraph.
            }
        }
        else
        {
            sb.AppendLine($"Chat log: {padName}");
            sb.AppendLine($"Exported from Inkwell on {exported}");
            if (people.Count > 0) sb.AppendLine($"With: {string.Join(", ", people)}");
            sb.AppendLine(new string('-', 40));

            foreach (var e in lines)
            {
                var time = $"[{e.Time:yyyy-MM-dd HH:mm}]";
                var body = e.NameInText ? e.Text : e.IsEmote ? $"{e.Name} {e.Text}" : $"{e.Name}: {e.Text}";
                sb.AppendLine($"{time} {body}");
            }
        }

        return sb.ToString();
    }

    /// <summary>A safe default file name like "Inkwell - Pad 1 - 2026-10-24 2130.md".</summary>
    public static string DefaultFileName(string padName)
    {
        var name = $"Inkwell - {padName} - {DateTime.Now:yyyy-MM-dd HHmm}";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    // Characters that would otherwise turn into formatting in Markdown.
    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '#' or '|' or '~')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
