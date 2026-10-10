---
name: run-fesh
description: Build, launch, and drive the Fesh editor app (WPF, Windows) to test a change in the real app - type code, check the completion window, read the status bar and log, take screenshots. Use when asked to run, start, or screenshot Fesh, or to try an editor feature like autocomplete in the running app.
---

Fesh is a WPF desktop app, so it is driven through Windows UI Automation and SendKeys by
`.claude/skills/run-fesh/driver.ps1`. Each call of the driver is one command. Between calls the driver keeps its state (process id, settings backup) in `$env:TEMP\fesh-run\state.json`.
All paths are relative to the repo root.

## Prerequisites

- Windows with an unlocked, visible desktop session. SendKeys and screenshots need a real foreground window.
- .NET SDK 10 (`dotnet --version` gave 10.0.401) and PowerShell 7 (`pwsh`).
- No other Fesh instance running. It would share the Settings folder, and the build can't overwrite a running `Fesh.exe`.

## Build

```powershell
dotnet build FeshStandalone.fsproj -f net10.0-windows
dotnet build FeshStandalone.fsproj -f net48
```

## Run (agent path)

```powershell
$d = ".claude/skills/run-fesh/driver.ps1"
pwsh -NoProfile -File $d launch "let s = `"abc`"`nlet t = s.Substring(1)"   # -Framework net48 for the .NET Framework build
pwsh -NoProfile -File $d keys "."        # a real key press at the caret (the end of the text)
pwsh -NoProfile -File $d popup           # -> Chars, Length, Clone, ... (or "no completion window")
pwsh -NoProfile -File $d shot my_case    # -> C:\Users\<you>\AppData\Local\Temp\fesh-run\shots\my_case.png
pwsh -NoProfile -File $d stop            # kill Fesh, restore the Settings folder
```

**Always end with `stop`.** `launch` changes the user's Settings, and `stop` restores them. Look at the screenshots with the Read tool.

| command | what it does |
|---|---|
| `launch [code]` | Backs up the Settings folder and opens `$env:TEMP\fesh-run\Scratch.fsx` containing `[code]` in a new Fesh. Waits for the first type check and puts the caret at the end. |
| `settext <code>` | Replaces the whole editor text as one change, so it does **not** trigger completion. Puts the caret at the end and waits for the type check. Prints the status bar. |
| `keys <keys>` | Sends keys in SendKeys syntax, e.g. `"."`, `"^{END}"`, `"^ "` (Ctrl+Space). Escape `+ ^ % ~ ( ) { } [ ]` as `{(}`. |
| `type <text>` | Types literal text one key at a time, with `-DelayMs` (default 40) between keys. |
| `popup` | Waits up to `-WaitMs` (default 4000) for the completion window. Prints its visible items, or `no completion window`. |
| `esc` | Closes the completion window. |
| `gettext` | Prints the editor text. |
| `status` | Prints the status bar texts: type check result like `1 compiler errors, first one on line: 3`, and the FSI state. |
| `log` | Prints the text of the log panel on the right. |
| `shot [name]` | Screenshot of the Fesh window plus the completion window. Prints the png path. |
| `stop` | Kills Fesh, copies changed Settings files back from the backup, and removes files created during the run. |

To test a typed trigger character, set the code without it, then type only the last character:
`settext` followed by `keys "."`.

## Run (human path)

`bin\Debug\net10.0-windows\win-x64\Fesh.exe` opens the tabs of the last session. With a file path as argument, it opens only that file.

## Test

There are no unit test projects. `.claude/skills/run-fesh/dot-completion-cases.ps1` checks 18 dot-completion cases against a launched Fesh: after `)`, after `]`, after whitespace and line breaks, and wrong indentation. It exits with 1 if a case fails:

```powershell
pwsh -NoProfile -File .claude/skills/run-fesh/driver.ps1 launch
pwsh -NoProfile -File .claude/skills/run-fesh/dot-completion-cases.ps1   # 18 x True, exit code 0
pwsh -NoProfile -File .claude/skills/run-fesh/driver.ps1 stop
```

Use it as a template for other editor features: a list of (code, key, expected item) cases.

## Gotchas

- **Launching Fesh rewrites the user's Settings.** Even a file argument does. Affected files in `%APPDATA%\Fesh\Settings` (net48: `%APPDATA%\Fesh.net48\Settings`) include `Currently-Open-Files.txt` (the user's tab list), `AutoComplete-Statistic.txt`, `Folding-States.txt`, `Position-of-Window-on-Screen.txt` and `Settings.txt`. A missing `NuGet-Statistic.txt` gets created. This is why only `launch`/`stop` should start and stop Fesh.
- **The completion window is not a child of the desktop** in the UI Automation tree. It is an owned window, so it sits under the Fesh main window. Searching the desktop's children by process id never finds it.
- **The completion list is virtualized.** Only the roughly 25 visible items exist for UI Automation. Check for items near the top of the list (e.g. `Length` for a string, not `Substring`).
- **The editor has no class name.** It is a `Document` element, and so is the log panel. Each also appears twice (TextEditor and TextArea). The current tab's editor is the first `Document` under the `TabControl`.
- **SendKeys goes to whatever window is in the foreground.** The driver brings Fesh to the front with an Alt key press plus `SetForegroundWindow`. It refuses to send keys if Fesh is still not in front, so keys never land in VS Code. Don't call SendKeys outside the driver.
- **`SetFocus` throws** `Operation is not valid due to the current state of the object` if Fesh isn't in the foreground. Calling it while the completion window is open would close the window, so the driver skips it then.
- **Typing brackets is not literal.** Typing `(` inserts `()`. Typing `)`, `,` or `;` inserts that character plus a trailing space. So `type "s.Substring(1)."` produces `s.Substring(1) .)`. Prefer `settext` for setup code, or type `(`, the arguments, then `keys "{END}"`.
- **The status bar shows `Checking for Errors ...` only for checks that take more than 200 ms.** The driver waits 400 ms, then polls until that text is gone. The first check after startup takes several seconds.
- **`stop` kills the process instead of closing the window.** Closing would show the `Fesh | Save Changes?` dialog for the changed scratch file (`Src/Views/Tabs.fs`).
- **The log always shows `Could not check for Velopack updates ... NotInstalledException`** for a dev build. That is expected.
- **Screenshots copy from the screen.** If another window covers Fesh, the screenshot shows that window.
- In your own case scripts, write each case as `,@("name", "code", "expected")`, with the leading comma. Without it PowerShell flattens all cases into one list of strings.

## Troubleshooting

- **`error MSB3027: Could not copy ... apphost.exe to bin\Debug\net10.0-windows\win-x64\Fesh.exe ... The file is locked by: "Fesh (<pid>)"`**: Fesh is running. Run `driver.ps1 stop`, or ask the user to close their instance.
- **`a previous run was not stopped, run 'stop' first`**: a crashed or forgotten run left `state.json`. Run `stop`; it restores the Settings from that run's backup.
- **`a Fesh process is already running, close it first`**: the user's own Fesh is open. Don't kill it; ask the user to close it.
- **`Fesh is not in the foreground (process N is), not sending keys`**: another window took the focus, e.g. a dialog in Fesh or another app. Take a `shot` to see it, then `stop` and `launch` again.
- **`popup` says `no completion window` although it should show**: the caret may not be where you think. Check with `gettext` and a `shot`. `launch` and `settext` put the caret at the end.
