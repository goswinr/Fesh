namespace Seff.Editor

open System
open System.Windows.Media
open AvalonEditB
open AvalonEditB.Rendering
open Seff.Util.General
open Seff.Util
open Seff.Model
open Seff.Editor.Selection
open Seff.Editor



/// Highlight-all-occurrences-of-selected-text in Text View
type SelectionHighlighter (edState:InteractionState, lgState:InteractionState) =    
    
    let colorEditor  = Brushes.PaleTurquoise |> AvalonLog.Brush.freeze      
    let colorLog     = Brushes.Blue          |> AvalonLog.Brush.brighter 210  |> AvalonLog.Brush.freeze
    let actionEditor  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colorEditor))
    let actionLog     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colorLog))

    let priority = Windows.Threading.DispatcherPriority.Render

    let foundSelectionEditorEv = new Event<ResizeArray<int>>()
    let foundSelectionLogEv = new Event<ResizeArray<int>>()

    
    let isTextToHighlight(t:string) = 
        t.Length > 1 && not (Str.isJustSpaceCharsOrEmpty t)  && not <| t.Contains("\n") 
     
    let clearFolds(state: InteractionState)=
        match state.FoldManager with 
        |None   -> () // no folds on log
        |Some m ->  
            for f in m.AllFoldings do 
                f.BackgroundColor <- null     
    
    let markFoldingsSorted(state: InteractionState, offs:ResizeArray<int>) =
        match state.FoldManager with 
        |None   -> () // no folds on log
        |Some m ->
            let mutable offsSearchFromIdx =  0
            for f in m.AllFoldings do 
                f.BackgroundColor <- null // first reset
                let rec loop i =                 
                    if i >= offs.Count then 
                        offsSearchFromIdx <- i // to exit on all next fold immediatly
                    else 
                        let off = offs.[i]
                        if f.EndOffset < off then // all following offset are bigger than this fold stop searching
                            offsSearchFromIdx <- i // to search from this index on in next fold
                        elif f.StartOffset < off && off < f.EndOffset then // this offest is the first within the range of the current fold
                            f.BackgroundColor <- colorEditor 
                            offsSearchFromIdx <- i // to search from this index on in next fold
                        else
                            loop (i+1)

                loop (offsSearchFromIdx)
  

    let justClear(state:InteractionState) =
        let trans = state.TransformersSelection
        match trans.Range with 
        | None       -> () // no transformers there anyway
        | Some (f,l) ->
            async{ 
                trans.ClearAllLines()
                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                clearFolds(state)
                state.Editor.TextArea.TextView.Redraw(f.from, l.till, priority)
            }|> Async.Start
    
    let mark (state:InteractionState, word:string, action, ev:Event<ResizeArray<int>>, selectionStartOff) =
        let id = state.DocChangedId.Value
        async{
            let lines = state.CodeLines
            let trans = state.TransformersSelection
            let codeStr  = lines.FullCode
            let lastLineNo = lines.LastLineIdx
            let wordLen = word.Length
            let offs = ResizeArray<int>()
            
            /// returns false if aborted because of newer doc change
            let rec loop lineNo = 
                if lineNo > lastLineNo then 
                    true // return true if loop completed
                else
                    match lines.GetLine(lineNo, id) with 
                    |ValueNone -> false // could not get code line, newer change happened already 
                    |ValueSome l -> 
                        let mutable off = codeStr.IndexOf(word, l.offStart, l.len, StringComparison.Ordinal)                        
                        while off >= 0 do
                            offs.Add off // also add for current selection
                            if off <> selectionStartOff then // skip the actual current selection
                                trans.Insert(lineNo, {from=off; till=off+wordLen; act=action})                                 
                            let start = off + word.Length // search from this for next occurrence in this line 
                            let lenReduction = start - l.offStart
                            let remainingLineLength = l.len - lenReduction
                            off <- codeStr.IndexOf(word, start, remainingLineLength , StringComparison.Ordinal)
                            
                        loop (lineNo + 1)
            
            let prev = trans.Range
            trans.ClearAllLines() // does nothing if already all cleared
            if loop 1 then // tests if ther is a newer doc change                 
                match  prev, trans.Range with 
                | None       , None  -> ()   // nothing before, nothing now
                
                | Some (f,l) , None          // some before, nothing now
                | None       , Some (f,l) -> // nothing before, some now
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context                    
                    markFoldingsSorted(state,offs)
                    state.Editor.TextArea.TextView.Redraw(f.from, l.till, priority)
                    ev.Trigger(offs)
                
                | Some (pf,pl),Some (f,l) ->   // both prev and current version have a selection                 
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context                    
                    markFoldingsSorted(state,offs)
                    state.Editor.TextArea.TextView.Redraw(min pf.from f.from, max pl.till l.till, priority)
                    ev.Trigger(offs)

            else
                () // dont redraw, there is already a new docchange happening that will be drawn
        }|> Async.Start
    
    
    let markBoth(selectionStartOff, word, isFromEd) =
        if isFromEd then 
            mark(edState, word, actionEditor, foundSelectionEditorEv, selectionStartOff)
            mark(lgState, word, actionLog   , foundSelectionLogEv   , -1)
        else
            mark(edState, word, actionEditor, foundSelectionEditorEv, -1)
            mark(lgState, word, actionLog   , foundSelectionLogEv   , selectionStartOff)

    let clearBoth() =
        justClear(edState)
        justClear(lgState)
    
    
    let update(this:TextEditor, isFromEd) =        
        match Selection.getSelType this.TextArea with 
        |RegSel  -> 
            let word = this.SelectedText
            if isTextToHighlight word then  //is at least two chars and has no line breaks
                let startOff = this.SelectionStart
                markBoth(startOff, word, isFromEd)
            else
                clearBoth()
        |NoSel   -> clearBoth()
        |RectSel -> clearBoth() 
    
    
    do         
        edState.Editor.TextArea.SelectionChanged.Add ( fun _ ->                                          update(edState.Editor, true)  )        
        lgState.Editor.TextArea.SelectionChanged.Add ( fun _ -> if IEditor.isCurrent edState.Editor then update(lgState.Editor, false) )


    [<CLIEvent>] 
    member _.FoundSelectionsEditor = foundSelectionEditorEv.Publish
    member _.FoundSelectionsLog    = foundSelectionLogEv.Publish

    