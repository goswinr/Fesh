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

    let colorLog     = Brushes.Blue          |> AvalonLog.Brush.brighter 210  |> AvalonLog.Brush.freeze
        
    let foundSelectionLogEv    = new Event<bool>()
    let foundSelectionEditorEv = new Event<bool>()

    [<CLIEvent>] 
    let FoundSelectionsEditor = foundSelectionEditorEv.Publish

    [<CLIEvent>] 
    let FoundSelectionsLog    = foundSelectionLogEv.Publish

    

open SelectionHighlighting

/// Highlight-all-occurrences-of-selected-text in Editor
type SelectionHighlighter (state:InteractionState) = 
    
    let action  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colorEditor))

    let priority = Windows.Threading.DispatcherPriority.Render

    let mutable lastWord = ""
    let mutable lastSels = ResizeArray<int>()      

    let ed = state.Editor
    
    let isTextToHighlight(t:string) = 
        t.Length > 1 && not (Str.isJustSpaceCharsOrEmpty t)  && not <| t.Contains("\n") 
  
    let markFoldingsSorted(offs:ResizeArray<int>) =       
        let mutable offsSearchFromIdx =  0
        for f in state.FoldManager.AllFoldings do 
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
  

    let justClear() =
        lastWord <- ""
        lastSels <- ResizeArray<int>()  
        let trans = state.TransformersSelection
        match trans.Range with 
        | None       -> () // no transformers there anyway
        | Some (f,l) ->
            async{ 
                trans.ClearAllLines()
                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                foundSelectionEditorEv.Trigger(false)
                for f in state.FoldManager.AllFoldings do  f.BackgroundColor <- null  
                ed.TextArea.TextView.Redraw(f.from, l.till, priority)
            }|> Async.Start
    
    let mark (word:string, selectionStartOff,triggerNext) =
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
            lastSels <- offs 
            lastWord <- word
            if loop 1 then // tests if ther is a newer doc change                 
                match  prev, trans.Range with 
                | None       , None  -> ()   // nothing before, nothing now
                
                | Some (f,l) , None          // some before, nothing now
                | None       , Some (f,l) -> // nothing before, some now
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                    markFoldingsSorted(offs)
                    ed.TextArea.TextView.Redraw(f.from, l.till, priority)
                    foundSelectionEditorEv.Trigger(triggerNext)                   
                
                | Some (pf,pl),Some (f,l) ->   // both prev and current version have a selection                 
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context 
                    markFoldingsSorted(offs)
                    ed.TextArea.TextView.Redraw(min pf.from f.from, max pl.till l.till, priority)
                    foundSelectionEditorEv.Trigger(triggerNext)
            else
                () // dont redraw, there is already a new docchange happening that will be drawn
            

        }|> Async.Start
    
    
    
    let update() =    
        match Selection.getSelType ed.TextArea with 
        |RegSel  -> 
            let word = ed.SelectedText
            if isTextToHighlight word then  //is at least two chars and has no line breaks
                let startOff = ed.SelectionStart
                mark(word, startOff,true)
            else
                justClear()
        |NoSel   -> justClear()
        |RectSel -> justClear() 
    
    
    do         
        ed.TextArea.SelectionChanged.Add ( fun _ -> update() ) 
    
    member _.Word    = lastWord 
    member _.Offsets = lastSels  
    
      member _.Mark(word,triggerNext) = mark(word,-1, triggerNext)

/// Highlight-all-occurrences-of-selected-text in Log
type SelectionHighlighterLog (lg:TextEditor) = 
    
    let action  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colorLog))

    let priority = Windows.Threading.DispatcherPriority.Render

    let mutable lastWord = ""
    let mutable lastSels = ResizeArray<int>()      
        
    let isTextToHighlight(t:string) = 
        t.Length > 1 && not (Str.isJustSpaceCharsOrEmpty t)  && not <| t.Contains("\n") 
    
    let trans = LineTransformers<LinePartChange>()    
    let colorizer = FastColorizer([|trans|], lg ) 

    let justClear() =
        lastWord <- ""
        lastSels <- ResizeArray<int>()  
        match trans.Range with 
        | None       -> () // no transformers there anyway
        | Some (f,l) ->
            async{ 
                trans.ClearAllLines()
                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                foundSelectionLogEv.Trigger(false)
                //for f in state.FoldManager.AllFoldings do  f.BackgroundColor <- null  
                lg.TextArea.TextView.Redraw(f.from, l.till, priority)
            }|> Async.Start
    
          
    let lines = CodeLinesSimple()
    let mutable linesNeedUpdate = true

    
    let mark (word:string, selectionStartOff,triggerNext) =
        let doc = lg.Document
        async{
            if linesNeedUpdate then 
                linesNeedUpdate <- false
                let t = doc.CreateSnapshot().Text
                lines.Update(t)            
            
            
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
            lastSels <- offs 
            lastWord <- word
            if loop 1 then // tests if ther is a newer doc change                 
                match  prev, trans.Range with 
                | None       , None  -> ()   // nothing before, nothing now
                
                | Some (f,l) , None          // some before, nothing now
                | None       , Some (f,l) -> // nothing before, some now
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                    //markFoldingsSorted(offs)
                    lg.TextArea.TextView.Redraw(f.from, l.till, priority)
                    foundSelectionLogEv.Trigger(triggerNext)                   
                
                | Some (pf,pl),Some (f,l) ->   // both prev and current version have a selection                 
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context 
                    //markFoldingsSorted(offs)
                    lg.TextArea.TextView.Redraw(min pf.from f.from, max pl.till l.till, priority)
                    foundSelectionLogEv.Trigger(triggerNext)
            else
                () // dont redraw, there is already a new docchange happening that will be drawn
           

        }|> Async.Start
    
    
    let update() =    
        match Selection.getSelType lg.TextArea with 
        |RegSel  -> 
            let word = lg.SelectedText
            if isTextToHighlight word then  //is at least two chars and has no line breaks
                let startOff = lg.SelectionStart
                mark(word, startOff, true)                
            else
                justClear()
        |NoSel   -> justClear()
        |RectSel -> justClear() 
    
    
    do         
        lg.TextArea.SelectionChanged.Add ( fun _ -> update() ) 
        lg.DocumentChanged.Add (fun _ -> linesNeedUpdate <- true)
        lg.TextArea.TextView.LineTransformers.Add colorizer
    
    member _.Word    = lastWord 
    member _.Offsets = lastSels 
    
    member _.Mark(word,triggerNext) = mark(word,-1, triggerNext)
