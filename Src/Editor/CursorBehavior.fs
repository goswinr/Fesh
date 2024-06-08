namespace Fesh.Editor

open System
open System.Windows

open AvalonEditB
open AvalonEditB.Document

open Fesh.Model
open Fesh.Util.Str


module Doc =

    /// Offset is at Line end.
    /// Or only has spaces before line end
    let offsetIsAtLineEnd offset (doc:TextDocument) = // removed inline to have function name in error stack trace
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


    /// Does not look for spaces after caret
    let spacesAtStartOfLineAndBeforeOffset offset (doc:TextDocument) = // removed inline to have function name in error stack trace
        let rec find off  k =  // or only has spaces before line end
            if off < 0 then k
            else
                match doc.GetCharAt(off) with
                | ' ' -> find (off-1) (k+1)
                //| '\r' -> true // not needed line ends are always \r\n
                | '\n' -> k
                | _ -> find (off-1) 0
        find (offset-1) 0

    /// If caret is in comment ( not at end of comment line) returns amount of slashes, otherwise 0
    let isCaretInComment (caret:int) (doc:TextDocument):int =
        let ln = doc.GetLineByOffset(caret)
        let rec isInside pos =
            pos = ln.EndOffset
            ||
            match doc.GetCharAt(pos) with
            | ' ' | '\n' | '\r' -> isInside (pos+1)
            |  _ -> false

        if isInside caret then
            let rec find off k =
                if off = caret then k
                else
                    match doc.GetCharAt(off) with
                    | ' ' when k=0-> find (off+1) k
                    | '/' -> find (off+1) (k+1)
                    |  _ -> k
            find ln.Offset 0
        else
            0

    (* unused:

    let commentSlashesTillCaret caret (doc:TextDocument) = // removed inline to have function name in error stack trace
        let ln = doc.GetLineByOffset(caret)
        let rec find off k =
            if off = caret then k
            else
                match doc.GetCharAt(off) with
                | ' ' when k=0-> find (off+1) k
                | '/' -> find (off+1) (k+1)
                |  _ -> k
        find ln.Offset 0
    *)

    /// Returns the string directly before the offset, it will be maximum of the specified length
    /// Will do a bound check and return less chars if needed
    let getTextBeforeOffsetSkipSpaces desiredCharsCount offset  (doc:TextDocument) = // removed inline to have function name in error stack trace
        if desiredCharsCount < 0 then failwithf "getTextBeforeOffsetSkipSpaces desiredCharsCount=%d must be positive" desiredCharsCount
        elif desiredCharsCount = 0 then ""
        elif offset-desiredCharsCount < 0 then ""
        //elif offset < desiredCharsCount then // covered by bound check below
        elif offset=doc.TextLength then // special case for end of document
            doc.GetText(offset-desiredCharsCount,desiredCharsCount)
        else
            let rec find off  =
                if off > 0 && doc.GetCharAt(off-1) = ' ' then
                    find (off-1)
                else
                    off
            let offNonWhite = find offset

            let st = max 0 (offNonWhite - desiredCharsCount ) // - 1)
            let en = min (doc.TextLength  - 1 ) (offNonWhite )//- 1) // only till char before offset
            let len = en-st
            if len < 1 then
                "" //doc.GetText(st,0) fails !!
            else
                doc.GetText(st,len)

    /// Returns offset of next non white char, passing max one line break
    let nextNonWhiteCharOneLine offset (doc:TextDocument) = // removed inline to have function name in error stack trace
        let len = doc.TextLength
        let rec find off rs =
            if off >= len then len
            else
                match doc.GetCharAt(off) with
                | '\r' -> if rs then off else   find (off+1) true
                | ' '  | '\n' ->                find (off+1) rs
                | _ -> off
        find offset false

    /// Returns spaces till next non white char on same line, or 0 if the rest of the line is just whitespace
    let countNextSpaces offset (doc:TextDocument) = // removed inline to have function name in error stack trace
        let last = doc.TextLength - 1
        let rec find off  k =
            if off > last then
                k
            else
                match doc.GetCharAt(off) with
                | ' '  -> find (off+1) (k+1)
                |  _   -> k // also exits for '\n' and '\r'
        find offset 0

    (* better use the highlighting engines state because it also works for multiline strings:

    /// State of the caret
    /// used in isCaretInStringOrChar
    type internal State =
        |Code  // in code
        |Str   // in string
        |Chr   // in  character
        |ChrSt // at character start, needed because ''' and '\'' are both valid


    /// Checks if Caret is in String or Character quotes: " or '
    /// only checks current line
    /// does not check for being in comment
    /// handles escaped quotes too
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
    *)


