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
    | ConsoleOut
    | FsiStdOut 
    | FsiErrorOut 
    | ConsoleError
    | InfoMsg 
    | FsiErrorMsg 
    | AppErrorMsg 
    | IOErrorMsg 
    | DebugMsg 

    static member getColor = function
        | ConsoleOut    ->Brushes.Yellow // default black forground is used ; never used should  // the out from printfn
        | FsiStdOut     ->Brushes.DarkGray |> Util.General.darker 20 // values printet by fsi iteself like "val it = ...."
        | FsiErrorOut   ->Brushes.DarkMagenta //are they all caught by evaluate non throwing ?
        | ConsoleError  ->Brushes.OrangeRed // this is used by eprintfn 
        | InfoMsg       ->Brushes.LightSeaGreen
        | FsiErrorMsg   ->Brushes.Red
        | AppErrorMsg   ->Brushes.LightSalmon
        | IOErrorMsg    ->Brushes.DarkRed
        | DebugMsg      ->Brushes.Green
        


[<Struct>]
type NewColor = 
    {off: int; brush: SolidColorBrush}
    
    /// Does binary search to find an offset that is equal or smaller than off
    static member findCurrentInList (cs:ResizeArray<NewColor>) off = 
        //try        
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
        //with _ ->             failwithf "Did not find off %d in cs of %d items %A" off cs.Count cs

[<Struct>]
type RangeColor = 
    {start: int; ende:int; brush: SolidColorBrush}    

    static member getInRange (cs:ResizeArray<NewColor>) st en =     
        let rec mkList i ls = 
            let c = NewColor.findCurrentInList cs i
            if c.off <= st then 
                {start = st; ende=en; brush = c.brush} :: ls
            else                 
                mkList (i-1) ({start = i; ende=en; brush = c.brush}  :: ls)
        mkList en [] 




/// A TextWriter that writes using a function (to an Avalonedit Control). used in FSI session constructor   
type FsxTextWriter(writeStr) =
    inherit TextWriter()
    override this.Encoding =  Text.Encoding.Default
    override this.Write     (s:string)  = writeStr (s)
    override this.WriteLine (s:string)  = writeStr (s + NewLine)    // actually never used see  https://github.com/dotnet/fsharp/issues/3712   
    override this.WriteLine ()          = writeStr (    NewLine)    
    
    

type LogLineColorizer(ed:AvalonEdit.TextEditor, offsetColors: ResizeArray<NewColor>) =  
    inherit AvalonEdit.Rendering.DocumentColorizingTransformer()
    
    let mutable selStart = -1
    let mutable selEnd   = -1

    member this.SelectionChangedDelegate ( a:EventArgs) =
        if ed.SelectionLength = 0 then // no selection 
            selStart <- -1
            selEnd   <- -1
        else 
            selStart <- ed.SelectionStart
            selEnd   <- selStart + ed.SelectionLength //TODO is this correct in case of block selection ?? use ed.TextArea.Selection.Segments ??
     

    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) =     
        //try
            if not line.IsDeleted then  
                let stLn = line.Offset
                let enLn = line.EndOffset
                let cs = RangeColor.getInRange offsetColors stLn enLn
                if selStart = selEnd  || selStart > enLn || selEnd < stLn then // no selection in general or on this line 
                    for c in cs do 
                        if notNull c.brush then
                            base.ChangeLinePart(c.start, c.ende, fun element -> element.TextRunProperties.SetForegroundBrush(c.brush))
                        else
                            base.ChangeLinePart(c.start, c.ende, fun element -> element.TextRunProperties.SetForegroundBrush(Brushes.DarkGreen))
                else
                    for c in cs do 
                        if notNull c.brush then 
                            let st = c.start
                            let en = c.ende
                            // consider block or rectangle selection:
                            for seg in ed.TextArea.Selection.Segments do
                                if st < seg.StartOffset && seg.StartOffset < en then base.ChangeLinePart(st, seg.StartOffset, fun element -> element.TextRunProperties.SetForegroundBrush(c.brush))
                                if en > seg.EndOffset   && seg.EndOffset   > st then base.ChangeLinePart(seg.EndOffset,   en, fun element -> element.TextRunProperties.SetForegroundBrush(c.brush))
        //with e ->             failwithf "LogLineColorizer override this.ColorizeLine failed with %A" e
            

        
    
