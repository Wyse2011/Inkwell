using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Inkwell.Grammar;

public enum IssueKind
{
    /// <summary>Very likely a mistake (orange squiggle).</summary>
    Grammar,
    /// <summary>A writing suggestion, not an error (blue dotted underline).</summary>
    Style,
}

/// <param name="Start">Character offset in the logical (unwrapped) text.</param>
/// <param name="Rule">Short rule id, used for ignoring.</param>
/// <param name="Original">The text that was flagged.</param>
/// <param name="Label">Short text for the issue's button.</param>
/// <param name="Fix">Suggested replacement for <paramref name="Original"/>, or null.</param>
public sealed record GrammarIssue(
    int Start, int Length, IssueKind Kind, string Rule, string Message, string Original, string Label, string? Fix);

/// <summary>
/// Offline, rule-based grammar and style checks. Not a full grammar engine: it looks for specific,
/// common problems and leans toward staying quiet rather than flagging correct writing.
/// </summary>
public sealed class GrammarChecker
{
    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex WordRegex = new(@"[\p{L}][\p{L}'’]*", RegexOptions.Compiled);
    private static readonly Regex DoubledWord = new(@"\b([\p{L}'’]+)\s+(\1)\b", Opts);
    private static readonly Regex Article = new(@"\b(a|an)\s+([\p{L}][\p{L}'’\-]*)", Opts);
    private static readonly Regex LowerI = new(@"(?<![\p{L}\p{N}'’.])i(?!\.e\.)(?=['’][\p{L}]|[\s,.!?;:]|$)", RegexOptions.Compiled);
    private static readonly Regex MissingSpace = new(@"(?<=\p{Ll})([,;:!?])(?=\p{L})|(?<=\p{Ll}\p{Ll})(\.)(?=\p{Lu}\p{Ll})", RegexOptions.Compiled);
    private static readonly Regex LowerAfterStop = new(@"(?<![.\s])([.!?])\s+(\p{Ll})", RegexOptions.Compiled);

    // Words where a double is usually intentional.
    private static readonly HashSet<string> AllowedDoubles = new(StringComparer.OrdinalIgnoreCase)
        { "had", "that", "is", "very", "so", "no", "bye", "ha", "haha", "knock", "now", "there", "really", "far", "long", "many", "more", "on", "out", "round", "over", "again" };

    private static readonly string[] ConsonantSoundPrefixes = { "uni", "use", "usu", "uti", "ure", "uro", "eu", "ewe", "one", "once", "ufo" };
    private static readonly string[] VowelSoundH = { "hour", "honest", "honor", "honour", "heir" };

    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
        { "e.g", "i.e", "etc", "vs", "mr", "mrs", "ms", "dr", "st", "approx", "ca" };

    private static readonly HashSet<string> Conjunctions = new(StringComparer.OrdinalIgnoreCase)
        { "and", "then", "so", "but" };

    private static readonly (Regex Pattern, Func<Match, string> Fix, string Message)[] Phrases =
    {
        (new Regex(@"\b(could|should|would|must|might)\s+of\b", Opts),
            m => $"{m.Groups[1].Value} have", "\"of\" should be \"have\" here."),
        (new Regex(@"\b(more|less|better|worse|rather|other|larger|smaller|faster|slower|higher|lower|older|younger|bigger|stronger|weaker|taller|shorter)\s+then\b", Opts),
            m => $"{m.Groups[1].Value} than", "Comparisons use \"than\", not \"then\"."),
        (new Regex(@"\byour\s+welcome\b", Opts), m => MatchCase(m.Value, "you're welcome"), "\"you're\" (you are) welcome."),
        (new Regex(@"\balot\b", Opts), m => MatchCase(m.Value, "a lot"), "\"a lot\" is two words."),
        (new Regex(@"\bsuppose\s+to\b", Opts), m => MatchCase(m.Value, "supposed to"), "Should be \"supposed to\"."),
        (new Regex(@"\bit['’]s\s+own\b", Opts), m => MatchCase(m.Value, "its own"), "\"its own\" has no apostrophe."),
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","the","and","or","but","so","then","than","if","of","to","in","on","at","by","for","with","from","into","onto",
        "up","down","out","over","under","off","as","is","am","are","was","were","be","been","being","do","does","did","have",
        "has","had","it","its","it's","this","that","these","those","there","their","they","them","he","him","his","she","her",
        "hers","we","us","our","you","your","i","me","my","mine","not","no","just","very","what","which","who","whom","when",
        "where","why","how","all","any","some","each","every","both","more","most","much","many","such","only","own","same",
        "too","can","could","will","would","shall","should","may","might","must","about","again","once","here","while","before",
        "after","through","between","because","until","against","also","back","even","still","yet","now","like","one","well",
        "her's","i'm","i'll","i'd","i've","you're","you'll","he's","she's","they're","we're","that's","what's","don't","didn't",
        "can't","won't","isn't","wasn't","aren't","weren't","doesn't","hasn't","haven't","hadn't","couldn't","wouldn't","shouldn't",
        "said","says","say","got","get","gets","let","went","goes","going","come","came","made","make","take","took","look","looks",
        "looked","seem","seemed","thing","things","something","nothing","anything","everything","someone","anyone","himself",
        "herself","themselves","itself","myself","yourself","ourselves","toward","towards","around","away","along","across",
    };

