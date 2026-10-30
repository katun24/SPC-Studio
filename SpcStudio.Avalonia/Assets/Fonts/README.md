# Fonts

The family the app draws with, embedded so it looks the same on a machine that has neither
face. SIL Open Font License 1.1, which allows redistribution; the license text sits beside the
files it covers.

| File | Family | Source |
|---|---|---|
| `Inter-Regular.ttf`, `Inter-SemiBold.ttf` | Inter | [rsms/inter](https://github.com/rsms/inter) v4.1 |

One family for everything, the numeric and hex fields included. Inter's digits are proportional
by default, so a readout that counts asks for tabular figures where it needs them.

The static faces, one file per weight, rather than the variable one.

## Removing them

Delete this folder. Nothing else has to change:

- `SpcStudio.Avalonia.csproj` globs `Assets\Fonts\*.ttf` rather than listing them, so no
  reference is left dangling.
- `Theme/AppFonts.cs` names stock fonts after the embedded one, and Avalonia takes that as a
  fallback chain - so the app drops back to the system's UI font on its own.

To swap in a different font instead, put the files here and change the family string in
`AppFonts.cs`. The name after the `#` is the font's own family name, not the file name.
