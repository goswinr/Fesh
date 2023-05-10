namespace Seff.Editor



// Highlighting needs:
    

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













type DocChangedConsequence = 
    | React
    | WaitForCompletions 

/// Tracking the lastest change Ids to the document
type InteractionState() =

    /// Does not increment while waiting for completion window to open 
    /// Or while waiting for an item in the completion window to be picked
    member val DocChangedId  = ref 0L with get 

    /// Checks if passed in int64 is same as current DocChangedId.
    /// if yes retuns Some true ( usefull for monadic chaining)
    member this.IfIsLatest id x =  if this.DocChangedId.Value = id then Some x else None

    /// Checks if passed in int64 is same as current DocChangedId.
    // if yes retuns second input ( usefull for monadic chaining)
    member this.IsLatest id  =  if this.DocChangedId.Value = id then Some true else None
   
    member val DocChangedConsequence = React with get, set

    /// To avoid re-trigger of completion window on single char completions
    /// the window may just have closed, but for pressing esc, not for completion insertion
    /// this is only true if it just closed for insertion
    member val JustCompleted = false with get, set


    member val FastColorizer = new FastColorizer() with get