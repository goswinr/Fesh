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
    let private LineColors = Collections.Generic.Dictionary<int,SolidColorBrush>()

    type private LogLineColorizer() = 
        inherit AvalonEdit.Rendering.DocumentColorizingTransformer()
        override this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) =       
           let ok,color = LineColors.TryGetValue(line.LineNumber)
           if ok && not line.IsDeleted then
               base.ChangeLinePart(line.Offset, line.EndOffset, fun element -> element.TextRunProperties.SetForegroundBrush(color))
       

    type LogView () =
        let editor =  
            let e = AvalonEdit.TextEditor()
            e.IsReadOnly <- true
            e.Encoding <- Text.Encoding.Default
            e.TextArea.TextView.LineTransformers.Add(new LogLineColorizer())
            e
        
        // The below functions are trying to work around double UI update in printfn for better UI performance
        // see  https://github.com/dotnet/fsharp/issues/3712   

        
        let printCallsCounter = ref 0L
        let mutable prevMsgType = IOErrorMsg
        let stopWatch = Stopwatch.StartNew()
        let buffer =  new StringBuilder()


        let printFromBufferAndScroll(ty:LogMessageType) = 
            prevMsgType <- ty
            stopWatch.Restart()
            
            let txt = buffer.ToString().Replace(NewLine, sprintf "(%A)%s" ty NewLine)             
            buffer.Clear()  |> ignore 
            let start = editor.Document.TextLength
            editor.AppendText(txt)
            
            let mutable line = editor.Document.GetLineByOffset(start) 
            //editor.Document.Insert( line.EndOffset, sprintf "(%d:%A)" line.LineNumber ty)
            //editor.AppendText(sprintf "(1st Line %d, %d chars:%A)" line.LineNumber line.Length ty)
            LineColors.[line.LineNumber] <- LogMessageType.getColor(ty) //only color this line if it does not start with a new line chatacter
            line <- line.NextLine                    
            while line <> null  do
                if line.Length>0 then
                    //editor.Document.Insert( line.EndOffset, sprintf "(%d:%A)" line.LineNumber ty)
                    //editor.AppendText(sprintf "(Line %d, %d chars:%A)" line.LineNumber line.Length ty)
                    LineColors.[line.LineNumber] <- LogMessageType.getColor(ty)
                line <- line.NextLine

            //editor.AppendText("|-scroll->") //for DEBUG only
            editor.ScrollToEnd()


        /// adds string on UI thread  every 150ms then scrolls to end after 300ms
        /// sets line color on LineColors dictionay for DocumentColorizingTransformer
        let printOrBuffer (s:string,ty:LogMessageType) =
            async {
                do! Async.SwitchToContext Sync.syncContext 
                buffer.Append(s)  |> ignore 

                if (prevMsgType<>ty || stopWatch.ElapsedMilliseconds > 150L ) && s.Contains(NewLine) then //only add to document every 150ms
                    printFromBufferAndScroll(ty)
                
                else                        
                    let k = Interlocked.Increment printCallsCounter
                    do! Async.Sleep 300
                    if !printCallsCounter = k  then //it is the last call for 300 ms
                        printFromBufferAndScroll(ty)
                    
            } |> Async.StartImmediate 



        /// to acces the underlying Avalonedit Texteditor
        member this.Editor = editor

        //used in fsi constructor:
        member val TextWriterFsiStdOut     = new FsxTextWriter(fun s -> printOrBuffer (s,FsiStdOut    ))
        member val TextWriterFsiErrorOut   = new FsxTextWriter(fun s -> printOrBuffer (s,FsiErrorOut  ))
        member val TextWriterConsoleOut    = new FsxTextWriter(fun s -> printOrBuffer (s,ConsoleOut   ))
        member val TextWriterConsoleError  = new FsxTextWriter(fun s -> printOrBuffer (s,ConsoleError ))
        // used for printf:
        member val TextWriterInfoMsg       = new FsxTextWriter(fun s -> printOrBuffer (s,InfoMsg      ))
        member val TextWriterFsiErrorMsg   = new FsxTextWriter(fun s -> printOrBuffer (s,FsiErrorMsg  ))
        member val TextWriterAppErrorMsg   = new FsxTextWriter(fun s -> printOrBuffer (s,AppErrorMsg  ))
        member val TextWriterIOErrorMsg    = new FsxTextWriter(fun s -> printOrBuffer (s,IOErrorMsg   ))
        member val TextWriterDebugMsg      = new FsxTextWriter(fun s -> printOrBuffer (s,DebugMsg     ))
        member val TextWriterPrintMsg      = new FsxTextWriter(fun s -> printOrBuffer (s,PrintMsg     ))

            
        //member this.printFsiStdOut    s =  Printf.fprintfn this.TextWriterFsiStdOut    s  //should not be used
        //member this.printFsiErrorOut  s =  Printf.fprintfn this.TextWriterFsiErrorOut  s
        //member this.printConsoleOut   s =  Printf.fprintfn this.TextWriterConsoleOut   s
        //member this.printConsoleError s =  Printf.fprintfn this.TextWriterConsoleError s
        member this.printInfoMsg      s =  Printf.fprintfn this.TextWriterInfoMsg      s
        member this.printFsiErrorMsg  s =  Printf.fprintfn this.TextWriterFsiErrorMsg  s
        member this.printAppErrorMsg  s =  Printf.fprintfn this.TextWriterAppErrorMsg  s
        member this.printIOErrorMsg   s =  Printf.fprintfn this.TextWriterIOErrorMsg   s        
        member this.printDebugMsg     s =  Printf.fprintfn this.TextWriterDebugMsg     s

        /// like printfn, use with format strings, adds new line
        member this.print s             =  Printf.fprintfn this.TextWriterPrintMsg     s
    
    let Log = new LogView()

