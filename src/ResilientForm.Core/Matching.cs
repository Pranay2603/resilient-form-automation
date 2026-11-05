using System.Text;

namespace ResilientForm.Core;

public sealed record Candidate(FieldSnapshot Field, double Score, string Reason);

public sealed class MatchResult
{
    public MatchResult(string what, IReadOnlyList<Candidate> ranked, double threshold)
    {
        What = what;
        Ranked = ranked;
        Threshold = threshold;
    }

    public string What { get; }
    public IReadOnlyList<Candidate> Ranked { get; }
    public double Threshold { get; }

    public Candidate? Best => Ranked.Count > 0 && Ranked[0].Score >= Threshold ? Ranked[0] : null;
    public double RunnerUpScore => Ranked.Count > 1 ? Ranked[1].Score : 0;
    /// <summary>True when a second field scored almost as well, so the pick is a judgement call.</summary>
    public bool Ambiguous => Best != null && Ranked.Count > 1 && Ranked[0].Score - Ranked[1].Score < 0.05 && Ranked[1].Score >= Threshold;

    public string Describe(int top = 3)
    {
        var sb = new StringBuilder();
        foreach (var c in Ranked.Take(top))
            sb.AppendLine($"    {c.Score:0.00}  {c.Field}  frame=[{string.Join(',', c.Field.FramePath)}]  via {c.Reason}");
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Scores every control on the page against what we are looking for. It reads what a person reads (labels,
/// text beside the control, placeholder, options) and treats id/name/class as weak hints, so changing those
/// does not break it.
/// </summary>
public static class FieldMatcher
{
    public const double DefaultThreshold = 0.5;

    static double Weight(string source) => source switch
    {
        "for" or "wrap" or "aria-labelledby" => 1.0,
        "aria-label" => 0.95,
        "near-right" => 0.95,
        "legend" or "group-aria" or "autocomplete" => 0.9,
        "near-left" => 0.85,
        "value" => 0.85,
        "near-above" => 0.75,
        "placeholder" => 0.7,
        "title" => 0.5,
        "id" or "name" => 0.45,
        "class" => 0.2,
        _ => 0
    };

    static IEnumerable<Evidence> Gather(FieldSnapshot f)
    {
        foreach (var l in f.Labels)
            if (l.Text.Length is > 0 and <= 120) yield return l;
        if (f.AriaLabel.Length > 0) yield return new Evidence { Source = "aria-label", Text = f.AriaLabel };
        if (f.Placeholder.Length > 0) yield return new Evidence { Source = "placeholder", Text = f.Placeholder };
        if (f.Title.Length > 0) yield return new Evidence { Source = "title", Text = f.Title };
        if (f.Autocomplete.Length > 0) yield return new Evidence { Source = "autocomplete", Text = f.Autocomplete };
        if (f.Id.Length > 0) yield return new Evidence { Source = "id", Text = f.Id };
        if (f.Name.Length > 0) yield return new Evidence { Source = "name", Text = f.Name };
        if (f.Cls.Length > 0) yield return new Evidence { Source = "class", Text = f.Cls };
    }

    static double Factor(Evidence e) =>
        e.Source.StartsWith("near-", StringComparison.Ordinal) ? 1.0 - Math.Min(e.Distance, 300) / 1000.0 : 1.0;

    static double Eligibility(FieldSnapshot f)
    {
        double m = 1;
        if (!f.EffVisible) m *= 0.25;
        if (f.Disabled || f.ReadOnly) m *= 0.3;
        return m;
    }

    static (double score, string reason) ScoreLabelled(FieldSnapshot f, FieldIntent intent)
    {
        double best = 0, negative = 0;
        string reason = "";
        var agreeing = new HashSet<string>();

        foreach (var ev in Gather(f))
        {
            var w = Weight(ev.Source);
            if (w == 0) continue;

            double m = ev.Source == "autocomplete"
                ? (intent.AutocompleteHints.Any(h => string.Equals(h, ev.Text, StringComparison.OrdinalIgnoreCase)) ? 1 : 0)
                : TextMatch.BestScore(ev.Text, intent.Phrases);

            var s = m * w * Factor(ev);
            if (s >= 0.5) agreeing.Add(ev.Source);
            if (s > best) { best = s; reason = $"{ev.Source} \"{Trim(ev.Text)}\""; }

            // A convincing label that says "Last Name" beats a class called "first-name-style".
            if (w >= 0.7 && ev.Source != "autocomplete")
                negative = Math.Max(negative, TextMatch.BestScore(ev.Text, intent.Negatives) * w);
        }

        if (agreeing.Count > 1) best = Math.Min(1.0, best + 0.05 * Math.Min(2, agreeing.Count - 1));
        if (negative >= 0.7) best -= 0.8;
        return (Math.Max(0, best), reason);
    }

    static string Trim(string s) => s.Length <= 40 ? s : s[..37] + "...";

    static List<Candidate> Order(IEnumerable<Candidate> items) =>
        items.OrderByDescending(c => Math.Round(c.Score, 3))
             .ThenBy(c => c.Field.FramePath.Length)
             .ThenBy(c => c.Field.Index)
             .ToList();

    /// <summary>A single line text box such as First Name.</summary>
    public static MatchResult FindTextBox(IEnumerable<FieldSnapshot> fields, FieldIntent intent, double threshold = DefaultThreshold)
    {
        var ranked = Order(fields
            .Where(f => f.Kind == "text")
            .Select(f =>
            {
                var (s, why) = ScoreLabelled(f, intent);
                return new Candidate(f, s * Eligibility(f), why);
            }));
        return new MatchResult(intent.Name, ranked, threshold);
    }

    /// <summary>A dropdown such as State.</summary>
    public static MatchResult FindDropdown(IEnumerable<FieldSnapshot> fields, FieldIntent intent, double threshold = DefaultThreshold)
    {
        var vocab = new HashSet<string>(intent.OptionVocabulary.Select(TextMatch.Normalize));
        var ranked = Order(fields
            .Where(f => f.Kind == "select" || (f.Kind == "combobox" && f.Options.Count > 0))
            .Select(f =>
            {
                var (s, why) = ScoreLabelled(f, intent);
                if (vocab.Count > 0)
                {
                    int hits = f.Options.Count(o => vocab.Contains(TextMatch.Normalize(o.Text)));
                    if (hits >= 3)
                    {
                        s = Math.Min(1.0, s + 0.2);
                        if (s < 0.5 && hits >= 5) { s = 0.55; why = $"options look like {intent.Name} names ({hits} matches)"; }
                    }
                }
                return new Candidate(f, s * Eligibility(f), why);
            }));
        return new MatchResult(intent.Name, ranked, threshold);
    }

    /// <summary>One option of a radio group, e.g. "Male" in Gender.</summary>
    public static MatchResult FindRadioOption(IEnumerable<FieldSnapshot> fields, RadioIntent intent, string option, double threshold = 0.6)
    {
        var radios = fields.Where(f => f.Kind == "radio").ToList();
        var wanted = Intents.OptionAliases(option);
        var vocab = intent.OptionVocabulary;

        var groups = radios.GroupBy(r => r.GroupKey).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Index).ToList());

        double GroupScore(List<FieldSnapshot> members)
        {
            double label = 0;
            foreach (var m in members)
                foreach (var ev in m.Labels)
                {
                    bool own = ev.Source is "legend" or "group-aria";
                    bool firstOnly = ev.Source is "near-left" or "near-above" && ReferenceEquals(m, members[0]);
                    if (!own && !firstOnly) continue;
                    label = Math.Max(label, TextMatch.BestScore(ev.Text, intent.GroupPhrases) * Weight(ev.Source) * Factor(ev));
                }

            int known = members.Count(m => OptionLabels(m).Any(ev => TextMatch.BestScore(ev.Text, vocab) >= 0.9));
            double vocabScore = known >= 2 ? 0.8 : known == 1 ? 0.4 : 0;
            double g = Math.Max(label, vocabScore);
            if (label >= 0.8 && vocabScore >= 0.5) g = Math.Min(1.0, g + 0.1);
            return g;
        }

        var groupScores = groups.ToDictionary(kv => kv.Key, kv => GroupScore(kv.Value));

        var ranked = Order(radios.Select(r =>
        {
            double opt = 0;
            string reason = "";
            foreach (var ev in OptionLabels(r))
            {
                var s = TextMatch.BestScore(ev.Text, wanted) * Weight(ev.Source) * Factor(ev);
                if (s > opt) { opt = s; reason = $"option {ev.Source} \"{Trim(ev.Text)}\""; }
            }
            var grp = groupScores[r.GroupKey];
            var total = 0.5 * opt + 0.5 * grp;
            if (opt < 0.7 || grp < 0.5) total *= 0.3;
            return new Candidate(r, total * Eligibility(r), $"{reason}; group score {grp:0.00}");
        }));
        return new MatchResult($"{intent.Name}: {option}", ranked, threshold);
    }

    static IEnumerable<Evidence> OptionLabels(FieldSnapshot r)
    {
        foreach (var l in r.Labels)
            if (l.Source is "for" or "wrap" or "aria-labelledby" or "near-right" && l.Text.Length is > 0 and <= 80) yield return l;
        if (r.AriaLabel.Length > 0) yield return new Evidence { Source = "aria-label", Text = r.AriaLabel };
        if (r.Value.Length > 0) yield return new Evidence { Source = "value", Text = r.Value };
    }
}

public static class OptionPicker
{
    public static bool IsPlaceholder(OptionInfo o)
    {
        if (string.IsNullOrWhiteSpace(o.Value)) return true;
        var t = o.Text.Trim().ToLowerInvariant();
        return TextMatch.Tokens(t).Count == 0
            || t.StartsWith("select", StringComparison.Ordinal) || t.StartsWith("choose", StringComparison.Ordinal)
            || t.StartsWith("pick", StringComparison.Ordinal) || t.StartsWith("--", StringComparison.Ordinal)
            || t.StartsWith("please", StringComparison.Ordinal);
    }

    /// <summary>Index of the option to choose. With no wanted text: the first real (non placeholder) option.</summary>
    public static int? Pick(IReadOnlyList<OptionInfo> options, string? wanted)
    {
        if (string.IsNullOrWhiteSpace(wanted))
        {
            for (int i = 0; i < options.Count; i++)
                if (!options[i].Disabled && !IsPlaceholder(options[i])) return i;
            return null;
        }

        for (int i = 0; i < options.Count; i++)
            if (!options[i].Disabled &&
                (string.Equals(options[i].Text.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(options[i].Value.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase)))
                return i;

        int best = -1; double bestScore = 0.8;
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].Disabled) continue;
            var s = Math.Max(TextMatch.Score(options[i].Text, wanted), TextMatch.Score(options[i].Value, wanted));
            if (s > bestScore) { bestScore = s; best = i; }
        }
        return best >= 0 ? best : null;
    }
}
