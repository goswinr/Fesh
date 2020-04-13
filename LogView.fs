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
        let stopWatch = Stopwatch.StartNew()
        let buffer =  new StringBuilder()

        /// adds string on UI thread  every 150ms then scrolls to end after 300ms
        /// sets line color on LineColors dictionay for DocumentColorizingTransformer
        let addStrAndScroll (s:string,ty:LogMessageType) =
            async {
                do! Async.SwitchToContext Sync.syncContext 
                buffer.Append(s)  |> ignore 
                
                if stopWatch.ElapsedMilliseconds > 150L && s.Contains(NewLine) then //only add top document every 150ms
                    stopWatch.Restart()
                    let start = editor.Document.TextLength
                    let txt = buffer.ToString()
                    buffer.Clear()  |> ignore 
                    editor.AppendText(txt)
                    
                    let mutable line = editor.Document.GetLineByOffset(start) 
                    LineColors.[line.LineNumber] <- LogMessageType.getColor(ty) //only color this line if it does not start with a new line chatacter
                    line <- line.NextLine                    
                    while line <> null  do
                        LineColors.[line.LineNumber] <- LogMessageType.getColor(ty)
                        line <- line.NextLine

                    //editor.AppendText("|-scroll->") //for DEBUG only
                    editor.ScrollToEnd()
                
                else                        
                    let k = Interlocked.Increment printCallsCounter
                    do! Async.Sleep 300
                    if !printCallsCounter = k  then //it is the last call for 300 ms
                        let txt = buffer.ToString()
                        buffer.Clear()  |> ignore 
                        editor.AppendText(txt)
                        //editor.AppendText("|*scroll*>") //for DEBUG only
                        editor.ScrollToEnd()
                    
            } |> Async.StartImmediate 



        /// to acces the underlying Avalonedit Texteditor
        member this.Editor = editor

        member this.printStdOut   (s) = addStrAndScroll(s,StdOut  )
        member this.printErrorOut (s) = addStrAndScroll(s,ErrorOut)
        member this.printInfoMsg  (s) = addStrAndScroll(s,InfoMsg )
        member this.printAppError (s) = addStrAndScroll(s,AppError)
        member this.printIOError  (s) = addStrAndScroll(s,IOError )
        member this.printDebugMsg (s) = addStrAndScroll(s,DebugMsg)


        ///for FSI session constructor
        member val StdOutTextWriter    = new FsxTextWriter(fun s -> addStrAndScroll (s,StdOut) )
        
        ///for FSI session constructor
        member val ErrorOutTextWriter  = new FsxTextWriter(fun s -> addStrAndScroll (s,ErrorOut) )

        /// a Textwriter for Log.print Formating
        member val InfoMsgTextWriter  = new FsxTextWriter(fun s -> addStrAndScroll (s,InfoMsg) )

        /// like printfn, use with format strings, adds new line
        member this.print s =  Printf.fprintfn this.InfoMsgTextWriter s
            
    
    
    let Log = new LogView()

