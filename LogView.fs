namespace Seff

open System
open System.Environment
open System.IO
open System.Threading
open Seff.Model
open ICSharpCode
open System.Windows.Media // for color brushes
open Seff.Model

module Logging =

    /// A TextWriter that writes using a function (to an Avalonedit Control). used in FSI session constructor   
    type FsxTextWriter(writeStr) =
        inherit TextWriter()
        override this.Encoding =  Text.Encoding.Default
        override this.Write     (s:string)  = writeStr (s)
        override this.WriteLine (s:string)  = writeStr (s + Environment.NewLine)    // actually never used see  https://github.com/dotnet/fsharp/issues/3712   
        override this.WriteLine ()          = writeStr (    Environment.NewLine)    
    
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
        let scrollSkipedTimes = ref 0
        let newLinesBuffer= ref 0
        

        /// adds string on UI thread then scrolls to end after 300ms , 50 lines or a color change
        /// adding just a new line character  is delayed till next text
        /// scroll to end and coloring is delayed too
        let addStrAndScroll (s,ty:LogMessageType) =
            async {
                if s = NewLine then 
                    Interlocked.Increment newLinesBuffer  |> ignore 
                else
                    do! Async.SwitchToContext Sync.syncContext 
                    let start = editor.Document.TextLength
                    let k = Interlocked.Exchange(newLinesBuffer , 0)
                    match k with 
                    | 0 -> editor.AppendText("|+| " + s)
                    | 1 -> editor.AppendText( NewLine + "|++| "+ s)
                    | 2 -> editor.AppendText( NewLine + NewLine + "|+++| " + s)
                    | x -> editor.AppendText(Text.StringBuilder(s.Length + x * 2).Insert(0, NewLine, x).Append("|++++| ").Append(s).ToString())   


                    let mutable line = editor.Document.GetLineByOffset(start)
                    LineColors.[line.LineNumber] <- LogMessageType.getColor(ty)
                    line <- line.NextLine                    
                    while line <> null  do
                        LineColors.[line.LineNumber] <- LogMessageType.getColor(ty)
                        line <- line.NextLine

                    if !scrollSkipedTimes> 100 then // scroll at least every 50 ( * 2) lines
                        scrollSkipedTimes := 0
                        editor.AppendText("*scroll*")
                        editor.ScrollToEnd()
                    else
                        let k = Interlocked.Increment printCallsCounter
                        do! Async.Sleep 300
                        if !printCallsCounter = k  then //it is the last call for 300 ms
                            editor.AppendText("*scroll*")
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

        member val InfoMsgTextWriter  = new FsxTextWriter(fun s -> addStrAndScroll (s,InfoMsg) )

        /// like printfn, use with format strings, adds new line
        member this.print s =  Printf.fprintfn this.InfoMsgTextWriter s
            
    
    
    let Log = new LogView()