    private readonly Configuration config;
    private readonly HashSet<string> ignored = new(StringComparer.OrdinalIgnoreCase);

    public GrammarChecker(Configuration config) => this.config = config;

    /// <summary>Hide this exact issue (rule + text) until the game is restarted.</summary>
    public void Ignore(GrammarIssue issue) => ignored.Add(IgnoreKey(issue.Rule, issue.Original));

    public void ClearIgnored() => ignored.Clear();

    /// <param name="emoteChannel">Emotes follow the character's name, so the first word may be lowercase.</param>
    public List<GrammarIssue> Check(string text, bool emoteChannel)
    {
        var issues = new List<GrammarIssue>();
        if (!config.GrammarEnabled || string.IsNullOrWhiteSpace(text)) return issues;

        var sentences = SplitSentences(text);

        if (config.GrammarDoubledWords) CheckDoubledWords(text, issues);
        if (config.GrammarArticles) CheckArticles(text, issues);
        if (config.GrammarLowercaseI) CheckLowercaseI(text, issues);
        if (config.GrammarCapitalization) CheckCapitalization(text, emoteChannel, issues);
        if (config.GrammarSpacing) CheckSpacing(text, issues);
        if (config.GrammarCommonMistakes) CheckPhrases(text, issues);
        if (config.StyleRunOns) CheckRunOns(text, sentences, issues);
        if (config.StyleOverusedWords) CheckOverused(text, issues);
        if (config.StyleRepeatedStarts) CheckRepeatedStarts(text, sentences, issues);

        return issues
            .Where(i => !ignored.Contains(IgnoreKey(i.Rule, i.Original)))
            .OrderBy(i => i.Start)
            .ToList();
    }

    // ------------------------------------------------------------------ grammar rules

    private static void CheckDoubledWords(string text, List<GrammarIssue> issues)
    {
        foreach (Match m in DoubledWord.Matches(text))
        {
            var word = m.Groups[1].Value;
            if (AllowedDoubles.Contains(word)) continue;
            issues.Add(new GrammarIssue(m.Index, m.Length, IssueKind.Grammar, "double", $"\"{word}\" is repeated.",
                m.Value, $"{word} {word}", word));
        }
    }

    private static void CheckArticles(string text, List<GrammarIssue> issues)
    {
        foreach (Match m in Article.Matches(text))
        {
            var article = m.Groups[1].Value;
            var next = m.Groups[2].Value;
            if (next.Length > 1 && next.All(c => !char.IsLetter(c) || char.IsUpper(c))) continue; // Acronyms: "an FC"

            var wantsAn = StartsWithVowelSound(next);
            var isAn = article.Equals("an", StringComparison.OrdinalIgnoreCase);
            if (wantsAn == isAn) continue;

            var correct = MatchCase(article, wantsAn ? "an" : "a");
            issues.Add(new GrammarIssue(m.Index, m.Length, IssueKind.Grammar, "article",
                $"Use \"{correct}\" before \"{next}\".", m.Value, m.Value, $"{correct} {next}"));
        }
    }

