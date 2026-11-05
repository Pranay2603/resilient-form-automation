namespace ResilientForm.Core;

/// <summary>What a text box or dropdown is called, in the many ways people name it.</summary>
public sealed class FieldIntent
{
    public FieldIntent(string name, string[] phrases, string[] negatives, string[]? autocomplete = null, string[]? optionVocabulary = null)
    {
        Name = name;
        Phrases = phrases;
        Negatives = negatives;
        AutocompleteHints = autocomplete ?? Array.Empty<string>();
        OptionVocabulary = optionVocabulary ?? Array.Empty<string>();
    }

    public string Name { get; }
    /// <summary>Phrases that mean "this is the field".</summary>
    public string[] Phrases { get; }
    /// <summary>Phrases that mean "this is a different, similar looking field".</summary>
    public string[] Negatives { get; }
    /// <summary>Standard HTML autocomplete tokens for this field.</summary>
    public string[] AutocompleteHints { get; }
    /// <summary>For dropdowns: values we expect to find among the options (used when the label is unhelpful).</summary>
    public string[] OptionVocabulary { get; }
}

/// <summary>A radio group, e.g. Gender, identified by its own label or by the options it offers.</summary>
public sealed class RadioIntent
{
    public RadioIntent(string name, string[] groupPhrases, string[] optionVocabulary)
    {
        Name = name;
        GroupPhrases = groupPhrases;
        OptionVocabulary = optionVocabulary;
    }

    public string Name { get; }
    public string[] GroupPhrases { get; }
    public string[] OptionVocabulary { get; }
}

public static class Intents
{
    public static readonly FieldIntent FirstName = new(
        "First Name",
        phrases: new[] { "first name", "given name", "forename", "fname", "first" },
        negatives: new[] { "last name", "surname", "family name", "middle name", "nickname", "full name", "user name" },
        autocomplete: new[] { "given-name" });

    public static readonly FieldIntent State = new(
        "State",
        phrases: new[] { "state", "province", "region", "state province", "state or province", "county" },
        negatives: new[] { "country", "city", "zip", "zip code", "postal", "pin code", "nation" },
        autocomplete: new[] { "address-level1" },
        optionVocabulary: StateNames.All);

    public static readonly RadioIntent Gender = new(
        "Gender",
        groupPhrases: new[] { "gender", "sex" },
        optionVocabulary: new[] { "male", "female", "other", "transgender", "man", "woman", "non binary", "nonbinary", "prefer not to say", "m", "f" });

    /// <summary>Different wordings for the radio option the caller asks for.</summary>
    public static string[] OptionAliases(string option)
    {
        var key = TextMatch.Normalize(option);
        return key switch
        {
            "male" or "man" or "m" => new[] { "male", "man", "m" },
            "female" or "woman" or "f" => new[] { "female", "woman", "f" },
            "other" or "transgender" or "non binary" or "nonbinary" => new[] { "other", "transgender", "non binary", "nonbinary", "prefer not to say" },
            _ => new[] { option }
        };
    }
}

static class StateNames
{
    public static readonly string[] All =
    {
        "alabama","alaska","arizona","arkansas","california","colorado","connecticut","delaware","florida","georgia",
        "hawaii","idaho","illinois","indiana","iowa","kansas","kentucky","louisiana","maine","maryland","massachusetts",
        "michigan","minnesota","mississippi","missouri","montana","nebraska","nevada","new hampshire","new jersey",
        "new mexico","new york","north carolina","north dakota","ohio","oklahoma","oregon","pennsylvania","rhode island",
        "south carolina","south dakota","tennessee","texas","utah","vermont","virginia","washington","west virginia",
        "wisconsin","wyoming","andhra pradesh","assam","bihar","chhattisgarh","goa","gujarat","haryana","himachal pradesh",
        "jharkhand","karnataka","kerala","madhya pradesh","maharashtra","manipur","meghalaya","mizoram","nagaland","odisha",
        "punjab","rajasthan","sikkim","tamil nadu","telangana","tripura","uttar pradesh","uttarakhand","west bengal","delhi"
    };
}
