namespace Seff.Editor

open AvalonEditB.Folding
open AvalonEditB.Document
open AvalonEditB

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
                    match code.IndexOf ('\r', stOff) with //TODO '\r' might fail if Seff is ever ported to AvaloniaEdit to work on MAC
                    | -1 -> 
                        let len = codeLen - stOff
                        let indent = spacesFrom stOff len code
                        newLns.Add {offStart=stOff; indent=indent; len=len}  // the last line      
                    | r -> 
                        let len = r - stOff
                        let indent = spacesFrom stOff len code
                        newLns.Add {offStart=stOff; indent=indent; len=len}
                        loop (r+2) // +2 to jump over \r and \n

            newLns.Add {offStart=0; indent=0; len=0}   // ad dummy line at index 0
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


    /// used for Log
    /// Holds a List who's indices correspond to each line with info about:
    /// offStart: the offset of the first character off this line 
    /// indent:  the count of spaces at the start of this line 
    /// len: the amount of characters in this line excluding the trailing \r\n
    /// if indent equals len the line is only whitespace
    type CodeLinesSimple() =
        
        let mutable lines = ResizeArray<LineInfo>()

        let mutable fullCode = ""

        let getNewLines(code:string) =   
            let newLns = ResizeArray<LineInfo>(lines.Count + 50) // TODO turn this into an append only , since the Log is append only, instead of reallocating:
            let codeLen = code.Length
            let rec loop stOff = 
                if stOff >= codeLen then // last line 
                    let len = codeLen - stOff
                    newLns.Add {offStart=stOff; indent=len; len=len}   
                else
                    match code.IndexOf ('\r', stOff) with //TODO '\r' might fail if Seff is ever ported to AvaloniaEdit to work on MAC
                    | -1 -> 
                        let len = codeLen - stOff
                        let indent = spacesFrom stOff len code
                        newLns.Add {offStart=stOff; indent=indent; len=len}  // the last line      
                    | r -> 
                        let len = r - stOff
                        let indent = spacesFrom stOff len code
                        newLns.Add {offStart=stOff; indent=indent; len=len}
                        loop (r+2) // +2 to jump over \r and \n

            newLns.Add {offStart=0; indent=0; len=0}   // ad dummy line at index 0
            loop (0)
            newLns


        member _.LastLineIdx = lines.Count - 1

        member _.FullCode = fullCode
                 
        member _.UpdateLogLines(code) = 
            lines <- getNewLines code
            fullCode <- code

        /// Safe: checks isDone && docChangedIdHolder.Value = id
        /// returns also none for bad indices
        member _.GetLine(lineIdx): LineInfo  =
            if lineIdx < 0 || lineIdx >= lines.Count then 
                failwithf "bad lineIdx %i for %d items in CodeLinesSimple" lineIdx lines.Count
            else
                lines.[lineIdx]
           
type DocChangedConsequence = 
    | React
    | WaitForCompletions 

/// Tracking the lastest change Ids to the document
/// FoldManager may be null for Log
[<AllowNullLiteral>] // for log initially
type InteractionState(ed:TextEditor, foldManager:FoldingManager, config:Seff.Config.Config)  =
    
    let changeId = ref 0L 
    
    /// reacts to doc changes
    /// for Semantics and bad indentations
    let transformersSemantic          = new LineTransformers<LinePartChange>() 
    
    /// reacts to doc changes
    /// for colorizing all brackets
    let transformersAllBrackets       = new LineTransformers<LinePartChange>() 

    /// reacts to caret changes
    /// for colorizing matching brackets
    let transformersMatchingBrackets  = new LineTransformers<LinePartChange>()   // TODO reenable use when fixed   

    /// reacts to selection changes
    /// for colorizing text that matches the current selection
    let transformersSelection         = new LineTransformers<LinePartChange>() 
    
    let fastColorizer = new FastColorizer( 
                                    [|
                                    transformersAllBrackets
                                    //transformersMatchingBrackets   // TODO reenable when fixed          
                                    transformersSelection
                                    transformersSemantic // draw errors last so they are on top of matching brackets
                                    |]
                                    ,ed // for debugging only
                                    ) 

    let errSegments = LineTransformers<SegmentToMark>()

    member _.ErrSegments = errSegments

    /// Does not increment while waiting for completion window to open 
    /// Or while waiting for an item in the completion window to be picked
    member _.DocChangedId  = changeId

    /// Threadsafe Increment of DocChangedId
    /// Returns the incremented value.
    member _.Increment() = System.Threading.Interlocked.Increment changeId
 
    /// Checks if passed in int64 is same as current DocChangedId.
    member _.IsLatestOpt id  =  if changeId.Value = id then Some true else None

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

