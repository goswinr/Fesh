namespace Fesh.Views

open System
open System.IO
open Avalonia.Media // for color brushes
open Avalonia.Media.Immutable
open System.Text
open Avalonia.Controls
open Avalonia
open Avalonia.Input
open Avalonia.Controls.Primitives

open AvaloniaEdit
open AvaloniaEdit.Utils
open AvaloniaEdit.Document
open AvaloniaLog.ImmBrush

open Fesh
open Fesh.Editor
open Fesh.Util.General
open Fesh.Model
open Fesh.Config


module LogColors =

    let mutable consoleOut = ImmutableSolidColorBrush Colors.Black              // should be same as default  foreground. Will be set on foreground changes
    let fsiStdOut          = ImmutableSolidColorBrush Colors.DarkGray |> darker 20      // values printed by fsi itself like "val it = ...."
    let fsiErrorOut        = ImmutableSolidColorBrush Colors.DarkMagenta                // are they all caught by evaluate non throwing ? prints "Stopped due to error" on non compiling code
    let consoleError       = ImmutableSolidColorBrush Colors.OrangeRed                  // this is used by eprintfn
    let infoMsg            = ImmutableSolidColorBrush Colors.LightSteelBlue
    let fsiErrorMsg        = ImmutableSolidColorBrush Colors.Magenta
    let appErrorMsg        = ImmutableSolidColorBrush Colors.LightSalmon |> darker 20
    let iOErrorMsg         = ImmutableSolidColorBrush Colors.DarkRed
    let debugMsg           = ImmutableSolidColorBrush Colors.LightSeaGreen
    let runtimeErr         = ImmutableSolidColorBrush Colors.Red  |> darker 55





