using System.Text;
using System.Text.RegularExpressions;

namespace ResilientForm.Core;

/// <summary>
/// Forgiving text comparison. "txtFirstName", "first_name", "First Name *" and "Enter your first name"
/// all normalise to the same tokens, so the matcher does not care how a developer spelled the identifier.
/// </summary>
public static class TextMatch
{
    // Words that carry no meaning for identifying a field.
    static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "a","an","the","your","enter","please","required","optional","field","input","text","box",
        "txt","tbx","lbl","label","ddl","drp","sel","select","choose","pick","here","of","mandatory"
    };

    static readonly Regex CamelBoundary = new(@"(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);
    static readonly Regex NonAlnum = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    public static List<string> Tokens(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        var spaced = CamelBoundary.Replace(raw, " ");
        spaced = NonAlnum.Replace(spaced.ToLowerInvariant(), " ");
        return spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !Noise.Contains(t)).ToList();
    }

    public static string Normalize(string? raw) => string.Join(' ', Tokens(raw));

    /// <summary>0..1 similarity between a piece of page text and a phrase we are looking for.</summary>
    public static double Score(string? text, string phrase)
    {
        var a = Tokens(text);
        var b = Tokens(phrase);
        if (a.Count == 0 || b.Count == 0) return 0;

        if (a.SequenceEqual(b)) return 1.0;

        if (ContainsRun(a, b))
            return Math.Max(0.55, 0.9 - 0.1 * (a.Count - b.Count));

        var ca = string.Concat(a);
        var cb = string.Concat(b);

        // "firstname" inside "userfirstname". Short phrases are excluded on purpose: "male" is inside "female".
        if (cb.Length >= 5 && ca.Contains(cb, StringComparison.Ordinal))
            return Math.Max(0.5, 0.8 - 0.01 * (ca.Length - cb.Length));

        // typo tolerance ("frist name")
        if (cb.Length >= 5)
        {
            var ratio = 1.0 - (double)Levenshtein(ca, cb) / Math.Max(ca.Length, cb.Length);
            if (ratio >= 0.8) return 0.7 * ratio;
        }

        if (b.All(a.Contains)) return Math.Max(0.4, 0.7 - 0.1 * (a.Count - b.Count));
        return 0;
    }

    public static double BestScore(string? text, IEnumerable<string> phrases)
    {
        double best = 0;
        foreach (var p in phrases) best = Math.Max(best, Score(text, p));
        return best;
    }

    static bool ContainsRun(List<string> haystack, List<string> needle)
    {
        for (int i = 0; i + needle.Count <= haystack.Count; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Count && ok; j++) ok = haystack[i + j] == needle[j];
            if (ok) return true;
        }
        return false;
    }

    public static int Levenshtein(string s, string t)
    {
        if (s.Length == 0) return t.Length;
        if (t.Length == 0) return s.Length;
        var prev = new int[t.Length + 1];
        var cur = new int[t.Length + 1];
        for (int j = 0; j <= t.Length; j++) prev[j] = j;
        for (int i = 1; i <= s.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= t.Length; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[t.Length];
    }
}
