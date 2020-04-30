namespace Seff

open System
open System.Environment
open System.IO
open System.Threading
open Seff.Model
open ICSharpCode
open System.Windows.Media // for color brushes
open Seff.Model
open System.Text
open System.Diagnostics

module Logging =

    /// A TextWriter that writes using a function (to an Avalonedit Control). used in FSI session constructor   
    type FsxTextWriter(writeStr) =
        inherit TextWriter()
        override this.Encoding =  Text.Encoding.Default
        override this.Write     (s:string)  = writeStr (s)
        override this.WriteLine (s:string)  = writeStr (s + NewLine)    // actually never used see  https://github.com/dotnet/fsharp/issues/3712   
        override this.WriteLine ()          = writeStr (    NewLine)    
    
    /// Dictionary holding the color of all non standart lines
    let private LineColors = Collections.Generic.Dictionary<int,SolidColorBrush>() // TODO store line type instead,  

    type private LogLineColorizer(editor:AvalonEdit.TextEditor) = 
        inherit AvalonEdit.Rendering.DocumentColorizingTransformer()
        
        /// This gets called for every visvble line on any view change
        override this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) =       
           let ok,color = LineColors.TryGetValue(line.LineNumber)
           if ok && not line.IsDeleted then
                
                // TODO skip ConsoleOut lines since they dont have a Foreground renderer line type instead,  

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
    
    [<AllowNullLiteral>] // so that Log.log can be null initially
    type LogView () =
        let editor =  
            let e = new AvalonEdit.TextEditor()
            e.IsReadOnly <- true
            e.Encoding <- Text.Encoding.Default
            e.TextArea.TextView.LineTransformers.Add(new LogLineColorizer(e))
            AvalonEdit.Search.SearchPanel.Install(e) |> ignore
            e
        
        // The below functions are trying to work around double UI update in printfn for better UI performance
        // see  https://github.com/dotnet/fsharp/issues/3712   

        
        let printCallsCounter = ref 0L
        let mutable prevMsgType = IOErrorMsg
        let stopWatch = Stopwatch.StartNew()
        let buffer =  new StringBuilder()


        let printFromBufferAndScroll(ty:LogMessageType) =             
            stopWatch.Restart()
            
            let txt = buffer.ToString()//.Replace(NewLine, sprintf "(%A)%s" ty NewLine)  //for DEBUG only           
            buffer.Clear()  |> ignore 
            let start = editor.Document.TextLength
            editor.AppendText(txt)
            let mutable line = editor.Document.GetLineByOffset(start) 
            if ty = ConsoleOut then //TODO exclude default print color, it should be same as foreground anyway                
                LineColors.Remove line.LineNumber |> ignore // claer any color that might exit from printing on same line before ( maybe just a return)
            else                
                //editor.Document.Insert( line.EndOffset, sprintf "(%d:%A)" line.LineNumber ty) //for DEBUG only
                //editor.AppendText(sprintf "(1st Line %d, %d chars:%A)" line.LineNumber line.Length ty) //for DEBUG only
                LineColors.[line.LineNumber] <- LogMessageType.getColor(ty) 
                line <- line.NextLine                    
                while line <> null  do
                    if line.Length>0 then // to exclude empty lines 
                        //editor.Document.Insert( line.EndOffset, sprintf "(%d:%A)" line.LineNumber ty) //for DEBUG only
                        //editor.AppendText(sprintf "(Line %d, %d chars:%A)" line.LineNumber line.Length ty)//for DEBUG only
                        LineColors.[line.LineNumber] <- LogMessageType.getColor(ty)
                    line <- line.NextLine

            //editor.AppendText("|-scroll->") //for DEBUG only
            editor.ScrollToEnd()


        /// adds string on UI thread  every 150ms then scrolls to end after 300ms
        /// sets line color on LineColors dictionay for DocumentColorizingTransformer
        member this.printOrBuffer (s:string,ty:LogMessageType) =
            async {
                do! Async.SwitchToContext Sync.syncContext 
                if prevMsgType<>ty then // print case 1, color change
                    printFromBufferAndScroll(prevMsgType) 
                    prevMsgType <- ty

                buffer.Append(s)  |> ignore 

                if stopWatch.ElapsedMilliseconds > 150L  && s.Contains(NewLine) then // print case 2, only add to document every 150ms
                    printFromBufferAndScroll(ty)                
                else                        
                    let k = Interlocked.Increment printCallsCounter
                    do! Async.Sleep 300
                    if !printCallsCounter = k  then //print case 3, it is the last call for 300 ms
                        printFromBufferAndScroll(ty)
                    
            } |> Async.StartImmediate 
        
        /// to acces the underlying read only Avalonedit Texteditor
        member this.Editor = editor



