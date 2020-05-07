namespace Seff

open System
open System.Environment
open System.IO
open System.Threading
open Seff.Model
open ICSharpCode
open System.Windows.Media // for color brushes
open System.Text
open System.Diagnostics
open System.Collections.Generic
open System.Windows.Controls

/// A TextWriter that writes using a function (to an Avalonedit Control). used in FSI session constructor   
type FsxTextWriter(writeStr) =
    inherit TextWriter()
    override this.Encoding =  Text.Encoding.Default
    override this.Write     (s:string)  = writeStr (s)
    override this.WriteLine (s:string)  = writeStr (s + NewLine)    // actually never used see  https://github.com/dotnet/fsharp/issues/3712   
    override this.WriteLine ()          = writeStr (    NewLine)    
    
    

type private LogLineColorizer(editor:AvalonEdit.TextEditor, lineColors:Collections.Generic.Dictionary<int,SolidColorBrush>) = 
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
    


/// A static class that hold the single neded instance of LogView and provides print formating methods for the LogView
[<Sealed>]
type Log private () =    
    // just uing a let value  like (let Log = new LogView()) has some bugs in hosted context (Rhino), I think due to late initalizing
    // so here is a static class with explicit init

    static let mutable log : AvalonEdit.TextEditor = null 
    static let printCallsCounter = ref 0L
    static let mutable prevMsgType = IOErrorMsg
    static let stopWatch = Stopwatch.StartNew()
    static let buffer =  new StringBuilder()
    static let textAddEv = new Event<string> ()

    /// Dictionary holding the color of all non standart lines
    static let lineColors = new Collections.Generic.Dictionary<int,SolidColorBrush>() 

    // The below functions are trying to work around double UI update in printfn for better UI performance, and the poor performance of log.ScrollToEnd().
    // see  https://github.com/dotnet/fsharp/issues/3712   
    static let printFromBufferAndScroll(ty:LogMessageType) =             
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
    static let printOrBuffer (s:string,ty:LogMessageType) =
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
    


    //----------------------members:------------------------------------------

    static member Initialize() = // to be able to eagerly create it, late initalizing (just "let" declarations in a module) caused problems when hosted in rhino
        log <- new AvalonEdit.TextEditor()
        log.IsReadOnly <- true
        log.Encoding <- Text.Encoding.Default

        log.ShowLineNumbers  <- true
        log.Options.EnableHyperlinks <- true 
        log.TextArea.SelectionCornerRadius <- 0.0 
        log.TextArea.SelectionBorder <- null         
        log.TextArea.TextView.LinkTextForegroundBrush <- Brushes.Blue //Hyperlinks color        
        log.TextArea.TextView.LineTransformers.Add(new LogLineColorizer(log,lineColors))
        AvalonEdit.Search.SearchPanel.Install(log) |> ignore  
        // font and fontsize will be set up in Config.initialize since it depends on config.
    
    static member setWordWrap(v)=
        if v then 
            log.WordWrap         <- true 
            log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled   
        else
            log.WordWrap         <- false 
            log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto 

    /// to acces the underlying read only Avalonedit Texteditor
    static member ReadOnlyEditor = log

    //used in FSI constructor:
    static member val TextWriterFsiStdOut     = new FsxTextWriter(fun s -> printOrBuffer (s,FsiStdOut    ))
    static member val TextWriterFsiErrorOut   = new FsxTextWriter(fun s -> printOrBuffer (s,FsiErrorOut  ))
    static member val TextWriterConsoleOut    = new FsxTextWriter(fun s -> printOrBuffer (s,ConsoleOut   ))
    static member val TextWriterConsoleError  = new FsxTextWriter(fun s -> printOrBuffer (s,ConsoleError ))
    
    // used for printf formaters:                                          
    static member val TextWriterInfoMsg       = new FsxTextWriter(fun s -> printOrBuffer (s,InfoMsg      ))
    static member val TextWriterFsiErrorMsg   = new FsxTextWriter(fun s -> printOrBuffer (s,FsiErrorMsg  ))
    static member val TextWriterAppErrorMsg   = new FsxTextWriter(fun s -> printOrBuffer (s,AppErrorMsg  ))
    static member val TextWriterIOErrorMsg    = new FsxTextWriter(fun s -> printOrBuffer (s,IOErrorMsg   ))
    static member val TextWriterDebugMsg      = new FsxTextWriter(fun s -> printOrBuffer (s,DebugMsg     ))
    static member val TextWriterPrintMsg      = new FsxTextWriter(fun s -> printOrBuffer (s,PrintMsg     ))


    //static member PrintFsiStdOut    s =  Printf.fprintfn Log.TextWriterFsiStdOut    s  //should not be used
    //static member PrintFsiErrorOut  s =  Printf.fprintfn Log.TextWriterFsiErrorOut  s
    //static member PrintConsoleOut   s =  Printf.fprintfn Log.TextWriterConsoleOut   s
    //static member PrintConsoleError s =  Printf.fprintfn Log.TextWriterConsoleError s
    static member PrintInfoMsg      s =  Printf.fprintfn Log.TextWriterInfoMsg      s
    static member PrintFsiErrorMsg  s =  Printf.fprintfn Log.TextWriterFsiErrorMsg  s
    static member PrintAppErrorMsg  s =  Printf.fprintfn Log.TextWriterAppErrorMsg  s
    static member PrintIOErrorMsg   s =  Printf.fprintfn Log.TextWriterIOErrorMsg   s        
    static member PrintDebugMsg     s =  Printf.fprintfn Log.TextWriterDebugMsg     s
    
    /// like printfn, use with format strings, adds new line
    static member Print s             =  Printf.fprintfn Log.TextWriterPrintMsg     s //TODO remove unused?

    /// this event occures on ever call to print, NOT on the aggregated strings that are appened to Log
    [<CLIEvent>]
    static member OnPrint = textAddEv.Publish