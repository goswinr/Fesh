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


module SelectionHighlighting =

    let colorEditor  = Brushes.PaleTurquoise |> AvalonLog.Brush.freeze      

    let colorLog     = Brushes.Blue          |> AvalonLog.Brush.brighter 210  |> AvalonLog.Brush.freeze
        
    let foundSelectionLogEv    = new Event<unit>()
    let foundSelectionEditorEv = new Event<unit>()

    [<CLIEvent>] 
    let FoundSelectionsEditor = foundSelectionEditorEv.Publish

    [<CLIEvent>] 
    let FoundSelectionsLog    = foundSelectionLogEv.Publish

    

open SelectionHighlighting

/// Highlight-all-occurrences-of-selected-text in Editor and Log
type SelectionHighlighter (edState:InteractionState, lgStateOpt:InteractionState option) = 
    
    let actionEditor  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colorEditor))
    let actionLog     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colorLog))

    let priority = Windows.Threading.DispatcherPriority.Render

    let mutable lastWord = ""
    let mutable lastSels = ResizeArray<int>()    
    let mutable lastSelsLog = ResizeArray<int>()    
    
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
    
    let mark (state:InteractionState, word:string, action, event:Event<unit>, isEditor, selectionStartOff) =
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
            if isEditor then lastSels <- offs else lastSelsLog <- offs
            lastWord <- word
            if loop 1 then // tests if ther is a newer doc change                 
                match  prev, trans.Range with 
                | None       , None  -> ()   // nothing before, nothing now
                
                | Some (f,l) , None          // some before, nothing now
                | None       , Some (f,l) -> // nothing before, some now
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                    markFoldingsSorted(state,offs)
                    state.Editor.TextArea.TextView.Redraw(f.from, l.till, priority)
                    event.Trigger()                   
                
                | Some (pf,pl),Some (f,l) ->   // both prev and current version have a selection                 
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context 
                    markFoldingsSorted(state,offs)
                    state.Editor.TextArea.TextView.Redraw(min pf.from f.from, max pl.till l.till, priority)
                    event.Trigger()
            else
                printfn $"cancelled redraw from Ed: {isEditor}"
                () // dont redraw, there is already a new docchange happening that will be drawn
            
            if isEditor then 
                printfn $"Found '{word}' {offs.Count} times in Editor:\r\n{lines.FullCode}"
            else
                printfn $"Found '{word}' {offs.Count} times in Log:\r\n{lines.FullCode}"
        }|> Async.Start
    
    
    let markBoth(selectionStartOff, word, isFromEd) =
        match lgStateOpt with 
        |None -> ISeffLog.log.PrintfnAppErrorMsg "Interaction state not set on AvalonLog (1)" // should never happen
        |Some lgState ->
            if isFromEd then 
                //mark(lgState, word, actionLog   , foundSelectionLogEv   , false,  -1)            
                mark(edState, word, actionEditor, foundSelectionEditorEv, true,  selectionStartOff)
            else
                mark(lgState, word, actionLog   , foundSelectionLogEv   , false,  selectionStartOff)
                //mark(edState, word, actionEditor, foundSelectionEditorEv, true,  -1)
              
    let clearBoth() =
        match lgStateOpt with 
        |None -> ISeffLog.log.PrintfnAppErrorMsg "Interaction state not set on AvalonLog (2)" // should never happen
        |Some lgState ->      
            lastWord <- ""
            lastSels <- ResizeArray<int>()    
            lastSelsLog <- ResizeArray<int>() 
            justClear(edState)
            justClear(lgState)
            foundSelectionLogEv.Trigger()
            foundSelectionEditorEv.Trigger()
    
    
    let updateBoth(this:TextEditor, isFromEd) =        
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
        match lgStateOpt with 
        |None -> ISeffLog.log.PrintfnAppErrorMsg "Interaction state not set on AvalonLog (3)" // should never happen
        |Some lgState ->         
            edState.Editor.TextArea.SelectionChanged.Add ( fun _ ->                                          updateBoth(edState.Editor, true)  )        
            lgState.Editor.TextArea.SelectionChanged.Add ( fun _ -> if IEditor.isCurrent edState.Editor then updateBoth(lgState.Editor, false) )
    
    member _.Word    = lastWord 
    member _.Offsets = lastSels    
    member _.OffsetsLog = lastSelsLog