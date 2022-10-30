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
type SelectionColorizier (ed:TextEditor,color:SolidColorBrush) = 
    inherit Rendering.DocumentColorizingTransformer()
    //  from https://stackoverflow.com/questions/9223674/highlight-all-occurrences-of-selected-word-in-avalonedit
       
    let mutable highTxt:string = null
    let mutable exclStart = -1
    let mutable exclEnd   = -1 // end offset is the last character with highlighting

    member this.HighlightText 
        with get() = highTxt 
        and set v  = highTxt <- v

    member this.Clear() = highTxt <- null
    
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
                let start = index + highTxt.Length // search fromthis for next occurrence in this line 
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

    let private foundNoneRedraw(ava:TextEditor, selTextHiLi:SelectionColorizier) = 
        selTextHiLi.HighlightText <- null
        selTextHiLi.ExcludeFrom   <- -1
        selTextHiLi.ExcludeTill   <- -1
        ava.TextArea.TextView.Redraw()
   
    let clearFolds(ed:IEditor) = 
        for fold in ed.FoldingManager.AllFoldings do 
            fold.BackbgroundColor <- null 
    
    let private foundNoneSel(ava:TextEditor, selTextHiLi:SelectionColorizier) =
        foundNoneRedraw(ava, selTextHiLi)  |> ignore 
        selectionChangedEv.Trigger(ava, FoundNone )

    let private foundNoneReq(ava:TextEditor, selTextHiLi:SelectionColorizier) =
        foundNoneRedraw(ava, selTextHiLi)  |> ignore 
        highlightRequestedEv.Trigger(ava, FoundNone )


    let private foundNoneSelFold(ed:IEditor, selTextHiLi:SelectionColorizier) =
        clearFolds(ed)
        foundNoneRedraw(ed.AvaEdit, selTextHiLi)  |> ignore 
        selectionChangedEv.Trigger(ed.AvaEdit, FoundNone )

    let private foundNoneReqFold(ed:IEditor, selTextHiLi:SelectionColorizier) =
        clearFolds(ed)
        foundNoneRedraw(ed.AvaEdit, selTextHiLi)  |> ignore 
        highlightRequestedEv.Trigger(ed.AvaEdit, FoundNone )


    let inline isTextToHighlight(t:string) = 
        t.Length > 1 && not (Str.isJustSpaceCharsOrEmpty t)  && not <| t.Contains("\n") 

    let setSelHighglight(t:string, ava:TextEditor, hiLi:SelectionColorizier) = 
        let cselst = ava.SelectionStart
        hiLi.ExcludeFrom   <- cselst // to exclude current selection from highlighting
        hiLi.ExcludeTill   <- cselst + t.Length - 1 // end offset is the last character with highlighting           
        hiLi.HighlightText <- t 
        cselst
   


    module HiEditor =  

        let checkFoldedBoxes (ed:IEditor,highTxt) =
            // for status bar and folds :
            match ed.FileCheckState.FullCodeAndId with
            | NoCode -> empty // for pereformance dont request full codestring if missing
            | CodeID (code,_) ->
                let mutable index = code.IndexOf(highTxt, 0, StringComparison.Ordinal)  
                for fold in ed.FoldingManager.AllFoldings do fold.BackbgroundColor <- null // reset all first, before setting some
                let offsets = ResizeArray<int>()
                while index >= 0 do                    
                    offsets.Add(index)
                    // check for text that is folded away:
                    let infs = ed.FoldingManager.GetFoldingsContaining(index)
                    for inf in infs do inf.BackbgroundColor <- colorEditor                    
                    let st =  index + highTxt.Length // endOffset // TODO or just +1 ???????
                    if st >= code.Length then
                        index <- -1 // this happens when word to highlight is at document end
                        //ISeffLog.log.PrintfnAppErrorMsg  "index  %d in %d ??" st code.Length
                    else
                        index <- code.IndexOf(highTxt, st, StringComparison.Ordinal)
                offsets        
    
        let handleSelection(ed:IEditor, selTextHiLi:SelectionColorizier) =
            match Selection.getSelType ed.AvaEdit.TextArea with 
            |RegSel  -> 
                let t = ed.AvaEdit.SelectedText
                if isTextToHighlight t then  //min two chars and no line breaks                    
                    let st = setSelHighglight(t,ed.AvaEdit,selTextHiLi)                
                    let offs = checkFoldedBoxes(ed,t)
                    if offs.Count > 0 then 
                        selectionChangedEv.Trigger(ed.AvaEdit, FoundSome {text=t; offsets=offs; selectionAt = st})
                        ed.AvaEdit.TextArea.TextView.Redraw()
                    else // this case should actullay never happen:
                        foundNoneSel(ed.AvaEdit, selTextHiLi)// no need to clear folds here too 
                else
                    foundNoneSelFold(ed, selTextHiLi) 
            |NoSel   -> foundNoneSelFold(ed, selTextHiLi) 
            |RectSel -> foundNoneSelFold(ed, selTextHiLi) 
            

      // returns a function for highlighting that does not call the UI continuation
        let setup(ed:IEditor) : (string->unit) =         
            // new higlighter per editor instance
            let selTextHiLi = new SelectionColorizier(ed.AvaEdit,colorEditor)
            let ta = ed.AvaEdit.TextArea
            ta.TextView.LineTransformers.Add(selTextHiLi)        
            ta.SelectionChanged.Add ( fun a -> handleSelection(ed, selTextHiLi) )

            // return a function to highlight without any text area selection happening 
            fun txt  -> 
                if isTextToHighlight txt then 
                    selTextHiLi.HighlightText <- txt
                    selTextHiLi.ExcludeFrom   <- -1
                    selTextHiLi.ExcludeTill   <- -1
                    let offs = checkFoldedBoxes(ed,txt)  
                    if offs.Count > 0 then
                        ed.AvaEdit.TextArea.TextView.Redraw()
                        highlightRequestedEv.Trigger(ed.AvaEdit, FoundSome {text=txt; offsets=offs; selectionAt= -1})
                    else // this case should actullay never happen:
                        foundNoneReq(ed.AvaEdit, selTextHiLi)// no need to clear folds here too 
                else  
                    foundNoneReqFold(ed, selTextHiLi) 


    module HiLog =

        let checkFoldedBoxesAsync(lg:TextEditor, selTextHiLi:SelectionColorizier, text, st, fromSelection)=
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
                    else                 highlightRequestedEv.Trigger(lg, r) 
                    lg.TextArea.TextView.Redraw()
                
                else // this case should actullay never happen:
                    if fromSelection then  foundNoneSel(lg, selTextHiLi)                         
                    else                   foundNoneReq(lg, selTextHiLi)  
                    
                } 
                |> Async.Start


        let handleSelection (lg:TextEditor, selTextHiLi:SelectionColorizier) =             
            match Selection.getSelType lg.TextArea with 
            |RegSel  -> 
                let t = lg.SelectedText
                if isTextToHighlight t then  //min two chars and no line breaks                    
                    let st = setSelHighglight(t,lg,selTextHiLi)                
                    checkFoldedBoxesAsync(lg,  selTextHiLi, t,st,true)                
            |NoSel   -> foundNoneSel(lg, selTextHiLi) 
            |RectSel -> foundNoneSel(lg, selTextHiLi)

        // returns a function for highlighting that does not call the UI continuation
        let setup(lg:TextEditor)  =         
            let ta = lg.TextArea
            // new higlighter per editor instance
            let selTextHiLi = new SelectionColorizier(lg,colorLog)
            ta.TextView.LineTransformers.Add(selTextHiLi)        
            ta.SelectionChanged.Add ( fun a -> handleSelection(lg,  selTextHiLi) )

            // return a function to highlight without any text area selection happening
            // this function will also raise the OnLocationsCounted
            fun txt  -> 
                if isTextToHighlight txt then 
                    selTextHiLi.HighlightText <- txt
                    selTextHiLi.ExcludeFrom   <- -1
                    selTextHiLi.ExcludeTill   <- -1
                    let st = setSelHighglight(txt,lg,selTextHiLi) 
                    checkFoldedBoxesAsync(lg,selTextHiLi,txt,st,false) 
                else
                   foundNoneReq(lg, selTextHiLi)  



