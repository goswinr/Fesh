namespace Seff.Views

open System
open System.IO
open System.Windows.Media // for color brushes
open System.Text
open System.Windows.Controls
open System.Windows

open AvalonLog.Brush

open Seff
open Seff.Editor
open Seff.Util.General
open Seff.Model
open Seff.Config


module LogColors = 

    let mutable consoleOut    = Brushes.Black             |> freeze // should be same as default  foreground. Will be set on foreground changes
    let fsiStdOut     = Brushes.DarkGray |> darker 20     |> freeze // values printed by fsi itself like "val it = ...."
    let fsiErrorOut   = Brushes.DarkMagenta               |> freeze //are they all caught by evaluate non throwing ? prints "Stopped due to error" on non compiling code
    let consoleError  = Brushes.OrangeRed                 |> freeze // this is used by eprintfn
    let infoMsg       = Brushes.LightSeaGreen             |> freeze
    let fsiErrorMsg   = Brushes.Magenta                   |> freeze
    let appErrorMsg   = Brushes.LightSalmon |> darker 20  |> freeze
    let iOErrorMsg    = Brushes.DarkRed                   |> freeze
    let debugMsg      = Brushes.Green                     |> freeze
    let runtimeErr    = Brushes.Red          |> darker 55 |> freeze

    //let red           = Brushes.Red                     |> freeze
    //let green         = Brushes.Green                   |> freeze
    //let blue          = Brushes.Blue                    |> freeze

#nowarn "44" //for obsolete grid.Log.AvalonLog.AvalonEdit

