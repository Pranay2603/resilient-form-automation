using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using ResilientForm.Runner;
using Xunit;

namespace ResilientForm.Tests;

/// <summary>
/// Opens every page in /fixtures in a real browser. Each page is the same form with something changed:
/// order, ids/names/classes, structure, iframe, shadow DOM, hidden radios, decoy fields...
/// The pages mark the correct control with data-expect. The locator never reads that attribute; only these
/// assertions do.
/// </summary>
public sealed class MutationTests : IDisposable
{
    static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "fixtures");
    readonly IWebDriver _driver;

    public MutationTests()
    {
        var o = new ChromeOptions();
        var exe = Environment.GetEnvironmentVariable("FORM_BROWSER_PATH");   // optional: run the tests in Brave etc.
        if (!string.IsNullOrWhiteSpace(exe)) o.BinaryLocation = exe;
        o.AddArgument("--headless=new");
        o.AddArguments("--window-size=1100,900", "--no-sandbox", "--disable-dev-shm-usage");
        _driver = new ChromeDriver(o);
    }

    public static IEnumerable<object[]> Pages() =>
        Directory.GetFiles(FixtureDir, "*.html").OrderBy(f => f).Select(f => new object[] { Path.GetFileName(f) });

    [Theory]
    [MemberData(nameof(Pages))]
    public void Finds_and_fills_all_three_fields(string page)
    {
        _driver.Navigate().GoToUrl(new Uri(Path.Combine(FixtureDir, page)).AbsoluteUri);
        var tester = new FormTester(_driver, TimeSpan.FromSeconds(10));

        Check(tester.FillFirstName("Pranay"), "first-name");
        Check(tester.ChooseGender("Male"), "gender-male");
        Check(tester.ChooseState(null), "state");
    }

    void Check(FieldOutcome outcome, string expected)
    {
        Assert.True(outcome.Passed, outcome.Field + ": " + outcome.Error);
        // an element can only be queried from inside the frame it lives in
        new PageScanner(_driver).EnterFrame(outcome.FramePath);
        try { Assert.Equal(expected, outcome.LiveElement!.GetAttribute("data-expect")); }
        finally { _driver.SwitchTo().DefaultContent(); }
    }

    public void Dispose() => _driver.Quit();
}
