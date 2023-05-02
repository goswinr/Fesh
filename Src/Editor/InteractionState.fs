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


type DocChangedConsequence = 
    | React //of ReactToChange
    | WaitForCompletions //of AfterWait
    
    
/// Do on mouse hover too ?
type CaretChangedConsequence = 
    | MatchBrackets // and redraw range
    | NoBrackets // no brackets at cursor
    | WaitForCompl // wait for completion window to close
    | SelectingText // there is a selection happening

type SelectionChangedConsequence = 
    | HighlightSelection // and redraw all or find range ?
    | NoSelectionHighlight // just on char,  white or multiline


/// Tracking the lastest change Ids to the document
type InteractionState() =

    member val DocChangedId  = ref 0L with get 
   
    member val DocChangedConsequence = React with get, set

