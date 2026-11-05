# Approach and limitations

## Approach

The task says the page may change in order, ids/names/classes, structure, and may contain similar fields. All of those
are *implementation* details; what stays true is what the field is **called on screen**. So the program never stores a
selector. It describes each field by meaning ("a text box called First Name", "the Male option of the Gender group",
"the dropdown called State"), then ranks every control on the page by how well it fits.

- **Evidence over attributes.** Linked labels, neighbouring text (found by geometry), aria text and placeholders count
  heavily. `id`, `name` and `class` count a little, and a clear contradicting label cancels them out.
- **Decoys are handled by context, not by luck.** Last Name loses to First Name because its label says so. A hidden
  duplicate is penalised. A "Male" option only counts inside a group that looks like Gender. `Male` is never matched
  inside `Female` because matching is on whole words.
- **Where it looks.** Main document, iframes (up to 3 deep) and open shadow roots. It polls until the form appears, so
  late-rendered pages work.
- **Honest output.** For each field it prints how it was found, the score, the runner-up, and a warning on a close call.
  Each action is verified by reading the page state back.
- **Testable.** Matching lives in a dependency-free library; 13 mutated pages exercise it in a real browser.

## Limitations

- **Scoring is heuristic.** Weights and thresholds were tuned on the 13 fixtures and the described CloudQA form. A page
  built very differently may need a weight adjusted. When no field scores 0.5 or better the run fails and shows the
  closest candidates rather than guessing.
- **Language.** Phrases are English (`first name`, `gender`, `state`...). Other languages need more phrases in
  `Intents.cs`.
- **Genuinely ambiguous pages.** If two visible fields have the same label (e.g. two forms on one page), it picks the
  one in the main document, earliest in the page, and prints a warning. It cannot know which one you meant.
- **Closed shadow roots** cannot be read from script. **Cross-origin iframes** are read through WebDriver frame
  switching, which works, but a frame that loads very late may be missed on the first poll (later polls retry).
- **Custom widgets.** Dropdowns built from `<div>`s (not `<select>`) are not selected; native `<select>` and
  hidden-input-plus-label radios are. Radios built purely from `role="radio"` divs are found but rely on a click.
- **Defaults.** Without `--state`, the first non-placeholder option is chosen.
- **Not a full test framework.** It checks the three fields, not form submission or validation messages.
