using System.Diagnostics;
using System.Text.Json.Serialization;
using OpenQA.Selenium;
using ResilientForm.Core;

namespace ResilientForm.Runner;

public sealed class FieldOutcome
{
    public string Field { get; set; } = "";
    public bool Found { get; set; }
    public bool Passed { get; set; }
    public double Score { get; set; }
    public double RunnerUp { get; set; }
    public bool Ambiguous { get; set; }
    public string FoundVia { get; set; } = "";
    public string Element { get; set; } = "";
    public string Frame { get; set; } = "main";
    /// <summary>Iframe indexes leading to the element (empty = main document).</summary>
    [JsonIgnore] public int[] FramePath { get; set; } = Array.Empty<int>();
    /// <summary>Other fields that matched about as well (the pick was a judgement call).</summary>
    public List<string> Alternatives { get; set; } = new();
    public string Action { get; set; } = "";
    public string? Error { get; set; }
    public long Milliseconds { get; set; }
    /// <summary>The live element, kept so tests can assert on it. Not written to the report.</summary>
    [JsonIgnore] public IWebElement? LiveElement { get; set; }
}

/// <summary>Finds each field by what it means, then fills it and checks the page really changed.</summary>
public sealed class FormTester
{
    readonly IWebDriver _driver;
    readonly PageScanner _scanner;
    readonly TimeSpan _timeout;

    public FormTester(IWebDriver driver, TimeSpan timeout)
    {
        _driver = driver;
        _scanner = new PageScanner(driver);
        _timeout = timeout;
    }

    public FieldOutcome FillFirstName(string value) =>
        Run("First Name", fields => FieldMatcher.FindTextBox(fields, Intents.FirstName), (el, f) => TypeText(el, value));

    public FieldOutcome ChooseGender(string option) =>
        Run($"Gender ({option})", fields => FieldMatcher.FindRadioOption(fields, Intents.Gender, option), (el, f) => SelectRadio(el));

    public FieldOutcome ChooseState(string? wanted) =>
        Run("State", fields => FieldMatcher.FindDropdown(fields, Intents.State), (el, f) => PickOption(el, f, wanted));

