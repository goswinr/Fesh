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



    let previewTextInput(ed:IEditor, e:Input.TextCompositionEventArgs) = 
         //if not ed.IsComplWinOpen then  
            match getSelType(ed.AvaEdit.TextArea) with 
            | NoSel 
            | RegSel -> 
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
                    Selection.insertAtCaretOrSelection (ed.AvaEdit, c+" ")
                    e.Handled <- true // TODO raise TextArea.TextEntered Event ?    
                
                | _ -> ()
        

            | RectSel ->  RectangleSelection.insertText(ed, e.Text) ; e.Handled <- true 
        
        
    let previewKeyDown (ed:IEditor, e: Input.KeyEventArgs) =  
        //if not ed.IsComplWinOpen then  
            match e.Key with  
            |Input.Key.Back ->           
                let ta = ed.AvaEdit.TextArea
                match getSelType(ta) with 
                | NoSel -> 
                    // --- Removes 4 charactes (Options.IndentationSize) ---
                    // --- on pressing backspace key instead of one ---                    
                    let doc = ed.AvaEdit.Document
                    let line = doc.GetText(doc.GetLineByOffset(ta.Caret.Offset)) // = get current line
                    let car = ta.Caret.Column
                    let prevC = line.Substring(0 ,car-1)
                    //log.PrintfnDebugMsg "--Substring length %d: '%s'" prevC.Length prevC
                    if prevC.Length > 0  then //TODO or also use to replace selected text ??
                        if isJustSpaceCharsOrEmpty prevC  then
                            let dist = prevC.Length % ed.AvaEdit.Options.IndentationSize
                            let clearCount = if dist = 0 then ed.AvaEdit.Options.IndentationSize else dist
                            //log.PrintfnDebugMsg "--Clear length: %d " clearCount
                            doc.Remove(ta.Caret.Offset - clearCount, clearCount)
                            e.Handled <- true // TODO raise TextEntered Event ?
                
                | RegSel  -> ()

                | RectSel ->  RectangleSelection.backspaceKey(ed) ; e.Handled <- true 

        
       
            |Input.Key.Delete ->                
                match getSelType(ed.AvaEdit.TextArea) with 
                | NoSel -> 
                    // --- Removes rest of line too if only whitespacxe ---
                    // --- also remove whitespace at stert of next line  ---               
                    let doc = ed.AvaEdit.Document
                    let caretOff = ed.AvaEdit.CaretOffset
                    let line = doc.GetLineByOffset(caretOff) // = get current line 
                    let endOff = line.EndOffset 
                    let len = endOff - caretOff
                    let txt = doc.GetText(caretOff,len)                
                    if isJustSpaceCharsOrEmpty txt  then
                        let nextLine = line.NextLine
                        if notNull nextLine then 
                            doc.BeginUpdate()
                            // also remove spaces at start of next line 
                    
                            //delete max up to caret pos on next line: 
                            let caretPosInLine = caretOff - line.Offset
                            let nextTxt = doc.GetText(line.NextLine) 
                            let nextLnSpacesAtStart = spacesAtStart(nextTxt) 

                            let delLengthOnNextLine = min nextLnSpacesAtStart caretPosInLine // to NOT delete all starting whispace on next line
                            //let delLengthOnNextLine = nextLnSpacesAtStart // to  delete all starting whispace on next line

                            let lenToDelete = endOff - caretOff + 2 + delLengthOnNextLine // + 2 for \r\n
                            doc.Remove(caretOff, lenToDelete)
                    
                            // now after this change ensure one space remains at caret
                            let prev = doc.GetCharAt(caretOff-1)
                            let next = doc.GetCharAt(caretOff)
                            let prevIsChar = not (Char.IsWhiteSpace(prev))
                            let nextIsChar = not (Char.IsWhiteSpace(next))
                            if  prevIsChar && nextIsChar then 
                                doc.Insert(caretOff, " ") 
                    
                            doc.EndUpdate()
                            e.Handled <- true // TODO raise TextEntered Event ?
                
                | RegSel _ -> ()

                | RectSel ->  RectangleSelection.deleteKey(ed) ; e.Handled <- true 


        

            // add indent after do, for , ->, =
            |Input.Key.Return 
            |Input.Key.Enter ->
                if hasNoSelection ed.AvaEdit.TextArea  then // TODO what happens if there is a selction ??
                    let doc = ed.AvaEdit.Document
                    let caret = ed.AvaEdit.CaretOffset
                    let line = doc.GetLineByOffset(caret)            
                    let txt = doc.GetText(line) // = get current line
                    let caretPosInLine = caret - line.Offset
                    let isCaretAtEnd = String.IsNullOrWhiteSpace (txt.[caretPosInLine .. line.EndOffset]) // ensure caret is at end off line !
                    //log.PrintfnDebugMsg "line:%s" txt
                    //log.PrintfnDebugMsg "caretPosInLine:%d isCaretAtEnd:%b" caretPosInLine isCaretAtEnd
                    let trimmed = txt.TrimEnd()
                    if isCaretAtEnd then //TODO or also use to replace selected text ??
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
                                let indent = ed.AvaEdit.Options.IndentationSize
                                let rem = st % indent
                                let ind = 
                                    if rem  = 0 then  st + indent // enure new indent is a multiple of avaEdit.Options.IndentationSize
                                    elif rem = 1 then st + indent + indent - 1 // to indent always at leat 2 chars
                                    else              st + indent - rem
                                doc.Insert(ed.AvaEdit.CaretOffset, " " + Environment.NewLine + String(' ',ind)) // add space before too for nice position of folding block
                                e.Handled <- true // to not actually add anothe new line
                                // TODO raise TextEntered Event ?
        

            | _ -> ()
    
     
    let dragAndDrop (ed:IEditor,  e:DragEventArgs) =        
        let doc = ed.AvaEdit.Document
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
                        doc.Insert (0, sprintf "#r \"%s\"\r\n" file)
                        ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line 0: %s"  file
                    let folder = IO.Path.GetDirectoryName(fs.[0])
                    doc.Insert (0, sprintf "#I @\"%s\"\r\n" folder)                    
                    ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line 0: %s"  folder
                else
                    for f in fs do
                        if isDll f then                            
                            let txt = sprintf "#r @\"%s\"\r\n" f
                            doc.Insert (0, txt )
                            ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line 0:"
                            printGreen "  %s" txt
                        elif isFsx f  then
                            let txt = sprintf "#load @\"%s\"\r\n" f
                            doc.Insert (0, txt)                            
                            ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line 0:" 
                            printGreen "  %s" txt
                        else 
                            match findInsertion doc.Text with 
                            | Some p -> 
                                let lnNo = doc.GetLineByOffset(p.offset)
                                let line = doc.GetText(lnNo) // = get current line
                                let prev = line.Trim()
                                let isNewLn = line.TrimStart().StartsWith "@"
                                if isNewLn then                                    
                                    let st = String(' ',spacesAtStart line)                                    
                                    doc.Insert (p.offset , sprintf "@\"%s\"%s%s//" f Environment.NewLine st ) 
                                    ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line %d:" p.line 
                                    printGreen "  %s" f
                                    ed.Log.PrintfnInfoMsg "  Previous Line at that position is commented out below:"
                                    ed.Log.PrintfnCustomColor 120 120 120 "  %s" prev
                                else
                                    doc.Insert (p.offset , sprintf "@\"%s\" //" f )
                                    ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line %d:" lnNo.LineNumber
                                    printGreen "  %s" f
                                    ed.Log.PrintfnInfoMsg "  Previous Line content is commented out:" 
                                    ed.Log.PrintfnCustomColor 120 120 120  "  %s" prev
                            | None ->   
                                let lnNo = doc.GetLineByOffset(ed.AvaEdit.CaretOffset)
                                ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line %d:" lnNo.LineNumber
                                printGreen "  %s" f
                                doc.Insert (ed.AvaEdit.CaretOffset , sprintf " @\"%s\"%s" f Environment.NewLine)
                            
            with e -> ed.Log.PrintfnIOErrorMsg "drag and drop failed: %A" e

