namespace Seff.Editor

open System
open System.Windows.Media
open AvalonEditB
open Seff.Util.General
open Seff.Util
open Seff.Model
open Seff.Editor.Selection

/// A DocumentColorizingTransformer.
/// Used to Highlight-all-occurrences-of-selected-text in Text View.
type SelectionColorizer (ed:TextEditor, color:SolidColorBrush) = 
    inherit Rendering.DocumentColorizingTransformer()
    //  from https://stackoverflow.com/questions/9223674/highlight-all-occurrences-of-selected-word-in-avalonedit
       
    let mutable highTxt:string = null
    let mutable exclStart = -1
    let mutable exclEnd   = -1 // end offset is the last character with highlighting 

    member this.HighlightText 
        with get() = highTxt 
        and set v  = highTxt <- v

    //member this.ClearHiLi() = highTxt <- null // unused
    
    /// set this to exclude a range. StartOffset
    member this.ExcludeFrom with set v = exclStart <- v // no get needed
    
    /// set this to exclude a range. EndOffset
    member this.ExcludeTill  with set v = exclEnd <- v  // no get needed

    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:Document.DocumentLine) = 
        
        //printfn "Sel in %s " <|  ed.Document.GetText(line)
        if notNull highTxt  then
            let  lineStartOffset = line.Offset
            let  linetext = ed.Document.GetText(line)
            let mutable index = linetext.IndexOf(highTxt, 0, StringComparison.Ordinal)

            while index >= 0 do
                let st = lineStartOffset + index  // startOffset
                let en = lineStartOffset + index + highTxt.Length - 1  // end offset is the last character with highlighting

                if (st < exclStart || st > exclEnd) && (en < exclStart || en > exclEnd )  then // to skip the actual current selection
                    //printfn "Sel %d to %d for %s, exclStart: %d" st en highTxt exclStart

                    // here end offset needs + 1  to be the first character without highlighting
                    base.ChangeLinePart( st,en + 1, fun el -> el.TextRunProperties.SetBackgroundBrush(color))
                let start = index + highTxt.Length // search from this for next occurrence in this line 
                index <- linetext.IndexOf(highTxt, start, StringComparison.Ordinal)


