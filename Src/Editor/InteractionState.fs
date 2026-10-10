namespace Fesh.Editor

open AvalonEditB
open AvalonEditB.Folding

module CodeLineTools =

    /// offStart: the offset of the first character off this line
    /// indent:  the count of spaces at the start of this line
    /// len: the amount of characters in this line excluding the trailing \r\n
    /// if indent equals len the line is only whitespace
    [<Struct>]
    type LineInfo = {
        offStart:int // the offset of the first character off this line
        indent:int // the count of spaces at the start of this line
        len: int // the amount of characters in this line excluding the trailing \r\n
        }


    /// Counts spaces after a position
    let inline private spacesFrom off len (str:string) =
        let mutable ind = 0
        while ind < len && str.[off+ind] = ' ' do
            ind <- ind + 1
        ind

    /// used for Editor, not Log
    /// Holds a List who's indices correspond to each line with info about:
    /// offStart: the offset of the first character off this line
    /// indent:  the count of spaces at the start of this line
    /// len: the amount of characters in this line excluding the trailing \r\n
    /// if indent equals len the line is only whitespace
    type CodeLines() =

        let mutable lines = ResizeArray<LineInfo>()

        let mutable fullCode = ""

        let mutable correspondingId = 0L

        let getNewLines(code:string) =

            let newLns = ResizeArray<LineInfo>(lines.Count + 2)

            let codeLen = code.Length

            let rec loop stOff =
                if stOff >= codeLen then // last line
                    let len = codeLen - stOff
                    newLns.Add {offStart=stOff; indent=len; len=len}
                else
                    match code.IndexOf ('\r', stOff) with //TODO '\r' might fail if Fesh is ever ported to AvaloniaEdit to work on MAC
                    | -1 ->
                        let len = codeLen - stOff
                        let indent = spacesFrom stOff len code
                        newLns.Add {offStart=stOff; indent=indent; len=len}  // the last line
                    | r ->
                        let len = r - stOff
                        let indent = spacesFrom stOff len code
                        newLns.Add {offStart=stOff; indent=indent; len=len}
                        loop (r+2) // +2 to jump over \r and \n

            newLns.Add {offStart=0; indent=0; len=0}   // ad dummy line at index 0 , line 0 is always empty, line start at 1 in AvalonEdit
            loop (0)
            newLns

        member _.LastLineIdx = lines.Count - 1

        member _.FullCode = fullCode

        /// checks if this codeLines does not correspond to a given ID
        member _.IsNotFromId(id) = id <> correspondingId

        /// ThreadSafe and in Sync: Only starts parsing when Done and also checks
        /// if docChangedIdHolder.Value = id before and after
        /// returns True
        member _.UpdateLines(code, changeId): unit =
            correspondingId <- 0L // reset to 0 to indicate that we are parsing
            let newLns = getNewLines code
            lines <- newLns
            fullCode <- code
            correspondingId <- changeId


        /// Safe: checks correspondingId = changeId
        /// returns also none for bad indices
        member _.GetLine(lineIdx, changeId): LineInfo voption =
            if correspondingId = changeId then
                if lineIdx < 0 || lineIdx >= lines.Count then
                    ValueNone
                else
                    ValueSome lines.[lineIdx]
            else
                ValueNone


        /// Safe: checks correspondingId = changeId
        /// returns also none for bad indices
        member _.GetLineText(lineIdx, changeId): string voption =
            if correspondingId = changeId then
                if lineIdx < 0 || lineIdx >= lines.Count then
                    ValueNone
                else
                    let l = lines.[lineIdx]
                    ValueSome (fullCode.Substring(l.offStart,l.len))
            else
                ValueNone

        member _.CorrespondingId = correspondingId


    /// used for Log, not Editor
    /// offStart: the offset of the first character of this line in the Log document
    /// text: the text of this line excluding the line break
    [<Struct>]
    type LogLine = {
        offStart:int
        text:string
        }

    /// An immutable view on the LogLines at the time it was taken. Safe to search on any thread.
    /// Line numbers start at 1, like in AvalonEdit.
    type LogLinesSnapshot =
        {
        /// Index 0 is a dummy line. The lines with a line break at the end are at index 1 to count-1.
        /// Elements below count never change. Appending only writes at count or above.
        lines: LogLine[]
        count: int
        /// The last line, it has no line break (yet). It might be empty.
        tail: LogLine
        }
        member s.LastLineNo = s.count
        member s.GetLine(lineNo) = if lineNo = s.count then s.tail else s.lines.[lineNo]


    /// used for Log, not Editor
    /// The lines of the Log, built only from the appended text, without ever copying the full text of the Log.
    /// Append and Reset must be called from the UI thread. The Snapshot can be searched on any thread.
    type LogLines() =

        let lineBreaks = [| '\r'; '\n' |] // same as AvalonEdit: \r\n, \r and \n are all line breaks

        let mutable lines : LogLine[] = Array.zeroCreate 256
        let mutable count = 1 // index 0 is a dummy line
        let mutable tail = {offStart=0; text=""}
        let mutable docLength = 0

        /// The last appended text ended with \r. Then a \n at the start of the next text belongs to the same line break.
        let mutable endsWithCR = false

        let mutable snap = {lines=lines; count=count; tail=tail}

        let addLine (l:LogLine) =
            if count = lines.Length then
                let bigger = Array.zeroCreate (lines.Length * 2) // a new array, so that existing snapshots keep their array unchanged
                Array.blit lines 0 bigger 0 count
                lines <- bigger
            lines.[count] <- l
            count <- count + 1

        /// The length of the text that the lines were built from.
        member _.DocLength = docLength

        member _.Snapshot = snap

        /// Call when text gets appended at the end of the Log document.
        member _.Append(txt:string) =
            let mutable st = 0 // start of the current line in txt
            if endsWithCR && txt.Length > 0 && txt.[0] = '\n' then // the \r\n line break was split over two appends
                st <- 1
                tail <- {tail with offStart = tail.offStart + 1} // tail is empty because the \r closed the previous line
            let mutable br = txt.IndexOfAny(lineBreaks, st)
            while br >= 0 do
                let part = txt.Substring(st, br - st)
                addLine {offStart = tail.offStart; text = if tail.text.Length = 0 then part else tail.text + part}
                st <- if txt.[br] = '\r' && br + 1 < txt.Length && txt.[br + 1] = '\n' then br + 2 else br + 1
                tail <- {offStart = docLength + st; text = ""}
                br <- if st < txt.Length then txt.IndexOfAny(lineBreaks, st) else -1
            if st < txt.Length then
                tail <- {tail with text = tail.text + txt.Substring(st)}
            if txt.Length > 0 then
                endsWithCR <- txt.[txt.Length - 1] = '\r'
            docLength <- docLength + txt.Length
            snap <- {lines=lines; count=count; tail=tail}

        /// Call when the Log document changed in any other way than appending, e.g. when it got cleared.
        member this.Reset(fullText:string) =
            lines <- Array.zeroCreate 256 // a new array, so that existing snapshots keep their array unchanged
            count <- 1
            tail <- {offStart=0; text=""}
            docLength <- 0
            endsWithCR <- false
            this.Append fullText

