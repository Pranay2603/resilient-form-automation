using System.Reflection;
using System.Text.Json;
using OpenQA.Selenium;
using ResilientForm.Core;

namespace ResilientForm.Runner;

/// <summary>Reads every form control from the page, its iframes and its open shadow roots.</summary>
public sealed class PageScanner
{
    const string FrameCss = "iframe,frame";
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    static readonly string Script = LoadScript();

    readonly IWebDriver _driver;

    public PageScanner(IWebDriver driver) => _driver = driver;

    public List<FieldSnapshot> Scan(int maxFrameDepth = 3)
    {
        var all = new List<FieldSnapshot>();
        try
        {
            _driver.SwitchTo().DefaultContent();
            ScanCurrent(new List<int>(), 0, maxFrameDepth, all);
        }
        finally
        {
            try { _driver.SwitchTo().DefaultContent(); } catch (WebDriverException) { }
        }
        return all;
    }

    void ScanCurrent(List<int> path, int depth, int maxDepth, List<FieldSnapshot> acc)
    {
        var json = (string?)((IJavaScriptExecutor)_driver).ExecuteScript(Script);
        foreach (var f in JsonSerializer.Deserialize<List<FieldSnapshot>>(json ?? "[]", Json) ?? new())
        {
            f.FramePath = path.ToArray();
            acc.Add(f);
        }
        if (depth >= maxDepth) return;

        int frames = _driver.FindElements(By.CssSelector(FrameCss)).Count;
        for (int i = 0; i < frames; i++)
        {
            try
            {
                EnterFrame(path);
                var list = _driver.FindElements(By.CssSelector(FrameCss));
                if (i >= list.Count) break;
                _driver.SwitchTo().Frame(list[i]);
                path.Add(i);
                try { ScanCurrent(path, depth + 1, maxDepth, acc); }
                finally { path.RemoveAt(path.Count - 1); }
            }
            catch (WebDriverException)
            {
                // frame detached or not loaded yet; the next poll will see it
            }
        }
        EnterFrame(path);
    }

    /// <summary>Switches the driver into the frame described by <paramref name="path"/> (empty = main document).</summary>
    public void EnterFrame(IEnumerable<int> path)
    {
        _driver.SwitchTo().DefaultContent();
        foreach (var i in path)
            _driver.SwitchTo().Frame(_driver.FindElements(By.CssSelector(FrameCss))[i]);
    }

    /// <summary>Fetches the live element behind a snapshot (works for shadow DOM too).</summary>
    public IWebElement? Resolve(FieldSnapshot f)
    {
        EnterFrame(f.FramePath);
        return ((IJavaScriptExecutor)_driver).ExecuteScript("return (window.__rf || [])[arguments[0]] || null;", f.Index) as IWebElement;
    }

    static string LoadScript()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("snapshot.js")
                      ?? throw new InvalidOperationException("snapshot.js is not embedded in the assembly");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