    private static bool StartsWithVowelSound(string word)
    {
        var w = word.ToLowerInvariant();
        if (VowelSoundH.Any(p => w.StartsWith(p, StringComparison.Ordinal))) return true;
        if (ConsonantSoundPrefixes.Any(p => w.StartsWith(p, StringComparison.Ordinal))) return false;
        return "aeiou".Contains(w[0]);
    }

    private static void CheckLowercaseI(string text, List<GrammarIssue> issues)
    {
        foreach (Match m in LowerI.Matches(text))
            issues.Add(new GrammarIssue(m.Index, 1, IssueKind.Grammar, "lower-i", "\"I\" is always capitalized.",
                "i", "i -> I", "I"));
    }

    private static void CheckCapitalization(string text, bool emoteChannel, List<GrammarIssue> issues)
    {
        // Start of the post.
        if (!emoteChannel)
        {
            var first = text.TakeWhile(c => !char.IsLetter(c)).Count();
            if (first < text.Length && char.IsLower(text[first]) && !(text[first] == 'i' && IsLoneI(text, first)))
                issues.Add(Capital(text, first));
        }

        // After . ! ? (but not after "..." or abbreviations like "e.g.").
        foreach (Match m in LowerAfterStop.Matches(text))
        {
            var letter = m.Groups[2].Index;
            var before = LastWord(text, m.Index);
            if (m.Groups[1].Value == "." && Abbreviations.Contains(before)) continue;
            if (text[letter] == 'i' && IsLoneI(text, letter)) continue; // The lowercase-i rule covers it.
            issues.Add(Capital(text, letter));
        }
    }

    private static GrammarIssue Capital(string text, int index)
    {
        var word = WordRegex.Match(text, index).Value;
        var fixedWord = char.ToUpperInvariant(word[0]) + word[1..];
        return new GrammarIssue(index, word.Length, IssueKind.Grammar, "capital",
            "Sentences start with a capital letter.", word, $"{word} -> {fixedWord}", fixedWord);
    }

    private static bool IsLoneI(string text, int i) =>
        i + 1 >= text.Length || !char.IsLetter(text[i + 1]);

    private static string LastWord(string text, int endExclusive)
    {
        int start = endExclusive;
        while (start > 0 && (char.IsLetter(text[start - 1]) || text[start - 1] == '.')) start--;
        return text[start..endExclusive];
    }

    private static void CheckSpacing(string text, List<GrammarIssue> issues)
    {
        foreach (Match m in MissingSpace.Matches(text))
        {
            // Show a little context ("word,word") on the button.
            int s = m.Index, e = m.Index + 1;
            while (s > 0 && char.IsLetter(text[s - 1]) && m.Index - s < 12) s--;
            while (e < text.Length && char.IsLetter(text[e]) && e - m.Index < 12) e++;
            var snippet = text[s..e];

            issues.Add(new GrammarIssue(m.Index, 1, IssueKind.Grammar, "spacing",
                $"Add a space after \"{m.Value}\".", m.Value, snippet, m.Value + " "));
        }
    }

    private static void CheckPhrases(string text, List<GrammarIssue> issues)
    {
        foreach (var (pattern, fix, message) in Phrases)
        {
            foreach (Match m in pattern.Matches(text))
            {
                var suggestion = fix(m);
                issues.Add(new GrammarIssue(m.Index, m.Length, IssueKind.Grammar, "phrase", message,
                    m.Value, m.Value, suggestion));
            }
        }
    }

    // ------------------------------------------------------------------ style rules

    private void CheckRunOns(string text, List<(int Start, int End)> sentences, List<GrammarIssue> issues)
    {
        foreach (var (start, end) in sentences)
        {
            var sentence = text[start..end];
            var words = WordRegex.Matches(sentence).Select(m => m.Value).ToList();
            if (words.Count < 15) continue;

            var joins = words.Count(Conjunctions.Contains);
            var commas = sentence.Count(c => c == ',');

            var reasons = new List<string>();
            if (words.Count > config.RunOnWordLimit) reasons.Add($"{words.Count} words long");
            if (joins >= 4) reasons.Add($"{joins} clauses joined by and/then/so/but");
            if (commas >= 5 && words.Count > 25) reasons.Add($"{commas} commas");
            if (reasons.Count == 0) continue;

            var preview = string.Join(' ', words.Take(3)) + "...";
            issues.Add(new GrammarIssue(start, end - start, IssueKind.Style, "runon",
                $"Possible run-on sentence ({string.Join(", ", reasons)}). Consider splitting it.",
                preview, $"Long: {preview}", null));
        }
    }

