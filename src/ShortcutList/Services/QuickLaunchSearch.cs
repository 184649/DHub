using System.Globalization;
using System.Text;
using ShortcutList.Models;

namespace ShortcutList.Services;

public sealed record QuickLaunchResults(IReadOnlyList<QuickLaunchEntry> Items, int TotalCount);

// Pure, bounded-result search over the caller's transient list. No target lookup,
// shell execution, de-duplication, or mutation of registrations occurs here.
public static class QuickLaunchSearch
{
    private sealed record Match(QuickLaunchEntry Entry, string Name, int Rank, int Index);
    private static readonly IComparer<Match> BestFirst = Comparer<Match>.Create(Compare);
    private static readonly IComparer<Match> WorstFirst = Comparer<Match>.Create((a, b) => Compare(b, a));

    public static QuickLaunchResults Query(IReadOnlyList<QuickLaunchEntry> source, string query, int limit = 100)
    {
        ArgumentNullException.ThrowIfNull(source);
        var normalizedQuery = Normalize(query);
        var words = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var capacity = Math.Min(Math.Max(0, limit), source.Count);
        var best = new PriorityQueue<Match, Match>(capacity, WorstFirst);
        var total = 0;
        for (var index = 0; index < source.Count; index++)
        {
            var entry = source[index];
            var name = Normalize(entry.Name);
            var rank = 0;
            if (words.Length > 0)
            {
                var searchable = name + " " + Normalize(entry.TargetPath) + " " + Normalize(entry.Detail) +
                    " " + Normalize(entry.SearchText);
                if (!words.All(word => searchable.Contains(word, StringComparison.Ordinal))) continue;
                rank = name == normalizedQuery ? 0 :
                    name.StartsWith(normalizedQuery, StringComparison.Ordinal) ? 1 :
                    words.All(word => name.Contains(word, StringComparison.Ordinal)) ? 2 : 3;
            }
            total++;
            if (capacity == 0) continue;
            var match = new Match(entry, name, rank, index);
            if (best.Count < capacity) best.Enqueue(match, match);
            else if (BestFirst.Compare(match, best.Peek()) < 0) best.DequeueEnqueue(match, match);
        }
        var matches = best.UnorderedItems.Select(item => item.Element).ToArray();
        Array.Sort(matches, BestFirst);
        return new(Array.AsReadOnly(matches.Select(match => match.Entry).ToArray()), total);
    }

    private static int Compare(Match a, Match b)
    {
        var comparison = a.Rank.CompareTo(b.Rank);
        if (comparison != 0) return comparison;
        comparison = b.Entry.IsFavorite.CompareTo(a.Entry.IsFavorite);
        if (comparison != 0) return comparison;
        comparison = a.Entry.SortOrder.CompareTo(b.Entry.SortOrder);
        if (comparison != 0) return comparison;
        comparison = StringComparer.Ordinal.Compare(a.Name, b.Name);
        if (comparison != 0) return comparison;
        comparison = StringComparer.Ordinal.Compare(a.Entry.Name, b.Entry.Name);
        if (comparison != 0) return comparison;
        comparison = StringComparer.Ordinal.Compare(a.Entry.Key, b.Entry.Key);
        return comparison != 0 ? comparison : a.Index.CompareTo(b.Index);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        string decomposed;
        try { decomposed = value.Normalize(NormalizationForm.FormKD); }
        catch (ArgumentException)
        {
            // Malformed imported UTF-16 is searchable without throwing. Rune
            // enumeration replaces invalid surrogate sequences with U+FFFD.
            decomposed = string.Concat(value.EnumerateRunes().Select(rune => rune.ToString()))
                .Normalize(NormalizationForm.FormKD);
        }
        var result = new StringBuilder(decomposed.Length);
        var latinBase = false;
        var pendingSpace = false;
        foreach (var rune in decomposed.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = result.Length > 0;
                latinBase = false;
                continue;
            }
            var category = Rune.GetUnicodeCategory(rune);
            var combining = category is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
            // Accent-insensitive Latin search must not erase Japanese dakuten,
            // emoji variation selectors, or other scripts' meaningful marks.
            if (latinBase && category == UnicodeCategory.NonSpacingMark) continue;
            if (pendingSpace) { result.Append(' '); pendingSpace = false; }
            result.Append(Rune.ToUpperInvariant(rune));
            if (!combining)
                latinBase = Rune.IsLetter(rune) && (rune.Value is >= 0x41 and <= 0x7A or
                    >= 0xC0 and <= 0x24F or >= 0x1E00 and <= 0x1EFF);
        }
        return result.ToString().Normalize(NormalizationForm.FormC);
    }
}