/// A ReadOnly text AvalonEdit Editor that provides print formatting methods
/// call ApplyConfig() once config is set up too, (config depends on this Log instance)
type Log private () = 

    let log =  new AvalonLog.AvalonLog()   

    let mutable addLogger : option<TextWriter> = None

    do
        log.SelectedTextHighLighter.IsEnabled <- false
        
        //styling:
        log.BorderThickness <- new Thickness( 0.5)
        log.Padding         <- new Thickness( 0.7)
        log.Margin          <- new Thickness( 0.7)
        log.BorderBrush <- Brushes.Black |> freeze

        log.VerticalScrollBarVisibility <- Controls.ScrollBarVisibility.Auto
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

    let textWriterConsoleOut    =  log.GetTextWriter   ( LogColors.consoleOut )
    let textWriterConsoleError  =  log.GetTextWriter   ( LogColors.consoleError)
    let textWriterFsiStdOut     =  log.GetTextWriter   ( LogColors.fsiStdOut )
    let textWriterFsiErrorOut   =  log.GetConditionalTextWriter ( (fun s -> fsiErrorsStringBuilder.Append(s)|> ignore; true) ,  LogColors.fsiErrorOut) // use filter for side effect

    
    /// for An additional textwriter to also write Info, AppError, IOError,Debug and FsiError messages to.
    /// But not any other text printed with any custom color. 
    let appendAndLogLn (b:SolidColorBrush) (tx:string) =
        log.AppendLineWithBrush (b, tx)
        match addLogger with 
        | Some tw -> tw.WriteLine tx
        | None ->()
    
    let appendAndLog (b:SolidColorBrush) (tx:string) =
        log.AppendWithBrush (b, tx)
        match addLogger with 
        | Some tw -> tw.Write tx
        | None ->()

    let highlightText = SelectionHighlighting.HiLog.setup(log.AvalonEdit)
    
    //-----------------------------------------------------------
    //----------------------members:------------------------------------------
    //------------------------------------------------------------

    member this.AvalonLog = log

    member this. HighlightText = highlightText

    member this.FsiErrorsStringBuilder = fsiErrorsStringBuilder  

    member internal this.AdjustToSettingsInConfig(config:Config)= 
        //this.OnPrint.Add (config.AssemblyReferenceStatistic.RecordFromlog) // TODO: does this have print performance impact ? measure do async ?
        setLineWrap( config.Settings.GetBool ("logHasLineWrap", true) )
        log.FontSize  <- config.Settings.GetFloat ("FontSize" , Seff.Style.fontSize )

    member this.ToggleLineWrap(config:Config)= 
        let newState = not log.WordWrap
        setLineWrap newState
        config.Settings.SetBool ("logHasLineWrap", newState)
        config.Settings.Save ()

    member this.Clear() = log.Clear()

    //used in FSI constructor:
    member this.TextWriterFsiStdOut    = textWriterFsiStdOut
    member this.TextWriterFsiErrorOut  = textWriterFsiErrorOut
    member this.TextWriterConsoleOut   = textWriterConsoleOut
    member this.TextWriterConsoleError = textWriterConsoleError

    member this.PrintfnRuntimeErr   msg =  Printf.kprintf ( appendAndLogLn LogColors.runtimeErr   ) msg
    member this.PrintfnInfoMsg      msg =  Printf.kprintf ( appendAndLogLn LogColors.infoMsg      ) msg
    member this.PrintfnAppErrorMsg  msg =  Printf.kprintf ( appendAndLogLn LogColors.appErrorMsg  ) msg
    member this.PrintfnIOErrorMsg   msg =  Printf.kprintf ( appendAndLogLn LogColors.iOErrorMsg   ) msg
    member this.PrintfnDebugMsg     msg =  Printf.kprintf ( appendAndLogLn LogColors.debugMsg     ) msg
    member this.PrintfnFsiErrorMsg  msg =  Printf.kprintf ( appendAndLogLn LogColors.fsiErrorMsg  ) msg                                                                                                    
    /// Prints without adding a new line at the end                                                 
    member this.PrintfFsiErrorMsg   msg =  Printf.kprintf ( appendAndLog   LogColors.fsiErrorMsg  ) msg


    /// Change custom color to a RGB value ( each between 0 and 255). Then print
    member this.PrintfnColor red green blue msg =  log.printfnColor red green blue msg

    /// Change custom color to a RGB value ( each between 0 and 255)
    /// Then print without adding a new line at the end
    member this.PrintfColor red green blue msg = log.printfColor red green blue msg


    // ------------------- for use from Seff.Rhino with just a string , no formatting: -------------------------------------

    /// Change custom color to a RGB value ( each between 0 and 255)
    /// Then print without adding a new line at the end
    member this.PrintColor red green blue s = log.AppendWithColor (red, green, blue, s)

    /// Change custom color to a RGB value ( each between 0 and 255)
    /// Adds a new line at the end
    member this.PrintnColor red green blue s = log.AppendLineWithColor (red, green, blue, s)

    /// An additional textwriter to also write Info, AppError, IOError,Debug and FsiError messages to.
    /// But not any other text printed with any custom color. 
    member this.AdditionalLogger with get() = addLogger and set l = addLogger <- l

    interface ISeffLog with
        
        //used in FSI constructor:
        member _.TextWriterFsiStdOut    = textWriterFsiStdOut    :> TextWriter
        member _.TextWriterFsiErrorOut  = textWriterFsiErrorOut  :> TextWriter
        member _.TextWriterConsoleOut   = textWriterConsoleOut   :> TextWriter
        member _.TextWriterConsoleError = textWriterConsoleError :> TextWriter

        member this.PrintfnRuntimeErr  msg =  this.PrintfnRuntimeErr   msg 
        member this.PrintfnInfoMsg     msg =   this.PrintfnInfoMsg     msg
        member this.PrintfnAppErrorMsg msg =   this.PrintfnAppErrorMsg msg
        member this.PrintfnIOErrorMsg  msg =   this.PrintfnIOErrorMsg  msg
        member this.PrintfnDebugMsg    msg =   this.PrintfnDebugMsg    msg

        member this.PrintfnFsiErrorMsg msg =   this.PrintfnFsiErrorMsg msg
        member this.PrintfFsiErrorMsg  msg =   this.PrintfFsiErrorMsg msg

        member this.PrintfnColor red green blue msg = this.PrintfnColor red green blue msg
        member this.PrintfColor  red green blue msg = this.PrintfColor red green blue msg

        member this.Clear() = this.Clear()

        /// An additional textwriter to also write Info, AppError, IOError,Debug and FsiError messages to.
        /// But not any other text printed with any custom color. 
        member this.AdditionalLogger with get() = addLogger and set l = addLogger <- l

    member this.SaveAllText (pathHint: FilePath) = 
        let dlg = new Microsoft.Win32.SaveFileDialog()
        match pathHint with
        |NotSet ->()
        |SetTo fi ->
            fi.Refresh()
            if fi.Directory.Exists then dlg.InitialDirectory <- fi.DirectoryName
            dlg.FileName <- fi.Name + "_Log"
        dlg.Title <- "SaveText from Log Window of " + Style.dialogCaption
        dlg.DefaultExt <- ".txt"
        dlg.Filter <- "Text Files(*.txt)|*.txt|Text Files(*.csv)|*.csv|All Files(*.*)|*"
        if isTrue (dlg.ShowDialog()) then
            try
                IO.File.WriteAllText(dlg.FileName, log.Text(), Text.Encoding.UTF8)
                this.PrintfnInfoMsg "Log File saved as:\r\n%s" dlg.FileName
            with e ->
                this.PrintfnIOErrorMsg "Failed to save text from Log at :\r\n%s\r\n%A" dlg.FileName e

    member this.SaveSelectedText (pathHint: FilePath) = 
        if log.Selection.Length > 0 then // this check is also done in "canexecute command"
           let txt = 
                log.Selection.Segments
                |> Seq.map (fun s -> log.Text(s) ) // to ensure block selection is saved correctly
                |> String.concat Environment.NewLine

           let dlg = new Microsoft.Win32.SaveFileDialog()
           match pathHint with
           |NotSet ->()
           |SetTo fi ->
               fi.Refresh()
               if fi.Directory.Exists then dlg.InitialDirectory <- fi.DirectoryName
               dlg.FileName <- fi.Name + "_Log"
           dlg.Title <- "Save Selected Text from Log Window of " + Style.dialogCaption
           dlg.DefaultExt <- ".txt"
           dlg.Filter <- "Text Files(*.txt)|*.txt|Text Files(*.csv)|*.csv|All Files(*.*)|*"
           if isTrue (dlg.ShowDialog()) then
              try
                   IO.File.WriteAllText(dlg.FileName, txt, Text.Encoding.UTF8)
                   this.PrintfnInfoMsg "Selected text from Log saved as:\r\n%s" dlg.FileName
              with e ->
                   this.PrintfnIOErrorMsg "Failed to save selected text from Log at :\r\n%s\r\n%A" dlg.FileName e

    //--------------------------------------------------------------------------------------------------------------------------------------------
    //-----------------------------Static members---------------------------------------------------------------------------------------------------------------
    //--------------------------------------------------------------------------------------------------------------------------------------------

    /// creates one instance
    static member Create() = 
        let l = Log()
        ISeffLog.log         <- l
        ISeffLog.printColor  <- l.PrintColor
        ISeffLog.printnColor <- l.PrintnColor
        ISeffLog.clear       <- l.Clear

        // these two where part of FSI initializing in the past
        Console.SetOut  (l.TextWriterConsoleOut)   // TODO needed to redirect printfn or covered by TextWriterFsiStdOut? //https://github.com/fsharp/FSharp.Compiler.Service/issues/201
        Console.SetError(l.TextWriterConsoleError) // TODO needed if evaluate non throwing or covered by TextWriterFsiErrorOut?
        
        l
   
        (*
        trying to enable Ansi Control sequences for https://github.com/spectreconsole/spectre.console

        but doesn't work yet ESC char seam to be swallowed by Console.SetOut to textWriter. see:

        //https://stackoverflow.com/a/34078058/969070
        //let stdout = Console.OpenStandardOutput()
        //let con = new StreamWriter(stdout, Encoding.ASCII)      
        
        The .Net Console.WriteLine uses an internal __ConsoleStream that checks if the Console.Out is as file handle or a console handle. 
        By default it uses a console handle and therefor writes to the console by calling WriteConsoleW. In the remarks you find:
        
        Although an application can use WriteConsole in ANSI mode to write ANSI characters, consoles do not support ANSI escape sequences. 
        However, some functions provide equivalent functionality. For more information, see SetCursorPos, SetConsoleTextAttribute, and GetConsoleCursorInfo.
        
        To write the bytes directly to the console without WriteConsoleW interfering a simple filehandle/stream will do which is achieved by calling OpenStandardOutput. 
        By wrapping that stream in a StreamWriter so we can set it again with Console.SetOut we are done. The byte sequences are send to the OutputStream and picked up by AnsiCon.
  
        let strWriter = l.AvalonLog.GetStreamWriter( LogColors.consoleOut) // Encoding.ASCII ??  
        Console.SetOut(strWriter)
        *)

      
