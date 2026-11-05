using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using ResilientForm.Runner;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        resilient-form: fills First Name, Gender and State on a web form, finding each field by meaning.

          --url <url|file>       page to test (default: the CloudQA practice form). Env: FORM_URL
          --first-name <text>    value to type            (default Pranay)
          --gender <option>      Male | Female | Other    (default Male)
          --state <text>         option to choose         (default: first real option)
          --browser <name>       chrome | edge | firefox   (default chrome)
          --browser-path <exe>   use a specific Chromium browser, e.g. Brave (default: Selenium finds/downloads Chrome)
          --headless <bool>      false to watch it run     (default true)
          --timeout <seconds>    how long to wait for a field to appear (default 20)
          --report <file>        JSON report path          (default report.json)
          --screenshot <file>    save a screenshot at the end
        """);
    return 0;
}

var opt = RunOptions.Load(args);
Console.WriteLine($"Target: {opt.Url}");

IWebDriver driver = CreateDriver(opt);
try
{
    driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Math.Max(30, opt.TimeoutSeconds));
    driver.Navigate().GoToUrl(opt.Url);

    var tester = new FormTester(driver, TimeSpan.FromSeconds(opt.TimeoutSeconds));
    var results = new[]
    {
        tester.FillFirstName(opt.FirstName),
        tester.ChooseGender(opt.Gender),
        tester.ChooseState(string.IsNullOrWhiteSpace(opt.State) ? null : opt.State)
    };

    foreach (var r in results)
    {
        Console.WriteLine($"[{(r.Passed ? "PASS" : "FAIL")}] {r.Field,-16} {r.Milliseconds,5} ms");
        if (r.Found)
        {
            Console.WriteLine($"       found    {r.Element} in {r.Frame}, via {r.FoundVia}");
            Console.WriteLine($"       score    {r.Score:0.00} (next best {r.RunnerUp:0.00}){(r.Ambiguous ? "  WARNING: close call" : "")}");
            foreach (var alt in r.Alternatives)
                Console.WriteLine($"       also     {alt}");
            if (r.Alternatives.Count > 0)
                Console.WriteLine("       chose    the first match in the main document (iframes and later copies rank lower on a tie)");
        }
        if (r.Passed) Console.WriteLine($"       did      {r.Action}");
        if (r.Error != null) Console.WriteLine($"       error    {r.Error.Replace("\n", "\n                ")}");
    }

    if (!string.IsNullOrWhiteSpace(opt.ScreenshotPath))
    {
        ((ITakesScreenshot)driver).GetScreenshot().SaveAsFile(opt.ScreenshotPath);
        Console.WriteLine($"Screenshot: {opt.ScreenshotPath}");
    }

    File.WriteAllText(opt.ReportPath, JsonSerializer.Serialize(
        new { url = opt.Url, ranAt = DateTime.UtcNow, passed = results.All(r => r.Passed), results },
        new JsonSerializerOptions { WriteIndented = true }));

    var ok = results.All(r => r.Passed);
    Console.WriteLine(ok ? "\nAll 3 fields passed." : "\nSome fields failed.");
    return ok ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Run aborted: " + ex.Message);
    return 2;
}
finally
{
    driver.Quit();
}

static IWebDriver CreateDriver(RunOptions o)
{
    switch (o.Browser)
    {
        case "edge":
        {
            var e = new EdgeOptions();
            if (!string.IsNullOrWhiteSpace(o.BrowserPath)) e.BinaryLocation = o.BrowserPath;
            if (o.Headless) e.AddArgument("--headless=new");
            e.AddArgument("--window-size=1280,1000");
            return new EdgeDriver(e);
        }
        case "firefox":
        {
            var f = new FirefoxOptions();
            if (o.Headless) f.AddArgument("-headless");
            return new FirefoxDriver(f);
        }
        default:
        {
            var c = new ChromeOptions();
            if (!string.IsNullOrWhiteSpace(o.BrowserPath)) c.BinaryLocation = o.BrowserPath;
            if (o.Headless) c.AddArgument("--headless=new");
            c.AddArguments("--window-size=1280,1000", "--no-sandbox", "--disable-dev-shm-usage");
            return new ChromeDriver(c);
        }
    }
}
