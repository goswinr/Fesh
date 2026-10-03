namespace AvaloniaLog

open AvaloniaLog.Util
open AvaloniaLog.ImmBrush
open System
open System.IO
open System.Threading
open AvaloniaEdit
open Avalonia.Media // for color brushes
open System.Text
open System.Diagnostics
open Avalonia.Controls
open AvaloniaEdit.Document


/// A TextWriter that writes using a function.
/// To set Console.Out to a text writer get one via AvaloniaLog.GetTextWriter(red,green,blue)
type LogTextWriter(write:string->unit, writeLine:string->unit) =
    inherit TextWriter()
    override _.Encoding = Text.Encoding.Default // ( UTF-16 )

    override _.Write (s:string)  =
        //if s.Contains "\u001b" then  write ("esc"+s) else write ("?"+s) //debugging for using  spectre ?? https://github.com/spectreconsole/spectre.console/discussions/573
        write s

    override _.WriteLine (s:string)  = // actually never used in F# printfn, but maybe buy other too using the console or error out , see  https://github.com/dotnet/fsharp/issues/3712
        //if s.Contains "\u001b" then  writeLine ("eSc"+s) else writeLine ("?"+s)
        writeLine s

    override _.WriteLine () =
        writeLine ""


    (*
       trying to enable ANSI Control sequences for https://github.com/spectreconsole/spectre.console

       but doesn't work yet ESC char seams to be swallowed by Console.SetOut to textWriter. see:

       //https://stackoverflow.com/a/34078058/969070
       //let stdout = Console.OpenStandardOutput()
       //let con = new StreamWriter(stdout, Encoding.ASCII)

       The .Net Console.WriteLine uses an internal __ConsoleStream that checks if the Console.Out is as file handle or a console handle.
       By default it uses a console handle and therefor writes to the console by calling WriteConsoleW. In the remarks you find:

       Although an application can use WriteConsole in ANSI mode to write ANSI characters, consoles do not support ANSI escape sequences.
       However, some functions provide equivalent functionality. For more information, see SetCursorPos, SetConsoleTextAttribute, and GetConsoleCursorInfo.

       To write the bytes directly to the console without WriteConsoleW interfering a simple file-handle/stream will do which is achieved by calling OpenStandardOutput.
       By wrapping that stream in a StreamWriter so we can set it again with Console.SetOut we are done. The byte sequences are send to the OutputStream and picked up by AnsiCon.

       let strWriter = l.AvaloniaLog.GetStreamWriter( LogColors.consoleOut) // Encoding.ASCII ??
       Console.SetOut(strWriter)

// A TextWriter that writes using a function.
// To set Console.Out to a text writer get one via AvaloniaLog.GetTextWriter(red,green,blue)
type LogStreamWriter(ms:MemoryStream,write,writeLine) =
    inherit StreamWriter(ms)
    override _.Encoding = Text.Encoding.Default // ( UTF-16 )
    override _.Write (s:string) :  unit =
        if s.Contains "\u001b" then  write ("esc"+s) else write ("?"+s) //use specter ?? https://github.com/spectreconsole/spectre.console/discussions/573
        //write (s)
    override _.WriteLine (s:string)  :  unit = // actually never used in F# printfn, but maybe buy other too using the console or error out , see  https://github.com/dotnet/fsharp/issues/3712
        if s.Contains "\u001b" then  writeLine ("eSc"+s) else writeLine ("?"+s)
        //writeLine (s)
    override _.WriteLine ()          = writeLine ("")
       *)



