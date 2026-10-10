# AGENTS.md

Fesh is an F# editor and scripting host for Windows, built on WPF and AvalonEditB. FSharp.Compiler.Service and F# Interactive run in the UI process. There is no language server and no separate FSI process. Fesh ships two ways: as a standalone app (Velopack installer), and as a NuGet package that other .NET apps embed as their scripting editor.

## Commands

```powershell
dotnet build FeshStandalone.fsproj                     # the app, for net48 and net10.0-windows
dotnet build FeshStandalone.fsproj -f net10.0-windows  # faster: one target only
dotnet build FeshHosting.fsproj                        # the NuGet library, net8.0-windows by default
```

CI (`.github/workflows/build.yml`) builds `FeshHosting.fsproj` in Release and publishes the app for both frameworks:

```powershell
dotnet publish FeshStandalone.fsproj --configuration Release --runtime win-x64 --framework net10.0-windows --no-self-contained
dotnet publish FeshStandalone.fsproj --configuration Release --runtime win-x64 --framework net48 --no-self-contained
```

If the build fails with `MSB3027 ... The file is locked by: "Fesh (<pid>)"`, Fesh is running. Ask the user to close it. Don't kill it.

## Testing

There are no unit test projects. To verify a change:

1. Build both projects. They compile the same files but for different target frameworks, so `#if` code can break in only one of them.
2. For editor behavior, run the real app with the run-fesh skill. `.claude/skills/run-fesh/SKILL.md` documents `driver.ps1`, which launches Fesh, types code, reads the completion list, status bar and log, and takes screenshots. `dot-completion-cases.ps1` in the same folder checks dot completion and is a template for checks of other features.

Don't start `Fesh.exe` yourself to test. Launching it rewrites the user's settings in `%APPDATA%\Fesh\Settings`, including the list of open tabs. The driver backs them up on `launch` and restores them on `stop`, so always end with `stop`.

`Test/` has .fsx scripts for manual checks.

## Layout

`FeshStandalone.fsproj` (WinExe) and `FeshHosting.fsproj` (library) compile the same files from `Src/`. F# compiles files in the listed order, so a new .fs file must go into the `<Compile>` list of both fsproj files, at the same position, after every file it uses.

In compile order:

- `Src/Util.fs`, `ParseFs.fs`, `Timer.fs`, `StyleState.fs`, `Theme.fs`, `ThemeChrome.fs`: helpers and theme colors
- `Src/Model.fs`: shared types and interfaces, e.g. `IFeshLog`, so that Config can be declared before the Log
- `Src/Config/`: the settings files, run context (standalone or hosted, .NET Framework or .NET), FSI arguments, open tabs and usage statistics
- `Src/Fsi.fs`: the in-process F# Interactive session
- `Src/CompileScript.fs`: turns a script into an .fsproj and builds it with MSBuild
- `Src/Editor/`: type checking (`Checker.fs`), completions, type info tooltips, semantic and error highlighting, folding, selection tools and NuGet version hints
- `Src/Views/`: WPF window, tabs, log, menus, commands and status bar
- `Src/Initialize.fs`: wires everything up and checks for Velopack updates
- `Src/App.fs`: `[<EntryPoint>]` for the standalone app and `createEditorForHosting` for host apps

Namespaces are `Fesh`, `Fesh.Model`, `Fesh.Config`, `Fesh.Editor` and `Fesh.Views`.

## Target frameworks

- The app targets `net48` and `net10.0-windows`.
- The hosting library has a single `<TargetFramework>`, `net8.0-windows` by default. `releaseNuget.yml` replaces it with a regex to build one package each for net472, net8, net9 and net10. Each target uses the FSharp.Compiler.Service and FSharp.Core versions from its conditional `ItemGroup` in `FeshHosting.fsproj`. Keep `<TargetFramework>` on a single line so the regex still matches.
- `NETFRAMEWORK` is defined for net48 and net472, e.g. for `Thread.Abort` to cancel FSI (.NET uses `ControlledExecution`). `NET8` is defined for the net8 hosting build. At runtime, check `RunContext.IsRunningOnDotNetCore`.

## Theme and highlighting

The light and dark theme switch live, without a restart:

- Syntax colors, including the `Semantic.*` colors, are in `Src/SyntaxHighlightingFSharp.xshd`. Dark colors are the `dark...` attributes next to the light ones.
- All other colors are in `Src/Theme.fs`.
- Dark styles for WPF controls are in `Src/ThemeChrome.xaml`, loaded by `Src/ThemeChrome.fs`. F# can't compile XAML, so it is an `EmbeddedResource` with `Page Remove`.

Don't hard-code colors. Read them from `Theme.*` when drawing, or update on `Theme.Changed`.

Highlighting runs in two passes: first the regex rules from the xshd file, then a semantic pass with the type checker results.

## CHANGELOG.md and versions

The version of the app and the package comes from `CHANGELOG.md`, through `Ionide.KeepAChangelog.Tasks`. The fsproj files have no `<Version>`. The release workflows fail if the git tag differs from the changelog version.

- Add user-facing changes under `## [Unreleased]`, in `### Added`, `### Changed` or `### Fixed`, worded like the existing entries.
- Write every bullet on a single line, with no wrapped lines and no nested bullets. The parser crashes on them.
- Don't mix line endings in the file (it is CRLF in the working tree). Mixed line endings fail the build with IKC0002.
- Run `dotnet build` after editing the changelog.

## Conventions

- `--warnon:1182` warns on unused variables. Remove the binding or prefix it with `_`.
- `LangVersion` is `preview`, for `^` end indexing.
- Releases start from a pushed `x.y.z` tag (`release.yml` for the installers, `releaseNuget.yml` for the packages). Only the maintainer creates tags.
