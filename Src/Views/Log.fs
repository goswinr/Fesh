namespace Seff.Views

open Seff
open Seff.Model
open Seff.Util.General
open Seff.Views.Util
open Seff.Config
open System
open System.IO
open System.Threading
open AvalonEditB
open System.Windows.Media // for color brushes
open System.Text
open System.Diagnostics
open System.Collections.Generic
open System.Windows.Controls
open System.Windows
open AvalonEditB

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

    let mutable customColor     = Brushes.Black        |> freeze   
    
    let inline clamp (i:int) =
        if   i <=   0 then 0uy
        elif i >= 255 then 255uy
        else byte i

    let setcustomColor(red,green,blue) = 
        let r = clamp red
        let g = clamp green
        let b = clamp blue
        let col = customColor.Color
        if col.R <> r || col.G <> g || col.B <> b then // only chnage if different
            customColor  <- freeze (new SolidColorBrush(Color.FromRgb(r,g,b)))  
   

/// describes the position in text where a new color starts
[<Struct>]
type NewColor = 
    {off: int; brush: SolidColorBrush}
    
    /// Does binary search to find an offset that is equal or smaller than currOff
    static member findCurrentInList (cs:ResizeArray<NewColor>) currOff =         
        let last = cs.Count-1 //TODO is it possible that count increases while iterating?
        let rec find lo hi =             
            let mid = lo + (hi - lo) / 2          //TODO test edge conditions !!  
            if cs.[mid].off <= currOff then 
                if mid = last                 then cs.[mid] // exit
                elif cs.[mid+1].off > currOff then cs.[mid] // exit
                else find (mid+1) hi
            else
                        find lo (mid-1)        
        find 0 last

/// describes the the start and end position of a color with one line
[<Struct>]
type RangeColor = 
    {start: int; ende:int; brush: SolidColorBrush} // brush must be frozen to use async   

    /// Finds all the offset that apply to this line  which is defined by the range of  tOff to enOff 
    /// even if the ResizeArray<NewColor> does not conrtain any offest between stOff and  enOff 
    /// it still retuens the a list with one item. The closest previous offset
    static member getInRange (cs:ResizeArray<NewColor>) stOff enOff =     
        let rec mkList i ls = 
            let c = NewColor.findCurrentInList cs i
            if c.off <= stOff  then 
                {start = stOff ; ende = enOff ; brush = c.brush} :: ls
            else                 
                mkList (i-1) ({start = i; ende = enOff ; brush = c.brush}  :: ls)
        mkList enOff [] 


/// A TextWriter that writes using a function (to an Avalonedit Control). used in FSI session constructor   
type FsxTextWriter(writeStr) =
    inherit TextWriter()
    override this.Encoding =  Text.Encoding.Default
    override this.Write     (s:string)  = writeStr (s)
    override this.WriteLine (s:string)  = writeStr (s + Environment.NewLine)    // actually never used see  https://github.com/dotnet/fsharp/issues/3712   
    override this.WriteLine ()          = writeStr (    Environment.NewLine)    
    