/// Highlight-all-occurrences-of-selected-text in Log Text View
type LogSelectedTextHighlighter (lg:AvalonEdit.TextEditor) = 
    inherit AvalonEdit.Rendering.DocumentColorizingTransformer()    
    
    let colorHighlight =      Brushes.Blue |> brighter 210  
    
    let mutable highTxt = null
    let mutable curSelStart = -1

    
    let highlightChangedEv  = new Event<string*int>()
    [<CLIEvent>]
    member this.HighlightChanged = highlightChangedEv.Publish

    member this.ColorHighlight = colorHighlight
    
    //member this.HighlightText  with get() = highTxt and set v = highTxt <- v
    //member this.CurrentSelectionStart  with get() = curSelStart and set v = curSelStart <- v
    
    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) =       
        //  from https://stackoverflow.com/questions/9223674/highlight-all-occurrences-of-selected-word-in-avalonedit
        //try    
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
        
        //with e ->            failwithf "LogSelectedTextHighlighter override this.ColorizeLine failed with %A" e
        
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
      
    let offsetColors = ResizeArray<NewColor>( [ {off=0; brush=null} ] )    
    
    let log =  new AvalonEdit.TextEditor()        
    let hiLi = new LogSelectedTextHighlighter(log)
    let colo = new LogLineColorizer(log,offsetColors)    
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
        log.TextArea.SelectionChanged.Add colo.SelectionChangedDelegate
        log.TextArea.TextView.LineTransformers.Add(hiLi)
        log.TextArea.TextView.LineTransformers.Add(colo)
        
     

    let printCallsCounter = ref 0L
    let mutable prevMsgType = ConsoleOut // same as first default item in offsetColors
    let stopWatch = Stopwatch.StartNew()
    let buffer =  new StringBuilder()


    // The below functions are trying to work around double UI update in printfn for better UI performance, 
    // and the poor performance of log.ScrollToEnd().
    // see  https://github.com/dotnet/fsharp/issues/3712   
    let printFromBuffer(scrollToo, typUpdate, newTy:LogMessageType, appendTxt:string) =        
        
        if buffer.Length <> 0 then  // it might be empty from flushing it at a color change            
            let txt = buffer.ToString()
            buffer.Clear()  |> ignore           
            log.AppendText(txt)
            if scrollToo then 
                log.ScrollToEnd()
                if log.WordWrap then log.ScrollToEnd() //this is needed a second time !
            stopWatch.Restart()
        
        if typUpdate then 
            buffer.Append(sprintf "-new: %A from %d ->" newTy log.Document.TextLength) |> ignore //Debug
            if newTy = ConsoleOut then //exclude default print color, it should be same as foreground anyway                
                offsetColors.Add { off = log.Document.TextLength; brush = null } 
            else
                offsetColors.Add { off = log.Document.TextLength ; brush = LogMessageType.getColor(newTy) } 
        
        if notNull appendTxt then //to ensure that append happens after the above lines
            buffer.Append appendTxt |> ignore 

    let printFromBufferSync(scrollToo, typUpdate, newTy:LogMessageType,appendTxt) =
        async{
            do! Async.SwitchToContext Sync.syncContext
            printFromBuffer(scrollToo,typUpdate,newTy,appendTxt)
            } |> Async.StartImmediate 

    /// adds string on UI thread  every 150ms then scrolls to end after 300ms
    /// sets line color on LineColors dictionay for DocumentColorizingTransformer
    let printOrBuffer (txt:string,typ:LogMessageType) =

        if prevMsgType <> typ then // print case 1, color change            
            // first print any potentially remainig stuff in buffer and then append string inside:
            //buffer.Append(sprintf "-new: %A->" ty) |> ignore //Debug
            printFromBufferSync(false, true, typ, txt)
            prevMsgType <- typ               
        else            
            buffer.Append(txt)  |> ignore 
        
        // star new if clause tu actaull print supplied string tx
        if stopWatch.ElapsedMilliseconds > 100L  then // print case 2, only add to document every 100ms  
            printFromBufferSync(true, false, typ, null)                
        else
            async {                        
                let k = Interlocked.Increment printCallsCounter
                do! Async.Sleep 100
                if !printCallsCounter = k  then //print case 3, it is the last call for 100 ms
                    do! Async.SwitchToContext Sync.syncContext
                    printFromBuffer(true, false, typ, null)                
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
        offsetColors.Clear()

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