module SelectionHighlighting =     
    
    let colorEditor  = Brushes.PaleTurquoise |> AvalonLog.Brush.freeze  
    let colorLog     = Brushes.Blue          |> AvalonLog.Brush.brighter 210  |> AvalonLog.Brush.freeze
    //let colorFoldBoxOutline = Brushes.Gray   |> AvalonLog.Brush.darker 30  
    //Folding.FoldingElementGenerator.TextBrush <- colorFoldBoxOutline
    
    type FoundOcc = {
        text:string
        offsets:ResizeArray<int>
        selectionAt:int
        }

    type HiLiResult = 
        | FoundSome of FoundOcc 
        | FoundNone

    // two events to distinguish what triggered the highlighting
    let private highlightRequestedEv  = new Event<TextEditor*HiLiResult>()    
    let private selectionChangedEv  = new Event<TextEditor*HiLiResult>() 
    [<CLIEvent>]
    let HighlightRequested : IEvent<TextEditor*HiLiResult> = highlightRequestedEv.Publish    
    [<CLIEvent>]
    let SelectionChanged : IEvent<TextEditor*HiLiResult> = selectionChangedEv.Publish

    let private empty = ResizeArray<int>(0)

    let private foundNoneRedraw(ava:TextEditor, selTextHiLi:SelectionColorizer) = 
        if notNull selTextHiLi.HighlightText then // to not needlessly redraw
            selTextHiLi.HighlightText<- null
            selTextHiLi.ExcludeFrom   <- -1
            selTextHiLi.ExcludeTill   <- -1
            //ISeffLog.printnColor 200 99 0 "foundNoneRedraw"
            ava.TextArea.TextView.Redraw() //delete redraw ??
   
    let clearFolds(ed:IEditor) = 
        for fold in ed.FoldingManager.AllFoldings do 
            fold.BackgroundColor <- null 
    
    let private foundNoneSel(ava:TextEditor, selTextHiLi:SelectionColorizer) =
        foundNoneRedraw(ava, selTextHiLi)  |> ignore 
        selectionChangedEv.Trigger(ava, FoundNone )

    let private foundNoneReq(ava:TextEditor, selTextHiLi:SelectionColorizer) =
        foundNoneRedraw(ava, selTextHiLi)  |> ignore 
        highlightRequestedEv.Trigger(ava, FoundNone )

    let private foundNoneSelFold(ed:IEditor, selTextHiLi:SelectionColorizer) =
        clearFolds(ed)
        foundNoneRedraw(ed.AvaEdit, selTextHiLi)  |> ignore 
        selectionChangedEv.Trigger(ed.AvaEdit, FoundNone )

    let private foundNoneReqFold(ed:IEditor, selTextHiLi:SelectionColorizer) =
        clearFolds(ed)
        foundNoneRedraw(ed.AvaEdit, selTextHiLi)  |> ignore 
        highlightRequestedEv.Trigger(ed.AvaEdit, FoundNone )


    let inline isTextToHighlight(t:string) = 
        t.Length > 1 && not (Str.isJustSpaceCharsOrEmpty t)  && not <| t.Contains("\n") 

    let setSelHighlight(t:string, ava:TextEditor, hiLi:SelectionColorizer) = 
        let selStart = ava.SelectionStart
        hiLi.ExcludeFrom   <- selStart // to exclude current selection from highlighting
        hiLi.ExcludeTill   <- selStart + t.Length - 1 // end offset is the last character with highlighting           
        hiLi.HighlightText <- t 
        selStart

    module HiEditor =  

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
    
        let handleSelection(ed:IEditor, fullCode:string, selTextHiLi:SelectionColorizer) =
            let av = ed.AvaEdit
            match Selection.getSelType av.TextArea with 
            |RegSel  -> 
                let t = av.SelectedText
                if isTextToHighlight t then  //is at least two chars and has no line breaks                    
                    let st = setSelHighlight(t,av, selTextHiLi)                
                    let offs = checkFoldedBoxes(ed, fullCode,t)
                    if offs.Count > 0 then 
                        selectionChangedEv.Trigger(av, FoundSome {text=t; offsets=offs; selectionAt = st})
                        //ISeffLog.printnColor 200 99 0 "handleSelection Redraw"
                        av.TextArea.TextView.Redraw() 

                    else // this case should actually never happen:
                        foundNoneSel(av, selTextHiLi)// no need to clear folds here too 
                else
                    foundNoneSelFold(ed, selTextHiLi) 
            |NoSel   -> foundNoneSelFold(ed, selTextHiLi) 
            |RectSel -> foundNoneSelFold(ed, selTextHiLi) 
            

        /// returns a function for highlighting that does not call the UI continuation
        let setup(ed:IEditor) : (string->unit) =         
            // new highlighter per editor instance
            let av = ed.AvaEdit
            let selTextHiLi = new SelectionColorizer(ed.AvaEdit,colorEditor)
            let ta = av.TextArea
            ta.TextView.LineTransformers.Add(selTextHiLi)        
            ta.SelectionChanged.Add ( fun _ -> handleSelection(ed, selTextHiLi) )

            // return a function to highlight without any text area selection happening 
            fun txt  -> 
                if isTextToHighlight txt then 
                    selTextHiLi.HighlightText <- txt
                    selTextHiLi.ExcludeFrom   <- -1
                    selTextHiLi.ExcludeTill   <- -1
                    let offs = checkFoldedBoxes(ed,txt)  
                    if offs.Count > 0 then
                        highlightRequestedEv.Trigger(av, FoundSome {text=txt; offsets=offs; selectionAt= -1})
                        //ISeffLog.printnColor 200 99 0 "hili setup Redraw"
                        ed.AvaEdit.TextArea.TextView.Redraw()
                    else // this case should actually never happen:
                        foundNoneReq(av, selTextHiLi)// no need to clear folds here too 
                else  
                    foundNoneReqFold(ed, selTextHiLi) 


    module HiLog =

        let checkFoldedBoxesAsync(lg:TextEditor, selTextHiLi:SelectionColorizer, text, st, fromSelection)=
            let doc = lg.Document // get doc in sync first !
            async{  
                // search full log async, it might be very large
                // then raise event in sync with this info:
                let tx = doc.CreateSnapshot().Text
                let offs = ResizeArray()
                let mutable  index = tx.IndexOf(text, 0, StringComparison.Ordinal)
                while index >= 0 do
                    offs.Add(index)
                    let st =  index + text.Length
                    if st >= tx.Length then
                        index <- -1
                    else
                        index <- tx.IndexOf(text, st, StringComparison.Ordinal)
                    
                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context 
                if offs.Count>0 then  
                    let r = FoundSome {text=text; offsets=offs; selectionAt = st}
                    if fromSelection then  selectionChangedEv.Trigger (lg,r ) 
                    else                   highlightRequestedEv.Trigger(lg, r) 
                    lg.TextArea.TextView.Redraw() 
                
                else // this case should actually never happen:
                    if fromSelection then  foundNoneSel(lg, selTextHiLi)                         
                    else                   foundNoneReq(lg, selTextHiLi)  
                    
                } 
                |> Async.Start


        let handleSelection (lg:TextEditor, selTextHiLi:SelectionColorizer) =             
            match Selection.getSelType lg.TextArea with 
            |RegSel  -> 
                let t = lg.SelectedText
                if isTextToHighlight t then  //min two chars and no line breaks                    
                    let st = setSelHighlight(t,lg,selTextHiLi)                
                    checkFoldedBoxesAsync(lg, selTextHiLi, t,st,true)  
                else
                    foundNoneSel(lg, selTextHiLi)
            |NoSel   -> foundNoneSel(lg, selTextHiLi) 
            |RectSel -> foundNoneSel(lg, selTextHiLi)

        // returns a function for highlighting that does not call the UI continuation
        let setup(lg:TextEditor)  =         
            let ta = lg.TextArea
            // new highlighter per editor instance
            let selTextHiLi = new SelectionColorizer(lg,colorLog)
            ta.TextView.LineTransformers.Add(selTextHiLi)        
            ta.SelectionChanged.Add ( fun a -> handleSelection(lg,  selTextHiLi) )

            // return a function to highlight without any text area selection happening
            // this function will also raise the OnLocationsCounted
            fun txt  -> 
                if isTextToHighlight txt then 
                    selTextHiLi.HighlightText <- txt
                    selTextHiLi.ExcludeFrom   <- -1
                    selTextHiLi.ExcludeTill   <- -1
                    let st = setSelHighlight(txt,lg,selTextHiLi) 
                    checkFoldedBoxesAsync(lg,selTextHiLi,txt,st,false) 
                else
                   foundNoneReq(lg, selTextHiLi)  

