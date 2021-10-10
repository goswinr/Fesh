namespace Seff.Views

open System
open System.IO
open System.Windows.Media // for color brushes
open System.Text
open System.Windows.Controls
open System.Windows

open AvalonLog.Brush

open Seff
open Seff.Util.General
open Seff.Model
open Seff.Config


module LogColors = 

    let mutable consoleOut    = Brushes.Black             |> freeze // should be same as default  forground. Will be set on foreground changes
    let fsiStdOut     = Brushes.DarkGray |> darker 20     |> freeze // values printet by fsi iteself like "val it = ...."
    let fsiErrorOut   = Brushes.DarkMagenta               |> freeze //are they all caught by evaluate non throwing ? prints "Stopped due to error" on non compiling code
    let consoleError  = Brushes.OrangeRed                 |> freeze // this is used by eprintfn
    let infoMsg       = Brushes.LightSeaGreen             |> freeze
    let fsiErrorMsg   = Brushes.Magenta                   |> freeze
    let appErrorMsg   = Brushes.LightSalmon |> darker 20  |> freeze
    let iOErrorMsg    = Brushes.DarkRed                   |> freeze
    let debugMsg      = Brushes.Green                     |> freeze

    let red           = Brushes.Red                     |> freeze
    let green         = Brushes.Green                   |> freeze
    let blue          = Brushes.Blue                    |> freeze


/// A ReadOnly text AvalonEdit Editor that provides print formating methods
/// call ApplyConfig() once config is set up too, (config depends on this Log instance)
type Log private () = 

    let log =  new AvalonLog.AvalonLog()

    do
        //styling:
        log.BorderThickness <- new Thickness( 0.5)
        log.Padding         <- new Thickness( 0.7)
        log.Margin          <- new Thickness( 0.7)
        log.BorderBrush <- Brushes.Black |> freeze

        log.VerticalScrollBarVisibility <- Controls.ScrollBarVisibility.Auto
        //log.HorizontalScrollBarVisibility <- Controls.ScrollBarVisibility.Auto // set below with word wrap

    let setLineWrap(v)= 
        if v then
            log.WordWrap         <- true
            log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled
        else
            log.WordWrap         <- false
            log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto


    //used in FSI constructor:
    let fsiErrorStream = StringBuilder()

    let textWriterConsoleOut    =  log.GetTextWriter   ( LogColors.consoleOut )
    let textWriterConsoleError  =  log.GetTextWriter   ( LogColors.consoleError)
    let textWriterFsiStdOut     =  log.GetTextWriter   ( LogColors.fsiStdOut )
    let textWriterFsiErrorOut   =  log.GetConditionalTextWriter ( (fun s -> fsiErrorStream.Append(s)|> ignore; true) ,  LogColors.fsiErrorOut) // use filter for side effect


    //-----------------------------------------------------------
    //----------------------members:------------------------------------------
    //------------------------------------------------------------

    member this.AvalonLog = log

    member this.FsiErrorStream = fsiErrorStream  

    member internal this.AdjustToSettingsInConfig(config:Config)= 
        //this.OnPrint.Add (config.AssemblyReferenceStatistic.RecordFromlog) // TODO: does this have print perfomance impact ? measure do async ?
        setLineWrap( config.Settings.GetBool ("logHasLineWrap", true) )
        log.FontSize  <- config.Settings.GetFloat ("FontSize" , Seff.Style.fontSize )

    member this.ToggleLineWrap(config:Config)= 
        let newState = not  log.WordWrap
        setLineWrap newState
        config.Settings.SetBool ("logHasLineWrap", newState)
        config.Settings.Save ()


    member this.Clear() = log.Clear()

    //used in FSI constructor:
    member this.TextWriterFsiStdOut    = textWriterFsiStdOut
    member this.TextWriterFsiErrorOut  = textWriterFsiErrorOut
    member this.TextWriterConsoleOut   = textWriterConsoleOut
    member this.TextWriterConsoleError = textWriterConsoleError

    member this.PrintfnInfoMsg      msg =  log.printfnBrush LogColors.infoMsg    msg
    member this.PrintfnFsiErrorMsg  msg =  log.printfnBrush LogColors.fsiErrorMsg  msg
    member this.PrintfnAppErrorMsg  msg =  log.printfnBrush LogColors.appErrorMsg  msg
    member this.PrintfnIOErrorMsg   msg =  log.printfnBrush LogColors.iOErrorMsg   msg
    member this.PrintfnDebugMsg     msg =  log.printfnBrush LogColors.debugMsg     msg


    /// Prints without adding a new line at the end
    member this.PrintfFsiErrorMsg  msg =  log.printfBrush LogColors.fsiErrorMsg  msg

    /// Change custom color to a RGB value ( each between 0 and 255). Then print
    member this.PrintfnColor red green blue msg =  log.printfnColor red green blue msg

    /// Change custom color to a RGB value ( each between 0 and 255)
    /// Then print without adding a new line at the end
    member this.PrintfColor red green blue msg = log.printfColor red green blue msg


    // ------------------- for use from Seff.Rhino with just a string , no formating: -------------------------------------

    /// Change custom color to a RGB value ( each between 0 and 255)
    /// Then print without adding a new line at the end
    member this.PrintColor red green blue s = log.AppendWithColor (red, green, blue, s)

    /// Change custom color to a RGB value ( each between 0 and 255)
    /// Adds a new line at the end
    member this.PrintnColor red green blue s = log.AppendLineWithColor (red, green, blue, s)


    interface ISeffLog with
        member _.FsiErrorStream         = fsiErrorStream

        //used in FSI constructor:
        member _.TextWriterFsiStdOut    = textWriterFsiStdOut    :> TextWriter
        member _.TextWriterFsiErrorOut  = textWriterFsiErrorOut  :> TextWriter
        member _.TextWriterConsoleOut   = textWriterConsoleOut   :> TextWriter
        member _.TextWriterConsoleError = textWriterConsoleError :> TextWriter

        member this.PrintfnInfoMsg     msg =            this.PrintfnInfoMsg     msg
        member this.PrintfnAppErrorMsg msg =            this.PrintfnAppErrorMsg msg
        member this.PrintfnIOErrorMsg  msg =            this.PrintfnIOErrorMsg  msg
        member this.PrintfnDebugMsg    msg =            this.PrintfnDebugMsg    msg

        member this.PrintfnFsiErrorMsg msg =            this.PrintfnFsiErrorMsg msg
        member this.PrintfFsiErrorMsg msg          = this.PrintfFsiErrorMsg msg

        member this.PrintfnColor red green blue msg =   this.PrintfnColor red green blue msg
        member this.PrintfColor red green blue msg = this.PrintfColor red green blue msg

        member this.Clear() = this.Clear()

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
           dlg.Title <- "Save Seleceted Text from Log Window of " + Style.dialogCaption
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

        // these two wher part of FSI initilizing in the past
        Console.SetOut  (l.TextWriterConsoleOut)   // TODO needed to redirect printfn or coverd by TextWriterFsiStdOut? //https://github.com/fsharp/FSharp.Compiler.Service/issues/201
        Console.SetError(l.TextWriterConsoleError) // TODO needed if evaluate non throwing or coverd by TextWriterFsiErrorOut?
        
        l
   
        (*
        trying to enable Ansi Control sequences for https://github.com/spectreconsole/spectre.console

        but doeant work yet ESC char seam to be swallowed by Console.SetOut to textWriter. see:

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

      
