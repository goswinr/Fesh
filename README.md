![Logo](https://raw.githubusercontent.com/goswinr/Fesh/main/Media/logo128.png)

# Fesh
[![Fesh on nuget.org](https://img.shields.io/nuget/v/Fesh.svg)](https://nuget.org/packages/Fesh)
[![Build](https://github.com/goswinr/Fesh/actions/workflows/build.yml/badge.svg?event=push)](https://github.com/goswinr/Fesh/actions/workflows/build.yml)
<!-- [![Check NuGet](https://github.com/goswinr/Fesh/actions/workflows/outdatedNuget.yml/badge.svg)](https://github.com/goswinr/Fesh/actions/workflows/outdatedNuget.yml) -->
![code size](https://img.shields.io/github/languages/code-size/goswinr/Fesh.svg)
[![license](https://img.shields.io/github/license/goswinr/Fesh)](LICENSE.md)

Fesh is an **F**# **E**ditor & **S**cripting **H**ost, for Windows.

I made it to embed F# as a scripting tool in other applications.
But it works just as well on its own.

![Screenshot](https://raw.githubusercontent.com/goswinr/Fesh/main/Media/screen-dark.png#gh-dark-mode-only)
![Screenshot](https://raw.githubusercontent.com/goswinr/Fesh/main/Media/screen-light.png#gh-light-mode-only)

Fesh is built on the excellent [AvalonEdit](https://github.com/goswinr/AvalonEditB). That is why it only runs on Windows (for now).
It supports F# 10 and has the IDE features you would expect: semantic syntax highlighting, type info, autocomplete and go to definition.

Unlike a typical F# REPL, Fesh has the input and the output in two separate windows.
Just [like Don Syme always wanted it](https://github.com/dotnet/fsharp/issues/2161#issuecomment-270465310).
You can even color the output with [Fesher](https://www.nuget.org/packages/Fesher).

Unlike most F# editors, Fesh runs [FSharp.Compiler.Service](https://www.nuget.org/packages/FSharp.Compiler.Service) in the same process as the UI.
There is no separate language server and no separate FSI process.
That is what makes it easy to host Fesh inside other apps, as an embedded scripting editor.
And that was in fact my main reason for building it.

I started prototyping Fesh in 2017, and I have used it professionally for scripting in Rhino3D since 2019.
It has been public since 2024.
Before that I used the Tsunami F# editor, as you can see in [my talk on the Louvre Abu Dhabi Dome](https://www.youtube.com/watch?v=ZY-bvZZZZnE).
But Tsunami is no longer available. So I built Fesh.

To host Fesh in your own app, there is the NuGet package [Fesh](https://www.nuget.org/packages/Fesh/).
See [Fesh.Rhino](https://github.com/goswinr/Fesh.Rhino) and [Fesh.Revit](https://github.com/goswinr/Fesh.Revit) for how I use it.

## Features

### Autocomplete
The completion list opens as you type and after a dot, also in the middle of a word.
A dot after a closing bracket works too, like in `xs[0].` or `Guid.NewGuid().`,
and so does a dot at the start of the next line of a method chain.

### Type info
Hover over a name to see its inferred signature, all of its xml docs, and the path of the assembly it comes from.

### Go to Definition
Press `F12`, or right-click a name, to jump to where it is defined: in the same file, or in a file loaded with `#load`.
For names from NuGet packages and other assemblies, Fesh offers to open their source in VS Code.
It finds the source on your computer, via SourceLink, or by cloning the GitHub repository of the package at the commit it was built from.

### NuGet
Type `#r "nuget: ` and you get a list of the packages you used before, plus what nuget.org finds for the name you typed.

After each `#r "nuget: ..."` line Fesh shows if the version is the latest.
One click updates it, pins a version, or takes you to a newer prerelease and back.
It also fixes the casing of the package name.
If you don't want this, set `CheckNugetVersions=False` in the Settings.txt file.

### Dark and light theme
Switch between them in the `View` menu, without a restart.
The dark colors follow F# in VS Code with the Ionide extension.

### Syntax highlighting
Highlighting runs in two passes.
First the regex rules of [SyntaxHighlightingFSharp.xshd](https://github.com/goswinr/Fesh/blob/main/Src/SyntaxHighlightingFSharp.xshd), then the semantic colors from the F# Compiler Service.
Mutable values are underlined, so you see at a glance where state can change.

If you want different colors, go to `About` → `Open and watch SyntaxHighlighting in VS Code`.
Each time you save the file in VS Code, all open tabs in Fesh update right away.

### Running code
`F5` evaluates the whole file, `Ctrl + Enter` the current lines, and `Alt + Enter` the selected text.
`Ctrl + Break` cancels a running script, even one that waits in a blocking call like `Thread.Sleep`.
`Ctrl + Alt + R` resets FSI.
All the other shortcuts are listed in the menus.

### Compile a script
When a script grows up, `FSI` → `Compile Script` turns it into an .fsproj and builds a dll from it, via the dotnet SDK or via MSBuild.
The `#r` line for the new dll is copied to your clipboard.

### Status bar
The status bar shows the number of compiler errors. Click on it to scroll to the first one.

### Selection highlighting
Select some text and all its occurrences get highlighted, in the editor and in the output window.
The status bar shows how many there are. Click on it to step through them.

### Drag and drop
Drag any file or folder into the editor to get its full path as a string at the cursor position.
For *.dll files it goes to the top, prefixed with `#r`.
For *.fsx files it goes to the top, prefixed with `#load`.

### Font
The default font is [Cascadia Mono](https://github.com/microsoft/cascadia-code).
Besides italic it also has a cursive script mode. I use it for comments.
To get ligatures, set the font to `Cascadia Code`.
You can change the font in the Settings.txt file, via the menu: `About` → `Open Settings Folder`.


## How to install the standalone app

### Installer

The easiest way is to run the Setup.exe from [Releases](https://github.com/goswinr/Fesh/releases).
Fesh will offer to update itself when a new version is available.
There is a .NET 10 and a .NET Framework 4.8 version. Apart from the runtime they are the same.

The installer is made with [Velopack](https://velopack.io) and is digitally signed.

No admin rights are needed to install or run Fesh.
It gets installed in `%LOCALAPPDATA%\Fesh` (or `%LOCALAPPDATA%\Fesh.net48`),
and it keeps its settings in `%APPDATA%\Fesh\Settings` (or `%APPDATA%\Fesh.net48\Settings`).
A shortcut is created on the desktop.

### Portable

Each release also has a portable zip.
Extract it and put it wherever you like.
It works just like the installed app, but keeps its Settings folder in its own folder.
The `.portable` file in that folder tells Fesh to do so.
It also offers to update itself when a new version is available.

### Build from source

```bash
dotnet build FeshStandalone.fsproj
```
You still get notified about new releases, but you have to install them yourself.

## How to host Fesh in another app

Use the NuGet package [Fesh](https://www.nuget.org/packages/Fesh/).
Each release has a package version for each runtime: one with a `-net472`, `-net8`, `-net9` or `-net10` suffix.
The version without a suffix is for .NET 10.

To build the package yourself, run:

```bash
dotnet build FeshHosting.fsproj
```

## Release Notes
For the changes in each release, see the [CHANGELOG.md](https://github.com/goswinr/Fesh/blob/main/CHANGELOG.md).

## License
Fesh is licensed under the [MIT License](https://github.com/goswinr/Fesh/blob/main/LICENSE.md).