/// A ReadOnly text AvalonEdit Editor that provides print formatting methods
/// call ApplyConfig() once config is set up too, (config depends on this Log instance)
type Log private () =

    let log =  new AvaloniaLog.AvaloniaLog()

    let mutable addLogger : option<TextWriter> = None

    do
        //styling:
        // log.BorderThickness <- new Thickness 0.5
        // log.Padding         <- new Thickness 0.7
        // log.Margin          <- new Thickness 0.7
        // log.BorderBrush     <- Brushes.Black

        log.VerticalScrollBarVisibility <- ScrollBarVisibility.Auto
        //log.HorizontalScrollBarVisibility <- Controls.ScrollBarVisibility.Auto // set below with word wrap
        log.MaximumCharacterAllowance <- 5_000_000

    let setLineWrap(v)=
        if v then
            log.WordWrap         <- true
            log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled
        else
            log.WordWrap         <- false
            log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto


    //used in FSI constructor:
    let fsiErrorsStringBuilder = StringBuilder()

    let textWriterConsoleOut    =  log.GetTextWriter LogColors.consoleOut
    let textWriterConsoleError  =  log.GetTextWriter LogColors.consoleError
    let textWriterFsiStdOut     =  log.GetTextWriter LogColors.fsiStdOut
    let textWriterFsiErrorOut   =  log.GetConditionalTextWriter ( (fun s -> fsiErrorsStringBuilder.Append s |> ignore; true) ,  LogColors.fsiErrorOut) // use filter for side effect


    /// for an additional textWriter to also write Info, AppError, IOError,Debug and FsiError messages to.
    /// But not any other text printed with any custom color.
    let appendAndLogLn (b:ImmutableSolidColorBrush) (tx:string) =
        log.AppendLineWithBrush (b, tx)
        match addLogger with
        | Some tw -> tw.WriteLine tx
        | None -> ()

    let appendAndLog (b:ImmutableSolidColorBrush) (tx:string) =
        log.AppendWithBrush (b, tx)
        match addLogger with
        | Some tw -> tw.Write tx
        | None -> ()

    let mutable selectionHighlighter: SelectionHighlighterLog option = None

    //-----------------------------------------------------------
    //----------------------members:------------------------------------------
    //------------------------------------------------------------

    /// should always be some
    member _.SelectionHighlighter = selectionHighlighter

    member _.AvaloniaLog = log

    member _.AvaloniaEditLog = log.AvaloniaEdit

    member _.FsiErrorsStringBuilder = fsiErrorsStringBuilder

    member internal _.FinishLogSetup(config:Config)=
        setLineWrap( config.Settings.GetBool ("logHasLineWrap", false) ) // no wrap by default
        log.FontSize  <- config.Settings.GetFloat ("SizeOfFont" , Fesh.StyleState.fontSize )
        let hiLi = new SelectionHighlighterLog(log.AvaloniaEdit)
        // to clear selection highlighter marks first , before opening the search window. if they would be the same as the search word.
        // creating a new command binding for 'ApplicationCommands.Find' would remove the existing one. so we add to the delegate instead
        for binding in log.AvaloniaEdit.TextArea.CommandBindings do if  binding.Command = ApplicationCommands.Find then binding.Executed.Add(fun _ -> hiLi.ClearMarksIfOneSelected())
        selectionHighlighter <- Some hiLi


    member _.ToggleLineWrap(config:Config)=
        let newState = not log.WordWrap
        setLineWrap newState
        config.Settings.SetBool ("logHasLineWrap", newState)
        config.Settings.SaveWithDelay 300

    member _.Clear() = log.Clear()

    //used in FSI constructor:
    member _.TextWriterFsiStdOut    = textWriterFsiStdOut
    member _.TextWriterFsiErrorOut  = textWriterFsiErrorOut
    member _.TextWriterConsoleOut   = textWriterConsoleOut
    member _.TextWriterConsoleError = textWriterConsoleError

    member _.PrintfnRuntimeErr   msg =  Printf.kprintf ( appendAndLogLn LogColors.runtimeErr   ) msg
    member _.PrintfnInfoMsg      msg =  Printf.kprintf ( appendAndLogLn LogColors.infoMsg      ) msg
    member _.PrintfnAppErrorMsg  msg =  Printf.kprintf ( appendAndLogLn LogColors.appErrorMsg  ) msg
    member _.PrintfnIOErrorMsg   msg =  Printf.kprintf ( appendAndLogLn LogColors.iOErrorMsg   ) msg
    member _.PrintfnDebugMsg     msg =  Printf.kprintf ( appendAndLogLn LogColors.debugMsg     ) msg
    member _.PrintfnFsiErrorMsg  msg =  Printf.kprintf ( appendAndLogLn LogColors.fsiErrorMsg  ) msg
    /// Prints without adding a new line at the end
    member _.PrintfFsiErrorMsg   msg =  Printf.kprintf ( appendAndLog   LogColors.fsiErrorMsg  ) msg


    /// Change custom color to a RGB value ( each between 0 and 255). Then print
    member _.PrintfnColor red green blue msg =  log.printfnColor red green blue msg

    /// Change custom color to a RGB value ( each between 0 and 255)
    /// Then print without adding a new line at the end
    member _.PrintfColor red green blue msg = log.printfColor red green blue msg


    // ------------------- for use from Fesh.Rhino with just a string , no formatting: -------------------------------------

    /// Change custom color to a RGB value ( each between 0 and 255)
    /// Then print without adding a new line at the end
    member _.PrintColor red green blue s = log.AppendWithColor (red, green, blue, s)

    /// Change custom color to a RGB value ( each between 0 and 255)
    /// Adds a new line at the end
    member _.PrintnColor red green blue s = log.AppendLineWithColor (red, green, blue, s)

    /// An additional TextWriter to also write Info, AppError, IOError,Debug and FsiError messages to.
    /// But not any other text printed with any custom color.
    member _.AdditionalLogger with get() = addLogger and set l = addLogger <- l

    interface IFeshLog with

        //used in FSI constructor:
        member _.TextWriterFsiStdOut    = textWriterFsiStdOut    :> TextWriter
        member _.TextWriterFsiErrorOut  = textWriterFsiErrorOut  :> TextWriter
        member _.TextWriterConsoleOut   = textWriterConsoleOut   :> TextWriter
        member _.TextWriterConsoleError = textWriterConsoleError :> TextWriter

        member this.PrintfnRuntimeErr  msg = this.PrintfnRuntimeErr   msg
        member this.PrintfnInfoMsg     msg = this.PrintfnInfoMsg     msg
        member this.PrintfnAppErrorMsg msg = this.PrintfnAppErrorMsg msg
        member this.PrintfnIOErrorMsg  msg = this.PrintfnIOErrorMsg  msg
        member this.PrintfnDebugMsg    msg = this.PrintfnDebugMsg    msg

        member this.PrintfnFsiErrorMsg msg = this.PrintfnFsiErrorMsg msg
        member this.PrintfFsiErrorMsg  msg = this.PrintfFsiErrorMsg msg

        member this.PrintfnColor red green blue msg = this.PrintfnColor red green blue msg
        member this.PrintfColor  red green blue msg = this.PrintfColor red green blue msg

        member this.Clear() = this.Clear()

        /// An additional TextWriter to also write Info, AppError, IOError,Debug and FsiError messages to.
        /// But not any other text printed with any custom color.
        member this.AdditionalLogger with get() = addLogger and set l = addLogger <- l

        member this.AvaloniaLog = log

    member this.SaveAllText (_pathHint: FilePath) =
        ()
        // let dlg = new Microsoft.Win32.SaveFileDialog()
        // match pathHint with
        // |NotSet _ ->()
        // |Deleted fi |SetTo fi ->
        //     fi.Refresh()
        //     if fi.Directory.Exists then dlg.InitialDirectory <- fi.DirectoryName
        //     dlg.FileName <- fi.Name + "_Log"
        // dlg.Title <- "Fesh | SaveText from Log Window"
        // dlg.DefaultExt <- ".txt"
        // dlg.Filter <- "Text Files(*.txt)|*.txt|Text Files(*.csv)|*.csv|All Files(*.*)|*"
        // if isTrue (dlg.ShowDialog()) then
        //     try
        //         IO.File.WriteAllText(dlg.FileName, log.Text(), Text.Encoding.UTF8)
        //         this.PrintfnInfoMsg "Log File saved as:\r\n%s" dlg.FileName
        //     with e ->
        //         this.PrintfnIOErrorMsg "Failed to save text from Log at :\r\n%s\r\n%A" dlg.FileName e

    member this.SaveSelectedText (_pathHint: FilePath) =
        ()
        // if log.Selection.Length > 0 then // this check is also done in "canexecute command"
        //    let txt =
        //         log.Selection.Segments
        //         |> Seq.map (fun s -> log.Text(s) ) // to ensure block selection is saved correctly
        //         |> String.concat Environment.NewLine

        //    let dlg = new Microsoft.Win32.SaveFileDialog()
        //    match pathHint with
        //    |NotSet _ ->()
        //    |Deleted fi |SetTo fi ->
        //        fi.Refresh()
        //        if fi.Directory.Exists then dlg.InitialDirectory <- fi.DirectoryName
        //        dlg.FileName <- fi.Name + "_Log"
        //    dlg.Title <- "Fesh | Save Selected Text from Log Window"
        //    dlg.DefaultExt <- ".txt"
        //    dlg.Filter <- "Text Files(*.txt)|*.txt|Text Files(*.csv)|*.csv|All Files(*.*)|*"
        //    if isTrue (dlg.ShowDialog()) then
        //       try
        //            IO.File.WriteAllText(dlg.FileName, txt, Text.Encoding.UTF8)
        //            this.PrintfnInfoMsg "Selected text from Log saved as:\r\n%s" dlg.FileName
        //       with e ->
        //            this.PrintfnIOErrorMsg "Failed to save selected text from Log at :\r\n%s\r\n%A" dlg.FileName e

    //--------------------------------------------------------------------------------------------------------------------------------------------
    //-----------------------------Static members---------------------------------------------------------------------------------------------------------------
    //--------------------------------------------------------------------------------------------------------------------------------------------

    /// creates one instance
    static member Create() =
        let l = Log()
        IFeshLog.log         <- l
        IFeshLog.printColor  <- l.PrintColor
        IFeshLog.printnColor <- l.PrintnColor
        IFeshLog.clear       <- l.Clear

        // these two where part of FSI initializing in the past
        Console.SetOut   l.TextWriterConsoleOut   // TODO needed to redirect printfn or covered by TextWriterFsiStdOut? //https://github.com/fsharp/FSharp.Compiler.Service/issues/201
        Console.SetError l.TextWriterConsoleError // TODO needed if evaluate non throwing or covered by TextWriterFsiErrorOut?
        l

        (*
        trying to enable ANSI Control sequences for https://github.com/spectreconsole/spectre.console

        but doesn't work yet ESC char seam to be swallowed by Console.SetOut to textWriter. see:

        //https://stackoverflow.com/a/34078058/969070
        //let stdout = Console.OpenStandardOutput()
        //let con = new StreamWriter(stdout, Encoding.ASCII)

        The .Net Console.WriteLine uses an internal __ConsoleStream that checks if the Console.Out is as file handle or a console handle.
        By default it uses a console handle and therefor writes to the console by calling WriteConsoleW. In the remarks you find:

        Although an application can use WriteConsole in ANSI mode to write ANSI characters, consoles do not support ANSI escape sequences.
        However, some functions provide equivalent functionality. For more information, see SetCursorPos, SetConsoleTextAttribute, and GetConsoleCursorInfo.

        To write the bytes directly to the console without WriteConsoleW interfering a simple filehandle/stream will do which is achieved by calling OpenStandardOutput.
        By wrapping that stream in a StreamWriter so we can set it again with Console.SetOut we are done. The byte sequences are send to the OutputStream and picked up by ANSI-Con.

        let strWriter = l.AvaloniaLog.GetStreamWriter( LogColors.consoleOut) // Encoding.ASCII ??
        Console.SetOut(strWriter)
        *)


