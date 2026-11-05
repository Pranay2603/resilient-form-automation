using ResilientForm.Core;
using Xunit;

namespace ResilientForm.Tests;

/// <summary>Fast tests of the matching rules. No browser needed.</summary>
public class MatcherTests
{
    static FieldSnapshot Text(int index, string label, string id = "", string name = "") => new()
    {
        Index = index, Tag = "input", Kind = "text", Id = id, Name = name, Visible = true, EffVisible = true,
        Labels = { new Evidence { Source = "for", Text = label } }
    };

    static FieldSnapshot Radio(int index, int group, string option, string value = "") => new()
    {
        Index = index, Tag = "input", Kind = "radio", GroupId = group, Value = value, Visible = true, EffVisible = true,
        Labels = { new Evidence { Source = "for", Text = option } }
    };

    [Theory]
    [InlineData("txtFirstName", "first name")]
    [InlineData("first_name", "first name")]
    [InlineData("Enter your first name *", "first name")]
    [InlineData("FIRST NAME:", "first name")]
    public void Normalize_ignores_how_a_name_is_written(string raw, string expected) =>
        Assert.Equal(expected, TextMatch.Normalize(raw));

    [Fact]
    public void Female_is_never_mistaken_for_male() =>
        Assert.Equal(0, TextMatch.Score("Female", "male"));

    [Fact]
    public void Label_beats_a_misleading_attribute()
    {
        var real = Text(0, "First Name", id: "lastName", name: "surname");
        var other = Text(1, "Last Name", id: "firstName", name: "fname");
        var result = FieldMatcher.FindTextBox(new[] { other, real }, Intents.FirstName);
        Assert.Equal(0, result.Best?.Field.Index);
    }

    [Fact]
    public void Hidden_duplicate_loses_to_the_visible_field()
    {
        var hidden = Text(0, "First Name");
        hidden.Visible = hidden.EffVisible = false;
        var shown = Text(1, "First Name");
        Assert.Equal(1, FieldMatcher.FindTextBox(new[] { hidden, shown }, Intents.FirstName).Best?.Field.Index);
    }

    [Fact]
    public void Gender_option_is_chosen_from_the_gender_group_only()
    {
        var pet = new[] { Radio(0, 1, "Male"), Radio(1, 1, "Female") };
        pet[0].Labels.Add(new Evidence { Source = "near-left", Text = "Pet type", Distance = 10 });
        pet[1].Labels.Add(new Evidence { Source = "near-left", Text = "Male", Distance = 5 });
        var people = new[] { Radio(2, 2, "Female"), Radio(3, 2, "Male") };
        people[0].Labels.Add(new Evidence { Source = "near-left", Text = "Gender", Distance = 10 });
        people[1].Labels.Add(new Evidence { Source = "near-left", Text = "Female", Distance = 5 });

        var best = FieldMatcher.FindRadioOption(pet.Concat(people), Intents.Gender, "Male").Best;
        Assert.Equal(3, best?.Field.Index);
    }

    [Fact]
    public void Dropdown_with_no_helpful_label_is_found_by_its_options()
    {
        var d = new FieldSnapshot
        {
            Index = 0, Tag = "select", Kind = "select", Visible = true, EffVisible = true,
            Options = new[] { "Choose", "Alabama", "Texas", "Ohio", "Utah", "Maine" }
                .Select((t, i) => new OptionInfo { Text = t, Value = i == 0 ? "" : t }).ToList()
        };
        Assert.NotNull(FieldMatcher.FindDropdown(new[] { d }, Intents.State).Best);
    }

    [Fact]
    public void Option_picker_skips_placeholders()
    {
        var o = new[]
        {
            new OptionInfo { Text = "-- Select State --", Value = "" },
            new OptionInfo { Text = "Texas", Value = "TX" }
        };
        Assert.Equal(1, OptionPicker.Pick(o, null));
        Assert.Equal(1, OptionPicker.Pick(o, "texas"));
        Assert.Equal(1, OptionPicker.Pick(o, "TX"));
        Assert.Null(OptionPicker.Pick(o, "Atlantis"));
    }
}
