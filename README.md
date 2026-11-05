# resilient-form-automation

A C# + Selenium program that fills three fields (**First Name**, **Gender**, **State**) on a web form and
keeps working when the page changes underneath it.

Built for the CloudQA practice form: <https://app.cloudqa.io/home/AutomationPracticeForm>

## The idea

A normal test says *"find `#fname`"*. When a developer renames the id, the test breaks, although nothing the user
sees has changed.

This program looks for fields the way a person does. It asks **"which box is labelled First Name?"** and treats
`id`, `name` and `class` as weak hints that can be wrong. A label is much more stable than an attribute.

```
page ──► snapshot.js ──► [every control + all text that describes it] ──► scorer ──► best match ──► act ──► verify
         (in browser)       labels, text left/above/right, aria,          (C#, explainable)         click/type   read the value back
                            placeholder, options, frame, shadow root
```

## Run it

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download) and Chrome. Selenium Manager downloads the matching
driver automatically.

```bash
git clone https://github.com/Pranay2603/resilient-form-automation.git && cd resilient-form-automation

# against the CloudQA form (default)
dotnet run --project src/ResilientForm.Runner

# against any other version of the page: a URL or a local file
dotnet run --project src/ResilientForm.Runner -- --url https://example.com/modified-form.html
dotnet run --project src/ResilientForm.Runner -- --url fixtures/03-obfuscated-attributes.html --headless false
```

The page URL is configurable in three ways. Highest priority first:

| Source | Example |
|---|---|
| Command line | `--url <url or file>` |
| Environment variable | `FORM_URL=https://...` |
| `appsettings.json` | `"url": "https://..."` |

Other options: `--browser-path` (e.g. Brave: `"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe"`), `--first-name`, `--gender Male|Female|Other`, `--state "Texas"` (default: first real option),
`--browser chrome|edge|firefox`, `--headless false` to watch it work, `--timeout 20`, `--report report.json`,
`--screenshot out.png`. Exit code is `0` when all three fields pass, `1` otherwise.

Example output (shape of the real thing):

```
Target: https://app.cloudqa.io/home/AutomationPracticeForm
[PASS] First Name         412 ms
       found    <input id="fname"> in main, via for "First Name"
       score    1.00 (next best 0.00)
       did      typed "Pranay" and read it back
[PASS] Gender (Male)      188 ms
       found    <input type=radio name="gender"> in main, via option for "Male"; group score 0.95
       score    0.97 (next best 0.14)
       did      clicked the radio button, confirmed it is selected
```

Every field is **verified after acting**: the text is read back, the radio is checked for `checked`, the dropdown for
its `selectedIndex`. A run that clicked the wrong thing fails instead of passing quietly.

## How a field is found

1. **Snapshot** (`Scripts/snapshot.js`). One script run in the page lists every input, select and textarea in the
   document, its open shadow roots (and, through the C# frame walker, its iframes). For each control it collects:
   - text of linked labels (`for=`, wrapping `<label>`, `aria-labelledby`, `aria-label`, `<legend>`)
   - text that merely *sits next to it* (left, above, or right for radio buttons), found from on-screen geometry, so
     a layout built from `<td>` or `<div>` with no real `<label>` still works
   - placeholder, title, `autocomplete`, id, name, class, the options of a dropdown, visibility
2. **Score** (`ResilientForm.Core`, plain C#, no browser needed). Each control is scored against what the field means.
   `txtFirstName`, `first_name`, `GIVEN NAME *:` and `Enter your first name` all normalise to the same words.
   Evidence is weighted: a linked label counts for much more than a class name. A clear label that says
   *Last Name* vetoes a control whose `name` happens to be `firstName`.
3. **Pick and explain.** The best score wins; if the second place is almost as good the run prints a warning.
   If nothing scores well enough it fails and lists the closest candidates.
4. **Act and verify.** Hidden, custom-styled radio buttons are clicked through their label, as a user would.

Gender needs care: `Male` is inside `Female`, so matching is done on whole words, and the radio must belong to a group
that is itself a gender group (labelled Gender/Sex, or offering Male/Female/Other), so a "Pet: Male / Female" group
does not win.

## What changes it survives

`fixtures/` holds 13 copies of the form, each broken in a different way. `dotnet test` opens each one in Chrome.

| Fixture | What changed |
|---|---|
| 01 baseline | reference form |
| 02 reordered | field order reversed, different layout |
| 03 obfuscated attributes | random ids, names and classes |
| 04 label not linked | layout table, text next to the field, no `<label for>` |
| 05 placeholder / aria only | no visible labels at all |
| 06 decoys | Last Name with a "first-name" class, hidden duplicate First Name, "Pet gender" group, Country dropdown |
| 07 iframe | the whole form lives inside an iframe |
| 08 shadow DOM | the form lives in an open shadow root |
| 09 custom radios | native radios hidden, styled pills shown |
| 10 delayed render | form appears 2 seconds after load |
| 11 inline layout | several fields on one row |
| 12 wording | `GIVEN NAME *:`, `Surname`, `Sex`, `State / Province` |
| 13 misleading attributes | ids and names deliberately point at the wrong field |

The pages mark the correct control with `data-expect`. The locator never reads it; only the test assertions do.

```bash
dotnet test
```

`MatcherTests` are fast unit tests of the scoring rules. `MutationTests` drive a real browser over every fixture.

## Layout

```
src/ResilientForm.Core     matching and scoring, no dependencies, easy to unit test
src/ResilientForm.Runner   Selenium glue: page scanner, actions, CLI
  Scripts/snapshot.js      the in-page snapshot
tests/ResilientForm.Tests  unit tests + browser tests over fixtures/
fixtures/                  the mutated pages
```

See [NOTES.md](NOTES.md) for the approach and its limits.
