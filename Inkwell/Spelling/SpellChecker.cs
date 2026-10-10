using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Inkwell.Spelling;

/// <summary>A misspelled word, located by character offset in the logical (unwrapped) text.</summary>
public readonly record struct Misspelling(int Start, int Length, string Word);

public sealed class SpellChecker
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz'";

    // A word starts with a letter, may contain letters, apostrophes and hyphens, and must not be
    // glued to digits on either side (so "2nd" or "ilvl690" are skipped). The atomic group stops
    // the regex from backtracking into a shorter match when followed by a digit.
    private static readonly Regex WordRegex =
        new(@"(?<![\p{L}\p{N}])(?>\p{L}[\p{L}'’\-]*)(?!\p{N})", RegexOptions.Compiled);

    private readonly Configuration config;
    private readonly HashSet<string> words = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> custom = new(StringComparer.OrdinalIgnoreCase);

    public SpellChecker(Configuration config)
    {
        this.config = config;
        foreach (var w in config.CustomWords) custom.Add(w);
    }

    public bool IsLoaded => words.Count > 0;

    /// <summary>
    /// Loads a plain word list (one word per line). Hunspell .dic files also work:
    /// the count on line one and the "/FLAGS" suffixes are ignored.
    /// </summary>
    public int LoadFile(string path)
    {
        words.Clear();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var slash = line.IndexOf('/');
            if (slash >= 0) line = line[..slash];
            if (line.All(char.IsDigit)) continue;
            words.Add(line.Replace('’', '\''));
        }
        return words.Count;
    }

    public void AddCustom(string word)
    {
        word = Normalize(word);
        if (word.Length == 0 || !custom.Add(word)) return;
        config.CustomWords.Add(word);
        config.Save();
    }

    public void RemoveCustom(string word)
    {
        custom.Remove(word);
        config.CustomWords.RemoveAll(w => w.Equals(word, StringComparison.OrdinalIgnoreCase));
        config.Save();
    }

    public List<Misspelling> Check(string text)
    {
        var result = new List<Misspelling>();
        if (!IsLoaded || string.IsNullOrEmpty(text)) return result;

        foreach (Match m in WordRegex.Matches(text))
        {
            // Trim leading/trailing apostrophes and hyphens so the highlight hugs the word.
            int start = m.Index, end = m.Index + m.Length;
            while (end > start && IsJoiner(text[end - 1])) end--;
            while (start < end && IsJoiner(text[start])) start++;
            if (end <= start) continue;

            var word = text[start..end];
            if (ShouldSkip(word) || IsKnown(word)) continue;
            result.Add(new Misspelling(start, end - start, word));
        }
        return result;
    }

    /// <summary>True if a raw token (may include punctuation) contains a misspelled word.</summary>
    public bool IsTokenMisspelled(string token)
    {
        if (!IsLoaded || token.StartsWith('/')) return false;
        foreach (var m in Check(token)) return true;
        return false;
    }

    public bool IsKnown(string word)
    {
        var w = Normalize(word);
        if (w.Length == 0) return true;
        if (words.Contains(w) || custom.Contains(w)) return true;

        // Possessives: "Aymeric's" is fine if "Aymeric" is.
        if (w.EndsWith("'s", StringComparison.OrdinalIgnoreCase) && w.Length > 2)
            return IsKnown(w[..^2]);

        // Hyphenated compounds: fine if every part is known.
        if (w.Contains('-'))
            return w.Split('-', StringSplitOptions.RemoveEmptyEntries).All(IsKnown);

        return false;
    }

    /// <summary>Edit-distance suggestions (distance 1 first, then 2). Only call on demand.</summary>
    public List<string> Suggest(string word, int max = 8)
    {
        var lower = Normalize(word).ToLowerInvariant();
        var found = new List<string>();
        var seen = new HashSet<string>();

        var e1 = Edits(lower).ToList();
        foreach (var c in e1)
            if (IsDictWord(c) && seen.Add(c)) found.Add(c);

        if (found.Count < max)
        {
            foreach (var a in e1)
            {
                foreach (var c in Edits(a))
                {
                    if (IsDictWord(c) && seen.Add(c)) found.Add(c);
                    if (found.Count >= max * 3) break;
                }
                if (found.Count >= max * 3) break;
            }
        }

        bool capitalize = word.Length > 0 && char.IsUpper(word[0]);
        return found.Take(max)
            .Select(s => capitalize && s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s)
            .ToList();
    }

    private bool IsDictWord(string w) => words.Contains(w) || custom.Contains(w);

    private bool ShouldSkip(string word)
    {
        if (config.IgnoreAllCaps && word.Length > 1 && word.All(c => !char.IsLetter(c) || char.IsUpper(c)))
            return true;
        if (config.IgnoreCapitalized && char.IsUpper(word[0]))
            return true;
        return false;
    }

    private static bool IsJoiner(char c) => c is '\'' or '’' or '-';

    private static string Normalize(string w) => w.Replace('’', '\'').Trim('\'', '-', ' ');

    private static IEnumerable<string> Edits(string w)
    {
        for (int i = 0; i <= w.Length; i++)
        {
            var left = w[..i];
            var right = w[i..];
            if (right.Length > 0) yield return left + right[1..];                              // delete
            if (right.Length > 1) yield return left + right[1] + right[0] + right[2..];        // transpose
            foreach (var c in Alphabet)
            {
                if (right.Length > 0) yield return left + c + right[1..];                      // replace
                yield return left + c + right;                                                 // insert
            }
        }
    }
}
