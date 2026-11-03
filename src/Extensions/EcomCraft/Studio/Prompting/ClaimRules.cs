using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio.Prompting;

/// <summary>Advertising-claim wording checks (F79, T99): words that advertising law or common enforcement practice questions — such as
/// superlatives — are only noticed, never blocked. The word list ships in Templates/claim-words.json and an administrator can replace it as a
/// whole with Data/EcomCraft/claim-words.json (the same mechanism as platforms.json). Pure rules, independent of SwarmUI, so the list format,
/// the fallback behavior and the hits can be unit tested.</summary>
public static class ClaimRules
{
    /// <summary>One word of the list: what it is, where the concern comes from, whether that basis is an actual cited provision, and what to write
    /// instead. <see cref="Verified"/> is true only when <see cref="Basis"/> cites the provision's original text; everything else marks common
    /// enforcement practice that still has to be checked against the official rules.</summary>
    public sealed record class ClaimWord(string Word, string Category, string Basis, bool Verified, string Suggestion);

    /// <summary>One word found in a text: the word, where it starts (character index), and the word's entry.</summary>
    public sealed record class ClaimHit(string Word, int Start, string Category, string Basis, bool Verified, string Suggestion);

    /// <summary>Most words accepted from one file, so a broken override cannot slow every check to a crawl.</summary>
    public const int MaxWords = 500;

    /// <summary>Longest word accepted, characters.</summary>
    public const int MaxWordLength = 30;

    /// <summary>Most hits reported for one text.</summary>
    public const int MaxHits = 50;

    /// <summary>Reads a word list from one parsed claim-words file: entries without a word or a category, words that are too long or contain line
    /// breaks or control characters, duplicates, more than <see cref="MaxWords"/> entries, and entries marked verified without a basis are
    /// dropped — a verified word without its citation is exactly what must not reach users.</summary>
    public static List<ClaimWord> Parse(JObject file)
    {
        List<ClaimWord> words = [];
        if (file?["words"] is not JArray entries)
        {
            return words;
        }
        foreach (JToken entry in entries)
        {
            if (entry is not JObject item || words.Count >= MaxWords)
            {
                continue;
            }
            string word = item["word"]?.ToString()?.Trim() ?? "";
            string category = item["category"]?.ToString()?.Trim() ?? "";
            string basis = item["basis"]?.ToString()?.Trim() ?? "";
            bool verified = item["verified"]?.Type == JTokenType.Boolean && item.Value<bool?>("verified") == true;
            string suggestion = item["suggestion"]?.ToString()?.Trim() ?? "";
            if (word.Length == 0 || word.Length > MaxWordLength || category.Length == 0 || word.Any(char.IsControl)
                || (verified && basis.Length == 0) || words.Any(existing => existing.Word == word))
            {
                continue;
            }
            words.Add(new ClaimWord(word, category, basis, verified, suggestion));
        }
        return words;
    }

    /// <summary>Chooses the effective word list from the file contents (F79): an administrator's override that parses as a claim-words file
    /// replaces the built-in list as a whole — an empty override word list is a valid way to switch the check off — while a damaged override (not
    /// JSON, or no word list inside) falls back to the built-in file, and when that is damaged too the check simply finds nothing. Returns the
    /// chosen list and where it came from: "custom", "builtin" or "none".</summary>
    public static (List<ClaimWord> Words, string Source) Load(string builtinText, string customText)
    {
        JObject ParseOne(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            try
            {
                return JObject.Parse(text);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        JObject custom = ParseOne(customText);
        if (custom is not null && custom["words"] is JArray)
        {
            return (Parse(custom), "custom");
        }
        JObject builtin = ParseOne(builtinText);
        return builtin is null ? ([], "none") : (Parse(builtin), "builtin");
    }

    /// <summary>Finds the listed words in a text (F79, T99): every occurrence is reported with its position. A word of ASCII letters must match
    /// on word boundaries so "best" does not hit "bestseller"; other words (Chinese) match as-is, because they cannot run into a longer word.
    /// The result only ever becomes a notice — saving and generating go on.</summary>
    public static List<ClaimHit> Check(string text, List<ClaimWord> words)
    {
        List<ClaimHit> hits = [];
        text ??= "";
        foreach (ClaimWord word in words ?? [])
        {
            if (hits.Count >= MaxHits)
            {
                break;
            }
            bool ascii = word.Word.All(c => c < 128);
            if (ascii)
            {
                foreach (Match match in Regex.Matches(text, $"(?<![0-9A-Za-z]){Regex.Escape(word.Word)}(?![0-9A-Za-z])", RegexOptions.CultureInvariant))
                {
                    hits.Add(new ClaimHit(word.Word, match.Index, word.Category, word.Basis, word.Verified, word.Suggestion));
                    if (hits.Count >= MaxHits)
                    {
                        break;
                    }
                }
            }
            else
            {
                for (int start = text.IndexOf(word.Word, StringComparison.Ordinal); start >= 0; start = text.IndexOf(word.Word, start + word.Word.Length, StringComparison.Ordinal))
                {
                    hits.Add(new ClaimHit(word.Word, start, word.Category, word.Basis, word.Verified, word.Suggestion));
                    if (hits.Count >= MaxHits)
                    {
                        break;
                    }
                }
            }
        }
        return hits;
    }
}
