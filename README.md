# Fesh


![code size](https://img.shields.io/github/languages/code-size/goswinr/Fesh.svg)
[![license](https://img.shields.io/github/license/goswinr/Fesh)](LICENSE)

![Logo](https://raw.githubusercontent.com/goswinr/Fesh/main/Doc/logo128.png)


Fesh is a Scripting Editor for F# on Windows.
![Screenshot](https://raw.githubusercontent.com/goswinr/Fesh/main/Doc/screenshot1.png)

The Editor supports modern IDE features like semantic syntax highlighting, typ info and autocomplete.
Unlike a typical F# REPL this editor has the input and the output in two separate windows.
Just [like Don Syme always wanted it to be](https://github.com/dotnet/fsharp/issues/2161#issuecomment-270465310).
The Editor can easily be hosted in other apps to create an application scripting tool.
This was in fact the primary motivation for creating this editor.
see the nuget package [![Fesh on nuget.org](https://img.shields.io/nuget/v/Fesh.svg)](https://www.nuget.org/packages/Fesh/)
and [Fesh.Rhino](https://github.com/goswinr/Fesh.Rhino) as an example.

## how to install
just download to any location
unblock and run

or build from source

## Features

# Syntax Highlighting
 xshd files

# Auto complete
 on enter and dot
 also in middle of word

#type info including full xml doc, ( some missing in Vs and code)

#status bar click to go error

#evaluation tracking

#colorful output

#selection highlighting



#script compiler
 to dll

It's design to be lightweight and fast.
It's interfacing directly with the F# Compiler service
for type info and autocompletion.
I initially created it in 2016 to host FSharp scripting in Rhino3D

