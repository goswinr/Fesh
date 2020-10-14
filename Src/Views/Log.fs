﻿namespace Seff.Views

open Seff
open Seff.Util.General
open Seff.Views.Util
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


type LogKind = 
    | ConsoleOut
    | FsiStdOut 
    | FsiErrorOut 
    | ConsoleError
    | InfoMsg 
    | FsiErrorMsg 
    | AppErrorMsg 
    | IOErrorMsg 
    | DebugMsg 
    | Custom

module LogColors = 

    let consoleOut    = Brushes.Black                     |> freeze // should be same as default  forground. is only used if a line has more than one color 
    let fsiStdOut     = Brushes.DarkGray |> darker 20     |> freeze // values printet by fsi iteself like "val it = ...."
    let fsiErrorOut   = Brushes.DarkMagenta               |> freeze //are they all caught by evaluate non throwing ?
    let consoleError  = Brushes.OrangeRed                 |> freeze // this is used by eprintfn 
    let infoMsg       = Brushes.LightSeaGreen             |> freeze
    let fsiErrorMsg   = Brushes.Red                       |> freeze
    let appErrorMsg   = Brushes.LightSalmon               |> freeze
    let iOErrorMsg    = Brushes.DarkRed                   |> freeze
    let debugMsg      = Brushes.Green                     |> freeze
    let mutable custom  = consoleOut // will be set in Log.PrintCustomBrush

    let inline getColor typ = 
        match typ with 
        | ConsoleOut    -> consoleOut  
        | FsiStdOut     -> fsiStdOut   
        | FsiErrorOut   -> fsiErrorOut 
        | ConsoleError  -> consoleError
        | InfoMsg       -> infoMsg     
        | FsiErrorMsg   -> fsiErrorMsg 
        | AppErrorMsg   -> appErrorMsg 
        | IOErrorMsg    -> iOErrorMsg  
        | DebugMsg      -> debugMsg
        | Custom        -> custom

[<Struct>]
type NewColor = 
    {off: int; brush: LogKind}
    
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
        //with _ -> LogFile.Post (sprintf "findCurrentInList: Did not find off %d in ResizeArray<NewColor> of %d items: %A" off cs.Count cs );   cs.[0]

[<Struct>]
type RangeColor = 
    {start: int; ende:int; brush: LogKind}    

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
    
    let mutable selStart = -9
    let mutable selEnd   = -9

    member this.SelectionChangedDelegate ( a:EventArgs) =
        if ed.SelectionLength = 0 then // no selection 
            selStart <- -9
            selEnd   <- -9
        else
            selStart <- ed.SelectionStart
            selEnd   <- selStart + ed.SelectionLength // this last selcetion in case of block selection too ! correct
        
        //for seg in ed.TextArea.Selection.Segments do LogFile.Post(sprintf "Segment %d-%d" seg.StartOffset seg.EndOffset)
        //for ln in ed.Document.Lines do LogFile.Post(sprintf "Line  %d-%d" ln.Offset ln.EndOffset)
        //LogFile.Post(sprintf "\r\nSelected Text:\r\n%s" ed.SelectedText)

    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) =     
        //try
            if not line.IsDeleted then  
                let stLn = line.Offset
                let enLn = line.EndOffset
                let cs = RangeColor.getInRange offsetColors stLn enLn
                let mutable any = false
                
                // color non selected lines 
                if selStart = selEnd  || selStart > enLn || selEnd < stLn then// no selection in general or on this line                 
                    for c in cs do 
                        if c.brush=ConsoleOut && any then //changing the basefore ground is only needed if any other color already exists on this line                        
                            base.ChangeLinePart(c.start, c.ende, fun element -> element.TextRunProperties.SetForegroundBrush(LogColors.getColor c.brush))
                        else
                            any <-true
                            base.ChangeLinePart(c.start, c.ende, fun el -> el.TextRunProperties.SetForegroundBrush(LogColors.getColor c.brush))
                
                /// exclude selection from coloring: 
                else                
                    for c in cs do
                        let br = LogColors.getColor c.brush
                        let st = c.start
                        let en = c.ende
                        // now consider block or rectangle selection:
                        for seg in ed.TextArea.Selection.Segments do
                            if   seg.EndOffset   < stLn then () // this segment is on another line 
                            elif seg.StartOffset > enLn then () // this segment is on another line 
                            else
                                if   seg.StartOffset =   seg.EndOffset then base.ChangeLinePart(st,  en, fun el -> el.TextRunProperties.SetForegroundBrush(br)) // the selection segment is after the line end, this might happen in block selection
                                elif seg.StartOffset >   en           then base.ChangeLinePart(st,  en, fun el -> el.TextRunProperties.SetForegroundBrush(br)) // the selection segment comes after this color section
                                elif seg.EndOffset   <=  st           then base.ChangeLinePart(st,  en, fun el -> el.TextRunProperties.SetForegroundBrush(br)) // the selection segment comes before this color section
                                else
                                    if st <  seg.StartOffset then base.ChangeLinePart(st           ,  seg.StartOffset, fun el -> el.TextRunProperties.SetForegroundBrush(br))
                                    if en <= seg.EndOffset   then base.ChangeLinePart(seg.EndOffset,  en             , fun el -> el.TextRunProperties.SetForegroundBrush(br))
                            
                            
        
        //with e -> LogFile.Post <| sprintf "LogLineColorizer override this.ColorizeLine failed with:\r\n %A" e
        
            

            