    FieldOutcome Run(string label, Func<List<FieldSnapshot>, MatchResult> find, Func<IWebElement, FieldSnapshot, string> act)
    {
        var sw = Stopwatch.StartNew();
        var outcome = new FieldOutcome { Field = label };
        try
        {
            MatchResult? last = null;
            List<FieldSnapshot> fields = new();
            var deadline = DateTime.UtcNow + _timeout;
            bool settled = false;

            // Poll: the page may still be rendering, so keep looking until the field shows up.
            while (true)
            {
                fields = _scanner.Scan();
                last = find(fields);
                if (last.Best != null)
                {
                    if (settled) break;
                    settled = true;               // look once more, a late render can change the winner
                    Thread.Sleep(300);
                    continue;
                }
                if (DateTime.UtcNow > deadline) break;
                Thread.Sleep(400);
            }

            var best = last?.Best;
            if (best == null)
            {
                outcome.Error = $"No field matched \"{label}\" within {_timeout.TotalSeconds:0}s. Closest candidates:\n{last?.Describe()}";
                return outcome;
            }

            outcome.Found = true;
            outcome.Score = Math.Round(best.Score, 2);
            outcome.RunnerUp = Math.Round(last!.RunnerUpScore, 2);
            outcome.Ambiguous = last.Ambiguous;
            outcome.FoundVia = best.Reason;
            outcome.Element = best.Field.ToString();
            outcome.FramePath = best.Field.FramePath;
            outcome.Alternatives = last.Ranked.Skip(1).Take(3).Where(c => c.Score >= last.Threshold)
                .Select(c => $"{c.Field} in {(c.Field.FramePath.Length == 0 ? "main" : "iframe " + string.Join(" > ", c.Field.FramePath))}, score {c.Score:0.00}, via {c.Reason}")
                .ToList();
            outcome.Frame = best.Field.FramePath.Length == 0 ? "main" : "iframe " + string.Join(" > ", best.Field.FramePath);

            // The element reference can go stale if the page re-renders between scan and action: retry once.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    var el = _scanner.Resolve(best.Field) ?? throw new NoSuchElementException("element disappeared after scan");
                    outcome.LiveElement = el;
                    outcome.Action = act(el, best.Field);
                    outcome.Passed = true;
                    break;
                }
                catch (StaleElementReferenceException) when (attempt == 0)
                {
                    Thread.Sleep(300);
                    var again = find(_scanner.Scan()).Best ?? throw new NoSuchElementException("field vanished while retrying");
                    best = again;
                }
            }
        }
        catch (Exception ex)
        {
            outcome.Error = ex.Message;
        }
        finally
        {
            outcome.Milliseconds = sw.ElapsedMilliseconds;
            try { _driver.SwitchTo().DefaultContent(); } catch (WebDriverException) { }
        }
        return outcome;
    }

    IJavaScriptExecutor Js => (IJavaScriptExecutor)_driver;

    void ScrollTo(IWebElement el) =>
        Js.ExecuteScript("arguments[0].scrollIntoView({block:'center',inline:'nearest'});", el);

    string TypeText(IWebElement el, string value)
    {
        ScrollTo(el);
        try
        {
            el.Clear();
            el.SendKeys(value);
        }
        catch (WebDriverException)
        {
            // Not interactable by keyboard: set the value the way a user would and fire the events frameworks listen to.
            Js.ExecuteScript(
                "var e=arguments[0];var s=Object.getOwnPropertyDescriptor(Object.getPrototypeOf(e),'value');" +
                "if(s&&s.set){s.set.call(e,arguments[1]);}else{e.value=arguments[1];}" +
                "e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));", el, value);
        }

        var actual = el.GetAttribute("value");
        if (actual != value) throw new InvalidOperationException($"Typed \"{value}\" but the field holds \"{actual}\".");
        return $"typed \"{value}\" and read it back";
    }

    string SelectRadio(IWebElement el)
    {
        ScrollTo(el);
        string how = "clicked the radio button";
        try { el.Click(); }
        catch (Exception ex) when (ex is ElementNotInteractableException or ElementClickInterceptedException)
        {
            // styled radios hide the real input; people click its label, so do the same
            Js.ExecuteScript("var e=arguments[0];var l=e.labels&&e.labels[0];(l||e).click();", el);
            how = "clicked its label (the input itself is hidden)";
        }

        if (!IsChecked(el))
        {
            Js.ExecuteScript("arguments[0].click();", el);
            how = "clicked via script";
        }
        if (!IsChecked(el)) throw new InvalidOperationException("Clicked the radio button but it is not selected.");
        return how + ", confirmed it is selected";
    }

    bool IsChecked(IWebElement el) =>
        (bool)Js.ExecuteScript("var e=arguments[0];return e.checked===true||e.getAttribute('aria-checked')==='true';", el)!;

    string PickOption(IWebElement el, FieldSnapshot f, string? wanted)
    {
        var idx = OptionPicker.Pick(f.Options, wanted)
                  ?? throw new InvalidOperationException(string.IsNullOrWhiteSpace(wanted)
                      ? "The dropdown has no selectable option."
                      : $"The dropdown has no option matching \"{wanted}\".");
        ScrollTo(el);
        try
        {
            el.FindElements(By.TagName("option"))[idx].Click();
        }
        catch (WebDriverException)
        {
            Js.ExecuteScript(
                "var e=arguments[0];e.selectedIndex=arguments[1];" +
                "e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));", el, idx);
        }

        var selected = Convert.ToInt32(Js.ExecuteScript("return arguments[0].selectedIndex;", el));
        if (selected != idx) throw new InvalidOperationException($"Tried to choose option {idx} but option {selected} is selected.");
        return $"selected \"{f.Options[idx].Text}\" and confirmed it";
    }
}
