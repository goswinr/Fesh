namespace Fesh.Editor

open System
open System.Windows.Media

open AvalonEditB
open AvalonEditB.Rendering
open AvalonEditB.Document

open Fesh
open Fesh.Util
open Fesh.Editor
open Fesh.Editor.Selection
open Fesh.Editor.CodeLineTools


module SelectionHighlighting =

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


    /// Pass in the doc too because its called async.
    /// Returns NONE if the doc has changed in the meantime
    let makeEditorSnapShot (doc:TextDocument, state:InteractionState, id) =
        if state.IsLatest id then
            // NOTE just checking only Partial Code till caret with (doc.CreateSnapshot(0, tillOffset).Text)
            // would make the GetDeclarationsList method miss some declarations !!
            let code = doc.CreateSnapshot().Text // the only threadsafe way to access the code string
            if state.IsLatest id then
                // async{ //DELETE
                //     do! Async.SwitchToContext Fittings.SyncWpf.context // for doc TextLength
                //     IFeshLog.log.PrintfnAppErrorMsg $"id:{id} makeEditorSnapShot: doc.TextLength={doc.TextLength} code.Length={code.Length}"
                //     } |> Async.RunSynchronously
                Some code
            else
                None
        else
            None


open SelectionHighlighting
open System.Threading

/// SkipOffset: the highlighted word is selected in this editor at this offset.
/// MarkAll: the highlighted word is not selected here (anymore), or it comes from the other editor (Log or Editor).
/// Either way all occurrences get marked, the FastColorizer skips the selected text because SkipSelectedText = true.
type SkipMarking =
    | SkipOffset of int
    | MarkAll

type SelRedraw =
    | SelRange of int*int
    | StatusbarOnly
    | NoSelRedraw