    private void CheckOverused(string text, List<GrammarIssue> issues)
    {
        var words = WordRegex.Matches(text).Cast<Match>()
            .Where(m => m.Length >= 4 && !StopWords.Contains(m.Value.Replace('’', '\'')))
            .ToList();

        var groups = words.GroupBy(m => m.Value.ToLowerInvariant());
        foreach (var group in groups)
        {
            var list = group.ToList();

            // Names are capitalized every time; repeating them is normal in RP.
            if (list.All(m => char.IsUpper(m.Value[0]))) continue;

            if (list.Count >= config.OveruseLimit)
            {
                foreach (var m in list)
                    issues.Add(new GrammarIssue(m.Index, m.Length, IssueKind.Style, "overused",
                        $"\"{group.Key}\" is used {list.Count} times. Try varying it.",
                        group.Key, $"{group.Key} x{list.Count}", null));
                continue;
            }

            if (!config.StyleCloseRepeats) continue;

            // Same word again within a few words.
            for (int i = 1; i < list.Count; i++)
            {
                var gap = WordRegex.Matches(text[(list[i - 1].Index + list[i - 1].Length)..list[i].Index]).Count;
                if (gap > 8) continue;
                issues.Add(new GrammarIssue(list[i].Index, list[i].Length, IssueKind.Style, "overused",
                    $"\"{group.Key}\" appears twice close together.", group.Key, $"{group.Key} (close repeat)", null));
            }
        }
    }

    private static void CheckRepeatedStarts(string text, List<(int Start, int End)> sentences, List<GrammarIssue> issues)
    {
        var firsts = sentences
            .Select(s => WordRegex.Match(text, s.Start, s.End - s.Start))
            .ToList();

        for (int i = 2; i < firsts.Count; i++)
        {
            var a = firsts[i - 2]; var b = firsts[i - 1]; var c = firsts[i];
            if (!a.Success || !b.Success || !c.Success) continue;
            if (!a.Value.Equals(b.Value, StringComparison.OrdinalIgnoreCase)
                || !b.Value.Equals(c.Value, StringComparison.OrdinalIgnoreCase)) continue;

            issues.Add(new GrammarIssue(c.Index, c.Length, IssueKind.Style, "starts",
                $"Several sentences in a row start with \"{c.Value}\". Try varying how they begin.",
                c.Value.ToLowerInvariant(), $"\"{c.Value}...\" starts", null));
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Sentence spans. A sentence ends at . ! or ? (plus closing quotes/brackets) followed by a space
    /// and a non-lowercase letter, so "She paused... then left" stays one sentence.
    /// </summary>
    public static List<(int Start, int End)> SplitSentences(string text)
    {
        var result = new List<(int, int)>();
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '!' or '?')) continue;

            int end = i + 1;
            while (end < text.Length && text[end] is '.' or '!' or '?' or '"' or '”' or '\'' or '’' or ')' or ']') end++;

            int next = end;
            while (next < text.Length && char.IsWhiteSpace(text[next])) next++;

            var atEnd = next >= text.Length;
            var spaced = next > end;
            var nextNotLower = !atEnd && !char.IsLower(text[next]);
            if (atEnd || (spaced && nextNotLower && !Abbreviations.Contains(LastWord(text, i))))
            {
                AddSentence(text, result, start, end);
                start = next;
            }
            i = end - 1;
        }

        AddSentence(text, result, start, text.Length);
        return result;
    }

    private static void AddSentence(string text, List<(int, int)> result, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        if (end > start) result.Add((start, end));
    }

    private static string MatchCase(string source, string replacement) =>
        source.Length > 0 && char.IsUpper(source[0]) && replacement.Length > 0
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;

    private static string IgnoreKey(string rule, string original) => $"{rule}\u0001{original.ToLowerInvariant()}";
}
