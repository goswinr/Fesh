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

        let newLinesBuffer= ref 0

        /// Add string to editor if it isn not just a new line.
        /// New lines get bufferd till next call
        /// returns start offset for coloring or -1 if nothing was added
        let addString (s) = 
            if s = NewLine then 
                Interlocked.Increment newLinesBuffer  |> ignore 
                -1
            else
                let st = editor.Document.TextLength
                let k = Interlocked.Exchange(newLinesBuffer , 0)
                match k with 
                | 0 -> editor.AppendText("+" + s)
                | 1 -> editor.AppendText("++" + NewLine + s)
                | 2 -> editor.AppendText("+++" + NewLine + NewLine + s)
                | x -> editor.AppendText("++++" + Text.StringBuilder(s.Length + x * 2).Insert(0, NewLine, x).Append(s).ToString())   
                st
        
        
        let offsetForColoring = ref 0

        let scrollToEndAndColorize(ty:LogMessageType, fromOffset:int) =
            let endOffset = editor.Document.TextLength
            offsetForColoring := endOffset
            if ty <> StdOut then 

                let start = 
                    let mutable r = fromOffset
                    while (let c = editor.Document.GetCharAt(r) in c='\r' || c='\n') do
                        r <- r+1
                    r
                let ende = 
                    let mutable r = endOffset
                    while (let c = editor.Document.GetCharAt(r) in c='\r' || c='\n') do
                        r <- r-1
                    r
                
                if fromOffset < endOffset then
                    let mutable line = editor.Document.GetLineByOffset(fromOffset)
                    LineColors.[line.LineNumber] <- LogMessageType.getColor(ty)
                    line <- line.NextLine                    
                    while line <> null && line.EndOffset <= endOffset do
                        LineColors.[line.LineNumber] <- LogMessageType.getColor(ty)
            editor.AppendText("*scroll*")
            editor.ScrollToEnd() // slow !!  https://github.com/icsharpcode/AvalonEdit/issues/15
        
        let printCallsCounter = ref 0L
        let scrollSkipedTimes = ref 0
        let printCallsType = ref StdOut
        

        /// adds string on UI thread then scrolls to end after 300ms , 50 lines or a color change
        /// adding just a new line character  is delayed till next text
        /// scroll to end and coloring is delayed too
        let addStrAndScroll (s,ty:LogMessageType) =
            async {
                do! Async.SwitchToContext Sync.syncContext 
                let prevType = Interlocked.Exchange (printCallsType, ty) // TODO Interlock not needed if always on UI thread ?
                if prevType <> ty then 
                    scrollToEndAndColorize(ty, !offsetForColoring)
                let startOffset = addString (s) 
                if startOffset >= 0 then 
                    if !scrollSkipedTimes> 100 then // scroll at least every 50 ( * 2) lines
                        scrollSkipedTimes := 0
                        scrollToEndAndColorize(ty, !offsetForColoring)
                    else
                        let k = Interlocked.Increment printCallsCounter
                        do! Async.Sleep 300
                        if !printCallsCounter = k  then //it is the last call for 300 ms
                            scrollToEndAndColorize(ty, !offsetForColoring)

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

    
