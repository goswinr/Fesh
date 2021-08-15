namespace Seff.Editor

open System
open System.Windows
open System.Windows.Input

open AvalonEditB
open AvalonEditB.Document

open Seff.Model
open Seff.Util.Str

module Doc = 
    
    /// offset is at Line end
    /// or only has spaces before line end
    let inline offsetIsAtLineEnd offset (doc:TextDocument) =
        let last = doc.TextLength - 1      
        let rec isAtLineEnd off =  // or only has spaces before line end
            if off > last then true // file end
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
    
    /// State of the caret
    // used in isCaretInStringOrChar
    type internal State = 
        |Code  // in code 
        |Str   // in string  
        |Chr   // in  character       
        |ChrSt // at charcater start, needed because ''' and '\'' are both valid
    
    /// Checks if Caret is in String  or Character quotes: " or '
    /// only checks current line
    /// does not check for beeing in comment 
    /// handels excaped quotes too
    let isCaretInStringOrChar(ed:TextEditor)=
        let caret = ed.TextArea.Caret.Offset
        let doc = ed.Document
        let rec getStart i =
            if i<0 then 0
            else
                match doc.GetCharAt(i) with 
                | '\n' -> i+1                
                | _    -> getStart (i-1)
        let startOffLine =  getStart (caret-1) 
        let rec getState i state= 
            if i >= caret then state
            else
                match doc.GetCharAt(i) with 
                | '\'' -> 
                    match state with 
                    |Code  -> getState (i+1) ChrSt
                    |Str   -> getState (i+1) Str
                    |Chr   -> getState (i+1) Code 
                    |ChrSt -> getState (i+1) Chr 
                | '"' -> 
                    match state with 
                    |Code  -> getState (i+1) Str
                    |Str   -> getState (i+1) Code
                    |Chr   -> getState (i+1) Chr // never happens?
                    |ChrSt -> getState (i+1) Chr       
                | '\\' -> 
                    match state with 
                    |ChrSt -> getState (i+2) Chr   // incr 2, to skip next chars   
                    | _    -> getState (i+2) state 
                | _ -> 
                    match state with 
                    |ChrSt -> getState (i+1) Chr   // to swap from ChrSt to Chr
                    | _    -> getState (i+1) state        
        match getState startOffLine Code with
        |Code -> false
        |Str 
        |Chr 
        |ChrSt -> true


            

module Keys =
    type CtrlKey = Ctrl | Alt | Shift
    
    let inline isUp k = 
        match k with 
        | Ctrl  ->  Keyboard.IsKeyUp Key.LeftCtrl  && Keyboard.IsKeyUp Key.RightCtrl
        | Alt   ->  Keyboard.IsKeyUp Key.LeftAlt   && Keyboard.IsKeyUp Key.RightAlt
        | Shift ->  Keyboard.IsKeyUp Key.LeftShift && Keyboard.IsKeyUp Key.RightShift
    
    let inline isDown k = 
        match k with 
        | Ctrl  ->  Keyboard.IsKeyDown Key.LeftCtrl  || Keyboard.IsKeyDown Key.RightCtrl
        | Alt   ->  Keyboard.IsKeyDown Key.LeftAlt   || Keyboard.IsKeyDown Key.RightAlt
        | Shift ->  Keyboard.IsKeyDown Key.LeftShift || Keyboard.IsKeyDown Key.RightShift

    /// because Alt key actually returns Key.System 
    let inline realKey(e:KeyEventArgs) =
        match e.Key with //https://stackoverflow.com/questions/39696219/how-can-i-handle-a-customizable-hotkey-setting
        | Key.System             -> e.SystemKey
        | Key.ImeProcessed       -> e.ImeProcessedKey
        | Key.DeadCharProcessed  -> e.DeadCharProcessedKey
        | k                      -> k