type LogLineColorizer(ed:TextEditor, offsetColors: ResizeArray<NewColor>) =  
    inherit Rendering.DocumentColorizingTransformer()
    
    let mutable selStart = -9
    let mutable selEnd   = -9
    let mutable any = false

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
    override this.ColorizeLine(line:Document.DocumentLine) =     
        //try
        //with e -> LogFile.Post <| sprintf "LogLineColorizer override this.ColorizeLine failed with:\r\n %A" e
            if not line.IsDeleted then  
                let stLn = line.Offset
                let enLn = line.EndOffset
                let cs = RangeColor.getInRange offsetColors stLn enLn
                any <- false
                
                // color non selected lines 
                if selStart = selEnd  || selStart > enLn || selEnd < stLn then// no selection in general or on this line                 
                    for c in cs do 
                        if c.brush = null && any then //changing the basefore ground is only needed if any other color already exists on this line                        
                            base.ChangeLinePart(c.start, c.ende, fun element -> element.TextRunProperties.SetForegroundBrush(LogColors.consoleOut))
                        else                            
                            if notNull c.brush then // might still happen on first line
                                any <-true
                                base.ChangeLinePart(c.start, c.ende, fun el -> el.TextRunProperties.SetForegroundBrush(c.brush))
                
                /// exclude selection from coloring: 
                else                
                    for c in cs do
                        let br = c.brush |> ifNull LogColors.consoleOut // null check
                        let st = c.start
                        let en = c.ende
                        // now consider block or rectangle selection:
                        for seg in ed.TextArea.Selection.Segments do
                            if   seg.EndOffset   < stLn then () // this segment is on another line 
                            elif seg.StartOffset > enLn then () // this segment is on another line 
                            else
                                if   seg.StartOffset =   seg.EndOffset then base.ChangeLinePart(st,  en, fun el -> el.TextRunProperties.SetForegroundBrush(br)) // the selection segment is after the line end, this might happen in block selection
                                elif seg.StartOffset >   en            then base.ChangeLinePart(st,  en, fun el -> el.TextRunProperties.SetForegroundBrush(br))  // the selection segment comes after this color section
                                elif seg.EndOffset   <=  st            then base.ChangeLinePart(st,  en, fun el -> el.TextRunProperties.SetForegroundBrush(br))  // the selection segment comes before this color section
                                else
                                    if st <  seg.StartOffset then base.ChangeLinePart(st           ,  seg.StartOffset, fun el -> el.TextRunProperties.SetForegroundBrush(br))
                                    if en >  seg.EndOffset   then base.ChangeLinePart(seg.EndOffset,  en             , fun el -> el.TextRunProperties.SetForegroundBrush(br))
 
 
/// Highlight-all-occurrences-of-selected-text in Log Text View
type LogSelectedTextHighlighter (lg:TextEditor) = 
    inherit Rendering.DocumentColorizingTransformer()    
    
    let colorHighlight =      Brushes.Blue |> brighter 210  |> freeze
    
    let mutable highTxt = null
    let mutable curSelStart = -1

    // events for status bar
    let highlightClearedEv  = new Event<unit>()
    let highlightChangedEv  = new Event<string*int>()
    
    [<CLIEvent>]
    member this.OnHighlightCleared = highlightClearedEv.Publish
    
    [<CLIEvent>]
    member this.OnHighlightChanged = highlightChangedEv.Publish
   

    member this.ColorHighlight = colorHighlight
    
    //member this.HighlightText  with get() = highTxt and set v = highTxt <- v
    //member this.CurrentSelectionStart  with get() = curSelStart and set v = curSelStart <- v
    
    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:Document.DocumentLine) =       
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
        
        //with e -> LogFile.Post <| sprintf "LogSelectedTextHighlighter override this.ColorizeLine failed with %A" e
        
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
                //let mutable anyInFolding = false
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
                highlightClearedEv.Trigger()
                lg.TextArea.TextView.Redraw() // to clear highlight
   

