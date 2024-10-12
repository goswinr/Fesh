![Logo](https://raw.githubusercontent.com/goswinr/Fesh/main/Media/logo128.png)
# Fesh
[![Fesh on nuget.org](https://img.shields.io/nuget/v/Fesh.svg)](https://nuget.org/packages/Fesh)
![code size](https://img.shields.io/github/languages/code-size/goswinr/Fesh.svg)
[![license](https://img.shields.io/github/license/goswinr/Fesh)](LICENSE.md)



Fesh is a Scripting Editor for F# on Windows.

![Screenshot](https://raw.githubusercontent.com/goswinr/Fesh/main/Media/screenshot1.png)

It is based on the excellent [AvalonEdit](https://github.com/goswinr/AvalonEditB).
The editor supports F# 8.0 and has modern IDE features like semantic syntax highlighting, type-info and autocomplete.
Unlike a typical F# REPL this editor has the input and the output in two separate windows.
Just [like Don Syme always wanted it to be](https://github.com/dotnet/fsharp/issues/2161#issuecomment-270465310).
By hosting [FSharp.Compiler.Service](https://www.nuget.org/packages/FSharp.Compiler.Service/43.8.400) in the same process the Editor can easily be hosted in other apps to create an embedded application scripting tool.
This was in fact the primary motivation for creating this editor.
I have used it for scripting in Rhino3D professionally since 2017. Initially I used the Tsunami F# editor [see my talk on Louvre Abu Dhabi](https://www.youtube.com/watch?v=ZY-bvZZZZnE). But it is no longer available.

For hosting there is the nuget package [Fesh](https://www.nuget.org/packages/Fesh/). See hosting examples
[Fesh.Rhino](https://github.com/goswinr/Fesh.Rhino) and [Fesh.Revit](https://github.com/goswinr/Fesh.Revit).


## How to install

### Standalone
Just download the from [Releases](https://github.com/goswinr/Fesh/releases).
to any location, **unblock** and run the Fesh.exe.

Or build from source with:

```bash
dotnet build FeshStandalone.fsproj
```

### for Hosting in another App
use the nuget package [Fesh](https://www.nuget.org/packages/Fesh/).
or, to build the Fesh nuget package run:
```bash
dotnet build FeshHosting.fsproj
```

## Features

### Syntax Highlighting
Initial static syntax highlighting is done via AvalonEdit's regex based highlighting.
See [SyntaxHighlightingFSharp.xshd](https://github.com/goswinr/Fesh/blob/main/Src/SyntaxHighlightingFSharp.xshd).
The F# Compiler Service provides additional semantic highlighting.

### Auto complete
Auto complete works on enter and dot, also in middle of a word.

### Type info
The type info includes the inferred signature.
All of the xml docs and information about the containing assembly

### Status bar
The statusbar at the bottom shows compiler error count, click on it to scroll to the first error.

### Selection highlighting
Selected text is highlighted in both code and output window. The count is shown in the status bar.


## Release notes
`0.11.1`
- fix expiry date

`0.11.0`
- fix VisualLine not collapsed crash

`0.10.0`
- enable DefaultCode in host settings

`0.9.0`
- first public release



