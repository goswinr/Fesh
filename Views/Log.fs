namespace Seff.Views

open Seff
open Seff.Config
open System
open System.Environment
open System.IO
open System.Threading
open ICSharpCode
open System.Windows.Media // for color brushes
open System.Text
open System.Diagnostics
open System.Collections.Generic
open System.Windows.Controls



type LogMessageType = 
    | FsiStdOut 
    | FsiErrorOut 
    | ConsoleOut
    | ConsoleError
    | InfoMsg 
    | FsiErrorMsg 
    | AppErrorMsg 
    | IOErrorMsg 
    | DebugMsg 

    static member getColor = function
        | FsiStdOut     ->Brushes.DarkGray |> Util.darker 20 // values printet by fsi iteself like "val it = ...."
        | FsiErrorOut   ->Brushes.DarkMagenta //are they all caught by evaluate non throwing ?
        | ConsoleOut    ->Brushes.Yellow // default black forground is used ; never used should  // the out from printfn
        | ConsoleError  ->Brushes.LightSalmon // this is never used, only FsiErrorOut is used?
        | InfoMsg       ->Brushes.Blue |> Util.brighter 20 
        | FsiErrorMsg   ->Brushes.Red
        | AppErrorMsg   ->Brushes.DarkOrange
        | IOErrorMsg    ->Brushes.DarkRed
        | DebugMsg      ->Brushes.Green
        


/// A TextWriter that writes using a function (to an Avalonedit Control). used in FSI session constructor   
type FsxTextWriter(writeStr) =
    inherit TextWriter()
    override this.Encoding =  Text.Encoding.Default
    override this.Write     (s:string)  = writeStr (s)
    override this.WriteLine (s:string)  = writeStr (s + NewLine)    // actually never used see  https://github.com/dotnet/fsharp/issues/3712   
    override this.WriteLine ()          = writeStr (    NewLine)    
    
    

type LogLineColorizer(editor:AvalonEdit.TextEditor, lineColors:Collections.Generic.Dictionary<int,SolidColorBrush>) = 
    inherit AvalonEdit.Rendering.DocumentColorizingTransformer()
        
    /// This gets called for every visvble line on any view change
    override this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) =       
        let ok,color = lineColors.TryGetValue(line.LineNumber) // consoleOut line are missing in dict so skiped
        if ok && not line.IsDeleted then                
            // consider selection and exclude fom higlighting:
            let st=line.Offset
            let en=line.EndOffset
            let selLen = editor.SelectionLength
            if selLen < 1 then // no selection 
                base.ChangeLinePart(st,en, fun element -> element.TextRunProperties.SetForegroundBrush(color)) // highlight full line
            else
                let selSt = editor.SelectionStart
                let selEn = selSt + selLen
                if selSt > en || selEn < st then // nothing slected on this line
                    base.ChangeLinePart(st,en, fun element -> element.TextRunProperties.SetForegroundBrush(color)) // highlight full line
                else
                    // consider block or rectangle selection:
                    for seg in editor.TextArea.Selection.Segments do
                        if st < seg.StartOffset && seg.StartOffset < en then base.ChangeLinePart(st, seg.StartOffset, fun element -> element.TextRunProperties.SetForegroundBrush(color))
                        if en > seg.EndOffset   && seg.EndOffset   > st then base.ChangeLinePart(seg.EndOffset,   en, fun element -> element.TextRunProperties.SetForegroundBrush(color))
    


