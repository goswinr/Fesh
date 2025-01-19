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
The editor supports F# 9.0 and has modern IDE features like semantic syntax highlighting, type-info and autocomplete.\
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

## How to install the  Standalone App

### Installer

The recommended way is to run the Setup.exe from [Releases](https://github.com/goswinr/Fesh/releases).\
Fesh will automatically offer to update itself when a new version is available.\
There is a .NET 9 and a .NET 4.8 version. Apart from the runtime they are the same.

The installer is created with [Velopack](https://velopack.io) and digitally signed.

No admin rights are required to install or run the app.\
The app will be installed in `\AppData\Local\Fesh`.\
A shortcut will be created on the desktop.

There is also a portable package in each release.\
Extract that zip and place it wherever you'd like.\
It has identical functionality to the installed app,\
but keeps it's Settings folder in the same directory.\
There is a ".portable" file in the folder to indicate that.\
It will also automatically offer to update itself when a new version is available.


###  Build from source

Build from the source with:

```bash
dotnet build FeshStandalone.fsproj
```
You will still get notifications about new releases, but you have to install them manually.


## How to host Fesh in another app

Use the nuget package [Fesh](https://www.nuget.org/packages/Fesh/).\
Or, to build the Fesh nuget package run:

```bash
dotnet build FeshHosting.fsproj
```


## Release Notes
For changes in each release see the  [CHANGELOG.md](https://github.com/goswinr/Fesh/blob/main/CHANGELOG.md)

## License
Fesh is licensed under the [MIT License](https://github.com/goswinr/Fesh/blob/main/LICENSE.md).