namespace Seff.Views

open Seff
open Seff.Util.General
open Seff.Util
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
open System.Windows



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
        | FsiStdOut     ->Brushes.DarkGray |> Util.General.darker 20 // values printet by fsi iteself like "val it = ...."
        | FsiErrorOut   ->Brushes.DarkMagenta //are they all caught by evaluate non throwing ?
        | ConsoleOut    ->Brushes.Yellow // default black forground is used ; never used should  // the out from printfn
        | ConsoleError  ->Brushes.OrangeRed // this is used by eprintfn 
        | InfoMsg       ->Brushes.LightSeaGreen
        | FsiErrorMsg   ->Brushes.Red
        | AppErrorMsg   ->Brushes.LightSalmon
        | IOErrorMsg    ->Brushes.DarkRed
        | DebugMsg      ->Brushes.Green
        

[<Struct>]
type ColorFromOffset = 
    {off: int; brush: SolidColorBrush}
    
    /// does binary search
    static member findCurrentInList (cs:ResizeArray<ColorFromOffset>) off = 
        let last = cs.Count-1

        let rec find lo hi =             
            let mid = lo + (hi - lo) / 2          //TODO test edge conditions !!  
            if cs.[mid].off <= off then 
                if mid = last             then cs.[mid] // exit
                elif cs.[mid+1].off > off then cs.[mid] // exit
                else find (mid+1) hi
            else
                     find lo (mid-1)
        
        find 0 last


/// A TextWriter that writes using a function (to an Avalonedit Control). used in FSI session constructor   
type FsxTextWriter(writeStr) =
    inherit TextWriter()
    override this.Encoding =  Text.Encoding.Default
    override this.Write     (s:string)  = writeStr (s)
    override this.WriteLine (s:string)  = writeStr (s + NewLine)    // actually never used see  https://github.com/dotnet/fsharp/issues/3712   
    override this.WriteLine ()          = writeStr (    NewLine)    
    
    

type LogLineColorizer(lg:AvalonEdit.TextEditor, lineColors:Collections.Generic.Dictionary<int,SolidColorBrush>) = 
    inherit AvalonEdit.Rendering.DocumentColorizingTransformer()
        
    /// This gets called for every visvble line on any view change
    override this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) =       
        let ok,color = lineColors.TryGetValue(line.LineNumber) // consoleOut line are missing in dict so skiped
        if ok && not line.IsDeleted then                
            // consider selection and exclude fom higlighting:
            let st=line.Offset
            let en=line.EndOffset
            let selLen = lg.SelectionLength
            if selLen < 1 then // no selection 
                base.ChangeLinePart(st,en, fun element -> element.TextRunProperties.SetForegroundBrush(color)) // highlight full line
            else
                let selSt = lg.SelectionStart
                let selEn = selSt + selLen
                if selSt > en || selEn < st then // nothing slected on this line
                    base.ChangeLinePart(st,en, fun element -> element.TextRunProperties.SetForegroundBrush(color)) // highlight full line
                else
                    // consider block or rectangle selection:
                    for seg in lg.TextArea.Selection.Segments do
                        if st < seg.StartOffset && seg.StartOffset < en then base.ChangeLinePart(st, seg.StartOffset, fun element -> element.TextRunProperties.SetForegroundBrush(color))
                        if en > seg.EndOffset   && seg.EndOffset   > st then base.ChangeLinePart(seg.EndOffset,   en, fun element -> element.TextRunProperties.SetForegroundBrush(color))
    


    