/// Highlight-all-occurrences-of-selected-text in Editor
type SelectionHighlighter (state:InteractionState) =

    let action  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(Theme.editor.selectionHighlight))

    let mutable lastSkipOff = MarkAll
    let mutable lastWord = ""
    let mutable lastSels = ResizeArray<int>()
    let mutable reactToSelChange = true
    let mutable thisRange: (int*int) option = None
    let mutable prevRange: (int*int) option = None

    let selChangeId = ref 0L

    /// To make the check if a finished search is still wanted and its commit one atomic step,
    /// because the search runs on a thread pool thread while forceClear runs on the UI thread.
    let commitLock = obj()

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
                        f.BackgroundColor <- Theme.editor.selectionHighlight
                        offsSearchFromIdx <- i // to search from this index on in next fold
                    else
                        loop (i+1)
            loop (offsSearchFromIdx)


    let forceClear triggerNext =
        let wasSet =
            lock commitLock (fun () ->
                if lastWord = "" then
                    false
                else
                    lastWord <- ""
                    lastSkipOff <- MarkAll
                    lastSels.Clear()
                    prevRange <- None
                    true )
        if wasSet then
            Threading.Interlocked.Increment selChangeId |> ignore // so that a redrawMarking that is still running does not redraw or update the status bar
            let trans = state.TransformersSelection
            async{
                do! Async.SwitchToContext Fittings.SyncWpf.context
                for fold in state.FoldManager.AllFoldings do fold.BackgroundColor <- null
                match thisRange with
                | None   ->  ()
                    // TODO ? still trigger event to clear the selection in StatusBar if it is just a selection without any highlighting(e.g. multiline)
                | Some (f,l) ->
                    trans.Update(empty)// using empty array
                    ed.TextArea.TextView.Redraw(f, l, priority)
                globalFoundSelectionEditorEv.Trigger(triggerNext)
            }|> Async.Start



    let clearIfNeeded(triggerNext) =
        if lastSels.Count > 0  then // to only clear once, then not again
            forceClear(triggerNext)

    /// this checks also if state.CodeLines are up to date before calling this
    /// returns true if not cancelled by newer change Id
    /// sets lastSels <- offs
    /// also triggers selTransformersSetEv
    let setTransformers (changeId:int64) =
        // the variables 'lastWord' and 'lastSkipOff' must be set already when calling this function.
        // this happens in 'let redrawMarking' below.
        // The variable 'lastSels' is set in this function.

        let lines = state.CodeLines
        (* // DELETE, even when typing in comments a full check is run see DocChangeCompletion.singleCharChange(..) case DoNothing

        // (1) update codeLines if needed because of some typing in comments
        // if notNull doc // when called immediately after a doc change, this update should never be needed, called from updateAllTransformersConcurrently via UpdateTransformers
        if lines.IsNotFromId(changeId) then  // some text might have been typed in comments, this would increment the doc change ID but not update the CodeLines.
            match SelectionHighlighting.makeEditorSnapShot(doc, state, changeId) with
            | None      -> ()
            | Some code -> state.CodeLines.UpdateLines(code, changeId)
        *)

        // (2) search for the word in the lines:
        let word = lastWord // a local copy, because lastWord might get changed from the UI thread while searching here on another thread
        if not <| state.IsLatest changeId then
            false
        else
            if word = "" then
                selTransformersSetEv.Trigger(changeId) // can by async, this still needs to be called this ! so the  EventCombiner can  the full redrawing of the other highlighters.
                true
            else
                let codeStr  = lines.FullCode
                let lastLineNo = lines.LastLineIdx
                let wordLen = word.Length
                let offs = ResizeArray<int>()

                let newMarks = ResizeArray<ResizeArray<LinePartChange>>()

                let mutable rangeStart = -1
                let mutable rangeEnd = -1
                /// returns false if aborted because of newer doc change
                let rec searchFromLine lineNo =
                    if lineNo > lastLineNo then
                        true // return true if loop completed
                    else
                        match lines.GetLine(lineNo, changeId) with
                        |ValueNone -> false // could not get code line, newer change happened already
                        |ValueSome l ->
                            // let line = codeStr.Substring(l.offStart, l.len)
                            let mutable off = codeStr.IndexOf(word, l.offStart, l.len, StringComparison.Ordinal)
                            while off >= 0 do
                                offs.Add off
                                // also mark the current selection, the FastColorizer skips selected text because SkipSelectedText = true
                                LineTransformers.Insert(newMarks, lineNo,  {from=off; till=off+wordLen; act=action})
                                rangeEnd <- off + wordLen
                                if rangeStart < 0 then // set range start if not set yet
                                    rangeStart <- off
                                let start = off + wordLen // search from this for next occurrence in this line
                                let lenReduction = start - l.offStart
                                let remainingLineLength = l.len - lenReduction
                                off <- codeStr.IndexOf(word, start, remainingLineLength , StringComparison.Ordinal)

                            searchFromLine (lineNo + 1)


                if searchFromLine 1 then // tests if there is a newer doc change
                    // lastWord might have been changed or cleared on the UI thread while searching. Then don't commit.
                    let committed =
                        lock commitLock (fun () ->
                            if word = lastWord then
                                thisRange <- if rangeStart < 0 then None else Some(rangeStart, rangeEnd)
                                lastSels <- offs
                                state.TransformersSelection.Update(newMarks)
                                true
                            else
                                false )
                    selTransformersSetEv.Trigger(changeId) // can by async, also needed if not committed, so the EventCombiner can do the full redraw for this changeId.
                    committed
                else
                    false


    // Also called from StatusBar to highlight the current selection of Log in Editor too
    // sets lastWords and lastSkipOff
    let redrawMarking (word:string, skipOff: SkipMarking, triggerNext:bool, selId) =
        let prevFoundCount = lastSels.Count
        lock commitLock (fun () ->
            lastWord <- word
            lastSkipOff <- skipOff )
        // lastSels <- offs is set in setTransformers 3 line below

        async{
            let transFormersDone = setTransformers( state.DocChangedId.Value )
            if transFormersDone && selId = selChangeId.Value  then
                // (1) get ranges to redraw
                let redrawRange = // get range to redraw
                    match  prevRange, thisRange with
                    | None       , None  ->    // nothing before, nothing now
                        if lastSels.Count = 1 || prevFoundCount = 1 then StatusbarOnly // but maybe just the current selection that doesn't need highlighting, but still show in status bar
                        else NoSelRedraw

                    | Some (f,l) , None          // some before, nothing now
                    | None       , Some (f,l) -> // nothing before, some now
                        SelRange (f, l)

                    | Some (pf,pl),Some (f,l) ->   // both prev and current version have a selection
                        SelRange(  min pf f, max pl l)

                //printfn $"+++redrawRange={redrawRange} skipOff={skipOff}+++" // prevRange={prevRange} thisRange={thisRange}"
                //(2) redraw statusbar and editor in range
                match redrawRange with
                | NoSelRedraw -> ()

                | StatusbarOnly ->
                    do! Async.SwitchToContext Fittings.SyncWpf.context
                    globalFoundSelectionEditorEv.Trigger(triggerNext)

                | SelRange (st,en) ->
                    do! Async.SwitchToContext Fittings.SyncWpf.context
                    markFoldingsSorted(lastSels)
                    prevRange <- thisRange
                    ed.TextArea.TextView.Redraw(st,en, priority)
                    globalFoundSelectionEditorEv.Trigger(triggerNext)

        }|> Async.Start

    let updateToCurrentSelection() =
        if reactToSelChange // in case the log request the clearing of a current selection
        && ed.TextArea.IsFocused then  // check IsFocused to not react to selections via the search bar!!
            let newSelId = Threading.Interlocked.Increment selChangeId

            // The highlighting stays till Esc is pressed. It only gets replaced by a new highlighting.
            match Selection.getSelType ed.TextArea with
            |RectSel ->
                () // keep the highlighting

            |RegSel  ->
                if not ed.TextArea.Selection.IsMultiline then // for multiline keep the highlighting
                    let word = ed.SelectedText
                    if isTextToHighlight word then  //is at least two chars and has no line breaks
                        let skip = SkipOffset ed.SelectionStart
                        redrawMarking(word, skip, true, newSelId)
                    // else keep the highlighting

            // keep highlighting if the cursor is just moved, even while typing in comments:
            // (any doc change, also in comments, updates the CodeLines and then searches again via UpdateTransformers)
            |NoSel   ->
                if lastWord <> "" && lastSkipOff <> MarkAll then  // if lastSkipOff = MarkAll then all words are highlighted there is no change to highlighting needed
                    redrawMarking(lastWord, MarkAll, true, newSelId) // keep highlighting and add the word that was selected before


    let debounce =
        let mutable lastId = ref 0L
        fun (milliSeconds:int) ->
            let thisId = Threading.Interlocked.Increment lastId
            async{
                do! Async.Sleep milliSeconds
                if thisId = lastId.Value then updateToCurrentSelection()
            } |> Async.StartImmediate


    do
        ed.TextArea.SelectionChanged.Add ( fun _ -> if reactToSelChange then debounce 150 ) // reactToSelChange is false while RedrawMarksInEditor clears the selection itself
        // ed.TextArea.SelectionChanged.Add ( fun _ -> updateToCurrentSelection() )
        // ed.Document.Changed.Add (fun _ ->  ) will call UpdateTransformers from DocChanged.fs

    [<CLIEvent>]
    member _.FoundSels = selTransformersSetEv.Publish // used only in EventCombiner

    /// This gets called on doc changes, to comments only, that do not trigger any other highlighting
    /// See let singleCharChange in DocChanged.fs DoNothing case
    /// It is also used by grid.Tabs.OnTabChanged
    member _.UpdateToCurrentSelection() = updateToCurrentSelection()

    /// This is called from DocChanged.fs when the document changes, to reset the selection highlighting
    /// It does not get called when only text in Comments changes, because the CodeLines are not updated then.
    member _.UpdateTransformers(id) =
        let k = lastSels.Count
        if setTransformers(id) && lastSels.Count <> k then // do only if the selection count changed
            async{
                do! Async.SwitchToContext Fittings.SyncWpf.context
                globalFoundSelectionEditorEv.Trigger(false) // to redraw statusbar
            } |> Async.Start


    member _.ClearMarksIfOneSelected() = // to be used when the search panel opens
        match lastSkipOff with
        | SkipOffset _ -> clearIfNeeded true // there is a selection to clear, then clear all its marks too
        | MarkAll      -> () // keep the marks, the do not match the search window probably


    // used when escape is pressed and not type info is open
    member _.ForceClear() = forceClear false

    member _.Word = lastWord

    member _.Offsets = lastSels

    member _.TriggerGlobalFoundSelectionEditorEv() = globalFoundSelectionEditorEv.Trigger(true) // to redraw statusbar and Log on Tab change

    /// Called from StatusBar to highlight the current selection of Log in Editor too
    member _.RedrawMarksInEditor(word) =
        if isTextToHighlight word then // isTextToHighlight is needed , word might be empty string
            // Clear an Editor selection, because it does not match the word from the Log.
            // Done here in sync and not after the async search, so that a selection the user starts meanwhile does not get cleared.
            if not ed.TextArea.Selection.IsEmpty then
                reactToSelChange <- false // to not trigger a selection changed event
                ed.TextArea.ClearSelection()
                reactToSelChange <- true
            redrawMarking(word, MarkAll, false, selChangeId.Value)
        else
            clearIfNeeded false


