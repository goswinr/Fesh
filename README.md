![Logo](https://raw.githubusercontent.com/goswinr/Fesh/main/Media/logo128.png)

# Fesh
[![Fesh on nuget.org](https://img.shields.io/nuget/v/Fesh.svg)](https://nuget.org/packages/Fesh)
[![Build](https://github.com/goswinr/Fesh/actions/workflows/build.yml/badge.svg?event=push)](https://github.com/goswinr/Fesh/actions/workflows/build.yml)
[![Hits](https://hits.seeyoufarm.com/api/count/incr/badge.svg?url=https%3A%2F%2Fgithub.com%2Fgoswinr%2FFesh&count_bg=%2379C83D&title_bg=%23555555&icon=github.svg&icon_color=%23E7E7E7&title=hits&edge_flat=false)](https://hits.seeyoufarm.com)
![code size](https://img.shields.io/github/languages/code-size/goswinr/Fesh.svg)
[![license](https://img.shields.io/github/license/goswinr/Fesh)](LICENSE)

Fesh is an  **F**# **E**ditor & **S**cripting **H**ost.\
On Windows.

It is designed for embedding F# as application scripting tool.\
But it works standalone just as well.

![Screenshot](https://raw.githubusercontent.com/goswinr/Fesh/main/Media/screen2.png)

It is based on the excellent [AvalonEdit](https://github.com/goswinr/AvalonEditB), that is why it only works on Windows (for now).\
The editor supports F# 8.0 and has modern IDE features like semantic syntax highlighting, type-info and autocomplete.\
Unlike a typical F# REPL this editor has the input and the output in two separate windows.\
Just [like Don Syme always wanted it](https://github.com/dotnet/fsharp/issues/2161#issuecomment-270465310).\
You can even color the output with [Fescher](https://www.nuget.org/packages/Fesher).

Contrary to most F# editors, it uses [FSharp.Compiler.Service](https://www.nuget.org/packages/FSharp.Compiler.Service) in the same process as the UI.\
There is no separate language server or FSI process.\
Therefore,  Fesh can easily be hosted in other apps to create an embedded application scripting tool.\
This was in fact the primary motivation for creating Fesh.\
It is public since 2024.\
But I started prototyping it in 2017. I used it for scripting in Rhino3D professionally since 2019.\
Initially I used the Tsunami F# editor, like seen in [my talk on the Louvre Abu Dhabi Dome](https://www.youtube.com/watch?v=ZY-bvZZZZnE).\
But it is no longer available. So I created Fesh.

For hosting there is the nuget package [Fesh](https://www.nuget.org/packages/Fesh/). See hosting examples
[Fesh.Rhino](https://github.com/goswinr/Fesh.Rhino) and [Fesh.Revit](https://github.com/goswinr/Fesh.Revit).

## How to install

### Standalone

Build from the source with

```bash
dotnet build FeshStandalone.fsproj
```
or download the from [Releases](https://github.com/goswinr/Fesh/releases) to any location, **unblock** and run Fesh.exe.

### For hosting in another app
use the nuget package [Fesh](https://www.nuget.org/packages/Fesh/).\
Or, to build the Fesh nuget package run:

```bash
dotnet build FeshHosting.fsproj
```


## Features

### Syntax Highlighting
Initial static syntax highlighting is done via AvalonEdit's regex based highlighting.\
See [SyntaxHighlightingFSharp.xshd](https://github.com/goswinr/Fesh/blob/main/Src/SyntaxHighlightingFSharp.xshd).\
The F# Compiler Service provides additional semantic highlighting.\
If you want different colors go the menu: `About` -> `Open and watch SyntaxHighlighting in VS Code`.\
You wil see any changes upon every save in VS Code.

### Auto complete
Auto complete works on enter and dot, also when typing in the middle of a word.

### Type info
The type info includes the inferred signature.\
All of the xml docs and information about the path of the containing assembly.

### Status bar
The statusbar at the bottom shows compiler error count, click on it to scroll to the first error.

### Selection highlighting
Selected text is highlighted in both code and output window. The count is shown in the status bar.

### Drag and drop
Drag any file or folder into the editor to get the full path as a string at the cursor position.\
For *.dlls it will be at the top and prefixed with `#r`.\
For *.fsx it will be at the top and prefixed with `#load`.

### Font
The default font is [Cascadia Mono](https://github.com/microsoft/cascadia-code).\
Besides italic it also supports a cursive script mode. Fesh is using cursive for comments.\
To enable ligatures set the font to `Cascadia Code`.\
You can change the font in the 'Settings.txt' file at `%APPDATA%\Local\Fesh\Settings\Standalone\Settings.txt`.\
Or via the menu: `About` -> `Open Settings Folder`.

### Changelog
see [CHANGELOG.md](https://github.com/goswinr/Fesh/blob/main/CHANGELOG.md)

### Publishing

or self contained:

```bash
dotnet publish FeshStandalone.fsproj -c release -r win-x64 -o bin/publish/net9 --framework net9.0-windows --self-contained
dotnet publish FeshStandalone.fsproj -c release -r win-x64 -o bin/publish/net9 --framework net9.0-windows
dotnet publish FeshStandalone.fsproj -c release -r win-x64 -o bin/publish/net48 --framework net48
```

the use [Velopack](https://docs.velopack.io/packaging/installer) to create an installer:

```bash
vpk pack --packId Fesh --packVersion 0.15.1 --packDir bin/publish/net9 --outputDir bin/installer/net9 --mainExe Fesh.exe --framework net9.0-x64-desktop --icon Media/logo.ico
```

https://docs.velopack.io/packaging/deltas#disabling-deltas