/// Highlight-all-occurrences-of-selected-text in Log Text View
type LogSelectedTextHighlighter (lg:AvalonEdit.TextEditor) = 
    inherit AvalonEdit.Rendering.DocumentColorizingTransformer()    
    
    let mutable highTxt = null
    let mutable curSelStart = -1
    let highlightChangedEv  = new Event<string*int>()

    let colorHighlight =      Brushes.Blue |> brighter 210    
    
    [<CLIEvent>]
    member this.HighlightChanged = highlightChangedEv.Publish
    member this.ColorHighlight = colorHighlight
    
    //member this.HighlightText  with get() = highTxt and set v = highTxt <- v
    //member this.CurrentSelectionStart  with get() = curSelStart and set v = curSelStart <- v
    
    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) =       
        //  from https://stackoverflow.com/questions/9223674/highlight-all-occurrences-of-selected-word-in-avalonedit
            
        if notNull highTxt  then             
    
            let  lineStartOffset = line.Offset;
            let  text = lg.Document.GetText(line)            
            let mutable  index = text.IndexOf(highTxt, 0, StringComparison.Ordinal)
    
            while index >= 0 do      
                let st = lineStartOffset + index  // startOffset
                let en = lineStartOffset + index + highTxt.Length // endOffset   
    
                if curSelStart <> st  then // skip the actual current selection
                    base.ChangeLinePart( st,en, fun el -> el.TextRunProperties.SetBackgroundBrush(colorHighlight))
                let start = index + highTxt.Length // search for next occurrence // TODO or just +1 ???????
                index <- text.IndexOf(highTxt, start, StringComparison.Ordinal)
        
    member this.SelectionChangedDelegate ( a:EventArgs) =
        // for text view:
        let selTxt = lg.SelectedText            
        let checkTx = selTxt.Trim()
        let doHighlight = 
            checkTx.Length > 1 // minimum 2 non whitecpace characters?
            && not <| selTxt.Contains("\n")  //no line beaks          
            && not <| selTxt.Contains("\r")  //no line beaks
            //&& config.Settings.SelectAllOccurences
            
        if doHighlight then 
            highTxt <- selTxt
            curSelStart <- lg.SelectionStart
            lg.TextArea.TextView.Redraw()
        
            // for status bar : 
            let doc = lg.Document // get in sync first !
            async{
                let tx = doc.CreateSnapshot().Text
                let mutable  index = tx.IndexOf(selTxt, 0, StringComparison.Ordinal)                
                let mutable k = 0
                let mutable anyInFolding = false
                while index >= 0 do        
                    k <- k+1 
                                
                    let st =  index + selTxt.Length // endOffset // TODO or just +1 ???????
                    if st >= tx.Length then 
                        index <- -99
                        //eprintfn "index  %d in %d ??" st tx.Length    
                    else
                        index <- tx.IndexOf(selTxt, st, StringComparison.Ordinal)
                                   
                do! Async.SwitchToContext Sync.syncContext
                highlightChangedEv.Trigger(selTxt, k  )    // will update status bar 
                }   |> Async.Start
    
        else
            if notNull highTxt then // to ony redraw if it was not null before
                highTxt <- null
                lg.TextArea.TextView.Redraw() // to clear highlight
    
        
    
    

