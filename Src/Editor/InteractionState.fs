namespace Seff.Editor

open AvalonEditB.Folding
open AvalonEditB.Document
open AvalonEditB

module CodeLineTools = 
    
    /// offStart: the offset of the first chracter off this line 
    /// indent:  the count of spaces at the start of this line 
    /// len: the amount of characters in this line excluding the trailing \r\n
    /// if indent equals len the line is only whitespace
    [<Struct>]
    type LineInfo = {
        offStart:int // the offset of the first chracter off this line 
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
    /// offStart: the offset of the first chracter off this line 
    /// indent:  the count of spaces at the start of this line 
    /// len: the amount of characters in this line excluding the trailing \r\n
    /// if indent equals len the line is only whitespace
    type CodeLines(docChangedIdHolder:int64 ref) =
        
        let lns = ResizeArray<LineInfo>(256)

        let mutable isDone = true

        let mutable fullCode = ""

        let mutable correspondingId = 0L

        let update(code:string) =
            isDone <- false
            fullCode <- code

            let codeLen = code.Length

            let rec loop stOff = 
                if stOff >= codeLen then // last line 
                    let len = codeLen - stOff
                    lns.Add {offStart=stOff; indent=len; len=len}   
                else
                    match code.IndexOf ('\r', stOff) with //TODO '\r' might fail if Seff is ever ported to AvaloniaEdit to work on MAC
                    | -1 -> 
                        let len = codeLen - stOff
                        let indent = spacesFrom stOff len code
                        lns.Add {offStart=stOff; indent=indent; len=len}  // the last line      
                    | r -> 
                        let len = r - stOff
                        let indent = spacesFrom stOff len code
                        lns.Add {offStart=stOff; indent=indent; len=len}
                        loop (r+2) // +2 to jump over \r and \n

            lns.Add {offStart=0; indent=0; len=0}   // ad dummy line at index 0
            loop (0)
            isDone <- true            

        member _.Lines = lns

        member _.LastLineIdx = lns.Count - 1

        member _.IsDone = isDone

        member _.FullCode = fullCode

        /// checks if this codelines correspond to a given ID
        member _.IsFromID(id) = id = correspondingId
        
        /// checks if this codelines does not correspond to a given ID
        member _.IsNotFromId(id) = id <> correspondingId

        /// ThreadSafe and in Sync: Only start sparsing when Done and also checks 
        /// if docChangedIdHolder.Value = id before and after
        /// returns True 
        member _.Update(code, changeId): bool = 
            async{
                //Wait til done
                while not isDone && docChangedIdHolder.Value = changeId do // because the id might expire while waiting
                    do! Async.Sleep 10
                return 
                    if docChangedIdHolder.Value <> changeId then 
                        false
                    else
                        lns.Clear()
                        update code
                        if docChangedIdHolder.Value = changeId then 
                            correspondingId <- changeId
                            true
                        else 
                            false
            } |> Async.RunSynchronously


        // Safe: checks isDone && docChangedIdHolder.Value = id
        //member this.Get(changeId): CodeLines option =  if isDone && docChangedIdHolder.Value = changeId then Some this else None

        /// Safe: checks isDone && docChangedIdHolder.Value = id
        /// retuns also none for bad indices
        member _.GetLine(lineIdx, changeId): LineInfo voption =
            if isDone && docChangedIdHolder.Value = changeId then 
                if lineIdx < 0 || lineIdx >= lns.Count then 
                    ValueNone
                else
                    ValueSome lns.[lineIdx]
            else 
                ValueNone

        /// Safe: checks isDone && docChangedIdHolder.Value = id
        /// retuns also none for bad indices
        member _.GetLineText(lineIdx, changeId): string voption =
            if isDone && docChangedIdHolder.Value = changeId then 
                if lineIdx < 0 || lineIdx >= lns.Count then 
                    ValueNone
                else
                    let l = lns.[lineIdx]
                    ValueSome (fullCode.Substring(l.offStart,l.len))
                    
            else 
                ValueNone

    
    /// used for Log
    /// Holds a List who's indices correspond to each line with info about:
    /// offStart: the offset of the first chracter off this line 
    /// indent:  the count of spaces at the start of this line 
    /// len: the amount of characters in this line excluding the trailing \r\n
    /// if indent equals len the line is only whitespace
    type CodeLinesSimple() =
        
        let lns = ResizeArray<LineInfo>(256)

        let mutable fullCode = ""

        let update(code:string) =            
            fullCode <- code

            let codeLen = code.Length

            let rec loop stOff = 
                if stOff >= codeLen then // last line 
                    let len = codeLen - stOff
                    lns.Add {offStart=stOff; indent=len; len=len}   
                else
                    match code.IndexOf ('\r', stOff) with //TODO '\r' might fail if Seff is ever ported to AvaloniaEdit to work on MAC
                    | -1 -> 
                        let len = codeLen - stOff
                        let indent = spacesFrom stOff len code
                        lns.Add {offStart=stOff; indent=indent; len=len}  // the last line      
                    | r -> 
                        let len = r - stOff
                        let indent = spacesFrom stOff len code
                        lns.Add {offStart=stOff; indent=indent; len=len}
                        loop (r+2) // +2 to jump over \r and \n

            lns.Add {offStart=0; indent=0; len=0}   // ad dummy line at index 0
            loop (0)                      

        member _.Lines = lns

        member _.LastLineIdx = lns.Count - 1

        member _.FullCode = fullCode
                 
        member _.Update(code) =             
            lns.Clear()
            update code

        /// Safe: checks isDone && docChangedIdHolder.Value = id
        /// retuns also none for bad indices
        member _.GetLine(lineIdx, changeId): LineInfo voption =
            if lineIdx < 0 || lineIdx >= lns.Count then 
                ValueNone
            else
                ValueSome lns.[lineIdx]
           
type DocChangedConsequence = 
    | React
    | WaitForCompletions 

/// Tracking the lastest change Ids to the document
/// foldManager may be null
[<AllowNullLiteral>] // for log initially
type InteractionState(ed:TextEditor, foldManager:FoldingManager, config:Seff.Config.Config)  =
    
    let changeId = ref 0L 
    
    /// reacts to doc changes
    /// for Errors and semantics
    let transformersSemantic          = new LineTransformers<LinePartChange>() 
    
    /// reacts to doc changes
    /// for Brackets, and bad indents
    let transformersAllBrackets       = new LineTransformers<LinePartChange>() 

    /// reacts to caret changes
    let transformersMatchingBrackets  = new LineTransformers<LinePartChange>()   

    /// reacts to document chnages
    let transformersSelection         = new LineTransformers<LinePartChange>() 
    
    let fastColorizer = new FastColorizer( 
                                    [|
                                    transformersAllBrackets
                                    transformersSelection
                                    transformersSemantic
                                    transformersMatchingBrackets            
                                    |]
                                    ,ed // for debugging only
                                    ) 


    
    /// Does not increment while waiting for completion window to open 
    /// Or while waiting for an item in the completion window to be picked
    member _.DocChangedId  = changeId

    /// Threadsave Increment of DocChangedId
    /// Returns the incremented value.
    member _.Increment() = System.Threading.Interlocked.Increment changeId
 
    /// Checks if passed in int64 is same as current DocChangedId.
    // if yes retuns second input ( usefull for monadic chaining)
    member _.IsLatestOpt id  =  if changeId.Value = id then Some true else None

    /// Checks if passed in int64 is same as current DocChangedId.
    member _.IsLatest id  =  changeId.Value = id 
   
    member val DocChangedConsequence = React with get, set

    member val CodeLines = CodeLineTools.CodeLines(changeId) with get

    /// To avoid re-trigger of completion window on single char completions
    /// the window may just have closed, but for pressing esc, not for completion insertion
    /// this is only true if it just closed for insertion
    member val JustCompleted = false with get, set
    
    /// reacts to doc changes
    /// for Errors and semantics
    member _.TransformersSemantic          = transformersSemantic
    
    /// reacts to doc changes
    /// for Brackets, and bad indents
    member _.TransformersAllBrackets       = transformersAllBrackets

    /// reacts to caret changes
    member _.TransformersMatchingBrackets  = transformersMatchingBrackets

    /// reacts to document chnages
    member _.TransformersSelection         = transformersSelection
    
    member _.FastColorizer                 = fastColorizer                


    member _.Config = config
    
    member _.Editor = ed

    member _.FoldManager = foldManager 

    // the caret position that can be savely accessed async  // DELETE
    //member val Caret = 0 with get,set
    


// Highlighting needs:  // DELETE
(*

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




(*
module LineOffsets = 
    
    /// offStart: the offset of the first chracter off this line 
    /// indent:  the count of spaces at the start of this line 
    /// len: the amount of characters in this line excluding the trailing \r\n
    /// if indent equals len the line is only whitespace
    [<Struct>]
    type LineOff = {
        /// The offset of the first chracter off this line 
        offStart:int 
        /// The count of spaces at the start of this line 
        indent:int 
        /// The amount of characters in this line excluding the trailing \r\n
        len: int 
        }


    /// Counts spaces after a position
    let inline private spacesFrom off len (str:string) = 
        let mutable ind = 0
        while ind < len && str.[off+ind] = ' ' do
            ind <- ind + 1
        ind

    /// Holds a list of:
    /// offStart: the offset of the first chracter off this line 
    /// indent:  the count of spaces at the start of this line 
    /// len: the amount of characters in this line excluding the trailing \r\n
    /// if indent equals len the line is only whitespace
    type LineOffsets(code) =
        
        let lns = ResizeArray<LineOff>(256)

        //let mutable isDone = false

        let parse(code:string) =
            //isDone <- false

            let codeLen = code.Length

            let rec loop stOff = 
                if stOff >= codeLen then // last line 
                    let len = codeLen - stOff
                    lns.Add {offStart=stOff; indent=len; len=len}   
                else
                    match code.IndexOf ('\r', stOff) with //TODO '\r' might fail if Seff is ever ported to AvaloniaEdit to work on MAC
                    | -1 -> 
                        let len = codeLen - stOff
                        let indent = spacesFrom stOff len code
                        lns.Add {offStart=stOff; indent=indent; len=len}        
                    | r -> 
                        let len = r - stOff
                        let indent = spacesFrom stOff len code
                        lns.Add {offStart=stOff; indent=indent; len=len}
                        loop (r+2)

            lns.Add {offStart=0; indent=0; len=0}   // ad dummy line at index 0
            loop (0)
            //isDone <- true         
        
        do parse code

        //member _.Lines = lns

        //member _.IsDone = isDone

        //member _.Parse code = parse code

        member _.Item(lineNumber) = lns.[lineNumber]
*)

