﻿namespace Seff.Editor


open System
open System.Windows
open System.Windows.Input
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.Document
open ICSharpCode.AvalonEdit.Editing
open Seff
open Seff.Model
open Seff.Util
open Seff.Util.String
open Seff.Util.General


module Doc = 
    
    /// offset is at Line end
    /// or only has spaces before line end
    let inline offsetIsAtLineEnd offset (doc:TextDocument) =
        let last = doc.TextLength - 1      
        let rec isAtLineEnd off =  // or only has spaces before line end
            if off > last then true
            else
                match doc.GetCharAt(off) with 
                | ' ' -> isAtLineEnd (off+1)
                | '\r' -> true
                //| '\n' -> true // not needed line ends are always \r\n
                | _ -> false
        isAtLineEnd offset


    /// does not look for spaces after caret
    let inline spacesAtStartOfLineAndBeforeOffset offset (doc:TextDocument) =            
        let rec find off  k =  // or only has spaces before line end
            if off < 0 then k
            else
                match doc.GetCharAt(off) with 
                | ' ' -> find (off-1) (k+1)
                //| '\r' -> true // not needed line ends are always \r\n
                | '\n' -> k
                | _ -> find (off-1) 0
        find (offset-1) 0
    
    (*
    /// wil do a bound check and return less chars if needed
    let inline getTextBeforOffset desiredCharsCount offset  (doc:TextDocument) =         
        if desiredCharsCount = 0 then ""
        elif desiredCharsCount < 0 then failwithf "getTextBeforOffset desiredCharsCount=%d" desiredCharsCount
        else
            let last = doc.TextLength  - 1 
            let st = max 0 (offset - desiredCharsCount ) // - 1)
            let en = min last (offset )//- 1) // only till char before offset 
            let len = en-st
            if len < 1 then 
                "" //doc.GetText(st,0) fails !!  
            else 
                let t = doc.GetText(st,len)  
                //ISeffLog.log.PrintfnDebugMsg "before Offset %d : doc.GetText(%d,%d)='%s'" offset st len t
                t
    *)

    /// wil do a bound check and return less chars if needed
    let inline getTextBeforOffsetSkipSpaces desiredCharsCount offset  (doc:TextDocument) =         
        if desiredCharsCount = 0 then ""
        elif desiredCharsCount < 0 then failwithf "getTextBeforOffset desiredCharsCount=%d" desiredCharsCount
        else
            let rec find off  =  
                if off > 0 && doc.GetCharAt(off-1) = ' ' then find (off-1)
                else off
            let offNonWhite = find offset            

            let st = max 0 (offNonWhite - desiredCharsCount ) // - 1)
            let en = min (doc.TextLength  - 1 ) (offNonWhite )//- 1) // only till char before offset 
            let len = en-st
            if len < 1 then 
                "" //doc.GetText(st,0) fails !!  
            else 
                let t = doc.GetText(st,len)  
                //ISeffLog.log.PrintfnDebugMsg "before Offset %d : doc.GetText(%d,%d)='%s'" offset st len t
                t
    (*
    /// returns offset of next non white char, pass ovver all line breaks topo
    let inline nextNonWhiteChar offset (doc:TextDocument) =            
        let last = doc.TextLength - 1  
        let rec find off =  
            if off > last then last
            else
                match doc.GetCharAt(off) with 
                | ' '  | '\r' | '\n' -> find (off+1) 
                | _ -> off
        find offset
    *)
    
    /// returns offset of next non white char, paasing max one line break
    let inline nextNonWhiteCharOneLine offset (doc:TextDocument) =            
        let len = doc.TextLength   
        let rec find off rs =  
            if off >= len then len
            else
                match doc.GetCharAt(off) with
                | '\r' -> if rs then off else   find (off+1) true
                | ' '  | '\n' ->                find (off+1) rs
                | _ -> off 
        find offset false

    /// returns spaces till next non white char on same line, or 0 if the rest of the line is just whitespace
    let inline countNextSpaces offset (doc:TextDocument) =            
        let last = doc.TextLength - 1  
        let rec find off  k =  
            if off > last then k
            else
                match doc.GetCharAt(off) with 
                | ' '  -> find (off+1) (k+1)
                | _ -> k
        find offset 0

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
        

            | RectSel ->  
                match e.Text with 
                | null | "" | "\x1b" | "\b" -> ()  
                // ASCII 0x1b = ESC. 
                // also see TextArea.OnTextInput implementation 
                // WPF produces a TextInput event with that old ASCII control char
                // when Escape is pressed. We'll just ignore it.
                // A deadkey followed by backspace causes a textinput event for the BS character.
                // Similarly, some shortcuts like Alt+Space produce an empty TextInput event.
                // We have to ignore those (not handle them) to keep the shortcut working.                
                | txt -> 
                    RectangleSelection.insertText(ed, txt) 
                    e.Handled <- true 
        
        
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

                | RectSel ->  
                    RectangleSelection.backspaceKey(ed) ; e.Handled <- true 

        
       
            |Input.Key.Delete ->                
                match getSelType(ed.AvaEdit.TextArea) with 
                | NoSel -> 
                    // -----------------------------------------
                    // --- Removes rest of line too if only whitespace ---
                    // --- also remove whitespace at start of next line  ---               
                    // -----------------------------------------
                    let doc = ed.AvaEdit.Document
                    let caret = ed.AvaEdit.CaretOffset
                    if Doc.offsetIsAtLineEnd caret doc then     
                        let nc = Doc.nextNonWhiteCharOneLine caret doc
                        let len = nc - caret
                        //ed.Log.PrintfnDebugMsg "remove len=%d "len
                        if len>2 then // leave handeling  other cases especially the end of flie to avaedit
                            if caret = 0  then 
                                doc.Replace(caret,len  , " ")//  add space at start 
                            else
                                match doc.GetCharAt(caret-1) with 
                                |' ' | '\n' -> doc.Remove(caret, len) // dont add space because there is already one before
                                |_ -> doc.Replace(caret,len  , " ")//  add space 
                        
                            e.Handled <- true // TODO raise TextEntered Event ?
                    
                
                | RegSel _ -> ()

                | RectSel -> 
                    RectangleSelection.deleteKey(ed) ; e.Handled <- true 


        
            // -----------------------------------------
            // add indent after do, for , ->, =
            // -----------------------------------------
            |Input.Key.Return 
            |Input.Key.Enter ->
                if hasNoSelection ed.AvaEdit.TextArea  then // TODO what happens if there is a selction ?? or also use to replace selected text ??
                    let doc = ed.AvaEdit.Document
                    let caret = ed.AvaEdit.CaretOffset                    
                    //if Doc.offsetIsAtLineEnd caret doc then                          
                    //let trimmed = Doc.getTextBeforOffset 6 caret doc
                    let trimmed = Doc.getTextBeforOffsetSkipSpaces 6 caret doc
                    //ed.Log.PrintfnDebugMsg "trimmed='%s' (%d chars)" trimmed trimmed.Length
                    if     trimmed.EndsWith " do"
                        || trimmed.EndsWith " then"
                        || trimmed.EndsWith " else"
                        || trimmed.EndsWith "="
                        || trimmed.EndsWith "("
                        || trimmed.EndsWith "["
                        || trimmed.EndsWith "{"
                        || trimmed.EndsWith "[|"
                        || trimmed.EndsWith "->" then                    
                            let st = Doc.spacesAtStartOfLineAndBeforeOffset caret doc
                            let indent = ed.AvaEdit.Options.IndentationSize
                            let rem = st % indent
                            let ind = 
                                if rem  = 0 then  st + indent // enure new indent is a multiple of avaEdit.Options.IndentationSize
                                elif rem = 1 then st + indent + indent - 1 // to indent always at leat 2 chars
                                else              st + indent - rem
                            let insertText = " " + Environment.NewLine + String(' ',ind)
                            let spaces = Doc.countNextSpaces caret doc
                            if spaces = 0 then 
                                doc.Insert(caret,insertText) // add space before too for nice position of folding block
                            else
                                doc.Replace(caret,spaces,insertText)
                            ed.AvaEdit.CaretOffset <- caret + insertText.Length //+ spaces
                            e.Handled <- true // to not actually add another new line
                            // TODO raise TextEntered Event ?
        
            | Input.Key.Down -> 
                if Keyboard.IsKeyDown(Key.LeftCtrl)then
                    if Keyboard.IsKeyDown(Key.LeftAlt)then 
                        RectangleSelection.expandDown
                    else  
                        // also use Ctrl key for swaping since Alt key does not work in rhino, 
                        // swaping with alt+up is set up in commands.fs via key gesteures                                        
                        SwapLines.swapLinesDown(ed)
                        e.Handled <- true
            
            | Input.Key.Up -> 
                if  Keyboard.IsKeyDown(Key.LeftCtrl)then 
                    SwapLines.swapLinesUp(ed)
                    e.Handled <- true

            | _ -> ()
    
     
    let TextAreaDragAndDrop (ed:IEditor,  e:DragEventArgs) =        
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
                let printGreen = ed.Log.PrintfnColor 0 150 0
               

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
                                    ed.Log.PrintfnColor 120 120 120 "  %s" prev
                                else
                                    doc.Insert (p.offset , sprintf "@\"%s\" //" f )
                                    ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line %d:" lnNo.LineNumber
                                    printGreen "  %s" f
                                    ed.Log.PrintfnInfoMsg "  Previous Line content is commented out:" 
                                    ed.Log.PrintfnColor 120 120 120  "  %s" prev
                            | None ->   
                                let lnNo = doc.GetLineByOffset(ed.AvaEdit.CaretOffset)
                                ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line %d:" lnNo.LineNumber
                                printGreen "  %s" f
                                doc.Insert (ed.AvaEdit.CaretOffset , sprintf " @\"%s\"%s" f Environment.NewLine)
                    
                    e.Handled <- true

            with er -> ed.Log.PrintfnIOErrorMsg "Drag & Drop in TextArea failed: %A" er

    let TabBarDragAndDrop (log:ISeffLog, openFiles: string[]->bool, e:DragEventArgs) = 
        if e.Data.GetDataPresent DataFormats.FileDrop then            
            let isFsx (p:string) = p.EndsWith(".fsx", StringComparison.OrdinalIgnoreCase) ||  p.EndsWith(".fs", StringComparison.OrdinalIgnoreCase)                        
            try                 
                let fs = (e.Data.GetData DataFormats.FileDrop :?> string []) 
                fs
                |> Array.filter isFsx
                |> openFiles
                |> ignore

                fs
                |> Seq.filter (not << isFsx)
                |> Seq.iter (log.PrintfnIOErrorMsg "skiped opening  file in drag and drop: %s")
                
                e.Handled <- true

            with er -> log.PrintfnIOErrorMsg "Other Drag & Drop failed: %A" er