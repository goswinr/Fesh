namespace Seff.Editor

open AvalonEditB.Folding
open AvalonEditB.Document
open AvalonEditB

module FullCode = 
    
    /// offStart: the offset of the first chracter off this line 
    /// indent:  the count of spaces at the start of this line 
    /// len: the amount of characters in this line excluding the trailing \r\n
    /// if indent equals len the line is only whitespace
    [<Struct>]
    type Line = {
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

    /// Holds a List who's indices correspond to each line with info about:
    /// offStart: the offset of the first chracter off this line 
    /// indent:  the count of spaces at the start of this line 
    /// len: the amount of characters in this line excluding the trailing \r\n
    /// if indent equals len the line is only whitespace
    type CodeLines(docChangedIdHolder:int64 ref) =
        
        let lns = ResizeArray<Line>(256)

        let mutable isDone = true

        let mutable fullCode = ""

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
                        lns.Add {offStart=stOff; indent=indent; len=len}        
                    | r -> 
                        let len = r - stOff
                        let indent = spacesFrom stOff len code
                        lns.Add {offStart=stOff; indent=indent; len=len}
                        loop (r+2)

            lns.Add {offStart=0; indent=0; len=0}   // ad dummy line at index 0
            loop (0)
            isDone <- true            

        member _.Lines = lns

        member _.LastLineIdx = lns.Count - 1

        member _.IsDone = isDone

        member _.FullCode = fullCode
        
        /// Safe: Only start sparsing when Done and also checks 
        /// if docChangedIdHolder.Value = id before and after
        member this.Update(code, chnageId): CodeLines option = 
            async{
                //Wait til done
                while not isDone && docChangedIdHolder.Value = chnageId do // because the id might expire while waiting
                    do! Async.Sleep 10
                return 
                    if docChangedIdHolder.Value <> chnageId then 
                        None
                    else
                        lns.Clear()
                        update code
                        if docChangedIdHolder.Value = chnageId then 
                            Some this 
                        else 
                            None
            } |> Async.RunSynchronously

        /// Safe: checks isDone && docChangedIdHolder.Value = id
        member this.Get(chnageId): CodeLines option =
            if isDone && docChangedIdHolder.Value = chnageId then Some this else None

        /// Safe: checks isDone && docChangedIdHolder.Value = id
        /// retuns also none for bad indices
        member this.GetLine(lineIdx, chnageId): Line voption =
            if isDone && docChangedIdHolder.Value = chnageId then 
                if lineIdx < 0 || lineIdx >= lns.Count then 
                    ValueNone
                else
                    ValueSome lns.[lineIdx]
            else 
                ValueNone


// Highlighting needs:
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



type DocChangedConsequence = 
    | React
    | WaitForCompletions 

/// Tracking the lastest change Ids to the document
/// foldManager may be null
type InteractionState(ed:TextEditor, foldManager:FoldingManager, config:Seff.Config.Config) as this =
    
    let cid = ref 0L

    //let folds = foldManager.AllFoldings :?> TextSegmentCollection<FoldingSection>
    
    /// Does not increment while waiting for completion window to open 
    /// Or while waiting for an item in the completion window to be picked
    member _.DocChangedId  = cid

    /// Threadsave Increment of DocChangedId
    /// Returns the incremented value.
    member _.Increment() = System.Threading.Interlocked.Increment cid
 
    /// Checks if passed in int64 is same as current DocChangedId.
    // if yes retuns second input ( usefull for monadic chaining)
    member _.IsLatestOpt id  =  if cid.Value = id then Some true else None

    /// Checks if passed in int64 is same as current DocChangedId.
    member _.IsLatest id  =  cid.Value = id 
   
    member val DocChangedConsequence = React with get, set

    member val CodeLines = FullCode.CodeLines(cid) with get

    /// To avoid re-trigger of completion window on single char completions
    /// the window may just have closed, but for pressing esc, not for completion insertion
    /// this is only true if it just closed for insertion
    member val JustCompleted = false with get, set
    
    
    member val TransformersSemantic          = new LineTransformers<LinePartChange>() with get
    member val TransformersAllBrackets       = new LineTransformers<LinePartChange>() with get
    member val TransformersMatchingBrackets  = new LineTransformers<LinePartChange>() with get
    member val TransformersSelection         = new LineTransformers<LinePartChange>() with get
    member val FastColorizer = new FastColorizer( [|
                                    this.TransformersAllBrackets
                                    this.TransformersSelection
                                    this.TransformersSemantic
                                    this.TransformersMatchingBrackets            
                                    |] ) with get    

    

    //member val Caret = 0 with get, set

    member _.Config = config
    
    member _.Editor = ed

    //member _.FoldSegments = folds

    member _.FoldManager = foldManager |> Option.ofObj