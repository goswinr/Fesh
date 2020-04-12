namespace Seff

open System
open System.Environment
open System.IO
open System.Threading
open Seff.Model
open ICSharpCode
open System.Windows.Media // for color brushes

module Logging =

    // This hole class is trying to work around double UI update in printfn  for better UI performance
    // see  https://github.com/dotnet/fsharp/issues/3712   


    /// A TextWriter that writes using a function (to an Avalonedit Control). used in FSI session constructor   
    type FsxTextWriter(writeStr) =
        inherit TextWriter()
        override this.Encoding =  Text.Encoding.Default
        override this.Write     (s:string)  = writeStr (s)
        override this.WriteLine (s:string)  = writeStr (s + Environment.NewLine)    // actually never used see  https://github.com/dotnet/fsharp/issues/3712   
        override this.WriteLine ()          = writeStr (    Environment.NewLine) 
    
   

   type TextColorizer(editor:AvalonEdit.TextEditor,startOffset,endOffset) = 
        inherit AvalonEdit.Rendering.DocumentColorizingTransformer()

        let colorizeLine(line:AvalonEdit.Document.DocumentLine) =       
           if (not line.IsDeleted) then
               this.ChangeLinePart(line.Offset, line.EndOffset, fun element -> element.TextRunProperties.SetForegroundBrush(Brushes.Red))
    
        override this.ColorizeText(startOffset,endOffset) = 
            let startLine = editor.Document.GetLineByOffset(startOffset)
            let startLineEnd = startLine.EndOffset
            if startLineEnd > endOffset 
                this.ChangeLinePart(  startOffset, line.EndOffset, fun element -> element.TextRunProperties.SetForegroundBrush(Brushes.Red))
            let  = editor.Document.GetLineByOffset(startOffset)




    type LogView () =
        let editor =  
            let e = AvalonEdit.TextEditor()
            e.IsReadOnly <- true
            e.Encoding <- Text.Encoding.Default
            e

        let newLinesBuffer= ref 0

        /// Add string to editor if it isn not just a new line.
        /// New lines get bufferd till next call
        /// returns start offset for coloring or -1 if nothing was added
        let addStr (s) = 
            if s = NewLine then 
                Interlocked.Increment newLinesBuffer  |> ignore 
                -1
            else
                let st = editor.Document.TextLength
                let k = Interlocked.Exchange(newLinesBuffer , 0)
                match k with 
                | 0 -> editor.AppendText(s)
                | 1 -> editor.AppendText(NewLine + s)
                | 2 -> editor.AppendText(NewLine + NewLine + s)
                | x  -> editor.AppendText(Text.StringBuilder(s.Length + x * 2).Insert(0, NewLine, x).Append(s).ToString())   
                st
        
        
        let offsetForColoring = ref 0

        let scrollToEndAndColorize(ty:LogMessageType, fromOffset:int) =
            let endOffset = editor.Document.TextLength
            offsetForColoring := endOffset
            editor.ScrollToEnd() // slow !!  https://github.com/icsharpcode/AvalonEdit/issues/15

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

                match ty with
                | StdOut    -> () // already exluded above
                | ErrorOut  -> () // exlude leading new lines
                | InfoMsg   -> ()
                | AppError  -> ()
                | IOError   -> ()
                | DebugMsg  -> ()

        
        let printCallsCounter = ref 0L
        let scrollSkipedTimes = ref 0
        let printCallsType = ref StdOut
        

        /// adds string on UI thread and scrolls to end after 300ms,addas coloring
        let addStrAndScroll (s,ty:LogMessageType) =
            async {
                do! Async.SwitchToContext Sync.syncContext 
                let prevType = Interlocked.Exchange (printCallsType, ty)
                if prevType <> ty then 
                    scrollToEndAndColorize(ty, !offsetForColoring)
                let startOffset = addStr (s) 
                if startOffset >= 0 then 
                    if !scrollSkipedTimes> 100 then // scroll at least ever 50 ( * 2) lines
                        scrollSkipedTimes := 0
                        scrollToEndAndColorize(ty, !offsetForColoring)
                    else
                        let k = Interlocked.Increment printCallsCounter
                        do! Async.Sleep 300
                        if !printCallsCounter = k  then //its the last call for 300 ms
                            scrollToEndAndColorize(ty, !offsetForColoring)

            } |> Async.StartImmediate 



        /// to acces the underlying Avalonedit Texteditor
        member this.Editor = editor


    