/// A static class that hold the single neded instance of LogView and provides print formating methods for the LogView
[<Sealed>]
type Log private () =    
    // just uing a let value  like (let Log = new LogView()) has some bugs in hosted context (Rhino), I think due to late initalizing
    // so here is a static class with explicit init

    static let mutable log : Logging.LogView = null 
    
    static member initialize() = log <- new Logging.LogView() //will be called in Conig.initialize()

    /// to acces the underlying read only Avalonedit Texteditor
    static member Editor = log.Editor

    //used in FSI constructor:
    static member val TextWriterFsiStdOut     = new Logging.FsxTextWriter(fun s -> log.printOrBuffer (s,FsiStdOut    ))
    static member val TextWriterFsiErrorOut   = new Logging.FsxTextWriter(fun s -> log.printOrBuffer (s,FsiErrorOut  ))
    static member val TextWriterConsoleOut    = new Logging.FsxTextWriter(fun s -> log.printOrBuffer (s,ConsoleOut   ))
    static member val TextWriterConsoleError  = new Logging.FsxTextWriter(fun s -> log.printOrBuffer (s,ConsoleError ))
    
    // used for printf formaters:                                                     
    static member val TextWriterInfoMsg       = new Logging.FsxTextWriter(fun s -> log.printOrBuffer (s,InfoMsg      ))
    static member val TextWriterFsiErrorMsg   = new Logging.FsxTextWriter(fun s -> log.printOrBuffer (s,FsiErrorMsg  ))
    static member val TextWriterAppErrorMsg   = new Logging.FsxTextWriter(fun s -> log.printOrBuffer (s,AppErrorMsg  ))
    static member val TextWriterIOErrorMsg    = new Logging.FsxTextWriter(fun s -> log.printOrBuffer (s,IOErrorMsg   ))
    static member val TextWriterDebugMsg      = new Logging.FsxTextWriter(fun s -> log.printOrBuffer (s,DebugMsg     ))
    static member val TextWriterPrintMsg      = new Logging.FsxTextWriter(fun s -> log.printOrBuffer (s,PrintMsg     ))


    //static member printFsiStdOut    s =  Printf.fprintfn Log.TextWriterFsiStdOut    s  //should not be used
    //static member printFsiErrorOut  s =  Printf.fprintfn Log.TextWriterFsiErrorOut  s
    //static member printConsoleOut   s =  Printf.fprintfn Log.TextWriterConsoleOut   s
    //static member printConsoleError s =  Printf.fprintfn Log.TextWriterConsoleError s
    static member printInfoMsg      s =  Printf.fprintfn Log.TextWriterInfoMsg      s
    static member printFsiErrorMsg  s =  Printf.fprintfn Log.TextWriterFsiErrorMsg  s
    static member printAppErrorMsg  s =  Printf.fprintfn Log.TextWriterAppErrorMsg  s
    static member printIOErrorMsg   s =  Printf.fprintfn Log.TextWriterIOErrorMsg   s        
    static member printDebugMsg     s =  Printf.fprintfn Log.TextWriterDebugMsg     s
    
    /// like printfn, use with format strings, adds new line
    static member print s             =  Printf.fprintfn Log.TextWriterPrintMsg     s