/// Highlight-all-occurrences-of-selected-text in Log
type SelectionHighlighterLog (lg:TextEditor) =

    let action  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(Theme.log.selectionHighlight))

    let mutable lastSkipOff = MarkAll
    let mutable lastWord = ""
    let mutable lastSels = ResizeArray<int>()
    let mutable reactToSelChange = true

    let mutable thisRange: (int*int) option = None
    let mutable prevRange: (int*int) option = None

    /// track new highlighting requests
    let markCallID  = ref 0 // because while searching below, the Log text or the selection might have changed already

    let trans = LineTransformers<LinePartChange>(SkipSelectedText = true) // the selected text has its own highlighting
    let colorizer = FastColorizer( [|trans|], lg.TextArea )

    /// The lines to search in. Only the appended text gets added on each change, the full text of the Log is never copied.
    /// A search uses a snapshot of it, so it stays consistent while more text gets appended.
    let logLines = LogLines()

    let forceClear(triggerNext) =
        if lastWord <> "" then
            Threading.Interlocked.Increment markCallID |> ignore // so that a mark call that is still running does not commit its results
            lastWord <- ""
            lastSkipOff <- MarkAll
            lastSels.Clear()
            prevRange <- None
            async{
                do! Async.SwitchToContext Fittings.SyncWpf.context
                match thisRange with
                | None   -> ()
                    // TODO ? still trigger event to clear the selection in StatusBar if it is just a selection without any highlighting(e.g. multiline)
                | Some (f,l) ->
                    trans.Update(empty)// using empty array
                    //for f in state.FoldManager.AllFoldings do f.BackgroundColor <- null  // no folds in Log !!
                    lg.TextArea.TextView.Redraw(f, l, priority)
                foundSelectionLogEv.Trigger(triggerNext)
            } |> Async.Start

    let clearLogIfNeeded(triggerNext) =
        if lastSels.Count > 0 then
            forceClear(triggerNext)

    // Called from StatusBar to highlight the current selection of Editor in Log too
    // All occurrences are marked and counted, the FastColorizer skips the selected text.
    // No debouncing needed for prints, AvalonLog already batches them into one append every 30 to 50 ms.
    let mark (word:string, skipOff: SkipMarking, triggerNext:bool) =
        let markId = Threading.Interlocked.Increment markCallID
        let lines  = logLines.Snapshot // taken here on the UI thread, so it matches the current Log text
        lastWord <- word // save last selection word even if it is not found, it might be found after a doc change
        lastSkipOff <- skipOff
        async{
            // search for the word in the lines:
            if markId = markCallID.Value then // false if there was a newer mark call or a forceClear in the meantime
                let lastLineNo = lines.LastLineNo
                let wordLen = word.Length
                let offs = ResizeArray<int>()

                let newMarks = ResizeArray<ResizeArray<LinePartChange>>()

                let mutable rangeStart = -1
                let mutable rangeEnd = -1

                /// returns false if aborted because of a newer mark call
                let rec searchFromLine lineNo =
                    if lineNo > lastLineNo then
                        true // return true if loop completed
                    elif markId <> markCallID.Value then
                        false // return false if there is a newer mark call, e.g. from a doc change
                    else
                        let l = lines.GetLine(lineNo)
                        let mutable i = l.text.IndexOf(word, StringComparison.Ordinal)
                        while i >= 0 do
                            let off = l.offStart + i
                            offs.Add off
                            // also mark the current selection, the FastColorizer skips selected text because SkipSelectedText = true
                            LineTransformers.Insert(newMarks,lineNo, {from=off; till=off+wordLen; act=action})
                            rangeEnd <- off + wordLen
                            if rangeStart < 0 then // set range start if not set yet
                                rangeStart <- off
                            i <- l.text.IndexOf(word, i + wordLen, StringComparison.Ordinal) // search for next occurrence in this line

                        searchFromLine (lineNo + 1)

                if searchFromLine 1 then // tests if there is a newer mark call
                    // Commit on the UI thread, so that forceClear (also on the UI thread) can't run between the check and the commit.
                    do! Async.SwitchToContext Fittings.SyncWpf.context
                    if markId = markCallID.Value then // false if there was a newer mark call or a forceClear in the meantime
                        thisRange <- if rangeStart < 0 then None else Some(rangeStart, rangeEnd)
                        trans.Update(newMarks)
                        let redrawRange = // get range to redraw
                            match  prevRange, thisRange with
                            | None       , None  ->    // nothing before, nothing now
                                if offs.Count = 1 || lastSels.Count = 1 then StatusbarOnly // but maybe just the current selection that doesn't need highlighting, but still show in status bar
                                else NoSelRedraw

                            | Some (f,l) , None          // some before, nothing now
                            | None       , Some (f,l) -> // nothing before, some now
                                SelRange (f, l)

                            | Some (pf,pl),Some (f,l) ->   // both prev and current version have a selection
                                SelRange(  min pf f, max pl l)

                        lastSels <- offs

                        // (2) redraw statusbar and editor
                        match redrawRange with
                        | NoSelRedraw -> ()

                        | StatusbarOnly ->
                            foundSelectionLogEv.Trigger(triggerNext)

                        | SelRange (st,en) ->
                            //markFoldingsSorted(offs) // no foldings in Log
                            prevRange <- thisRange
                            lg.TextArea.TextView.Redraw(st,en, priority)
                            foundSelectionLogEv.Trigger(triggerNext)

                else
                    () // don't redraw, there is already a newer mark call that will be drawn

        }|> Async.Start

    let updateToCurrentSelection() =
        if reactToSelChange // in case the editor request the clearing of a current selection
        && lg.TextArea.IsFocused then  // check IsFocused to not react to selections via the search bar!! // TextView.IsFocused  does not work
            // The highlighting stays till Esc is pressed. It only gets replaced by a new highlighting.
            match Selection.getSelType lg.TextArea with
            |RectSel ->  () // keep the highlighting
            |RegSel  ->
                if not lg.TextArea.Selection.IsMultiline then // for multiline keep the highlighting
                    let word = lg.SelectedText
                    if isTextToHighlight word then  //is at least two chars and has no line breaks
                        let skip = SkipOffset lg.SelectionStart
                        mark(word, skip, true)
                    // else keep the highlighting

            // keep highlighting if the cursor is just repositioned, but nothing selected:
            |NoSel  ->
                if lastWord <> "" && lastSkipOff <> MarkAll then  // if lastSkipOff = MarkAll then all words are highlighted. there is no change to highlighting needed
                    mark(lastWord, MarkAll, true) // keep highlighting and add the word that was selected before

    let debounce =
        let mutable lastId = ref 0L
        fun (milliSeconds:int) ->
            let thisId = Threading.Interlocked.Increment lastId
            async{
                do! Async.Sleep milliSeconds
                if thisId = lastId.Value then updateToCurrentSelection()
            } |> Async.StartImmediate


    do
        lg.TextArea.TextView.LineTransformers.Insert(0, colorizer) // insert at index 0 so that it is drawn first, so that text color is overwritten the selection highlighting

        // lg.TextArea.SelectionChanged.Add ( fun _ -> updateToCurrentSelection() )
        lg.TextArea.SelectionChanged.Add ( fun _ -> if reactToSelChange then debounce 150 ) // reactToSelChange is false while MarkInLog clears the selection itself

        logLines.Reset(lg.Document.Text) // the Log might have some text already

        lg.Document.Changed.Add (fun e ->
            if e.RemovalLength = 0 && e.Offset = logLines.DocLength then
                logLines.Append(e.InsertedText.Text) // the usual case, the Log is append only, so only the new text gets added
            else
                logLines.Reset(lg.Document.Text) // e.g. when the Log got cleared

            if lastWord <> "" then // redraw highlighting because new text to highlight might get printed to log
                mark(lastWord, lastSkipOff, false) // using lastSkipOff is OK for Log because if there is a selection in the Log the text in a selection can not move or be deleted
            )

    /// used when escape is pressed and not type info is open
    member _.ForceClear() = forceClear(false) // used when escape is pressed and not type info is open

    member _.Word    = lastWord

    member _.Offsets = lastSels

    //member _.Clear() = justClear(true) // used when search panel gets opened

    //member _.UpdateToCurrentSelection() = updateToCurrentSelection() // used by grid.Tabs.OnTabChanged

    member _.ClearMarksIfOneSelected() = // to be used when the search panel opens
        match lastSkipOff with
        | SkipOffset _ -> clearLogIfNeeded(true) // there is a selection to clear, then clear all its marks too
        | MarkAll      -> () // keep the marks, the do not match the search window probably

    /// Called from StatusBar to highlight the current selection of Editor in Log too
    member _.MarkInLog(word) =
        if isTextToHighlight word then // isTextToHighlight is needed , word might be empty string
            // Clear a Log selection, because it does not match the word from the Editor.
            // Done here in sync and not after the async search, so that a selection the user starts meanwhile does not get cleared.
            if not lg.TextArea.Selection.IsEmpty then
                reactToSelChange <- false // to not trigger a selection changed event
                lg.TextArea.ClearSelection()
                reactToSelChange <- true
            mark(word, MarkAll, false)
        else
            clearLogIfNeeded(false)