module CursorBehaviour  =
    open Selection
    open Keys
    
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
        
    /// When pressing enter add indentation on next line if appropiate.
    let internal addIndentation(ed:IEditor,e:Input.KeyEventArgs) =
        if hasNoSelection ed.AvaEdit.TextArea  then // TODO what happens if there is a selction ?? or also use to replace selected text ??
            let caret = ed.AvaEdit.CaretOffset                    
            let doc = ed.AvaEdit.Document
            //if Doc.offsetIsAtLineEnd caret doc then                          
            //let trimmed = Doc.getTextBeforOffset 6 caret doc
            let trimmed = Doc.getTextBeforOffsetSkipSpaces 6 caret doc
            //ed.Log.PrintfnDebugMsg "trimmed='%s' (%d chars)" trimmed trimmed.Length
            if trimmed.EndsWith " do"
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
                    e.Handled <- true // to not actually add another new line // TODO raise TextArea.TextEntered Event ?
    
    /// Removes 4 charactes (Options.IndentationSize) 
    /// On pressing backspace key instead of one                       
    let internal backspace4Chars(ed:IEditor,e:Input.KeyEventArgs) =     
        let doc = ed.AvaEdit.Document
        let ta = ed.AvaEdit.TextArea
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
                e.Handled <- true // TODO raise TextArea.TextEntered Event ?
    
    /// Removes rest of line too if only whitespace 
    /// also remove whitespace at start of next line                
    let internal deleteTillNonWhite(ed:IEditor,e:Input.KeyEventArgs)=
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
        
                e.Handled <- true // TODO raise TextArea.TextEntered Event ?

    /// for no and regular selection                   
    let addWhitespaceAfterChar(ed:IEditor, e:Input.TextCompositionEventArgs) = 
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
                if Selection.hasNoSelection ed.AvaEdit.TextArea && not <| Doc.isCaretInStringOrChar(ed.AvaEdit)  then 
                    ed.AvaEdit.Document.Insert(ed.AvaEdit.TextArea.Caret.Offset, c + " ") // add trailing space
                    e.Handled <- true // TODO raise TextArea.TextEntered Event ?                     
            | _ -> ()

    let previewTextInput(ed:IEditor, e:Input.TextCompositionEventArgs) = 
         //if not ed.IsComplWinOpen then  
            match getSelType(ed.AvaEdit.TextArea) with 
            | NoSel | RegSel ->     addWhitespaceAfterChar(ed,e)
            | RectSel ->            RectangleSelection.insertText(ed, e.Text) ; e.Handled <- true // all input in rectangular selection is handeled here.
        
        
    let previewKeyDown (ed:IEditor, ke: Input.KeyEventArgs) =  
        //if not ed.IsComplWinOpen then  
            match realKey ke  with  
            
            |Input.Key.Back ->  
                /// TODO check for modifier keys like Alt or Ctrl ?
                match getSelType(ed.AvaEdit.TextArea) with 
                | NoSel ->     backspace4Chars(ed,ke)
                | RectSel ->   RectangleSelection.backspaceKey(ed) ; ke.Handled <- true 
                | RegSel  ->   ()        
       
            |Input.Key.Delete ->                
                /// TODO check for modifier keys like Alt or Ctrl ?
                match getSelType(ed.AvaEdit.TextArea) with 
                | NoSel  ->   deleteTillNonWhite(ed,ke)                
                | RectSel ->  RectangleSelection.deleteKey(ed) ; ke.Handled <- true 
                | RegSel ->   ()

            | Input.Key.Enter | Input.Key.Return ->
                if isUp Ctrl // if alt or ctrl is down this means sending to fsi ...         
                && isUp Alt 
                && isUp Shift then addIndentation(ed,ke)  // add indent after do, for , ->, =  
        
            | Input.Key.Down -> 
                if isDown Ctrl && isUp Shift then
                    if isDown Alt then
                        RectangleSelection.expandDown(ed)
                    else  
                        // also use Ctrl key for swaping since Alt key does not work in rhino, 
                        // swaping with alt+up is set up in commands.fs via key gesteures                                        
                        SwapLines.swapLinesDown(ed)
                        ke.Handled <- true
            
            | Input.Key.Up -> 
                if isDown Ctrl && isUp Shift then
                    if isDown Alt then
                        RectangleSelection.expandUp(ed)
                    else 
                        SwapLines.swapLinesUp(ed)
                        ke.Handled <- true
            
            | Input.Key.Left -> 
                if isDown Ctrl && isUp Shift then                
                    match getSelType(ed.AvaEdit.TextArea) with 
                    | RegSel ->   if SwapWords.left(ed.AvaEdit) then  ke.Handled <- true
                    | NoSel  | RectSel ->  ()
            
            | Input.Key.Right -> 
                if isDown Ctrl && isUp Shift then                
                    match getSelType(ed.AvaEdit.TextArea) with 
                    | RegSel ->   if SwapWords.right(ed.AvaEdit) then  ke.Handled <- true
                    | NoSel  | RectSel ->  ()

            | _ -> ()
    
     
    let TextAreaDragAndDrop (ed:IEditor,  e:DragEventArgs) =        
        let doc = ed.AvaEdit.Document
        if e.Data.GetDataPresent DataFormats.FileDrop then
            let isDll (p:string) = p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||  p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            let isFsx (p:string) = p.EndsWith(".fsx", StringComparison.OrdinalIgnoreCase) ||  p.EndsWith(".fs", StringComparison.OrdinalIgnoreCase)

            //let findInsertion (code:string) =    
            //    match ParseFs.findWordAhead "[<Literal>]" 0 code with 
            //    | Some p  -> 
            //        ParseFs.findWordAhead "@\"" p.offset code 
            //    | None -> 
            //        let rec allRefs off =  // loop to skip over the #r and #I statments                   
            //            match ParseFs.findWordAhead "#" off code with
            //            | Some p -> allRefs (p.offset + 7)  // gap of 7 between #r or #load and @"C:\...
            //            | None -> off
            //        ParseFs.findWordAhead "@\"" (allRefs 0) code     
            
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
                    
                    // Find insert location: try to insert at drop location
                    // TODO add drag and drop preview cursor
                    // calculate this before looping through all drops
                    let pos = ed.AvaEdit.GetPositionFromPoint(e.GetPosition(ed.AvaEdit))
                    let lnNo,off = 
                        if pos.HasValue then               
                            let dropLine = ed.AvaEdit.Document.GetLineByNumber(pos.Value.Line)
                            let carLine = doc.GetLineByOffset(ed.AvaEdit.CaretOffset)
                            if abs(dropLine.LineNumber-carLine.LineNumber) < 5 then
                                /// if drop location is close by 5 lines to caret use caret location otherwies use drop line end 
                                carLine.LineNumber,ed.AvaEdit.CaretOffset
                            else
                                dropLine.LineNumber,dropLine.EndOffset
                        else
                            let lnNo = doc.GetLineByOffset(ed.AvaEdit.CaretOffset)
                            lnNo.LineNumber,ed.AvaEdit.CaretOffset
                    
                    
                    for f in fs do
                        if isDll f then                            
                            let txt = sprintf "#r @\"%s\"\r\n" f
                            doc.Insert (0, txt )
                            ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line 0:"
                            printGreen "  %s" txt
                        elif isFsx f  then
                            let txt = sprintf "#load @\"%s\"\r\n" f
                            doc.Insert (0, txt)     // TODO find end or #r statements                       
                            ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line 0:" 
                            printGreen "  %s" txt
                        else                            
                                    
                            //match findInsertion doc.Text with 
                            //| Some p -> 
                            //    let lnNo = doc.GetLineByOffset(p.offset)
                            //    let line = doc.GetText(lnNo) // = get current line
                            //    let prev = line.Trim()
                            //    let isNewLn = line.TrimStart().StartsWith "@"
                            //    if isNewLn then                                    
                            //        let st = String(' ',spacesAtStart line)                                    
                            //        doc.Insert (p.offset , sprintf "@\"%s\"%s%s//" f Environment.NewLine st ) 
                            //        ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line %d:" p.line
                            //        printGreen "  %s" f
                            //        ed.Log.PrintfnInfoMsg "  Previous Line at that position is commented out below:"
                            //        ed.Log.PrintfnColor 120 120 120 "  %s" prev
                            //    else
                            //        doc.Insert (p.offset , sprintf "@\"%s\" //" f )
                            //        ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line %d:" lnNo.LineNumber
                            //        printGreen "  %s" f
                            //        ed.Log.PrintfnInfoMsg "  Previous Line content is commented out:" 
                            //        ed.Log.PrintfnColor 120 120 120  "  %s" prev
                            //| None ->   
                                
                            ed.Log.PrintfnInfoMsg "Drag & Drop inserted at Line %d:" lnNo
                            printGreen "  %s" f
                            doc.Insert (off , sprintf " @\"%s\"%s" f Environment.NewLine)
                            ed.AvaEdit.CaretOffset <- off
                    
                    e.Handled <- true

            with er -> ed.Log.PrintfnIOErrorMsg "Drag & Drop in TextArea failed: %A" er

    let TabBarDragAndDrop (log:ISeffLog, openFiles: string[] -> bool, e:DragEventArgs) = 
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