/// A ReadOnly text AvalonEdit Editor that provides print formating methods 
/// call ApplyConfig() once config is set up too, (config depends on this Log instance)
type Log () =    
    
    /// Dictionary holding the color of all non standart lines
    let lineColors = new Collections.Generic.Dictionary<int,SolidColorBrush>() 
    let offsetColors = ResizeArray<ColorFromOffset>()

    
    let log =  new AvalonEdit.TextEditor()        
    let hiLi = new LogSelectedTextHighlighter(log)
    let colo = new LogLineColorizer(log,lineColors)
    let search = AvalonEdit.Search.SearchPanel.Install(log) |> ignore //TODO disable search and replace ?
    
    do
        //styling: 
        log.BorderThickness <- new Thickness( 0.5)
        log.Padding         <- new Thickness( 0.7)
        log.Margin          <- new Thickness( 0.7)
        log.BorderBrush <- Brushes.Black

        log.IsReadOnly <- true
        log.Encoding <- Text.Encoding.Default
        log.ShowLineNumbers  <- true
        log.Options.EnableHyperlinks <- true 
        log.TextArea.SelectionCornerRadius <- 0.0 
        log.TextArea.SelectionBorder <- null         
        log.TextArea.TextView.LinkTextForegroundBrush <- Brushes.Blue //Hyperlinks color 
        
        log.TextArea.SelectionChanged.Add hiLi.SelectionChangedDelegate
        log.TextArea.TextView.LineTransformers.Add(hiLi)
        log.TextArea.TextView.LineTransformers.Add(colo)
        
     

    let printCallsCounter = ref 0L
    let mutable prevMsgType = IOErrorMsg
    let stopWatch = Stopwatch.StartNew()
    let buffer =  new StringBuilder()
    //let textAddEv = new Event<string> ()



    // The below functions are trying to work around double UI update in printfn for better UI performance, 
    // and the poor performance of log.ScrollToEnd().
    // see  https://github.com/dotnet/fsharp/issues/3712   
    let printFromBufferAndScroll(ty:LogMessageType) = 
        try
            //buffer.Insert(0,"£") |> ignore // Debug only
            let txt = buffer.ToString() //.Replace(NewLine, sprintf "(%A)%s" ty NewLine)  //for DEBUG only           
            buffer.Clear()  |> ignore 
            let start = log.Document.TextLength
            log.AppendText(txt)
            let mutable line = log.Document.GetLineByOffset(start) 
            if ty = ConsoleOut then //exclude default print color, it should be same as foreground anyway                
                lineColors.Remove line.LineNumber |> ignore // clears any color that might exit from printing on same line before ( maybe just a return)
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
            if log.WordWrap then log.ScrollToEnd() //is needed a second time !
        with 
            ex -> 
                async{
                    do! Async.SwitchToContext Sync.syncContext 
                    log.AppendText (sprintf "ERROR in printFromBufferAndScroll %A" ex)
                    }|> Async.StartImmediate 
        stopWatch.Restart()

    /// adds string on UI thread  every 150ms then scrolls to end after 300ms
    /// sets line color on LineColors dictionay for DocumentColorizingTransformer
    let printOrBuffer (s:string,ty:LogMessageType) =
        async {
            do! Async.SwitchToContext Sync.syncContext 
            if prevMsgType<>ty then // print case 1, color change, do before append new string
                printFromBufferAndScroll(prevMsgType) 
                prevMsgType <- ty

            buffer.Append(s)  |> ignore 
            //textAddEv.Trigger(s)

            if stopWatch.ElapsedMilliseconds > 100L  then // print case 2, only add to document every 100ms  
                printFromBufferAndScroll(ty)                
            else                        
                let k = Interlocked.Increment printCallsCounter
                do! Async.Sleep 100
                if !printCallsCounter = k  then //print case 3, it is the last call for 100 ms
                    printFromBufferAndScroll(ty)
                
        } |> Async.StartImmediate 
    
    let setLineWrap(v)=
        if v then 
            log.WordWrap         <- true 
            log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled 
        else
            log.WordWrap         <- false 
            log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto 
        

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
    
    // this event occures on every call to print, NOT on the aggregated strings that are appened to Log
    //[<CLIEvent>]member this.OnPrint = textAddEv.Publish
       
    member this.AdjustToSettingsInConfig(config:Config)=        
        //this.OnPrint.Add (config.AssemblyReferenceStatistic.RecordFromlog) // TODO: does this have print perfomance impact ? measure do async ?
        setLineWrap( config.Settings.GetBool "logHasLineWrap" true )
        log.FontSize  <- config.Settings.GetFloat "FontSize" Seff.Style.fontSize                
        
    member this.ToggleLineWrap(config:Config)=
        let newState = not  log.WordWrap 
        setLineWrap newState
        config.Settings.SetBool "logHasLineWrap" newState
        config.Settings.Save ()
    
    /// to acces the underlying read-only Avalonedit Texteditor
    member this.ReadOnlyEditor = log

    member this.SelectedTextHighLighter = hiLi
        
    member this.Clear() = 
        log.SelectionStart <- 0
        log.SelectionLength <- 0
        log.Clear()
        lineColors.Clear()

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
        if Util.isTrue (dlg.ShowDialog()) then                
            try
                IO.File.WriteAllText(dlg.FileName, log.Text) 
                this.PrintInfoMsg "Log File saved as:\r\n%s" dlg.FileName
            with e -> 
                this.PrintIOErrorMsg "Failed to save text from Log at :\r\n%s\r\n%A" dlg.FileName e
    
    member this.SaveSelectedText (pathHint: FilePath) = 
        if log.SelectedText.Length > 0 then // this check is also done in "canexecute command"
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
           if Util.isTrue (dlg.ShowDialog()) then                
              try 
                   IO.File.WriteAllText(dlg.FileName, log.SelectedText) 
                   this.PrintInfoMsg "Selected text from Log saved as:\r\n%s" dlg.FileName
              with e -> 
                   this.PrintIOErrorMsg "Failed to save selected text from Log at :\r\n%s\r\n%A" dlg.FileName e