module CursorBehavior  =
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

    /// When pressing enter add indentation on next line if appropriate for FSharp.
    /// for avaEdit.PreviewKeyDown
    let internal addFSharpIndentation(ed:TextEditor,ke:Input.KeyEventArgs) =
        if hasNoSelection ed.TextArea  then // TODO what happens if there is a selection ?? or also use to replace selected text ??
            let caret = ed.CaretOffset
            let doc = ed.Document
            let trimmed = Doc.getTextBeforeOffsetSkipSpaces 6 caret doc
            //IFeshLog.log.PrintfnDebugMsg "current line ='%s'" (doc.GetText(doc.GetLineByOffset(caret)))
            //IFeshLog.log.PrintfnDebugMsg "trimmed='%s' (%d chars)" trimmed trimmed.Length

            // special enter that increases the indent :
            if     trimmed.EndsWith " do"
                || trimmed.EndsWith " then"
                || trimmed.EndsWith " else"
                || trimmed.EndsWith "="
                || trimmed.EndsWith "("
                || trimmed.EndsWith "{"
                || trimmed.EndsWith "["
                || trimmed.EndsWith "[|"
                || trimmed.EndsWith "->" then
                    ke.Handled <- true // to not actually add another new line too // TODO raise TextArea.TextEntered Event ?
                    let st = Doc.spacesAtStartOfLineAndBeforeOffset caret doc
                    let indent = ed.Options.IndentationSize
                    let rem = st % indent
                    let ind =
                        if rem  = 0 then  st + indent // ensure new indent is a multiple of avaEdit.Options.IndentationSize
                        elif rem = 1 then st + indent + indent - 1 // to indent always at least 2 chars
                        else              st + indent - rem
                    let insertText = " " + Environment.NewLine + String(' ',ind)
                    let spaces = Doc.countNextSpaces caret doc
                    if spaces = 0 then
                        doc.Insert(caret,insertText) // add space before too for nice position of folding block
                    else
                        doc.Replace(caret,spaces,insertText)
                    ed.CaretOffset <- caret + insertText.Length //+ spaces
                    //IFeshLog.log.PrintfnDebugMsg "trimmed='%s' (%d chars)" trimmed trimmed.Length
            else
                // start the next line with comment if the return was pressed inside a comment
                let slashes = Doc.isCaretInComment caret doc
                if slashes > 1 then
                    ke.Handled <- true // to not actually add another new line too // TODO raise TextArea.TextEntered Event ?
                    let spacesBefore = Doc.spacesAtStartOfLineAndBeforeOffset caret doc
                    let insertText = Environment.NewLine + String(' ',spacesBefore) + String('/',slashes)
                    let spacesAfter = Doc.countNextSpaces caret doc
                    if spacesAfter = 0 then
                        doc.Insert(caret,insertText) // add space before too for nice position of folding block
                    else
                        doc.Replace(caret,spacesAfter,insertText)
                    ed.CaretOffset <- caret + insertText.Length //+ spacesBefore + slashes
                else
                    // also indent on any regular 'return'
                    // this would actually also be done by the DefaultIndentationStrategy of Avalonedit but the DefaultIndentationStrategy
                    // would raise the document text change event twice, once after 'return' and once after indenting.
                    // this does not work well with the current implementation of the Evaluation tracker.
                    // the below code does the same as the DefaultIndentationStrategy but avoids raising to events.
                    // DefaultIndentationStrategy does not get triggered because of the e.Handled <- true

                    ke.Handled <- true // to not actually add another new line too // TODO raise TextArea.TextEntered Event ?
                    let spacesBefore = Doc.spacesAtStartOfLineAndBeforeOffset caret doc
                    let insertText = Environment.NewLine + String(' ',spacesBefore)
                    let spacesAfter = Doc.countNextSpaces caret doc
                    if spacesAfter = 0 then
                        doc.Insert(caret,insertText) // add space before too for nice position of folding block
                    else
                        doc.Replace(caret, spacesAfter, insertText)
                    ed.CaretOffset <- caret + insertText.Length //+ spacesBefore



    /// Removes 4 characters (Options.IndentationSize)
    /// On pressing backspace key instead of one
    let internal backspace4Chars(ed:TextEditor,e:Input.KeyEventArgs) =
        let doc = ed.Document
        let ta = ed.TextArea
        let line = doc.GetText(doc.GetLineByOffset(ta.Caret.Offset)) // = get current line
        let car = ta.Caret.Column
        let prevC = line.Substring(0 ,car-1)
        //log.PrintfnDebugMsg "--Substring length %d: '%s'" prevC.Length prevC
        if prevC.Length > 0  then //TODO or also use to replace selected text ??
            if isJustSpaceCharsOrEmpty prevC  then
                let dist = prevC.Length % ed.Options.IndentationSize
                let clearCount = if dist = 0 then ed .Options.IndentationSize else dist
                //log.PrintfnDebugMsg "--Clear length: %d " clearCount
                doc.Remove(ta.Caret.Offset - clearCount, clearCount)
                e.Handled <- true // TODO raise TextArea.TextEntered Event ?

    /// Removes rest of line too if only whitespace
    /// also remove whitespace at start of next line
    let internal deleteTillNonWhite(ed:TextEditor,e:Input.KeyEventArgs)=
        let doc = ed.Document
        let caret = ed.CaretOffset
        if Doc.offsetIsAtLineEnd caret doc then
            let nc = Doc.nextNonWhiteCharOneLine caret doc
            let len = nc - caret
            //ed.Log.PrintfnDebugMsg "remove len=%d "len
            if len>2 then // leave handling  other cases especially the end of file to avaEdit
                if caret = 0  then
                    doc.Replace(caret,len  , " ")//  add space at start
                else
                    match doc.GetCharAt(caret-1) with
                    |' ' | '\n' -> doc.Remove(caret, len) // don't add space because there is already one before
                    |_ -> doc.Replace(caret,len  , " ")//  add space

                e.Handled <- true // TODO raise TextArea.TextEntered Event ?

    /// for no and regular selection
    let addWhitespaceAfterChar(ed:TextEditor, e:Input.TextCompositionEventArgs) =
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
        | ")" // this bracket get added most of the time automatically via addClosingBracket()
        | ","
        | ";"  as c ->
            if Selection.hasNoSelection ed.TextArea (* && not <| Doc.isCaretInStringOrChar(ed) *)  then
                ed.Document.Insert(ed.TextArea.Caret.Offset, c + " ") // add trailing space
                e.Handled <- true // TODO raise TextArea.TextEntered Event ?
        | _ -> ()

    let addClosingBracket(ed:TextEditor, e:Input.TextCompositionEventArgs) =
        let inline addPair caretForward (s:string)  =
            let caret = ed.TextArea.Caret.Offset
            ed.Document.Insert(caret, s );
            ed.TextArea.Caret.Offset <- caret+caretForward // it was moved 2, now set it to one ahead, in the middle
            e.Handled <- true

        let inline prevChar() = ed.Document.GetCharAt(max 0 (ed.TextArea.Caret.Offset-1))

        /// test if next character is whitespace or end of file
        let inline nextSpace() =
            let i = ed.TextArea.Caret.Offset
            if ed.Document.TextLength <= i then
                true
            else
                let c = ed.Document.GetCharAt(i)
                c=' '|| c='\r'

        /// test if next character is whitespace, double quote or end of file
        let inline nextSpaceQ() =
            let i = ed.TextArea.Caret.Offset
            if ed.Document.TextLength <= i then
                true
            else
                let c = ed.Document.GetCharAt(i)
                c=' '|| c='\r'|| c='"' // " for being in a string

        /// test if previous character is not from the alphanumeric( so a space, a bracket or similar)
        let inline prevNonAlpha() =
            let i = ed.TextArea.Caret.Offset
            if i = 0  then
                true
            else
                let c = ed.Document.GetCharAt(i) // TODO:only testsing for ascii here, good enough!?
                c < '0'
                ||
                (c > '9' && c < 'A')
                ||
                (c > 'Z' && c < '_') // _ then ` then a
                ||
                c > 'z'


        /// test if the current line has an even count of quotes ?
        let inline evenQuoteCount() =
            let mutable i = ed.TextArea.Caret.Offset - 1 // do minus one first, caret might be at end of document. that would not be a valid index
            let rec count(k) =
                if i = -1 then k
                else
                    let c = ed.Document.GetCharAt(i)
                    i<-i-1 // do minus one first, caret might be at end of document. that would not be a valid index
                    match c with
                    | '"'  -> count(k+1)
                    | '\n' -> k
                    | _    -> count(k)
            count(0) % 2 = 0


        match Selection.getSelType(ed.TextArea) with
        |RectSel -> ()

        // if there is a simple selection on one line surround it in Brackets
        |RegSel ->
            match e.Text with
            | "("  ->
                let s = Selection.getSelectionOrdered(ed.TextArea)
                if s.LineCount = 1 then
                    ed.Document.BeginUpdate()
                    ed.Document.Insert(s.enOffset(ed.Document),")")
                    ed.Document.Insert(s.stOffset(ed.Document),"(")
                    ed.Document.EndUpdate()
                    ed.SelectionLength <- 0
                    ed.CaretOffset <- s.enOffset(ed.Document)+1
                    e.Handled<-true
            | _ -> ()

        // if no selection for an opening bracket add a closing bracket
        |NoSel ->

            match e.Text with
            | "("  -> if nextSpace()  then addPair 1 "()"
            | "{"  -> if nextSpaceQ() then addPair 1 "{}"
            | "["  -> if nextSpace()  then addPair 1 "[]"
            | "'"  -> if nextSpace()&&prevNonAlpha()  then addPair 1 "''"
            | "\"" -> if nextSpace()&&evenQuoteCount() then addPair 1 "\"\""
            | "$"  -> if nextSpace()  then addPair 2 "$\"\"" // for formatting string
            | "`"  -> if nextSpace()  then addPair 2 "````"  // for quoted identifiers

            | "|" ->
                // first check previous character:
                match prevChar() with
                | '{' | '[' | '(' -> addPair 2 "|  |" // it was moved 2, now set it to one ahead, in the middle
                | _ -> ()

            | "*" -> // for comments with  (* *)
                // first check previous character:
                match prevChar() with
                | '(' -> addPair 2 "*  *"
                | _ -> ()

            | _ -> ()


    let previewTextInput(ed:TextEditor, e:Input.TextCompositionEventArgs) =
        // not needed: if not ed.IsComplWinOpen then
        match getSelType(ed.TextArea) with
        | RectSel ->
            RectangleSelection.insertText(ed, e.Text) ; e.Handled <- true // all input in rectangular selection is handled here.
        | NoSel | RegSel ->
            addWhitespaceAfterChar(ed,e)
            if not e.Handled then
                addClosingBracket(ed,e)



