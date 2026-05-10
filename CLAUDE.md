# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Fesh is an **F# Editor & Scripting Host** for Windows, built on WPF and AvalonEdit. It uses FSharp.Compiler.Service in-process (no separate language server or FSI process) to provide semantic highlighting, type info, and autocomplete. It can run standalone or be hosted/embedded in other .NET apps as a scripting tool.

## Build Commands

```bash
# Build standalone app (targets net48 and net10.0-windows)
dotnet build FeshStandalone.fsproj

# Build hosting library (NuGet package)
dotnet build FeshHosting.fsproj

# Publish standalone for release (matches CI)
dotnet publish FeshStandalone.fsproj --configuration Release --runtime win-x64 --framework net10.0-windows --no-self-contained
dotnet publish FeshStandalone.fsproj --configuration Release --runtime win-x64 --framework net48 --no-self-contained
```

There are no unit test projects in the solution. The `Test/` folder contains an .fsx script for manual testing.

## Architecture

**Two .fsproj files, one codebase:** Both projects compile the same source files. `FeshStandalone.fsproj` produces a WinExe; `FeshHosting.fsproj` produces a NuGet library package. File compile order is defined in the fsproj (F# requires ordered compilation).

**Entry points:**
- `Src/App.fs` — contains `[<EntryPoint>]` for standalone and `createEditorForHosting` for hosted mode
- `Src/Initialize.fs` — bootstraps the editor, checks for Velopack updates

**Core layers (in compile order):**
- `Src/Model.fs` — shared types and interfaces (e.g., `IFeshLog`)
- `Src/Config/` — settings persistence, run context, FSI arguments, tab state
- `Src/Fsi.fs` — F# Interactive session management (in-process)
- `Src/CompileScript.fs` — script compilation via FSharp.Compiler.Service
- `Src/Editor/` — editor features: syntax highlighting, autocomplete, type info, error highlighting, code folding, bracket matching, selection operations
- `Src/Views/` — WPF UI: windows, tabs, menus, status bar, log output

**Key design decisions:**
- FSharp.Compiler.Service runs in the same process as the UI — no LSP, no out-of-process FSI
- Dual target framework: `net48` (uses `NETFRAMEWORK` define, allows `Thread.Abort`) and `net10.0-windows`
- Versioning is extracted from `CHANGELOG.md` by `Ionide.KeepAChangelog.Tasks`
- Updates use Velopack with GitHub Releases as the source
- Syntax highlighting: initial regex-based via `Src/SyntaxHighlightingFSharp.xshd`, then semantic pass from compiler service

**Key dependencies:**
- `AvalonEditB` (fork of AvalonEdit) — text editor control
- `AvalonLog` — output log window
- `FSharp.Compiler.Service` — parsing, checking, completions, semantic info
- `Fittings` — utility library
- `Velopack` — auto-update mechanism

## CI/CD

GitHub Actions on `windows-latest`. Workflows:
- `build.yml` — builds both projects on push/PR to main
- `release.yml` — creates installer via Velopack, publishes GitHub Release
- `releaseNuget.yml` — publishes hosting NuGet package

## Conventions

- F# with `--warnon:1182` (unused variable warnings enabled)
- `LangVersion preview` for features like `^` end-indexing
- Namespace: `Fesh` (with sub-namespaces `Fesh.Model`, `Fesh.Config`, `Fesh.Views`, `Fesh.Editor`)
