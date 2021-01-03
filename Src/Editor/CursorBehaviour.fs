namespace Seff.Editor


open Seff
open Seff.Model
open ICSharpCode.AvalonEdit
open Seff.Util
open Seff.Util.String
open Seff.Util.General
open System.Windows
open System
open ICSharpCode.AvalonEdit.Editing
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.Editing


module CursorBehaviour  =
    open Selection


    ///replace 'true' with 'false' and vice versa
    let toggleBoolean(avaEdit:TextEditor) = 
        let doc = avaEdit.Document
        doc.BeginUpdate()//avaEdit.Document.RunUpdate
        for seg in avaEdit.TextArea.Selection.Segments do
            if seg.Length = 5 then 
                let tx = doc.GetText(seg)
                if   tx = "false" then doc.Replace(seg, "true ") 
                elif tx = "true " then doc.Replace(seg, "false") // true with a space, so that it works in block selection
            elif seg.Length = 4 && doc.GetText(seg) = "true"  then 
                let afterOff = seg.EndOffset // do not add +1
                if doc.TextLength > (afterOff+1) && doc.GetCharAt(afterOff) = ' '  && not (Char.IsLetter(doc.GetCharAt(afterOff+1))) then // try to keep total length the same                    
                    doc.Remove(afterOff,1)
                doc.Replace(seg, "false")
        doc.EndUpdate()                    

    let insertAtCaretOrSelections (avaEdit:TextEditor, tx:string) = 
        if avaEdit.TextArea.Selection.IsEmpty then
            avaEdit.Document.Insert(avaEdit.TextArea.Caret.Offset, tx)
        else 
            avaEdit.Document.BeginUpdate()
            let mutable shift = 0
            for seg in avaEdit.TextArea.Selection.Segments do
                let segShifted = new SelectionSegment(seg.StartOffset+shift, seg.EndOffset+shift)
                avaEdit.Document.Replace(segShifted, tx) // becaus the next segment will not be correcty anymore after this insert
                shift <- shift - seg.Length + tx.Length
            avaEdit.Document.EndUpdate()

    let previewTextInput(avaEdit:TextEditor,log:ISeffLog, e:Input.TextCompositionEventArgs) = 
        match getSelection(avaEdit.TextArea,log) with 
        | NoSel -> 
            match e.Text with 
            // space before and after:
            //| "=" //TODO check previous char is not punctuation for <= // space is added in previewKeyDown before return 
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
                insertAtCaretOrSelections (avaEdit, c+" ")
                e.Handled <- true     
                // TODO raise TextArea.TextEntered Event ?
            | _ -> ()
        
        | RegSel _ -> ()

        | RectSelEmpty rs ->
            let s = Selection.getRectSelPos rs
            RectangleSelection.previewTextInput(e.Text,s,avaEdit,log)
            e.Handled <- true 
            // TODO raise TextArea.TextEntered Event ?
           

        | RectSel rs -> 
            let s = Selection.getRectSelPos rs
            RectangleSelection.previewTextInputNonEmpty(e.Text,s,avaEdit,log)
            e.Handled <- true 
            // TODO raise TextArea.TextEntered Event ?
            

        
    let previewKeyDown (avaEdit:TextEditor, compls:Completions, log:ISeffLog, e: Input.KeyEventArgs) =  
        match e.Key with  
        |Input.Key.Back -> 
            if compls.IsNotOpen then 
                match getSelection(avaEdit.TextArea,log) with 
                | NoSel -> 
                    // --- Removes 4 charactes (Options.IndentationSize) ---
                    // --- on pressing backspace key instead of one ---                
                    let line = avaEdit.Document.GetText(avaEdit.Document.GetLineByOffset(avaEdit.CaretOffset)) // = get current line
                    let car = avaEdit.TextArea.Caret.Column
                    let prevC = line.Substring(0 ,car-1)
                    //log.PrintfnDebugMsg "--Substring length %d: '%s'" prevC.Length prevC
                    if prevC.Length > 0 && avaEdit.TextArea.Selection.IsEmpty then //TODO or also use to replace selected text ??
                        if isJustSpaceCharsOrEmpty prevC  then
                            let dist = prevC.Length % avaEdit.Options.IndentationSize
                            let clearCount = if dist = 0 then avaEdit.Options.IndentationSize else dist
                            //log.PrintfnDebugMsg "--Clear length: %d " clearCount
                            avaEdit.Document.Remove(avaEdit.CaretOffset - clearCount, clearCount)
                            e.Handled <- true // to not actually delete one char

                | RectSelEmpty rs ->
                    let s = Selection.getRectSelPos rs
                    RectangleSelection.backSpaceEmpty(s,avaEdit,log)
                    e.Handled <- true // to not use the avaedit delete 
                    // TODO raise TextEntered Event ?
                    ()

                | RectSel rs ->
                    let s = Selection.getRectSelPos rs
                    RectangleSelection.deleteNonEmpty(s,avaEdit,log)
                    e.Handled <- true // to not use the avaedit delete 
                    // TODO raise TextEntered Event ?
                    ()

                | RegSel _ -> ()
        
       
        |Input.Key.Delete ->
            if compls.IsNotOpen then 
                match getSelection(avaEdit.TextArea,log) with 
                | NoSel -> 
                     // --- Removes rest of line too if only whitespacxe ---
                     // --- also remove whitespace at stert of next line  ---               
                    let caretOff = avaEdit.CaretOffset
                    let line = avaEdit.Document.GetLineByOffset(caretOff) // = get current line 
                    let endOff = line.EndOffset 
                    let len = endOff - caretOff
                    let txt = avaEdit.Document.GetText(caretOff,len)                
                    if isJustSpaceCharsOrEmpty txt  then
                        let nextLine = line.NextLine
                        if notNull nextLine then 
                            //avaEdit.Document.BeginUpdate()
                            // also remove spaces at start of next line 
                    
                            //delete max up to caret pos on next line: 
                            let caretPosInLine = caretOff - line.Offset
                            let nextTxt = avaEdit.Document.GetText(line.NextLine) 
                            let nextLnSpacesAtStart = spacesAtStart(nextTxt) 

                            let delLengthOnNextLine = min nextLnSpacesAtStart caretPosInLine // to NOT delete all starting whispace on next line
                            //let delLengthOnNextLine = nextLnSpacesAtStart // to  delete all starting whispace on next line

                            let lenToDelete = endOff - caretOff + 2 + delLengthOnNextLine // + 2 for \r\n
                            avaEdit.Document.Remove(caretOff, lenToDelete)
                    
                            // now after this change ensure one space remains at caret
                            let prev = avaEdit.Document.GetCharAt(caretOff-1)
                            let next = avaEdit.Document.GetCharAt(caretOff)
                            let prevIsChar = not (Char.IsWhiteSpace(prev))
                            let nextIsChar = not (Char.IsWhiteSpace(next))
                            if  prevIsChar && nextIsChar then 
                                avaEdit.Document.Insert(caretOff, " ") 
                    
                            e.Handled <- true // to not actually delete one char
                            //avaEdit.Document.EndUpdate()

                | RectSelEmpty rs ->
                    let s = Selection.getRectSelPos rs
                    RectangleSelection.deleteKeyEmpty(s,avaEdit,log)
                    e.Handled <- true
                    // TODO raise TextEntered Event ?
                    ()

                | RectSel rs -> 
                    let s = Selection.getRectSelPos rs
                    RectangleSelection.deleteNonEmpty(s,avaEdit,log)
                    e.Handled <- true
                    // TODO raise TextEntered Event ?
                    ()

                | RegSel _ -> ()

        

        // add indent after do, for , ->, =
        |Input.Key.Return 
        |Input.Key.Enter ->
            if hasNoSelection avaEdit.TextArea  then // TODO what happens if there is a selction ??
                let caret = avaEdit.CaretOffset
                let line = avaEdit.Document.GetLineByOffset(caret)            
                let txt = avaEdit.Document.GetText(line) // = get current line
                let caretPosInLine = caret - line.Offset
                let isCaretAtEnd = String.IsNullOrWhiteSpace (txt.[caretPosInLine .. line.EndOffset]) // ensure caret is at end off line !
                //log.PrintfnDebugMsg "line:%s" txt
                //log.PrintfnDebugMsg "caretPosInLine:%d isCaretAtEnd:%b" caretPosInLine isCaretAtEnd
                let trimmed = txt.TrimEnd()
                if isCaretAtEnd && avaEdit.TextArea.Selection.IsEmpty then //TODO or also use to replace selected text ??
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
                            avaEdit.Document.Insert(avaEdit.CaretOffset, " " + Environment.NewLine + String(' ',ind)) // add space before too for nice position of folding block
                            e.Handled <- true // to not actually add anothe new line
                            // TODO raise TextEntered Event ?
        

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
                let printGreen = ed.Log.PrintfnCustomColor 0 150 0
               

                let fs = (e.Data.GetData DataFormats.FileDrop :?> string []) |> Array.sort |> Array.rev // to get file path 
                if fs.Length > 2 && Array.forall isDll fs then      // TODO make path relatriv to script location    
                    for f in fs  do 
                        let file = IO.Path.GetFileName(f)
                        avaEdit.Document.Insert (0, sprintf "#r \"%s\"\r\n" file)
                        ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line 0: %s"  file
                    let folder = IO.Path.GetDirectoryName(fs.[0])
                    avaEdit.Document.Insert (0, sprintf "#I @\"%s\"\r\n" folder)                    
                    ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line 0: %s"  folder
                else
                    for f in fs do
                        if isDll f then                            
                            let txt = sprintf "#r @\"%s\"\r\n" f
                            avaEdit.Document.Insert (0, txt )
                            ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line 0:"
                            printGreen "  %s" txt
                        elif isFsx f  then
                            let txt = sprintf "#load @\"%s\"\r\n" f
                            avaEdit.Document.Insert (0, txt)                            
                            ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line 0:" 
                            printGreen "  %s" txt
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
                                    ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line %d:" p.line 
                                    printGreen "  %s" f
                                    ed.Log.PrintfnInfoMsg "  Previous Line at that position is commented out below:"
                                    ed.Log.PrintfnCustomColor 120 120 120 "  %s" prev
                                else
                                    avaEdit.Document.Insert (p.offset , sprintf "@\"%s\" //" f )
                                    ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line %d:" lnNo.LineNumber
                                    printGreen "  %s" f
                                    ed.Log.PrintfnInfoMsg "  Previous Line content is commented out:" 
                                    ed.Log.PrintfnCustomColor 120 120 120  "  %s" prev
                            | None ->   
                                let lnNo = avaEdit.Document.GetLineByOffset(avaEdit.CaretOffset)
                                ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line %d:" lnNo.LineNumber
                                printGreen "  %s" f
                                avaEdit.Document.Insert (avaEdit.CaretOffset , sprintf " @\"%s\"%s" f Environment.NewLine)
                            
            with e -> log.PrintfnIOErrorMsg "drag and drop failed: %A" e

