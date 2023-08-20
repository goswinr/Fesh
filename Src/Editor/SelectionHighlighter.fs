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
    let globalFoundSelectionEditorEv = new Event<bool>()

    [<CLIEvent>] 
    let GlobalFoundSelectionsEditor = globalFoundSelectionEditorEv.Publish

    [<CLIEvent>] 
    let FoundSelectionsLog    = foundSelectionLogEv.Publish

    let inline isTextToHighlight(t:string) = 
        t.Length > 1 && not (Str.isJustSpaceCharsOrEmpty t)  && not <| t.Contains("\n") 

    let priority = Windows.Threading.DispatcherPriority.Render
    
    let empty = ResizeArray()

open SelectionHighlighting
open Seff.XmlParser

type SkipMarking = 
    | SkipOffset of int
    | MarkAll

type SelRedraw =
    | SelRange of int*int
    | StatusbarOnly  
    | NoSelRedraw

/// Highlight-all-occurrences-of-selected-text in Editor
type SelectionHighlighter (state:InteractionState) = 
    
    let action  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colorEditor))

    let mutable lastSkipOff = MarkAll
    let mutable lastWord = ""
    let mutable lastSels = ResizeArray<int>()  
    let mutable reactToSelChange = true
    let mutable prevRange: (LinePartChange*LinePartChange) option = None
    
    let ed = state.Editor  

   
    let selTransformersSetEv = new Event<int64>() // used in EventCombiner for redrawing on doc changes

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
    

    let justClear(triggerNext) =
        if lastSels.Count > 0 then // to only clear once, then not again
            //eprintfn $"justClear"
            lastWord <- ""
            lastSkipOff <- MarkAll
            lastSels.Clear()
            prevRange <- None
            let trans = state.TransformersSelection
            async{                 
                let thisRange = trans.Range                 
                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context   
                match thisRange with 
                | None   ->  ()                   
                    // TODO ? still trigger event to clear the selection in StatusBar if it is just a selection without any highlighting(e.g. multiline)   

                | Some (f,l) ->                
                    trans.Update(empty)// using empty array                               
                    for f in state.FoldManager.AllFoldings do f.BackgroundColor <- null  
                    ed.TextArea.TextView.Redraw(f.from, l.till, priority)                
                globalFoundSelectionEditorEv.Trigger(triggerNext)
            }|> Async.Start

    /// state.CodeLines must be up to date before calling this
    /// returns true if not cancelled by newer change Id
    /// sets lastSels <- offs
    let setTransformers (changeId:int64) =       
        // must be set:
        // lastWord <- word
        // lastSkipOff <- skipOff
        if lastWord = "" then 
            selTransformersSetEv.Trigger(changeId) // can by async, still call this so the full redrawing is triggered
            true
        else  
            //eprintfn $"search in '{state.CodeLines.FullCode}'" //TODO: text typed on the last line after a comment might be missing somehow!
            
            let lines = state.CodeLines
            let codeStr  = lines.FullCode
            let lastLineNo = lines.LastLineIdx
            let wordLen = lastWord.Length
            let offs = ResizeArray<int>()
            
            let newMarks = ResizeArray<ResizeArray<LinePartChange>>()           
            let selectionStartOff = 
                match lastSkipOff with
                | SkipOffset skipOff -> skipOff
                | MarkAll -> -1

            /// returns false if aborted because of newer doc change
            let rec searchFromLine lineNo = 
                if lineNo > lastLineNo then 
                    true // return true if loop completed
                else
                    match lines.GetLine(lineNo, changeId) with 
                    |ValueNone -> false // could not get code line, newer change happened already 
                    |ValueSome l -> 
                        let line = codeStr.Substring(l.offStart, l.len)  
                        let mutable off = codeStr.IndexOf(lastWord, l.offStart, l.len, StringComparison.Ordinal)
                        while off >= 0 do
                            offs.Add off // also add for current selection                            
                            if off <> selectionStartOff then // skip the actual current selection from highlighting                               
                                LineTransformers.Insert(newMarks, lineNo, {from=off; till=off+wordLen; act=action})                                 
                            let start = off + lastWord.Length // search from this for next occurrence in this line 
                            let lenReduction = start - l.offStart
                            let remainingLineLength = l.len - lenReduction
                            off <- codeStr.IndexOf(lastWord, start, remainingLineLength , StringComparison.Ordinal) 
                                                    
                        searchFromLine (lineNo + 1)
                        
            
            if searchFromLine 1 then // tests if there is a newer doc change                 
                lastSels <- offs 
                state.TransformersSelection.Update(newMarks)
                selTransformersSetEv.Trigger(changeId) // can by async
                true
            else
                false


    // Called from StatusBar to highlight the current selection of Log in Editor too    
    let redrawMarking (word:string, skipOff: SkipMarking, triggerNext:bool) =
        let id = state.DocChangedId.Value
        lastWord <- word
        lastSkipOff <- skipOff
        // lastSels <- offs is set in setTransformers

        //eprintfn $"search in '{state.CodeLines.FullCode}'" //TODO: text typed on the last line after a comment might be missing somehow!
        
        async{
            if setTransformers(!state.DocChangedId) then                 
                let thisRange = state.TransformersSelection.Range              
                let redrawRange = // get range to redraw
                    match  prevRange, thisRange with 
                    | None       , None  ->    // nothing before, nothing now
                        if lastSels.Count = 1 then StatusbarOnly // but maybe just the current selection that doesn't need highlighting, but still show in status bar
                        else NoSelRedraw                        

                    | Some (f,l) , None          // some before, nothing now
                    | None       , Some (f,l) -> // nothing before, some now                    
                        SelRange (f.from, l.till)

                    | Some (pf,pl),Some (f,l) ->   // both prev and current version have a selection                    
                        SelRange(  min pf.from f.from, max pl.till l.till)                
                

                // (2) If there is a Editor selection but skipOff is set to MarkAll 
                // , because the mark call is coming from the Log selection, 
                // then clear the this editor selection, because it will not match the word to highlight from Log. 
                match skipOff with
                | SkipOffset _-> ()
                | MarkAll -> 
                    match Selection.getSelType ed.TextArea with 
                    |NoSel   -> ()
                    |RectSel 
                    |RegSel  ->
                        do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context 
                        reactToSelChange <- false // to not trigger a selection changed event 
                        ed.TextArea.ClearSelection()                            
                        reactToSelChange <- true                
                                
                //(3) redraw statusbar and editor
                match redrawRange with               
                | NoSelRedraw -> ()

                | StatusbarOnly -> 
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context 
                    globalFoundSelectionEditorEv.Trigger(triggerNext) 
                
                | SelRange (st,en) ->                     
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context 
                    markFoldingsSorted(lastSels)
                    prevRange <- thisRange
                    ed.TextArea.TextView.Redraw(st,en, priority)
                    globalFoundSelectionEditorEv.Trigger(triggerNext) 
                    
        
            else
                () // don't redraw, there is already a new doc change happening that will be drawn
            
        }|> Async.Start
     
    let updateToCurrentSelection() = 
        if reactToSelChange // in case the log request the clearing of a current selection
        && ed.TextArea.IsFocused then  // check IsFocused to not react to selections via the search bar!! // TextView.IsFocused  does not work
            //eprintfn $"updateToCurrentSelection()"
            match Selection.getSelType ed.TextArea with 
            |RectSel -> justClear(true) 
            |RegSel  -> 
                if ed.TextArea.Selection.IsMultiline then 
                    justClear(true)
                else
                    let word = ed.SelectedText
                    if isTextToHighlight word then  //is at least two chars and has no line breaks
                        let skip = SkipOffset ed.SelectionStart                    
                        redrawMarking(word, skip, true)
                    else
                        justClear(true)
            
            // keep highlighting if the cursor is just moved ?
            |NoSel   -> // justClear(true)
                if lastWord <> "" && lastSkipOff <> MarkAll then  // if lastSelOffset = -1 then all words are highlighted there is no change to highlighting needed
                    redrawMarking(lastWord, MarkAll, true) // keep highlighting and add the word that was selected before
        
        
    do         
        ed.TextArea.SelectionChanged.Add ( fun _ -> updateToCurrentSelection() ) 
        
        // Not needed for Editor because any cursor changes or typing will always trigger the selection change event:
        // ed.Document.Changed.Add (fun _ ->  ) 

    
    member _.FoundSels = selTransformersSetEv.Publish // used in EventCombiner

    member _.DocChangedResetTransformers(id) = setTransformers(id)

    member _.ClearMarksIfOneSelected() = // to be used when the search panel opens        
        match lastSkipOff with
        | SkipOffset _ -> justClear(true) // there is a selection to clear, then clear all its marks too
        | MarkAll      -> () // keep the marks, the do not match the search window probably        
        

    member _.Word = lastWord 

    member _.Offsets = lastSels  
    

    /// Called from StatusBar to highlight the current selection of Log in Editor too   
    member _.RedrawMarksInEditor(word) = 
        if isTextToHighlight word then // isTextToHighlight is needed , word might be empty string
            redrawMarking(word, MarkAll, false)
        else 
            justClear(false) 


