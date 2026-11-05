using System.Text.Json;

namespace ResilientForm.Runner;

/// <summary>
/// Settings. Highest priority first: command line, environment variables (FORM_URL, FORM_FIRST_NAME, ...),
/// appsettings.json next to the executable, built-in defaults.
/// </summary>
public sealed class RunOptions
{
    public string Url { get; set; } = "https://app.cloudqa.io/home/AutomationPracticeForm";
    public string FirstName { get; set; } = "Pranay";
    public string Gender { get; set; } = "Male";
    /// <summary>Empty = pick the first real option of the dropdown.</summary>
    public string State { get; set; } = "";
    public string Browser { get; set; } = "chrome";
    /// <summary>Optional path to a Chromium based browser (Brave, Chrome, Edge). Empty = let Selenium find or download one.</summary>
    public string BrowserPath { get; set; } = "";
    public bool Headless { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 20;
    public string ReportPath { get; set; } = "report.json";
    public string? ScreenshotPath { get; set; }

    public static RunOptions Load(string[] args)
    {
        var o = new RunOptions();

        var file = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(file))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var p in doc.RootElement.EnumerateObject()) Apply(o, p.Name, p.Value.ToString());
        }

        foreach (var (key, env) in new[]
        {
            ("url","FORM_URL"),("first-name","FORM_FIRST_NAME"),("gender","FORM_GENDER"),("state","FORM_STATE"),
            ("browser","FORM_BROWSER"),("browser-path","FORM_BROWSER_PATH"),("headless","FORM_HEADLESS"),("timeout","FORM_TIMEOUT"),("report","FORM_REPORT")
        })
        {
            var v = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrWhiteSpace(v)) Apply(o, key, v);
        }

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal)) continue;
            var key = a[2..];
            string? val;
            int eq = key.IndexOf('=');
            if (eq >= 0) { val = key[(eq + 1)..]; key = key[..eq]; }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) val = args[++i];
            else val = "true";
            Apply(o, key, val);
        }

        o.Url = NormaliseUrl(o.Url);
        return o;
    }

    static void Apply(RunOptions o, string key, string value)
    {
        switch (key.Replace("_", "").Replace("-", "").ToLowerInvariant())
        {
            case "url": o.Url = value; break;
            case "firstname": o.FirstName = value; break;
            case "gender": o.Gender = value; break;
            case "state": o.State = value; break;
            case "browser": o.Browser = value.ToLowerInvariant(); break;
            case "browserpath": o.BrowserPath = value; break;
            case "headless": o.Headless = !value.Equals("false", StringComparison.OrdinalIgnoreCase) && value != "0"; break;
            case "timeout" or "timeoutseconds": if (int.TryParse(value, out var t) && t > 0) o.TimeoutSeconds = t; break;
            case "report": o.ReportPath = value; break;
            case "screenshot": o.ScreenshotPath = value; break;
        }
    }

    /// <summary>Accepts a URL, a bare host, or a path to a local .html file.</summary>
    static string NormaliseUrl(string url)
    {
        if (File.Exists(url)) return new Uri(Path.GetFullPath(url)).AbsoluteUri;
        if (!url.Contains("://", StringComparison.Ordinal)) return "https://" + url;
        return url;
    }
}
