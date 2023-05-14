namespace Seff.Editor

open System
open System.Windows.Media
open AvalonEditB
open AvalonEditB.Rendering
open Seff.Util.General
open Seff.Util
open Seff.Model
open Seff.Editor.Selection
open Seff.Editor.FullCode
open Seff.Editor



/// Highlight-all-occurrences-of-selected-text in Text View
type SelectionHighlighter (edState:InteractionState, lgState:InteractionState) =    
    
    let colorEditor  = Brushes.PaleTurquoise |> AvalonLog.Brush.freeze      
    let colorLog     = Brushes.Blue          |> AvalonLog.Brush.brighter 210  |> AvalonLog.Brush.freeze
    let actionEditor  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colorEditor))
    let actionLog     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colorLog))

    let priority = Windows.Threading.DispatcherPriority.Render


    let isTextToHighlight(t:string) = 
        t.Length > 1 && not (Str.isJustSpaceCharsOrEmpty t)  && not <| t.Contains("\n") 
    
    let checkFoldedBoxes (ed:IEditor, fullCode:string ,highTxt) =
        // for status bar and folds :
           
        let mutable index = fullCode.IndexOf(highTxt, 0, StringComparison.Ordinal)  
        for fold in ed.FoldingManager.AllFoldings do fold.BackgroundColor <- null // reset all first, before setting some
        let offsets = ResizeArray<int>()
        while index >= 0 do                    
            offsets.Add(index)
            // check for text that is folded away:
            let infs = ed.FoldingManager.GetFoldingsContaining(index)
            for inf in infs do inf.BackgroundColor <- colorEditor                    
            let st =  index + highTxt.Length // endOffset // TODO or just +1 ???????
            if st >= fullCode.Length then
                index <- -1 // this happens when word to highlight is at document end
                //ISeffLog.log.PrintfnAppErrorMsg  "index  %d in %d ??" st code.Length
            else
                index <- fullCode.IndexOf(highTxt, st, StringComparison.Ordinal)
        offsets 
    


    let markFoldings() =
        for fold in edState.FoldManager.AllFoldings do 
            mar foldings

    let justClear(state:InteractionState) =
        let trans = state.TransformersSelection
        match trans.Range with 
        | None       -> () // no transformers there anyway
        | Some (f,l) ->
            async{ 
                trans.ClearAllLines()
                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                state.Editor.TextArea.TextView.Redraw(f.from, l.till, priority)
            }|> Async.Start
    
    let mark (state:InteractionState, word:string, action, selectionStartOff) =
        let id = state.DocChangedId.Value
        async{
            let lines = state.CodeLines
            let trans = state.TransformersSelection
            let codeStr  = lines.FullCode
            let lastLineNo = lines.LastLineIdx
            let wordLen = word.Length
            
            /// returns false if aborted because of newer doc change
            let rec loop lineNo = 
                if lineNo > lastLineNo then 
                    true // return true if loop completed
                else
                    match lines.GetLine(lineNo, id) with 
                    |ValueNone -> false // coun not get code lien, newer chnage happen 
                    |ValueSome l -> 
                        let mutable off = codeStr.IndexOf(word, l.offStart, l.len, StringComparison.Ordinal)                        
                        while off >= 0 do
                            if off <> selectionStartOff then // skip the actual current selction
                                trans.Insert(lineNo, {from=off; till=off+wordLen; act=action})                                 
                            let start = off + word.Length // search from this for next occurrence in this line 
                            let lenReduction = start - l.offStart
                            let remainingLineLength = l.len - lenReduction
                            off <- codeStr.IndexOf(word, start, remainingLineLength , StringComparison.Ordinal)
                        loop (lineNo + 1)
            
            let prev = trans.Range
            trans.ClearAllLines() // does nothing if already all clered
            if loop 1 then // test if thet is a newer doc change                 
                match  prev, trans.Range with 
                | None       , None  -> () // nothing before nothing now
                | Some (f,l) , None 
                | None       , Some (f,l) ->
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context                    
                    state.Editor.TextArea.TextView.Redraw(f.from, l.till, priority)
                | Some (pf,pl),Some (f,l) ->   /// both prev and current version have a selection                 
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context                    
                    state.Editor.TextArea.TextView.Redraw(min pf.from f.from, max pl.till l.till, priority)
            else
                () // dont redraw, there is already a new docchange happening that will be drawn
        }|> Async.Start

    
    
    let markBoth(selectionStartOff, word, isFromEd) =
        if isFromEd then 
            mark(edState, word, actionEditor, selectionStartOff)
            mark(lgState, word, actionLog   , -1)
        else
            mark(edState, word, actionEditor, -1)
            mark(lgState, word, actionLog   , selectionStartOff)

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
        edState.Editor.TextArea.SelectionChanged.Add ( fun _ ->                              update(edState.Editor, true)  )        
        lgState.Editor.TextArea.SelectionChanged.Add ( fun _ -> if IEditor.isCurrent ed then update(lgState.Editor, false) )