/// Highlight-all-occurrences-of-selected-text in Log 
type SelectionHighlighterLog (lg:TextEditor) = 
    
    let action  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colorLog))
    
    let mutable lastSkipOff = MarkAll
    let mutable lastWord = ""
    let mutable lastSels = ResizeArray<int>()      
    let mutable reactToSelChange = true    
    let mutable prevRange: (LinePartChange*LinePartChange) option = None
    
    let mutable linesNeedUpdate = true
        
    /// tracks changes to the log
    let logChangeID = ref 0L

    /// track new highlighting requests 
    let markCallID  = ref 0 // because while getting the text below, the Editor selection might have changed already
        
    let trans = LineTransformers<LinePartChange>()    
    let colorizer = FastColorizer([|trans|], lg ) 
    let lines = CodeLinesSimple()
    
    let justClear(triggerNext) =
        if lastSels.Count > 0 then 
            lastWord <- ""
            lastSkipOff <- MarkAll
            lastSels.Clear()
            prevRange <- None
            async{ 
                let thisRange = trans.Range                 
                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context   
                match thisRange with 
                | None   -> ()                    
                    // TODO ? still trigger event to clear the selection in StatusBar if it is just a selection without any highlighting(e.g. multiline)                                       
                | Some (f,l) ->                
                    trans.Update(empty)// using empty array                                   
                    //for f in state.FoldManager.AllFoldings do f.BackgroundColor <- null  // no folds in Log !!
                    lg.TextArea.TextView.Redraw(f.from, l.till, priority)                
                foundSelectionLogEv.Trigger(triggerNext)

            } |> Async.Start
    
    // Called from StatusBar to highlight the current selection of Editor in Log too   
    // selectionStartOff is the offset of the current selection in the Editor, it is excluded from highlighting
    // but included in the count of offsets in the StatusBar     
    let mark (word:string, skipOff: SkipMarking, triggerNext:bool) =
        let changeId = logChangeID.Value
        let markId   = Threading.Interlocked.Increment markCallID
        let lgDoc = lg.Document
        lastWord <- word // save last selection word even if it is not found, it might be found after a doc change
        lastSkipOff <- skipOff
        async{
            
            // (1) make sure the lines for searching are up to date 
            // TODO: could be optimized to append changes to the lines instead of recreating the whole text
            while linesNeedUpdate && logChangeID.Value = changeId do                 
                do! Async.Sleep 50 // needed for getting correct text in snapshot
                if logChangeID.Value = changeId then
                    let t = lgDoc.CreateSnapshot().Text
                    lines.UpdateLogLines(t)
                    if logChangeID.Value = changeId then // this forces waiting till there are no more updates
                        linesNeedUpdate <- false                     
           
           // (2) search for the word in the lines:
            if markId = markCallID.Value && logChangeID.Value = changeId  then // because while getting the text above, the text or the selection might have changed already
                let codeStr  = lines.FullCode
                let lastLineNo = lines.LastLineIdx
                let wordLen = word.Length
                let offs = ResizeArray<int>()
                
                let newMarks = ResizeArray<ResizeArray<LinePartChange>>()
                let selectionStartOff = 
                    match skipOff with
                    | SkipOffset skipOff -> skipOff
                    | MarkAll -> -1
                
                /// returns false if aborted because of newer doc change
                let rec searchFromLine lineNo = 
                    if lineNo > lastLineNo then 
                        true // return true if loop completed
                    elif linesNeedUpdate then 
                        false // return false if there is a newer doc change
                    else
                        let l = lines.GetLine(lineNo)
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
                        
                        searchFromLine (lineNo + 1)                
                
                if searchFromLine 1 && markId = markCallID.Value then // tests if there is a newer doc change                     
                    lastSels <- offs 
                    trans.Update(newMarks)
                    let thisRange = trans.Range 
                    let redrawRange = // get range to redraw
                        match  prevRange, thisRange with 
                        | None       , None  ->    // nothing before, nothing now
                            if offs.Count = 1 then StatusbarOnly // but maybe just the current selection that doesn't need highlighting, but still show in status bar
                            else NoSelRedraw                        

                        | Some (f,l) , None          // some before, nothing now
                        | None       , Some (f,l) -> // nothing before, some now                    
                            SelRange (f.from, l.till)

                        | Some (pf,pl),Some (f,l) ->   // both prev and current version have a selection                    
                            SelRange(  min pf.from f.from, max pl.till l.till)

                    

                    // (2) if there is a selection but skipOff is set to MarkAll 
                    //( because the mark call is coming from the Editor selection ) 
                    // then clear the selection, because it will not match the word to highlight.
                    match skipOff with
                    | SkipOffset _-> ()
                    | MarkAll -> 
                        match Selection.getSelType lg.TextArea with 
                        |NoSel   -> ()
                        |RectSel 
                        |RegSel  ->
                            do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                            reactToSelChange <- false // to not trigger a selection changed event 
                            lg.TextArea.ClearSelection()                            
                            reactToSelChange <- true

                
                    // (3) redraw statusbar and editor
                    match redrawRange with               
                    | NoSelRedraw -> ()

                    | StatusbarOnly ->
                        do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context 
                        foundSelectionLogEv.Trigger(triggerNext)
                    
                    | SelRange (st,en) ->                     
                        do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context 
                        //markFoldingsSorted(offs) // no foldings in Log
                        prevRange <- thisRange
                        lg.TextArea.TextView.Redraw(st,en, priority)
                        foundSelectionLogEv.Trigger(triggerNext) 
            
                else
                    () // don't redraw, there is already a new doc change happening that will be drawn
                
        }|> Async.Start
    
    let updateToCurrentSelection() =         
        if reactToSelChange // in case the editor request the clearing of a current selection
        && lg.TextArea.IsFocused then  // check IsFocused to not react to selections via the search bar!! // TextView.IsFocused  does not work
            match Selection.getSelType lg.TextArea with 
            |RectSel ->  justClear(true) 
            |RegSel  -> 
                if lg.TextArea.Selection.IsMultiline then
                    justClear(true)
                else
                    let word = lg.SelectedText
                    if isTextToHighlight word then  //is at least two chars and has no line breaks
                        let skip = SkipOffset lg.SelectionStart 
                        mark(word, skip, true)
                    else
                        justClear(true)
            
            // keep highlighting if the cursor is just moved ?
            |NoSel  -> // justClear(true)
                if lastWord <> "" && lastSkipOff <> MarkAll then  // if lastSkipOff = MarkAll then all words are highlighted. there is no change to highlighting needed
                    mark(lastWord, MarkAll, true) // keep highlighting and add the word that was selected before

   
    do         
        lg.TextArea.TextView.LineTransformers.Insert(0, colorizer) // insert at index 0 so that it is drawn first, so that text color is overwritten the selection highlighting
        
        lg.TextArea.SelectionChanged.Add ( fun _ -> updateToCurrentSelection()  )        
        
        lg.Document.Changing.Add (fun _ -> 
            Threading.Interlocked.Increment logChangeID |> ignore
            linesNeedUpdate <- true
            )

        lg.Document.Changed.Add (fun _ -> // redraw highlighting because new text to highlight might get printed to log
            if lastWord <> "" then  
                mark(lastWord, lastSkipOff, false) // using lastSkipOff is OK for Log because if there is a selection in the Log the text in a selection can not move or be deleted
            )
    

    member _.Word    = lastWord 
    member _.Offsets = lastSels 
    
    member _.Clear() = justClear(true) // used when search panel gets opened

    member _.Update() = updateToCurrentSelection() // used by grid.Tabs.OnTabChanged

    member _.ClearMarksIfOneSelected() = // to be used when the search panel opens
        match lastSkipOff with
        | SkipOffset _ -> justClear(true) // there is a selection to clear, then clear all its marks too
        | MarkAll      -> () // keep the marks, the do not match the search window probably    

    /// Called from StatusBar to highlight the current selection of Editor in Log too    
    member _.MarkInLog(word) = 
        if isTextToHighlight word then // isTextToHighlight is needed , word might be empty string
            mark(word, MarkAll, false) 
        else 
            justClear(false)    