/// A ReadOnly text AvalonEdit Editor that provides print formating methods 
/// call ApplyConfig() once config is set up too, (config depends on this Log instance)
type Log () =    
    // just using a let value  like (let log = new LogView()) has some bugs in hosted context (Rhino), I think due to late initalizing
    // so here is a class with explicit init

    let mutable log : AvalonEdit.TextEditor = null 
    let printCallsCounter = ref 0L
    let mutable prevMsgType = IOErrorMsg
    let stopWatch = Stopwatch.StartNew()
    let buffer =  new StringBuilder()
    let textAddEv = new Event<string> ()

    /// Dictionary holding the color of all non standart lines
    let lineColors = new Collections.Generic.Dictionary<int,SolidColorBrush>() 

    // The below functions are trying to work around double UI update in printfn for better UI performance, and the poor performance of log.ScrollToEnd().
    // see  https://github.com/dotnet/fsharp/issues/3712   
    let printFromBufferAndScroll(ty:LogMessageType) =             
        stopWatch.Restart()
        
        let txt = buffer.ToString()//.Replace(NewLine, sprintf "(%A)%s" ty NewLine)  //for DEBUG only           
        buffer.Clear()  |> ignore 
        let start = log.Document.TextLength
        log.AppendText(txt)
        let mutable line = log.Document.GetLineByOffset(start) 
        if ty = ConsoleOut then //exclude default print color, it should be same as foreground anyway                
            lineColors.Remove line.LineNumber |> ignore // claer any color that might exit from printing on same line before ( maybe just a return)
        else                
            //log.Document.Insert( line.EndOffset, sprintf "(%d:%A)" line.LineNumber ty) //for DEBUG only
            //log.AppendText(sprintf "(1st Line %d, %d chars:%A)" line.LineNumber line.Length ty) //for DEBUG only
            lineColors.[line.LineNumber] <- LogMessageType.getColor(ty) 
            line <- line.NextLine                    
            while line <> null  do
                if line.Length>0 then // to exclude empty lines 
                    //log.Document.Insert( line.EndOffset, sprintf "(%d:%A)" line.LineNumber ty) //for DEBUG only
                    //log.AppendText(sprintf "(Line %d, %d chars:%A)" line.LineNumber line.Length ty)//for DEBUG only
                    lineColors.[line.LineNumber] <- LogMessageType.getColor(ty)
                line <- line.NextLine

        //log.AppendText("|-scroll->") //for DEBUG only
        log.ScrollToEnd()
        


    /// adds string on UI thread  every 150ms then scrolls to end after 300ms
    /// sets line color on LineColors dictionay for DocumentColorizingTransformer
    let printOrBuffer (s:string,ty:LogMessageType) =
        async {
            do! Async.SwitchToContext Sync.syncContext 
            if prevMsgType<>ty then // print case 1, color change, do before append new string
                printFromBufferAndScroll(prevMsgType) 
                prevMsgType <- ty

            buffer.Append(s)  |> ignore 
            textAddEv.Trigger(s)

            if stopWatch.ElapsedMilliseconds > 150L  && s.Contains(NewLine) then // print case 2, only add to document every 150ms
                printFromBufferAndScroll(ty)                
            else                        
                let k = Interlocked.Increment printCallsCounter
                do! Async.Sleep 300
                if !printCallsCounter = k  then //print case 3, it is the last call for 300 ms
                    printFromBufferAndScroll(ty)
                
        } |> Async.StartImmediate 
    
    let setLineWrap(v)=
        if v then 
            log.WordWrap         <- true 
            log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled 
        else
            log.WordWrap         <- false 
            log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto 
    
    
    do    
        //styling: 
        log <- new AvalonEdit.TextEditor()
        log.IsReadOnly <- true
        log.Encoding <- Text.Encoding.Default
        log.ShowLineNumbers  <- true
        log.Options.EnableHyperlinks <- true 
        log.TextArea.SelectionCornerRadius <- 0.0 
        log.TextArea.SelectionBorder <- null         
        log.TextArea.TextView.LinkTextForegroundBrush <- Brushes.Blue //Hyperlinks color        
        log.TextArea.TextView.LineTransformers.Add(new LogLineColorizer(log,lineColors))
        AvalonEdit.Search.SearchPanel.Install(log) |> ignore //TODO disable search and replace ?
   

    //used in FSI constructor:
    let textWriterFsiStdOut     = new FsxTextWriter(fun s -> printOrBuffer (s,FsiStdOut    ))
    let textWriterFsiErrorOut   = new FsxTextWriter(fun s -> printOrBuffer (s,FsiErrorOut  ))
    let textWriterConsoleOut    = new FsxTextWriter(fun s -> printOrBuffer (s,ConsoleOut   ))
    let textWriterConsoleError  = new FsxTextWriter(fun s -> printOrBuffer (s,ConsoleError ))
    
    // used for printf formaters:                                          
    let textWriterInfoMsg       = new FsxTextWriter(fun s -> printOrBuffer (s,InfoMsg      ))
    let textWriterFsiErrorMsg   = new FsxTextWriter(fun s -> printOrBuffer (s,FsiErrorMsg  ))
    let textWriterAppErrorMsg   = new FsxTextWriter(fun s -> printOrBuffer (s,AppErrorMsg  ))
    let textWriterIOErrorMsg    = new FsxTextWriter(fun s -> printOrBuffer (s,IOErrorMsg   ))
    let textWriterDebugMsg      = new FsxTextWriter(fun s -> printOrBuffer (s,DebugMsg     ))
    
    //----------------------members:------------------------------------------    
    
    /// this event occures on ever call to print, NOT on the aggregated strings that are appened to Log
    [<CLIEvent>]
    member this.OnPrint = textAddEv.Publish
       
    member this.AdjustToSettingsInConfig(config:Config)=        
        this.OnPrint.Add (config.AssemblyReferenceStatistic.RecordFromlog) // TODO: does this have print perfomance impact ? measure do async ?
        setLineWrap( config.Settings.GetBool "logHasLineWrap" true )
        log.FontFamily       <- Seff.Appearance.font
        log.FontSize         <- config.Settings.GetFloat "FontSize" Seff.Appearance.fontSize                
        
    member this.ToggleLineWrap(config:Config)=
        let newState = not  log.WordWrap 
        setLineWrap newState
        config.Settings.SetBool "logHasLineWrap" newState
        config.Settings.Save ()
    
    /// to acces the underlying read-only Avalonedit Texteditor
    member this.ReadOnlyEditor = log
        
    //used in FSI constructor:
    member this.TextWriterFsiStdOut    = textWriterFsiStdOut    
    member this.TextWriterFsiErrorOut  = textWriterFsiErrorOut  
    member this.TextWriterConsoleOut   = textWriterConsoleOut   
    member this.TextWriterConsoleError = textWriterConsoleError 

    member this.PrintInfoMsg      s =  Printf.fprintfn textWriterInfoMsg      s
    member this.PrintFsiErrorMsg  s =  Printf.fprintfn textWriterFsiErrorMsg  s
    member this.PrintAppErrorMsg  s =  Printf.fprintfn textWriterAppErrorMsg  s
    member this.PrintIOErrorMsg   s =  Printf.fprintfn textWriterIOErrorMsg   s        
    member this.PrintDebugMsg     s =  Printf.fprintfn textWriterDebugMsg     s

    interface Seff.ISeffLog with        
        member this.ReadOnlyEditor         = log
        //used in FSI constructor:
        member this.TextWriterFsiStdOut    = textWriterFsiStdOut    :> TextWriter   
        member this.TextWriterFsiErrorOut  = textWriterFsiErrorOut  :> TextWriter   
        member this.TextWriterConsoleOut   = textWriterConsoleOut   :> TextWriter   
        member this.TextWriterConsoleError = textWriterConsoleError :> TextWriter   
        
        member this.PrintInfoMsg     s = Printf.fprintfn textWriterInfoMsg      s
        member this.PrintFsiErrorMsg s = Printf.fprintfn textWriterFsiErrorMsg  s
        member this.PrintAppErrorMsg s = Printf.fprintfn textWriterAppErrorMsg  s
        member this.PrintIOErrorMsg  s = Printf.fprintfn textWriterIOErrorMsg   s 
        member this.PrintDebugMsg    s = Printf.fprintfn textWriterDebugMsg     s

    
    member this.SaveAllText (pathHint: FileInfo Option) = 
        let dlg = new Microsoft.Win32.SaveFileDialog()
        if pathHint.IsSome && pathHint.Value.Directory.Exists then dlg.InitialDirectory <- pathHint.Value.DirectoryName
        if pathHint.IsSome then dlg.FileName <- pathHint.Value.Name  + "_Log" 
        dlg.Title <- "SaveText from Log Window of " + Appearance.dialogCaption
        dlg.DefaultExt <- ".txt"
        dlg.Filter <- "Text Files(*.txt)|*.txt|Text Files(*.csv)|*.csv|All Files(*.*)|*"
        if Util.isTrue (dlg.ShowDialog()) then                
            try
                log.Save dlg.FileName
                this.PrintInfoMsg "Log File saved as:\r\n%s" dlg.FileName
            with e -> 
                this.PrintIOErrorMsg "Failed to save text from Log at :\r\n%s\r\n%A" dlg.FileName e
    
    member this.SaveSelectedText (pathHint: FileInfo Option) = 
        if log.SelectedText.Length > 0 then // this check is also done in "canexecute command"
           let dlg = new Microsoft.Win32.SaveFileDialog()
           if pathHint.IsSome && pathHint.Value.Directory.Exists then dlg.InitialDirectory <- pathHint.Value.DirectoryName
           if pathHint.IsSome then dlg.FileName <- pathHint.Value.Name  + "_Log" 
           dlg.Title <- "Save Seleceted Text from Log Window of " + Appearance.dialogCaption
           dlg.DefaultExt <- ".txt"
           dlg.Filter <- "Text Files(*.txt)|*.txt|Text Files(*.csv)|*.csv|All Files(*.*)|*"
           if Util.isTrue (dlg.ShowDialog()) then                
              try 
                   IO.File.WriteAllText(dlg.FileName, log.SelectedText) 
                   this.PrintInfoMsg "Selected text from Log saved as:\r\n%s" dlg.FileName
              with e -> 
                   this.PrintIOErrorMsg "Failed to save selected text from Log at :\r\n%s\r\n%A" dlg.FileName e