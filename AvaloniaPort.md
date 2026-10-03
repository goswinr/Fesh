# Avalonia port: notes for continuing the work

This branch (`Avalonia2`) ports Fesh from WPF + AvalonEdit to Avalonia + AvaloniaEdit.
`main` is the WPF version and the reference for how a feature is supposed to behave.
When something is missing or behaves differently here, first look at the same file on `main`
(`git show main:Src/Views/Tabs.fs`) and port the logic, not the WPF API.

`CLAUDE.md` still describes the WPF version (WPF, net48, AvalonLog, Fittings as NuGet packages).
Where it disagrees with this file, this file is right for this branch.

## State (2026-10-03)

The standalone app starts, runs and shuts down cleanly on Windows with Avalonia 12.1.3.
These were checked by driving the app, headless and in the real window:

- typing, F# auto indent, auto closing brackets, completion window and inserting a completion
- semantic highlighting, error squiggles and line backgrounds, status bar error count
- type info and error tooltips on hover, Ctrl + P pop out
- FSI evaluation on its own thread (UI stays responsive), cancel, evaluate again, sync and async mode
- tabs: new, switch, close, closing the last one opens a new empty one
- open and save, paths with spaces, reload of externally changed files, session restore
- the native open file dialog (the window stays responsive while it is open)
- menus incl. shortcut text, Edit menu commands (Copy, Paste, Undo, Find ...) and keyboard shortcuts
- foldings, error and selection marks on collapsed foldings
- closing with unsaved changes and while FSI is running (the confirmation dialogs), app shutdown
- documents with `\n` line endings (as on Linux and macOS) and mixed line endings in the log
- hosted mode: a host that is not an Avalonia app calls `createEditorForHosting`, the window shows,
  scripts run, the host window is the owner of the editor window

Not verified yet: drag and drop of files from the file explorer, the 'Save Text in Log' dialogs,
hover tooltips in the real window (only headless), running inside Rhino, Revit or AutoCAD,
and anything on Linux or macOS.

## Setup

### The AvaloniaEdit fork

Fesh uses features that only the fork of AvaloniaEdit at https://github.com/goswinr/AvaloniaEdit has,
so the NuGet package `Avalonia.AvaloniaEdit` can't be used:

- `FoldingSection.BackgroundColor` and `FoldingSection.DecorateRectangle` (marks on collapsed foldings)
- `CompletionWindow.ToolTipContent`
- `TextEditor.ScrollViewer` is public, `SimpleSelection`, `EmptySelection` and `PopupWithCustomPosition` are public

Both fsproj files reference it as a sibling folder: `../AvaloniaEdit/src/AvaloniaEdit/AvaloniaEdit.csproj`.

**The fork must be on its branch `main`.** It has upstream's Avalonia 12 release merged in
and uses Avalonia 12.1.3 in its `Directory.Packages.props`.
The fork's `dev` branch is still built for Avalonia 11.3.0 and does not work with this branch.

Check with: `git -C ../AvaloniaEdit branch --show-current`

When bumping Avalonia, bump both `FeshStandalone.fsproj` / `FeshHosting.fsproj` and the fork's
`Directory.Packages.props` together. The fork treats warnings as errors, including NuGet audit warnings.

### Build

```bash
dotnet build FeshStandalone.fsproj   # Exe, net10.0
dotnet build FeshHosting.fsproj      # library + Fesh nuget package, net8.0 by default
```

Both projects compile the same source files in the same order. When adding a file, add it to both.
They share the `obj` folder, so building one after the other triggers a restore each time. That is expected.

There is no .NET Framework build anymore: Avalonia 12 needs .NET 8 or higher.

### Hosting