[<RequireQualifiedAccess>]
module DragAndDrop =
    let onTextArea (ed:TextEditor,  e:DragEventArgs) =
        let doc = ed.Document
        if e.Data.GetDataPresent DataFormats.FileDrop then
            let isDll (p:string) = p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||  p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            let isFsx (p:string) = p.EndsWith(".fsx", StringComparison.OrdinalIgnoreCase) ||  p.EndsWith(".fs", StringComparison.OrdinalIgnoreCase)

            //let findInsertion (code:string) =
            //    match ParseFs.findWordAhead "[<Literal>]" 0 code with
            //    | Some p  ->
            //        ParseFs.findWordAhead "@\"" p.offset code
            //    | None ->
            //        let rec allRefs off =  // loop to skip over the #r and #I statements
            //            match ParseFs.findWordAhead "#" off code with
            //            | Some p -> allRefs (p.offset + 7)  // gap of 7 between #r or #load and @"C:\...
            //            | None -> off
            //        ParseFs.findWordAhead "@\"" (allRefs 0) code

            try
                let printGreen = IFeshLog.log.PrintfnColor 0 150 0

                let fs = (e.Data.GetData DataFormats.FileDrop :?> string []) |> Array.sort |> Array.rev // to get file path
                if fs.Length > 2 && Array.forall isDll fs then      // TODO make path relative to script location
                    for f in fs  do
                        let file = IO.Path.GetFileName(f)
                        doc.Insert (0, sprintf "#r \"%s\"\r\n" file)
                        IFeshLog.log.PrintfnInfoMsg "Drag & Drop inserted at Line 0: %s"  file
                    let folder = IO.Path.GetDirectoryName(fs.[0]).Replace("\\","/")
                    doc.Insert (0, sprintf "#I \"%s\"\r\n" folder)
                    IFeshLog.log.PrintfnInfoMsg "Drag & Drop inserted at Line 0: %s"  folder
                else

                    // Find insert location: try to insert at drop location
                    // TODO add drag and drop preview cursor
                    // calculate this before looping through all drops
                    let pos = ed.GetPositionFromPoint(e.GetPosition(ed))
                    let lnNo,off =
                        if pos.HasValue then
                            let dropLine = ed.Document.GetLineByNumber(pos.Value.Line)
                            let carLine = doc.GetLineByOffset(ed.CaretOffset)
                            if abs(dropLine.LineNumber-carLine.LineNumber) < 5 then
                                // If drop location is close by 5 lines to caret use caret location otherwies use drop line end
                                carLine.LineNumber,ed.CaretOffset
                            else
                                dropLine.LineNumber,dropLine.EndOffset
                        else
                            let lnNo = doc.GetLineByOffset(ed.CaretOffset)
                            lnNo.LineNumber,ed.CaretOffset


                    for f0 in fs do
                        let f = f0.Replace("\\","/")
                        if isDll f then
                            let txt = sprintf "#r \"%s\"\r\n" f
                            doc.Insert (0, txt )
                            IFeshLog.log.PrintfnInfoMsg "Drag & Drop inserted at Line 0:"
                            printGreen "  %s" txt
                        elif isFsx f  then
                            let txt = sprintf "#load \"%s\"\r\n" f
                            doc.Insert (0, txt)     // TODO find end or #r statements
                            IFeshLog.log.PrintfnInfoMsg "Drag & Drop inserted at Line 0:"
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

                            IFeshLog.log.PrintfnInfoMsg "Drag & Drop inserted at Line %d:" lnNo
                            printGreen "  %s" f
                            doc.Insert (off , sprintf "\"%s\"%s" f Environment.NewLine)
                            ed.CaretOffset <- off

                    e.Handled <- true

            with er -> IFeshLog.log.PrintfnIOErrorMsg "Drag & Drop in TextArea failed: %A" er


    /// A Event handler that will open a new tab per file.
    /// If event comes from a AvalonEditB.TextEditor that is not read only ( like the Log) the event is ignored.
    let onTabHeaders (openFiles: string[] -> bool, e:DragEventArgs) =
        //IFeshLog.log.PrintfnDebugMsg "Drop onto e.Source :%A ; e.OriginalSource:%A" e.Source e.OriginalSource
        let addTabsForFiles() =
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
                    |> Seq.iter (IFeshLog.log.PrintfnIOErrorMsg " Only *.fsx and *.fs files can be opened to tabs. Ignoring darg-and-drop of  %A " )

                    e.Handled <- true

                with er ->
                    IFeshLog.log.PrintfnIOErrorMsg "Other Drag & Drop failed: %A" er

        match e.Source with
        | :? AvalonEditB.TextEditor  as te ->
                // do only when on AvalonLog, not if drop happens on code editor, for code editor the file is not opened
                // but a link to it inserted into the code, see separate TextAreaDragAndDrop event above.
                if te.IsReadOnly then addTabsForFiles()
                else () // do nothing
        | _ -> addTabsForFiles()