type DocChangedConsequence =
    | React
    | WaitForCompletions

/// Tracking the lastest change Ids to the document
/// FoldManager may be null for Log
[<AllowNullLiteral>] // for log initially
type InteractionState(ed:TextEditor, foldManager:FoldingManager, config:Fesh.Config.Config)  =

    let changeId = ref 0L

    /// reacts to doc changes
    /// for Semantics and bad indentations
    let transformersSemantic          = new LineTransformers<LinePartChange>()

    /// reacts to doc changes
    /// for colorizing all brackets
    let transformersAllBrackets       = new LineTransformers<LinePartChange>()

    /// reacts to caret changes
    /// for colorizing matching brackets
    let transformersMatchingBrackets  = new LineTransformers<LinePartChange>()

    /// reacts to selection changes
    /// for colorizing text that matches the current selection
    let transformersSelection         = new LineTransformers<LinePartChange>(SkipSelectedText = true) // the selected text has its own highlighting

    let fastColorizer = new FastColorizer(
                                    [|
                                    transformersAllBrackets
                                    transformersMatchingBrackets
                                    transformersSelection
                                    transformersSemantic // draw errors last so they are on top of matching brackets
                                    |]
                                    , ed.TextArea // for SkipSelectedText
                                    )

    let errSegments = LineTransformers<SegmentToMark>()

    member _.ErrSegments = errSegments

    /// Does not increment while waiting for completion window to open
    /// Or while waiting for an item in the completion window to be picked
    member _.DocChangedId  = changeId

    /// Threadsafe Increment of DocChangedId
    /// Returns the incremented value.
    member _.Increment() = System.Threading.Interlocked.Increment changeId

    /// Checks if passed in int64 is same as current DocChangedId
    /// returns option type for monadic composition
    member _.IsLatestOpt id  = if changeId.Value = id then Some true else None

    /// Checks if passed in int64 is same as current DocChangedId.
    member _.IsLatest id  =  changeId.Value = id

    member val DocChangedConsequence = React with get, set

    member val CodeLines = CodeLineTools.CodeLines() with get

    /// To avoid re-trigger of completion window on single char completions
    /// the window may just have closed, but for pressing esc, not for completion insertion
    /// this is only true if it just closed for insertion
    member val JustCompleted = false with get, set

    /// reacts to doc changes
    /// for Semantics and bad indentations
    member _.TransformersSemantic          = transformersSemantic

    /// reacts to doc changes
    /// for colorizing all brackets
    member _.TransformersAllBrackets       = transformersAllBrackets

    /// reacts to caret changes
    /// for colorizing matching brackets
    member _.TransformersMatchingBrackets  = transformersMatchingBrackets

    /// reacts to selection changes
    /// for colorizing text that matches the current selection
    member _.TransformersSelection         = transformersSelection

    member _.FastColorizer                 = fastColorizer

    member _.Config = config

    member _.Editor = ed

    member _.FoldManager = foldManager


(*
// general Highlighting needs:

type AfterWait =
    | DidNotEvenShow // prefilter found no items
    | Canceled // esc was pressed
    | Inserted // successful completion

type ReactToChange =
    | ShowCompletions

    /// when two or more characters changed
    /// first: Foldings, ColorBrackets and BadIndentation when full text available async.
    /// second: Errors and Semantic Highlighting on check result .
    | TwoStepMarking

    /// when only one character changed
    /// first just shift everything by offset, then mark all with check results
    | ShiftAndOneStepMarking

    //| JustShift // when typing single chars in in comments or strings (detect via xshd highlighting)


/// Do on mouse hover too ?
type CaretChangedConsequence =
    | MatchBrackets // and redraw range
    | NoBrackets // no brackets at cursor
    | WaitForCompl // wait for completion window to close
    | SelectingText // there is a selection happening

type SelectionChangedConsequence =
    | HighlightSelection // and redraw all or find range ?
    | NoSelectionHighlight // just on char,  white or multiline
*)

