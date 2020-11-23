namespace Seff.Editor


open Seff
open Seff.Model
open ICSharpCode.AvalonEdit
open Seff.Util.String
open System.Windows
open System
open System.Windows.Media

module CursorBehaviour  =
    
    let toggleBoolean(avaEdit:TextEditor) = 
        let doc = avaEdit.Document
        for seg in avaEdit.TextArea.Selection.Segments do
            if seg.Length = 5 then 
                let tx = doc.GetText(seg)
                if   tx = "false" then doc.Replace(seg, "true ") 
                elif tx = "true " then doc.Replace(seg, "false") // true with a space, so that it works in block selection
            elif seg.Length = 4 && doc.GetText(seg) = "true"  then 
                let afterOff = seg.EndOffset // do not add +1
                if doc.TextLength > afterOff && doc.GetCharAt(afterOff) = ' ' then // try to keep total length the same                    
                    doc.Remove(afterOff,1)
                doc.Replace(seg, "false")


    let previewTextInput(avaEdit:TextEditor, a:Input.TextCompositionEventArgs) = 
        match a.Text with 
        // space before and after:
        //| "=" //TODO check previous char is not punctuation for <=
        //| ">" //TODO check previous char is not punctuation
        //| "+"  as c -> 
            //avaEdit.Document.Insert(avaEdit.TextArea.Caret.Offset, " "+c+" ") // space before and after
            //a.Handled <- true
        
        //TODO check previous char is not punctuation
        // space  before:
        //| "-" as c -> //not both because of -> and -1
        //    avaEdit.Document.Insert(avaEdit.TextArea.Caret.Offset, " "+c)
        //    a.Handled <- true
        
        // space  after:
        | ")"
        | ","
        | ";"  as c -> 
            avaEdit.Document.Insert(avaEdit.TextArea.Caret.Offset, c+" ")
            a.Handled <- true
        
        | _ -> ()

    let previewKeyDown (avaEdit:TextEditor,log:ISeffLog, e: Input.KeyEventArgs) =  

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
            let caret = avaEdit.CaretOffset
            let line = avaEdit.Document.GetLineByOffset(caret)            
            let txt = avaEdit.Document.GetText(line) // = get current line
            let caretPosInLine = caret - line.Offset
            let isCaretAtEnd = String.IsNullOrWhiteSpace (txt.[caretPosInLine .. line.EndOffset]) // ensure caret is at end off line !
            //log.PrintDebugMsg "line:%s" txt
            //log.PrintDebugMsg "caretPosInLine:%d isCaretAtEnd:%b" caretPosInLine isCaretAtEnd
            let trimmed = txt.TrimEnd()
            if isCaretAtEnd then 
                if     trimmed.EndsWith " do"
                    || trimmed.EndsWith " then"
                    || trimmed.EndsWith " else"
                    || trimmed.EndsWith "="
                    || trimmed.EndsWith "("
                    || trimmed.EndsWith "["
                    || trimmed.EndsWith "{"
                    || trimmed.EndsWith "[|"
                    || trimmed.EndsWith "->" then                    
                        let st = spacesAtStart trimmed
                        let rem = st % avaEdit.Options.IndentationSize
                        let ind = 
                            if rem  = 0 then  st + avaEdit.Options.IndentationSize // enure new indent is a multiple of avaEdit.Options.IndentationSize
                            elif rem = 1 then st + avaEdit.Options.IndentationSize + avaEdit.Options.IndentationSize - 1 // to indent always at leat 2 chars
                            else              st + avaEdit.Options.IndentationSize - rem
                        avaEdit.Document.Insert(avaEdit.CaretOffset, " " + Environment.NewLine + String(' ',ind)) // add space before to for nice position of folding block
                        e.Handled <- true // to not actually add anothe new line

        | _ -> ()


    let dragAndDrop (ed:IEditor, log:ISeffLog,  e:DragEventArgs) =
        let avaEdit = ed.AvaEdit
        if e.Data.GetDataPresent DataFormats.FileDrop then
            let isDll (p:string) = p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||  p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            let isFsx (p:string) = p.EndsWith(".fsx", StringComparison.OrdinalIgnoreCase) ||  p.EndsWith(".fs", StringComparison.OrdinalIgnoreCase)

            let findInsertion (code:string) =    
                match Util.Parse.findWordAhead "[<Literal>]" 0 code with 
                | Some p  -> 
                    Util.Parse.findWordAhead "@\"" p.offset code 
                | None -> 
                    let rec allRefs off =  // loop to skip over the #r and #I statments                   
                        match Util.Parse.findWordAhead "#" off code with
                        | Some p -> allRefs (p.offset + 7)  // gap of 7 between #r or #load and @"C:\...
                        | None -> off
                    Util.Parse.findWordAhead "@\"" (allRefs 0) code 

                
            try
                let insertColor = Brushes.DarkGreen
                insertColor.Freeze()

                let fs = (e.Data.GetData DataFormats.FileDrop :?> string []) |> Array.sort |> Array.rev // to get file path 
                if fs.Length > 2 && Array.forall isDll fs then      // TODO make path relatriv to script location    
                    for f in fs  do 
                        let file = IO.Path.GetFileName(f)
                        avaEdit.Document.Insert (0, sprintf "#r \"%s\"\r\n" file)
                        ed.Log.PrintInfoMsg "Drag & Drop inserted at Line 0: %s"  file
                    let folder = IO.Path.GetDirectoryName(fs.[0])
                    avaEdit.Document.Insert (0, sprintf "#I @\"%s\"\r\n" folder)                    
                    ed.Log.PrintInfoMsg "Drag & Drop inserted at Line 0: %s"  folder
                else
                    for f in fs do
                        if isDll f then                            
                            let txt = sprintf "#r @\"%s\"\r\n" f
                            avaEdit.Document.Insert (0, txt )
                            ed.Log.PrintInfoMsg "Drag & Drop inserted at Line 0:"
                            ed.Log.PrintCustomBrush insertColor "  %s" txt
                        elif isFsx f  then
                            let txt = sprintf "#load @\"%s\"\r\n" f
                            avaEdit.Document.Insert (0, txt)                            
                            ed.Log.PrintInfoMsg "Drag & Drop inserted at Line 0:" 
                            ed.Log.PrintCustomBrush insertColor "  %s" txt
                        else 
                            match findInsertion avaEdit.Document.Text with 
                            | Some p -> 
                                let lnNo = avaEdit.Document.GetLineByOffset(p.offset)
                                let line = avaEdit.Document.GetText(lnNo) // = get current line
                                let prev = line.Trim()
                                let isNewLn = line.TrimStart().StartsWith "@"
                                if isNewLn then                                    
                                    let st = String(' ',spacesAtStart line)                                    
                                    avaEdit.Document.Insert (p.offset , sprintf "@\"%s\"%s%s//" f Environment.NewLine st ) 
                                    ed.Log.PrintInfoMsg "Drag & Drop inserted at Line %d:" p.line 
                                    ed.Log.PrintCustomBrush insertColor "  %s" f
                                    ed.Log.PrintInfoMsg "  Previous Line at that position is commented out below:"
                                    ed.Log.PrintCustomColor 120 120 120 "  %s" prev
                                else
                                    avaEdit.Document.Insert (p.offset , sprintf "@\"%s\" //" f )
                                    ed.Log.PrintInfoMsg "Drag & Drop inserted at Line %d:" lnNo.LineNumber
                                    ed.Log.PrintCustomBrush insertColor "  %s" f
                                    ed.Log.PrintInfoMsg "  Previous Line content is commented out:" 
                                    ed.Log.PrintCustomColor 120 120 120  "  %s" prev
                            | None ->   
                                let lnNo = avaEdit.Document.GetLineByOffset(avaEdit.CaretOffset)
                                ed.Log.PrintInfoMsg "Drag & Drop inserted at Line %d:" lnNo.LineNumber
                                ed.Log.PrintCustomBrush insertColor "  %s" f
                                avaEdit.Document.Insert (avaEdit.CaretOffset , sprintf " @\"%s\"%s" f Environment.NewLine)
                            
            with e -> log.PrintIOErrorMsg "drag and drop failed: %A" e
                