/// Highlight-all-occurrences-of-selected-text in Log Text View
type LogSelectedTextHighlighter (lg:AvalonEdit.TextEditor) = 
    inherit AvalonEdit.Rendering.DocumentColorizingTransformer()    
    
    let colorHighlight =      Brushes.Blue |> brighter 210  |> freeze
    
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
        try    
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
        
        with e -> LogFile.Post <| sprintf "LogSelectedTextHighlighter override this.ColorizeLine failed with %A" e
        
    member this.SelectionChangedDelegate ( a:EventArgs) =
        // for text view:
        let selTxt = lg.SelectedText    // for block selection this will contain everything from first segment till last segment, even the unselected.       
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
      
    let offsetColors = ResizeArray<NewColor>( [ {off=0; brush=ConsoleOut} ] )    
    
    let log =  new AvalonEdit.TextEditor()        
    let hiLi = new LogSelectedTextHighlighter(log)
    let colo = new LogLineColorizer(log,offsetColors)    
    let search = AvalonEdit.Search.SearchPanel.Install(log) |> ignore //TODO disable search and replace ?
    
    do        
        //styling: 
        log.BorderThickness <- new Thickness( 0.5)
        log.Padding         <- new Thickness( 0.7)
        log.Margin          <- new Thickness( 0.7)
        log.BorderBrush <- Brushes.Black |> freeze

        log.IsReadOnly <- true
        log.Encoding <- Text.Encoding.Default
        log.ShowLineNumbers  <- true
        log.Options.EnableHyperlinks <- true 
        log.TextArea.SelectionCornerRadius <- 0.0 
        log.TextArea.SelectionBorder <- null 
        //log.TextArea.SelectionBrush <- Brushes.Blue |> brighter 190|> freeze//Hyperlinks color 
        log.TextArea.TextView.LinkTextForegroundBrush <- Brushes.Blue |> freeze//Hyperlinks color 
        
        log.TextArea.SelectionChanged.Add colo.SelectionChangedDelegate
        log.TextArea.TextView.LineTransformers.Add(colo)
        log.TextArea.SelectionChanged.Add hiLi.SelectionChangedDelegate
        log.TextArea.TextView.LineTransformers.Add(hiLi)
        
     

    let printCallsCounter = ref 0L
    let mutable prevMsgType = ConsoleOut // same as first default item in offsetColors
    let stopWatch = Stopwatch.StartNew()
    let buffer =  new StringBuilder()
    let mutable docLength = 0  //to be able to have the doc length async


    // The below functions are trying to work around double UI update in printfn for better UI performance, 
    // and the poor performance of log.ScrollToEnd().
    // see  https://github.com/dotnet/fsharp/issues/3712   
    let printFromBuffer() =                  
        let txt = buffer.ToString()
        buffer.Clear()  |> ignore           
        log.AppendText(txt)            
        log.ScrollToEnd()
        if log.WordWrap then log.ScrollToEnd() //this is needed a second time !
        stopWatch.Restart() 

    let printFromBufferSync() =
        async {
            do! Async.SwitchToContext Sync.syncContext
            printFromBuffer()
            } |> Async.StartImmediate 

    /// adds string on UI thread  every 150ms then scrolls to end after 300ms
    /// sets line color on LineColors dictionay for DocumentColorizingTransformer
    let printOrBuffer (txt:string,typ:LogKind) =
        
        if prevMsgType <> typ then 
            offsetColors.Add { off = docLength; brush = typ } 
            prevMsgType <- typ 
            //LogFile.Post <| sprintf "offset %d new color: %A" docLength typ
            
        if txt.Length <> 0 then 
            buffer.Append(txt)  |> ignore
            docLength <- docLength + txt.Length

            // star new if clause tu actaull print supplied string tx
            if stopWatch.ElapsedMilliseconds > 100L  then // print case 2, only add to document every 100ms  
                printFromBufferSync()                
            else
                async {                        
                    let k = Interlocked.Increment printCallsCounter
                    do! Async.Sleep 100
                    if !printCallsCounter = k  then //print case 3, it is the last call for 100 ms
                        do! Async.SwitchToContext Sync.syncContext
                        printFromBuffer()                
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

    let textWriterCustomColor   = new FsxTextWriter(fun s -> printOrBuffer (s,Custom     ))
    
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
        log.SelectionLength <- 0
        log.SelectionStart <- 0        
        log.Clear()
        docLength <- 0
        offsetColors.Clear()
        offsetColors.Add {off=0; brush=ConsoleOut}
        

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

    /// Print using the Brush or color provided 
    /// at last custom printing call via PrintCustomBrush or PrintCustomColor 
    member this.PrintCustom s = 
        Printf.fprintfn textWriterCustomColor s
    
    /// Change custom color to a new SolidColorBrush (e.g. from System.Windows.Media.Brushes)
    /// This wil also freeze the Brush.
    /// Then print 
    member this.PrintCustomBrush (br:SolidColorBrush) s = 
        LogColors.custom <- br
        LogColors.custom.Freeze()
        Printf.fprintfn textWriterCustomColor s
    
    /// Change custom color to a RGB value ( each between 0 and 255) 
    /// Then print 
    member this.PrintCustomColor red green blue s = 
        LogColors.custom <- SolidColorBrush(Color.FromRgb(byte red, byte green, byte blue))
        LogColors.custom.Freeze()
        Printf.fprintfn textWriterCustomColor s


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
           let txt =
                log.TextArea.Selection.Segments 
                |> Seq.map (fun s -> log.Document.GetText(s)) // to ensure block selection is saved correctly
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
           if Util.isTrue (dlg.ShowDialog()) then                
              try 
                   IO.File.WriteAllText(dlg.FileName, txt) 
                   this.PrintInfoMsg "Selected text from Log saved as:\r\n%s" dlg.FileName
              with e -> 
                   this.PrintIOErrorMsg "Failed to save selected text from Log at :\r\n%s\r\n%A" dlg.FileName e