/// A ReadOnly text AvalonEdit Editor that provides print formating methods 
/// call ApplyConfig() once config is set up too, (config depends on this Log instance)
type Log private () =    
    
    //static let mutable lgs = Unchecked.defaultof<Log>
    
    let offsetColors = ResizeArray<NewColor>( [ {off = -1 ; brush=null} ] )    // null is console out // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) .. 
    
    let log =  new TextEditor()    
    let hiLi = new LogSelectedTextHighlighter(log)
    let colo = new LogLineColorizer(log,offsetColors)    
    let search = Search.SearchPanel.Install(log) |> ignore //TODO disable search and replace ?
    
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
        log.TextArea.TextView.LinkTextForegroundBrush <- Brushes.Blue |> freeze//Hyperlinks color         
        
        log.TextArea.SelectionChanged.Add colo.SelectionChangedDelegate
        log.TextArea.TextView.LineTransformers.Add(colo)
        log.TextArea.SelectionChanged.Add hiLi.SelectionChangedDelegate
        log.TextArea.TextView.LineTransformers.Add(hiLi)
        
        LogColors.consoleOut <- (log.Foreground.Clone() :?> SolidColorBrush |> freeze) // just to be sure they are the same
        //log.Foreground.Changed.Add ( fun _ -> LogColors.consoleOut <- (log.Foreground.Clone() :?> SolidColorBrush |> freeze)) // this eventy attaching can't  be done because it is already frozen

    let printCallsCounter = ref 0L
    let mutable prevMsgType = null //null is no color for console // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) .. 
    let stopWatch = Stopwatch.StartNew()
    let buffer =  new StringBuilder()
    let mutable docLength = 0  //to be able to have the doc length async
    let maxCharsInLog = 512_000 // about 5k lines with 100 chars each
    let mutable stillLessThanMaxChars = true

    let getBufferText () =
        let txt = buffer.ToString()
        buffer.Clear()  |> ignore 
        txt

    // The below functions are trying to work around double UI update in printfn for better UI performance, 
    // and the poor performance of log.ScrollToEnd().
    // see  https://github.com/dotnet/fsharp/issues/3712  
    let printToLog() =          
        let txt = lock buffer getBufferText //lock for safe access    // or rwl.EnterWriteLock() //https://stackoverflow.com/questions/23661863/f-synchronized-access-to-list
        log.AppendText(txt)       
        log.ScrollToEnd()
        if log.WordWrap then log.ScrollToEnd() //this is needed a second time. see  https://github.com/dotnet/fsharp/issues/3712  
        stopWatch.Restart()

    
    /// adds string on UI thread  every 150ms then scrolls to end after 300ms.
    /// otipnally adds new line at end
    /// sets line color on LineColors dictionay for DocumentColorizingTransformer
    /// printOrBuffer (txt:string, addNewLine:bool, typ:SolidColorBrush)
    let printOrBuffer (txt:string, addNewLine:bool, typ:SolidColorBrush) =
        if stillLessThanMaxChars && txt.Length <> 0 then
            // Change color if needed:
            if prevMsgType <> typ then 
                lock buffer (fun () -> 
                    offsetColors.Add { off = docLength; brush = typ } // TODO filter out ANSI escape chars first or just keep them in the doc but not in the visual line ??
                    prevMsgType <- typ )
                //LogFile.Post <| sprintf "offset %d new color: %A" docLength typ
            
            // add to buffer locked:
            if addNewLine then 
                lock buffer (fun () -> 
                    buffer.AppendLine(txt)  |> ignore
                    docLength <- docLength + txt.Length + 2) // TODO, is a new line always two ?
            else                
                lock buffer (fun () -> 
                    buffer.Append(txt)  |> ignore
                    docLength <- docLength + txt.Length   ) 
            
            // check if buffer is already to big , print it and then stop printing
            if docLength > maxCharsInLog then // neded when log gets piled up with exception messages form Avalonedit rendering pipeline.
                stillLessThanMaxChars <- false
                async {
                    do! Async.SwitchToContext Sync.syncContext
                    printToLog()
                    log.AppendText(sprintf "\r\n\r\n  *** STOP OF LOGGING *** Log has more than %d characters! clear Log view first" maxCharsInLog)
                    log.ScrollToEnd()
                    log.ScrollToEnd()
                    } |> Async.StartImmediate
            else
                // check the two criteria for actually printing
                // print case 1: sine the last printing more than 100ms have elapsed
                // print case 2, wait 0.1 seconds and print if nothing els has been added to the buffer during the last 100 ms
                if stopWatch.ElapsedMilliseconds > 100L  then // print case 1: only add to document every 100ms  
                    async {
                        do! Async.SwitchToContext Sync.syncContext
                        printToLog()
                        } |> Async.StartImmediate                 
                else
                    async {                        
                        let k = Interlocked.Increment printCallsCounter
                        do! Async.Sleep 100
                        if !printCallsCounter = k  then //print case 2, it is the last call for 100 ms
                            do! Async.SwitchToContext Sync.syncContext
                            printToLog()                
                        } |> Async.StartImmediate 
               
    

    let setLineWrap(v)=
        if v then 
            log.WordWrap         <- true 
            log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled 
        else
            log.WordWrap         <- false 
            log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto 
        

    //used in FSI constructor:
    let fsiErrorStream = StringBuilder()
    let textWriterConsoleOut    = new FsxTextWriter(fun s -> printOrBuffer (s, false, LogColors.consoleOut   ))
    let textWriterConsoleError  = new FsxTextWriter(fun s -> printOrBuffer (s, false, LogColors.consoleError ))                                                                              
    let textWriterFsiStdOut     = new FsxTextWriter(fun s -> printOrBuffer (s, false, LogColors.fsiStdOut    ))
    let textWriterFsiErrorOut   = new FsxTextWriter(fun s -> printOrBuffer (s, false, LogColors.fsiErrorOut  ); fsiErrorStream.Append(s)|> ignore )

 
     
    //-----------------------------------------------------------    
    //----------------------members:------------------------------------------    
    //------------------------------------------------------------    
    
    member this.FsiErrorStream = fsiErrorStream

    //member this.OffsetColors = offsetColors // TODO delete , for debug only

    member internal this.AdjustToSettingsInConfig(config:Config)=        
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
        prevMsgType <- null
        stillLessThanMaxChars <- true
        offsetColors.Clear()
        LogColors.customColor <- Brushes.Black        |> freeze     // or remeber it?
        offsetColors.Add {off = -1 ; brush=null} //TODO use -1 instead? // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) .. 
        log.TextArea.TextView.linesCollapsedVisualPosOffThrowCount <- 0 // custom property in Avalonedit to avoid throwing too many exceptions. set 0 so exceptions appear again
        GlobalErrorHandeling.throwCount <- 0 // set 0 so exceptions appear again
       

    //used in FSI constructor:
    member this.TextWriterFsiStdOut    = textWriterFsiStdOut    
    member this.TextWriterFsiErrorOut  = textWriterFsiErrorOut  
    member this.TextWriterConsoleOut   = textWriterConsoleOut   
    member this.TextWriterConsoleError = textWriterConsoleError 

    member this.PrintfnInfoMsg      msg =  Printf.kprintf (fun s -> printOrBuffer (s, true, LogColors.infoMsg      ))  msg 
    member this.PrintfnFsiErrorMsg  msg =  Printf.kprintf (fun s -> printOrBuffer (s, true, LogColors.fsiErrorMsg  ))  msg
    member this.PrintfnAppErrorMsg  msg =  Printf.kprintf (fun s -> printOrBuffer (s, true, LogColors.appErrorMsg  ))  msg
    member this.PrintfnIOErrorMsg   msg =  Printf.kprintf (fun s -> printOrBuffer (s, true, LogColors.iOErrorMsg   ))  msg        
    member this.PrintfnDebugMsg     msg =  Printf.kprintf (fun s -> printOrBuffer (s, true, LogColors.debugMsg     ))  msg
    member this.PrintfnLastColor    msg =  Printf.kprintf (fun s -> printOrBuffer (s, true, LogColors.customColor  ))  msg
    /// Print using the Brush or color provided 
    /// at last custom printing call via.PrintfnCustomBrush or.PrintfnColor 
    //member this.PrintfnCustom s = Printf.kprintf (fun s -> printOrBuffer (s, true, LogColors.customColor ))  s       
        
    /// Change custom color to a RGB value ( each between 0 and 255) 
    /// Then print 
    member this.PrintfnColor red green blue msg =          
            LogColors.setcustomColor (red,green,blue)
            Printf.kprintf (fun s -> printOrBuffer (s,true, LogColors.customColor ))  msg        

     //--- without new line: --------------

    member this.PrintfInfoMsg  msg  =      Printf.kprintf (fun s -> printOrBuffer (s, false, LogColors.infoMsg     ))  msg 

    /// Prints without adding a new line at the end
    member this.PrintfFsiErrorMsg  msg =  Printf.kprintf (fun s -> printOrBuffer (s, false, LogColors.fsiErrorMsg  ))  msg

    /// Prints without adding a new line at the end
    member this.PrintfAppErrorMsg  msg = Printf.kprintf (fun s -> printOrBuffer (s, false, LogColors.appErrorMsg  ))  msg

    /// Prints without adding a new line at the end
    member this.PrintfIOErrorMsg   msg =   Printf.kprintf (fun s -> printOrBuffer (s, false, LogColors.iOErrorMsg   ))  msg

    /// Prints without adding a new line at the end
    member this.PrintfDebugMsg     msg =  Printf.kprintf (fun s -> printOrBuffer (s, false, LogColors.debugMsg     ))  msg    
    
    /// Print using the Brush or color provided 
    /// at last custom printing call via.PrintfnCustomBrush or.PrintfnColor 
    /// without adding a new line at the end
    member this.PrintfLastColor msg =     Printf.kprintf (fun s -> printOrBuffer (s, false, LogColors.customColor ))  msg
   
    /// Change custom color to a RGB value ( each between 0 and 255) 
    /// Then print without adding a new line at the end
    member this.PrintfColor red green blue msg =
            LogColors.setcustomColor (red,green,blue)
            Printf.kprintf (fun s -> printOrBuffer (s,false, LogColors.customColor ))  msg
    
    // for use from Seff.Rhino:

    /// Change custom color to a RGB value ( each between 0 and 255) 
    /// Then print without adding a new line at the end
    member this.PrintColor red green blue s = 
            LogColors.setcustomColor (red,green,blue)
            printOrBuffer (s,false, LogColors.customColor )    
       
    /// Change custom color to a RGB value ( each between 0 and 255) 
    /// Adds a new line at the end
    member this.PrintnColor red green blue s =
            LogColors.setcustomColor (red,green,blue)
            printOrBuffer (s,true, LogColors.customColor ) 
   

    interface ISeffLog with        
        member _.ReadOnlyEditor         = log
        member _.ReadOnlyDoc            = log.Document
        member _.FsiErrorStream         = fsiErrorStream
        
        //used in FSI constructor:
        member _.TextWriterFsiStdOut    = textWriterFsiStdOut    :> TextWriter   
        member _.TextWriterFsiErrorOut  = textWriterFsiErrorOut  :> TextWriter   
        member _.TextWriterConsoleOut   = textWriterConsoleOut   :> TextWriter   
        member _.TextWriterConsoleError = textWriterConsoleError :> TextWriter          

        member this.PrintfnInfoMsg  msg =            this.PrintfnInfoMsg     msg
        member this.PrintfnFsiErrorMsg msg =            this.PrintfnFsiErrorMsg msg
        member this.PrintfnAppErrorMsg msg =            this.PrintfnAppErrorMsg msg
        member this.PrintfnIOErrorMsg  msg =            this.PrintfnIOErrorMsg  msg
        member this.PrintfnDebugMsg    msg =            this.PrintfnDebugMsg    msg
        member this.PrintfnLastColor   msg =            this.PrintfnLastColor   msg
        member this.PrintfnColor red green blue msg =   this.PrintfnColor red green blue msg 
        //member this.PrintfnCustomBrush (br:SolidColorBrush) msg = this.PrintfnCustomBrush (br:SolidColorBrush) msg

        //without the new line:
        member this.PrintfInfoMsg     msg          = this.PrintfInfoMsg     msg          
        member this.PrintfFsiErrorMsg msg          = this.PrintfFsiErrorMsg msg          
        member this.PrintfAppErrorMsg msg          = this.PrintfAppErrorMsg msg          
        member this.PrintfIOErrorMsg  msg          = this.PrintfIOErrorMsg  msg          
        member this.PrintfDebugMsg    msg          = this.PrintfDebugMsg    msg          
        member this.PrintfLastColor   msg          = this.PrintfLastColor   msg          
        member this.PrintfColor red green blue msg = this.PrintfColor red green blue msg        
        //member this.PrintfCustomBrush (br:SolidColorBrush) msg =  LogColors.customColor  <- br |> freeze;  Printf.kprintf (fun s -> printOrBuffer (s,false, LogColors.customColor ))  msg

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
        if Util.isTrue (dlg.ShowDialog()) then                
            try
                IO.File.WriteAllText(dlg.FileName, log.Text) 
                this.PrintfnInfoMsg "Log File saved as:\r\n%s" dlg.FileName
            with e -> 
                this.PrintfnIOErrorMsg "Failed to save text from Log at :\r\n%s\r\n%A" dlg.FileName e
   
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
                   this.PrintfnInfoMsg "Selected text from Log saved as:\r\n%s" dlg.FileName
              with e -> 
                   this.PrintfnIOErrorMsg "Failed to save selected text from Log at :\r\n%s\r\n%A" dlg.FileName e
                       
    //--------------------------------------------------------------------------------------------------------------------------------------------
    //-----------------------------Static members---------------------------------------------------------------------------------------------------------------
    //--------------------------------------------------------------------------------------------------------------------------------------------
    
    /// creates one instance
    static member Create() = 
        let l = Log()
        ISeffLog.log <- l
        ISeffLog.printColor  <- l.PrintColor
        ISeffLog.printnColor <- l.PrintnColor
        ISeffLog.clear       <- (fun () -> Sync.doSync l.Clear)
        l