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
open Seff.Editor.CodeLineTools


module SelectionHighlighting =

    let colorEditor  = Brushes.PaleTurquoise |> AvalonLog.Brush.freeze      

    let colorLog     = Brushes.Blue |> AvalonLog.Brush.brighter 210  |> AvalonLog.Brush.freeze
    let colorInactive= Brushes.Gray                                  |> AvalonLog.Brush.freeze
        
    let foundSelectionLogEv    = new Event<bool>() 
    let foundSelectionEditorEv = new Event<bool>()

    [<CLIEvent>] 
    let FoundSelectionsEditor = foundSelectionEditorEv.Publish

    [<CLIEvent>] 
    let FoundSelectionsLog    = foundSelectionLogEv.Publish

    let inline isTextToHighlight(t:string) = 
        t.Length > 1 && not (Str.isJustSpaceCharsOrEmpty t)  && not <| t.Contains("\n") 

    let priority = Windows.Threading.DispatcherPriority.Render
    
    let empty = ResizeArray()

open SelectionHighlighting

/// Highlight-all-occurrences-of-selected-text in Editor
type SelectionHighlighter (state:InteractionState) = 
    
    let action  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colorEditor))

    let mutable lastWord = ""
    let mutable lastSels = ResizeArray<int>()      

    let ed = state.Editor  
      
    let markFoldingsSorted(offs:ResizeArray<int>) =       
        let mutable offsSearchFromIdx =  0
        for f in state.FoldManager.AllFoldings do 
            f.BackgroundColor <- null // first reset
            let rec loop i =                 
                if i >= offs.Count then 
                    offsSearchFromIdx <- i // to exit on all next fold immediately
                else 
                    let off = offs.[i]
                    if f.EndOffset < off then // all following offset are bigger than this fold stop searching
                        offsSearchFromIdx <- i // to search from this index on in next fold
                    elif f.StartOffset < off && off < f.EndOffset then // this offset is the first within the range of the current fold
                        f.BackgroundColor <- colorEditor 
                        offsSearchFromIdx <- i // to search from this index on in next fold
                    else
                        loop (i+1)
            loop (offsSearchFromIdx)  
    
    let mutable prevRange: (LinePartChange*LinePartChange) option = None

    let justClear(triggerNext) =
        if lastSels.Count > 0 then 
            lastWord <- ""
            lastSels.Clear()
            prevRange <- None
            let trans = state.TransformersSelection
            async{                 
                let thisRange = trans.Range                 
                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context   
                match thisRange with 
                | None   ->  ()                   
                    // still trigger event to clear the selection in StatusBar if it is just a selection without any highlighting(e.g. multiline)                                     
                | Some (f,l) ->                
                    trans.Update(empty)// using empty array                               
                    for f in state.FoldManager.AllFoldings do f.BackgroundColor <- null  
                    ed.TextArea.TextView.Redraw(f.from, l.till, priority)                
                foundSelectionEditorEv.Trigger(triggerNext)
            }|> Async.Start

    // Called from StatusBar to highlight the current selection of Log in Editor too
    let mark (word:string, selectionStartOff, triggerNext:bool) =
        let id = state.DocChangedId.Value
        async{
            let lines = state.CodeLines
            let trans = state.TransformersSelection
            let codeStr  = lines.FullCode
            let lastLineNo = lines.LastLineIdx
            let wordLen = word.Length
            let offs = ResizeArray<int>()
            
            let newMarks = ResizeArray<ResizeArray<LinePartChange>>()
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
                            if off <> selectionStartOff then // skip the actual current selection from highlighting                               
                                LineTransformers.Insert(newMarks, lineNo, {from=off; till=off+wordLen; act=action})                                 
                            let start = off + word.Length // search from this for next occurrence in this line 
                            let lenReduction = start - l.offStart
                            let remainingLineLength = l.len - lenReduction
                            off <- codeStr.IndexOf(word, start, remainingLineLength , StringComparison.Ordinal)                            
                        loop (lineNo + 1)
                     
            
            if loop 1 then // tests if there is a newer doc change 
                lastSels <- offs 
                lastWord <- word
                trans.Update(newMarks)
                let thisRange = trans.Range              
                let st,en = // get range to redraw
                    match  prevRange, thisRange with 
                    | None       , None  ->    // nothing before, nothing now
                        if offs.Count = 1 then -1,-1 // but maybe just the current selection that doesn't need highlighting, but still show in status bar
                        else -2,-2

                    | Some (f,l) , None          // some before, nothing now
                    | None       , Some (f,l) -> // nothing before, some now                    
                        f.from, l.till

                    | Some (pf,pl),Some (f,l) ->   // both prev and current version have a selection                    
                        min pf.from f.from, max pl.till l.till

                if st > -2 then
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                    if st > -1 then // if there is only one item in text it is akready selected, no need for redrawing, just update status bar
                        markFoldingsSorted(offs)
                        prevRange <- thisRange
                        ed.TextArea.TextView.Redraw(st,en, priority)
                    //if not triggerNext then ed.TextArea.ClearSelection() //don't becaus this trigger a selection changed event
                    foundSelectionEditorEv.Trigger(triggerNext) 
            
            else
                () // don't redraw, there is already a new doc change happening that will be drawn
            

        }|> Async.Start
     
    let update() = 
        match Selection.getSelType ed.TextArea with 
        |RegSel  -> 
            if ed.TextArea.Selection.IsMultiline then 
                justClear(true)
            else
                let word = ed.SelectedText
                if isTextToHighlight word then  //is at least two chars and has no line breaks
                    let startOff = ed.SelectionStart
                    mark(word, startOff, true)
                else
                    justClear(true)
        |NoSel   -> justClear(true)
        |RectSel -> justClear(true) 
    
    
    do         
        ed.TextArea.SelectionChanged.Add ( fun _ -> update() ) 
    
    member _.Word    = lastWord 

    member _.Offsets = lastSels  
    
    member _.Mark(word) = 
        if isTextToHighlight word then 
            mark(word,-1, false)
        else 
            justClear(false) // isTextToHighlight is needed , word might be empty string


