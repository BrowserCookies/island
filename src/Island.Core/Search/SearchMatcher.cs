using System;
using System.Globalization;
using System.Text;

namespace Island.Core.Search;

/// <summary>
/// Scores how well a query matches a name. 0 means no match. Higher tiers always beat lower ones;
/// within a tier, shorter names win (so "calc" ranks Calculator above longer names).
/// </summary>
public static class SearchMatcher
{
    private const int Exact = 1000, Prefix = 800, WordStart = 600, Initials = 500, Substring = 300, Subsequence = 100;

    public static int Score(string? query, string? text)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(text)) return 0;
        var q = Normalize(query.Trim());
        var t = Normalize(text);
        if (q.Length == 0 || q.Length > t.Length && !IsSubsequence(q, t)) return 0;

        int penalty = Math.Min(t.Length, 50);
        if (t == q) return Exact;
        if (t.StartsWith(q, StringComparison.Ordinal)) return Prefix - penalty;
        if (StartsAWord(t, q)) return WordStart - penalty;
        if (WordInitials(text).StartsWith(q, StringComparison.Ordinal) && q.Length >= 2) return Initials - penalty;
        if (t.Contains(q, StringComparison.Ordinal)) return Substring - penalty;
        if (IsWordAwareSubsequence(q, 0, t, -1)) return Subsequence - penalty;
        return 0;
    }

    /// <summary>
    /// In-order letters starting at a word start, staying within a word and jumping to another word only at its start
    /// ("ntpd" → Notepad, "gchr" → Google Chrome), so letters scattered across unrelated words
    /// ("calc" in "Epic Games Launcher") don't count.
    /// </summary>
    private static bool IsWordAwareSubsequence(string q, int qi, string t, int prev)
    {
        if (qi == q.Length) return true;
        for (int i = prev + 1; i < t.Length; i++)
        {
            if (t[i] != q[qi]) continue;
            bool wordStart = i == 0 || IsBoundary(t[i - 1]);
            bool sameWord = prev >= 0 && !t.AsSpan(prev + 1, i - prev - 1).ContainsAny(' ', '-', '_');
            if ((wordStart || qi > 0 && sameWord) && IsWordAwareSubsequence(q, qi + 1, t, i)) return true;
        }
        return false;
    }

    /// <summary>Lower-case, accents removed.</summary>
    public static string Normalize(string s)
    {
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    private static bool IsBoundary(char c) => c is ' ' or '-' or '_' or '.' or '(' or '[' or '/' or '\\';

    private static bool StartsAWord(string t, string q)
    {
        for (int i = t.IndexOf(q, StringComparison.Ordinal); i > 0; i = t.IndexOf(q, i + 1, StringComparison.Ordinal))
            if (IsBoundary(t[i - 1])) return true;
        return false;
    }

    /// <summary>"Visual Studio Code" → "vsc"; "OneNote" → "on" (camel case counts as a word start).</summary>
    private static string WordInitials(string original)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < original.Length; i++)
        {
            char c = original[i];
            if (!char.IsLetterOrDigit(c)) continue;
            bool start = i == 0 || IsBoundary(original[i - 1]) || char.IsUpper(c) && char.IsLower(original[i - 1]);
            if (start) sb.Append(c);
        }
        return Normalize(sb.ToString());
    }

    private static bool IsSubsequence(string q, string t)
    {
        int j = 0;
        foreach (var c in t)
            if (j < q.Length && c == q[j]) j++;
        return j == q.Length;
    }
}
