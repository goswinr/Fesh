namespace Seff.Editor


open Seff
open ICSharpCode
open ICSharpCode.AvalonEdit
open System.Windows.Media
open Seff.Config
open Seff.Util.String
open FSharp.Compiler.SourceCodeServices
open System.Windows
open System.IO
open System

module CursorBehaviour  =
    
    
    
    let previewKeyDown (avaEdit:TextEditor, e: Input.KeyEventArgs) =  

        match e.Key with
        /// Removes 4 charactes (Options.IndentationSize) on pressing backspace key instead of one 
        |Input.Key.Back ->
            let line = avaEdit.Document.GetText(avaEdit.Document.GetLineByOffset(avaEdit.CaretOffset)) // = get current line
            let car = avaEdit.TextArea.Caret.Column
            let prevC = line.Substring(0 ,car-1)
            //log.PrintDebugMsg "--Substring length %d: '%s'" prevC.Length prevC
            if prevC.Length > 0 then 
                if isJustSpaceCharsOrEmpty prevC  then
                    let dist = prevC.Length % avaEdit.Options.IndentationSize
                    let clearCount = if dist = 0 then avaEdit.Options.IndentationSize else dist
                    //log.PrintDebugMsg "--Clear length: %d " clearCount
                    avaEdit.Document.Remove(avaEdit.CaretOffset - clearCount, clearCount)
                    e.Handled <- true // to not actually delete one char

        // add indent after do, for , ->, =
        |Input.Key.Return ->
            let line = avaEdit.Document.GetText(avaEdit.Document.GetLineByOffset(avaEdit.CaretOffset)) // = get current line
            if     line.EndsWith " do"
                || line.EndsWith " then"
                || line.EndsWith " else"
                || line.EndsWith "="
                || line.EndsWith "->" then                    
                    let st = spacesAtStart line
                    let rem = st % avaEdit.Options.IndentationSize
                    let ind = 
                        if rem  = 0 then  st + avaEdit.Options.IndentationSize // enure new indent is a multiple of avaEdit.Options.IndentationSize
                        elif rem = 1 then st + avaEdit.Options.IndentationSize + avaEdit.Options.IndentationSize - 1 // to indent always at leat 2 chars
                        else              st + avaEdit.Options.IndentationSize - rem
                    avaEdit.Document.Insert(avaEdit.CaretOffset, Environment.NewLine + String(' ',ind))
                    e.Handled <- true // to not actually add anothe new line

        | _ -> ()


    let dragAndDrop (avaEdit:TextEditor, log:ISeffLog,  e:DragEventArgs) =
        if e.Data.GetDataPresent DataFormats.FileDrop then
            let isDll (p:string) = p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||  p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                
            try
                let fs = (e.Data.GetData DataFormats.FileDrop :?> string []) |> Array.sort |> Array.rev // to get file path 
                if fs.Length > 2 && Array.forall isDll fs then      // TODO make path relatriv to script location    
                    for f in fs  do 
                        let file = IO.Path.GetFileName(f)
                        avaEdit.Document.Insert (0, sprintf "#r \"%s\"\r\n" file)
                    let folder = IO.Path.GetDirectoryName(fs.[0])
                    avaEdit.Document.Insert (0, sprintf "#I @\"%s\"\r\n" folder)                    
                else
                    for f in fs do
                        if isDll f then 
                            avaEdit.Document.Insert (0, sprintf "#r @\"%s\"\r\n" f)
                        elif f.EndsWith(".fsx", StringComparison.OrdinalIgnoreCase)  then 
                            avaEdit.Document.Insert (0, sprintf "#load @\"%s\"\r\n" f)                            
                        else 
                            avaEdit.Document.Insert (avaEdit.CaretOffset , sprintf " @\"%s\"\r\n" f)
                            
            with e -> log.PrintIOErrorMsg "full drop failed: %A" e
                





