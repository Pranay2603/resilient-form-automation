namespace ResilientForm.Core;

/// <summary>One piece of text that may describe a control (a label, a placeholder, text sitting next to it...).</summary>
public sealed class Evidence
{
    /// <summary>for | wrap | aria-labelledby | aria-label | legend | group-aria | placeholder | title |
    /// near-left | near-above | near-right</summary>
    public string Source { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>Pixel distance to the control for the near-* sources.</summary>
    public double Distance { get; set; }
}

public sealed class OptionInfo
{
    public string Text { get; set; } = "";
    public string Value { get; set; } = "";
    public bool Disabled { get; set; }
}

public sealed class Rect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
}

/// <summary>A plain-data description of one form control, produced by Scripts/snapshot.js.</summary>
public sealed class FieldSnapshot
{
    public int Index { get; set; }
    /// <summary>Position of the owning iframe(s), outermost first. Empty = main document.</summary>
    public int[] FramePath { get; set; } = Array.Empty<int>();
    public string Tag { get; set; } = "";
    /// <summary>text | textarea | radio | checkbox | select | combobox | input-{type} | other</summary>
    public string Kind { get; set; } = "other";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Cls { get; set; } = "";
    public string Placeholder { get; set; } = "";
    public string AriaLabel { get; set; } = "";
    public string Title { get; set; } = "";
    public string Autocomplete { get; set; } = "";
    public string Value { get; set; } = "";
    public bool Visible { get; set; }
    /// <summary>Visible, or a hidden radio/checkbox whose label is visible (custom styled controls).</summary>
    public bool EffVisible { get; set; }
    public bool Disabled { get; set; }
    public bool ReadOnly { get; set; }
    public bool Checked { get; set; }
    public int GroupId { get; set; } = -1;
    public Rect Rect { get; set; } = new();
    public List<Evidence> Labels { get; set; } = new();
    public List<OptionInfo> Options { get; set; } = new();

    public string GroupKey => string.Join('.', FramePath) + "#" + GroupId;

    public override string ToString() =>
        $"<{Tag}{(Kind is "radio" or "checkbox" ? " type=" + Kind : "")}" +
        (Id.Length > 0 ? $" id=\"{Id}\"" : "") + (Name.Length > 0 ? $" name=\"{Name}\"" : "") + ">";
}