/// Highlight-all-occurrences-of-selected-text in Log 
type SelectionHighlighterLog (lg:TextEditor) = 
    
    let action  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colorLog))

    let mutable lastWord = ""
    let mutable lastSels = ResizeArray<int>()      
        
    let isTextToHighlight(t:string) = 
        t.Length > 1 && not (Str.isJustSpaceCharsOrEmpty t)  && not <| t.Contains("\n") 
    
    let trans = LineTransformers<LinePartChange>()    
    let colorizer = FastColorizer([|trans|], lg ) 
    
    let mutable prevRange: (LinePartChange*LinePartChange) option = None
    
    let justClear(triggerNext) =
        if lastSels.Count > 0 then 
            lastWord <- ""
            lastSels.Clear()
            prevRange <- None
            async{ 
                let thisRange = trans.Range                 
                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context   
                match thisRange with 
                | None   -> ()                    
                    // still trigger event to clear the selection in StatusBar if it is just a selection without any highlighting(e.g. multiline)                                       
                | Some (f,l) ->                
                    trans.Update(empty)// using empty array                                   
                    //for f in state.FoldManager.AllFoldings do f.BackgroundColor <- null  // no folds in Log !!
                    lg.TextArea.TextView.Redraw(f.from, l.till, priority)                
                foundSelectionEditorEv.Trigger(triggerNext)

            }|> Async.Start

    let lines = CodeLinesSimple()
    let mutable linesNeedUpdate = true
    
    /// tracks changes to the log
    let logChangeID = ref 0L

    /// track new highlighting requests 
    let markCallID  = ref 0 // because while getting the text below, the Editor selection might have changed already
    
    // Called from StatusBar to highlight the current selection of Editor in Log too        
    let mark (word:string, selectionStartOff, triggerNext:bool) =
        let chnageId = logChangeID.Value
        let markId   = Threading.Interlocked.Increment markCallID
        let doc = lg.Document
        async{
            while linesNeedUpdate && logChangeID.Value = chnageId do                 
                do! Async.Sleep 50 // needed for getting correct text in snapshot
                if logChangeID.Value = chnageId then
                    let t = doc.CreateSnapshot().Text
                    lines.Update(t)
                    if logChangeID.Value = chnageId then // this forces waiting till there are no more updates
                        linesNeedUpdate <- false                     
            
            if markId = markCallID.Value && logChangeID.Value = chnageId  then // because while getting the text above, the text or the selection might have changed already
                let codeStr  = lines.FullCode
                let lastLineNo = lines.LastLineIdx
                let wordLen = word.Length
                let offs = ResizeArray<int>()
                let newMarks = ResizeArray<ResizeArray<LinePartChange>>()

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
                                    //ISeffLog.log.PrintfnInfoMsg $"trans.Insert({lineNo}, from={off}; till={off+wordLen}; act=action word='{word}'"
                                    LineTransformers.Insert(newMarks,lineNo, {from=off; till=off+wordLen; act=action})                                 
                                let start = off + word.Length // search from this for next occurrence in this line 
                                let lenReduction = start - l.offStart
                                let remainingLineLength = l.len - lenReduction
                                off <- codeStr.IndexOf(word, start, remainingLineLength , StringComparison.Ordinal)
                            
                            loop (lineNo + 1)                
                
                if loop 1 && markId = markCallID.Value then // tests if there is a newer doc change 
                    lastSels <- offs 
                    lastWord <- word
                    trans.Update(newMarks)
                    let thisRange = trans.Range              
                    let st,en = // get range to redraw
                        match  prevRange, thisRange with 
                        | None       , None  ->    // nothing before, nothing now
                            if offs.Count = 1 then -1,-1 // but maybe just the current selection that doesn't need highlighting, but still show in status bar
                            else -2,-2

                        | Some (f,l) , None          // some before, nothing now
                        | None       , Some (f,l) -> // nothing before, some now                    
                            f.from, l.till

                        | Some (pf,pl),Some (f,l) ->   // both prev and current version have a selection                    
                            min pf.from f.from, max pl.till l.till

                    if st > -2 then
                        do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                        if st > -1 then // if there is only one item in text it is akready selected, no need for redrawing, just update status bar
                            //markFoldingsSorted(offs) // no foldings in Log
                            prevRange <- thisRange
                            lg.TextArea.TextView.Redraw(st,en, priority)
                        //if not triggerNext then lg.TextArea.ClearSelection()//don't becaus this trigger a selection changed event
                        foundSelectionLogEv.Trigger(triggerNext) 
                
                else
                    () // don't redraw, there is already a new doc change happening that will be drawn
                
        }|> Async.Start
    
    let update() = 
        match Selection.getSelType lg.TextArea with 
        |RegSel  -> 
            if lg.TextArea.Selection.IsMultiline then 
                justClear(true)
            else
                let word = lg.SelectedText
                if isTextToHighlight word then  //is at least two chars and has no line breaks
                    let startOff = lg.SelectionStart
                    mark(word, startOff, true)
                else
                    justClear(true)
        |NoSel   -> justClear(true)
        |RectSel -> justClear(true) 

   
    do         
        lg.TextArea.TextView.LineTransformers.Insert(0, colorizer) // insert at index 0 so that it is drawn first, so that text color is overwritten the selection highlighting
        lg.TextArea.SelectionChanged.Add ( fun _ -> update() )        
        lg.Document.Changing.Add (fun _ -> 
            Threading.Interlocked.Increment logChangeID |> ignore
            linesNeedUpdate <- true
            )
    
    member _.Word    = lastWord 
    member _.Offsets = lastSels 
    
    /// Called from StatusBar to highlight the current selection of Editor in Log too
    member _.Mark(word) = 
        if isTextToHighlight word then // isTextToHighlight is needed , word might be empty string
            mark(word,-1, false) 
        else 
            justClear(false)    