(*

                if offs.Count > 0 then  uiContinuation(ed.AvaEdit, FoundSome {text=txt; offsets=offs; selectionAt= -1})
                else  clear(ed, selTextHiLi)
            else  clear(ed, selTextHiLi)  
            ed.AvaEdit.TextArea.TextView.Redraw()
    




/// This singleton class only exist to provide events for the status bar to update
type SelectedTextTracer private () = 
    
    

    //---------------------------
    //-----static members --------
    //--------------------------
    static member val Instance = SelectedTextTracer() // singleton pattern

    static member highlightText(highTxt:string, ed:IEditor, hili:SelectedTextHighlighter, fromSelection, raiseEvent:int)= 
        // for text view:        
        let checkTx = highTxt.Trim()
        let doHighlight = 
            checkTx.Length > 1 // minimum 2 non whitespace characters?
            && not <| highTxt.Contains("\n")  //no line beaks
            && not <| highTxt.Contains("\r")  //no line beaks            

        if doHighlight then            
            hili.HighlightText <- highTxt
            if fromSelection then // to exclude current selection from highlighting
                let cselst = ed.AvaEdit.SelectionStart
                hili.CurrentSelectionStart <- cselst
                hili.CurrentSelectionEnd <- cselst + highTxt.Length - 1 // end offset is the last character with highlighting
            else
                hili.CurrentSelectionStart <- -1
                hili.CurrentSelectionEnd   <- -1
            
            // for status bar and folds :
            match ed.FileCheckState.FullCodeAndId with
            | NoCode -> () //OccurrencesTracer.Instance.InfoText <- ""
            | CodeID (code,_) ->
                offsets.Clear()
                let mutable  index = code.IndexOf(highTxt, 0, StringComparison.Ordinal)
                let mutable k = 0
                let mutable anyInFolding = false
                for fold in ed.FoldingManager.AllFoldings do  fold.BackbgroundColor <- null // reset all first, before setting some
                while index >= 0 do
                    k <- k+1
                    offsets.Add(index)
                    // check for text that is folded away:
                    let infs = ed.FoldingManager.GetFoldingsContaining(index)
                    for inf in infs do // should be just one or none
                        // if && infs.[0].IsFolded then // do always !
                        inf.BackbgroundColor <-  SelectedTextHighlighter.ColorHighlightInBox
                        anyInFolding <- true

                    let st =  index + highTxt.Length // endOffset // TODO or just +1 ???????
                    if st >= code.Length then
                        index <- -99 // this happens when word to highlight is at document end
                        //ISeffLog.log.PrintfnAppErrorMsg  "index  %d in %d ??" st code.Length
                    else
                        index <- code.IndexOf(highTxt, st, StringComparison.Ordinal)

                if raiseEvent>0 then 
                    if offsets.Count > 0 then  SelectedTextTracer.Instance.TriggerOnHighlightChanged(raiseEvent-1, highTxt, offsets)    // this will update status bar
                    else 
                        hili.HighlightText <- null
                        SelectedTextTracer.Instance.TriggerOnHighlightCleared(raiseEvent-1)
                //if anyInFolding then ta.TextView.Redraw()


        else
            if notNull hili.HighlightText then // to ony redraw if it was not null before
                hili.HighlightText <- null
                for f in ed.FoldingManager.AllFoldings do  f.BackbgroundColor <- null
                if raiseEvent>0 then SelectedTextTracer.Instance.TriggerOnHighlightCleared(raiseEvent-1)
                //ta.TextView.Redraw() // to clear highlight

        ed.AvaEdit.TextArea.TextView.Redraw() //do just once at end ?

    static member Setup(ed:IEditor) = 
        Folding.FoldingElementGenerator.TextBrush <- SelectedTextHighlighter.ColorFoldBoxOutline
        let ta = ed.AvaEdit.TextArea
        // new higlighter per editor instance
        let selTextTracer = new SelectedTextHighlighter(ed.AvaEdit)
        ta.TextView.LineTransformers.Add(selTextTracer)
        ta.SelectionChanged.Add ( fun a -> SelectedTextTracer.highlightText(ed.AvaEdit.SelectedText, ed, selTextTracer, true, 3))// raise count 3 is needed to update both status bars

        // return a function to highglight any non selected text.
        (fun (txt:string, fromSelection:bool, raiseCount:int ) -> SelectedTextTracer.highlightText(txt, ed, selTextTracer, fromSelection, raiseCount))

        
    

        


*)