- `FeshHosting.fsproj` defaults to `net8.0` (lowest F# version, FSharp.Core 8). `releaseNuget.yml` replaces
  the `<TargetFramework>` line to build other versions. The package version conditions use `StartsWith`,
  so `net8.0` and `net8.0-windows` both work.
- The fork's `AvaloniaEdit.dll` is packed into `lib/` of the Fesh package (target `IncludeAvaloniaEditForkInPackage`),
  the project reference has `PrivateAssets="all"` so that the package does not depend on `Avalonia.AvaloniaEdit`.
  A project that references `FeshHosting.fsproj` directly (not the package) needs its own reference to the fork
  if it uses AvaloniaEdit types.
- `App.createEditorForHosting` (in [Src/App.fs](Src/App.fs)) sets up Avalonia with `SetupWithoutStarting`
  if the host is not an Avalonia app, the host's message loop then drives the Avalonia windows.
  It must be called on the host's UI thread. If the host is an Avalonia app, the AvaloniaEdit styles get added to its Application.
- The host window becomes the owner of the editor window via `SetWindowLongPtr` (Windows only, `NativeOwner` in App.fs).
  Avalonia resets the owner on every `Show()`, so it is set in the `Opened` event.

## Where the port specific code is

| What | Where |
|---|---|
| Fittings (was a NuGet package): sync context, MessageBox (MessageBox.Avalonia, async), PositionedWindow, ErrorHandling, commands | [Src/Fittings/](Src/Fittings/) |
| `TipPopup`: a Popup that looks like a ToolTip, for type info and error tooltips | [Src/Fittings/DependencyProps.fs](Src/Fittings/DependencyProps.fs) |
| AvaloniaLog (was the AvalonLog NuGet package), incl. `unifyNewLines` | [Src/AvaloniaLog/](Src/AvaloniaLog/) |
| `EvalThreadContext`: the message loop of the FSI evaluation thread | [Src/Fsi.fs](Src/Fsi.fs) |
| `routed`: makes AvaloniaEdit's RoutedCommands work from menus | [Src/Views/Commands.fs](Src/Views/Commands.fs) |
| `MenuUtil.menuItem`: menu items with shortcut text in the header | [Src/Views/StatusBar.fs](Src/Views/StatusBar.fs) |
| `FileDialogs`, open and save via StorageProvider | [Src/Views/Tabs.fs](Src/Views/Tabs.fs) |
| `findTemplatePart`, scrollbar error markers in the AdornerLayer | [Src/Views/Scrollbar.fs](Src/Views/Scrollbar.fs) |
| Tunneling key and text input handlers | [Src/Views/Editor.fs](Src/Views/Editor.fs), [Src/Editor/KeyboardShortcuts.fs](Src/Editor/KeyboardShortcuts.fs) |
| Line index that accepts any line ending, `Str.lineBreakLength` | [Src/Editor/InteractionState.fs](Src/Editor/InteractionState.fs), [Src/Util.fs](Src/Util.fs) |
| Hosting setup and native owner | [Src/App.fs](Src/App.fs) |

## WPF to Avalonia: what tripped up the port

Each of these was a real bug on this branch. Check new code against this list.

- **Template parts:** `control.FindControl<T>(name)` only searches XAML name scopes and throws for template parts.
  Walk the visual tree (`findTemplatePart` in Scrollbar.fs) or use `e.NameScope.Find` in `TemplateApplied`.
- **Preview events:** WPF `PreviewKeyDown` / `PreviewTextInput` are tunneling handlers in Avalonia:
  `ctrl.AddHandler(InputElement.KeyDownEvent, handler, RoutingStrategies.Tunnel)`.
  A normal `.KeyDown.Add` on the editor never sees Enter, Backspace, Delete or typed text,
  because AvaloniaEdit's TextArea handles them first and marks them handled.
- **KeyBindings run before KeyDown:** Avalonia checks `KeyBindings` from the focused element up to the window
  before it raises `KeyDown`. Window level gestures win over the editor.
- **Focus:** the AvaloniaEdit `TextEditor` is not focusable, focus its `TextArea`.
  In `Window.Opened` the tab content is not in the visual tree yet; post the focus call with `DispatcherPriority.Background`.
- **ActualWidth / ActualHeight:** `Width` and `Height` are `NaN` unless set explicitly. Use `Bounds.Width` / `Bounds.Height`.
- **Popups:** a `Popup` that is not in the logical tree gets no styles, so its content renders as nothing.
  It also has no background or border. Use `TipPopup` (takes a logical parent).
  A `PlacementRect` of zero size is ignored (the popup lands at the corner of the target), give it a size of 1.
  `PlacementMode.Top` / `Bottom` center the popup horizontally, WPF aligned it left: use `TopEdgeAlignedLeft` / `BottomEdgeAlignedLeft`.
- **ToolTips:** `ToolTip.SetTip(control, content)`, content can be a string or a control. Don't put a Popup into a ToolTip.
  The defaults of attached ToolTip properties can't be overridden for `Control` itself, only for subclasses (see Initialize.fs).
- **RoutedCommands of AvaloniaEdit** (`ApplicationCommands.Copy`, `AvaloniaEditCommands.DeleteLine` ...) execute on the element
  that got the focus last. From a menu that is the menu item, so they are always disabled. Wrap them with `routed` in Commands.fs.
- **CanExecute:** there is no `CommandManager.RequerySuggested`. Menu items re-check `CanExecute` when their submenu opens, that is enough for menus.
- **Routed SelectionChanged:** `SelectionChanged` bubbles up from child controls (e.g. the completion list). Check `e.Source` (see Tabs.fs).
- **One Dispatcher only:** Avalonia has no Dispatcher per thread. A background thread that needs a message loop
  gets its own SynchronizationContext (`EvalThreadContext`). Scripts that want to show Avalonia UI must use `Dispatcher.UIThread`.
- **Never block the UI thread on a Task:** dialogs (MessageBox, file pickers) are async in Avalonia.
  `.Wait()`, `.Result` or `Async.RunSynchronously` on them deadlocks. Use `task { let! ... }`.
- **File paths from the StorageProvider:** `IStorageItem.Path.AbsolutePath` is URL encoded (`%20` for spaces). Use `TryGetLocalPath()`.
- **Window.StateChanged** does not exist: observe `Window.WindowStateProperty` via `PropertyChanged`.
- **DispatcherUnhandledException** is `Dispatcher.UIThread.UnhandledException` (Avalonia 11.3+).
- **Right mouse button:** `e.Pointer.IsPrimary` is true for every mouse button. Use `e.GetCurrentPoint(ctrl).Properties.IsRightButtonPressed`.
- **Window owner:** `Window.Show(owner)` for Avalonia owners, a native owner handle needs user32 (see App.fs).
- **Styles:** an F# subclass of a templated control (`Window`, `TabItem`, ...) needs `override _.StyleKeyOverride = typeof<BaseControl>`,
  otherwise the theme has no template for it and it shows nothing.

Avalonia 12 API changes that were needed when moving from 11.3:

- `DragEventArgs.Data` is now `DragEventArgs.DataTransfer` (`Contains DataFormat.File`, `TryGetFiles()`)
- `GotFocusEventArgs` is now `FocusChangedEventArgs`
- `IClipboard.SetTextAsync` is an extension method in `Avalonia.Input.Platform`
- `VisualExtensions.GetVisualRoot` is gone, use `IsAttachedToVisualTree` or `TopLevel.GetTopLevel`
- `Popup.Host` is gone

## Line endings

`Environment.NewLine` is `"\r\n"` on Windows and `"\n"` on Linux and macOS. The rules on this branch:

- Code that **parses** text accepts `"\r\n"`, `'\n'` and `'\r'` (`Str.lineBreakLength`, `getLineInfos`).
  Never search for `'\r'` and then skip 2 characters.
- Code that **inserts** text into a document or onto the clipboard uses `Environment.NewLine`,
  or the line break the document already has when it rearranges existing lines (SwapLines, AlignText).
- Files are normalized to `Environment.NewLine` when they are opened (`Str.normalizeCode`).
- Everything printed to the log is normalized to `Environment.NewLine` in `AvaloniaLog.printOrBuffer`.
- Messages, tooltips and dialog texts still contain literal `"\r\n"`. That only affects display and renders as one line break everywhere.

## Open work

Porting gaps (commented out with TODO in the code):

- Background color of the line number and folding margins (`ColumnRulers.fs`, `LineNumberMargin` has no such property in AvaloniaEdit)
- Restoring foldings when `CollapsedLinesAreInconsistent` (`Foldings.fs`, needs the property in the fork first)
- Cursive variant of Cascadia Mono via typography properties (`SemanticHighlighter.fs`)
- Border styling of the completion window (`Completions.fs`)
- `SearchPanel.MarkerCornerRadius`, `Logging.LogAction`, `TextArea.AlternativeRectangularPaste` (AvalonEditB features)
- A dark theme and color palette (the README asks for it, colors are hard coded for a light background)

Not part of the port itself:

- CI: the workflows don't check out the AvaloniaEdit fork, so they can't build this branch.
  The net472 step in `releaseNuget.yml` can't work anymore (Avalonia 12 needs .NET 8+).
- The host plugins (Fesh.Rhino, Fesh.Revit, Fesh.AutoCAD in sibling folders) still use the WPF `Fesh` NuGet package.
- `CLAUDE.md` and the README section about this branch are out of date. `Fesh.slnOFF` looks like a leftover.

Behaviour that is the same as on main, so not a port bug:

- Typing a closing bracket or quote next to an auto inserted one adds a second one (there is no type over).
- If the keyword completion popup is open when Enter is pressed (e.g. right after typing `do`), Enter accepts the completion instead of starting a new line.
- Bracket pair highlighting at the caret is disabled (`BracketHighlighter.fs`, also on main).
- 'Could not check for Velopack updates ... NotInstalledException' in the log of a local build.

## Testing

There are no automated tests. The app was checked by driving it from outside. Three things to know first:

### Keep the user's settings out of it

The standalone app keeps its settings in `%APPDATA%\Fesh\Settings`, shared with an installed WPF Fesh.
A hosted Fesh uses `%APPDATA%\Fesh.<hostName>\Settings`.
For a test run of the standalone app create an empty file `bin/Debug/.portable`
(the parent of the folder of `Fesh.exe`), settings then go to `bin/Debug/Settings`. Delete both afterwards.

On an unhandled exception Fesh writes `Fesh-UnhandledException-<time>.txt` to the Desktop.

### Headless (preferred)

A throwaway console project (outside the repo) can run the real app on the Avalonia headless platform
and drive it with simulated input, without touching the real keyboard, mouse or screen:

```xml
<!-- Harness.fsproj, net10.0, Exe -->
<ProjectReference Include="path/to/Fesh/FeshStandalone.fsproj" />
<PackageReference Include="Avalonia.Headless" Version="12.1.3" />
<PackageReference Include="Avalonia.Desktop" Version="12.1.3" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.3" />
```

```fsharp
[<EntryPoint>]
let main _ =
    // put an empty '.portable' file into the parent of AppContext.BaseDirectory first, see above
    AppBuilder.Configure<Fesh.App.FeshApp>()
        .UseSkia()
        .UseHeadless(AvaloniaHeadlessPlatformOptions(UseHeadlessDrawing = false))
        .AfterSetup(fun _ -> Dispatcher.UIThread.Post(fun () -> runScenario() |> Async.StartImmediate))
        .StartWithClassicDesktopLifetime([||])
```

In the scenario (on the UI thread, wait with `Async.Sleep` and then `Async.SwitchToContext` back to the UI context):

- `Fesh.App.current` gives access to `.Tabs`, `.Log.AvaloniaLog.Text()`, `.Commands.RunAllText.cmd.Execute null`, `.Fsi.State`
- input: `window.KeyPress(key, modifiers, PhysicalKey.None, null)`, `window.KeyTextInput "x"`, `window.MouseMove(point, ...)`
- screenshots: `window.CaptureRenderedFrame().Save(path)`; popups are overlay popups in headless mode, so they are in the main frame
- give the startup ~6 s (type checker and FSI initialize)
- set test code with `"\r\n"` line endings on Windows, as a file loaded from disk would have, unless line endings are what you test

### Real window

To check platform specific parts (native dialogs, real popup windows, the clipboard), run `bin/Debug/net10.0/Fesh.exe`
(with `.portable`, see above) and drive it from PowerShell:

- `PostMessage` of `WM_KEYDOWN` works for keys without modifiers (F5, Enter, arrows).
- `WM_CHAR` is ignored by Avalonia after a key down that was handled, post an unused key (F13) before each char.
- Modifier chords (Ctrl + O) need real input (`keybd_event`), only send them if Fesh is the foreground window.
- `PrintWindow(hwnd, hdc, 2)` takes a screenshot of a window; popups are separate windows of the process.
- UI Automation finds the top level menu items, but they don't support Invoke or ExpandCollapse.
