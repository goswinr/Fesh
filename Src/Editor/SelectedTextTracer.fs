namespace Seff.Editor

open Seff
open Seff.Util
open System
open System.Windows
open System.Windows.Controls
open System.Windows.Media

open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.Editing
open ICSharpCode.AvalonEdit.Document
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open ICSharpCode

/// Highlight-all-occurrences-of-selected-text in Text View
type SelectedTextHighlighter (ed:TextEditor) = 
    inherit AvalonEdit.Rendering.DocumentColorizingTransformer()    

    let mutable highTxt = null
    let mutable curSelStart = -1

    let color = Brushes.Yellow

    member this.HighlightText  with get() = highTxt and set v = highTxt <- v
    member this.CurrentSelectionStart  with get() = curSelStart and set v = curSelStart <- v

    /// This gets called for every visvble line on any view change
    override this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) =       
        //  from https://stackoverflow.com/questions/9223674/highlight-all-occurrences-of-selected-word-in-avalonedit
        
        if not <| isNull highTxt  then             

            let  lineStartOffset = line.Offset;
            let  text = ed.Document.GetText(line)            
            let mutable  index = text.IndexOf(highTxt, 0, StringComparison.Ordinal)

            while index >= 0 do      
                let st = lineStartOffset + index  // startOffset
                let en = lineStartOffset + index + highTxt.Length // endOffset
                if curSelStart <> st  then // skip the actual current selection
                    base.ChangeLinePart( st,en, fun el -> el.TextRunProperties.SetBackgroundBrush(color))
                let start = index + highTxt.Length // search for next occurrence // TODO or just +1 ???????
                index <- text.IndexOf(highTxt, start, StringComparison.Ordinal)
                  

/// Highlight-all-occurrences-of-selected-text in Text View and Statusbar
type SelectedTextTracer () =   
    
    // the only purpose of this singelyon is to rasie the HighlightChanged event to update the status bar
    
    let highlightChangedEv  = new Event<string*int>()
    
    member this.ChangeInfoText(newInfoText,i) = highlightChangedEv.Trigger(newInfoText,i)  // will update status bar             

    [<CLIEvent>]
    member this.HighlightChanged = highlightChangedEv.Publish

    static member val Instance = SelectedTextTracer() // singelton pattern

    static member Setup(ed:TextEditor,ch:Checker) = 
        let ta = ed.TextArea
        let oh = new SelectedTextHighlighter(ed)
        ta.TextView.LineTransformers.Add(oh)

        ta.SelectionChanged.Add ( fun a -> 
            
            // for text view:
            let highTxt = ed.SelectedText            
            let checkTx = highTxt.Trim()
            let doHighlight = checkTx.Length > 1 && not <| checkTx.Contains("\n") // minimum 2 non whitecpace characters? no line beaks
            if doHighlight then 
                oh.HighlightText <- highTxt
                oh.CurrentSelectionStart <- ed.SelectionStart
            else
                oh.HighlightText <- null 
            ta.TextView.Redraw()

            // for status bar :
            if doHighlight then 
               match ch.Status with       
               | NotStarted | Running _ | Failed -> 
                    () //OccurencesTracer.Instance.InfoText <- ""
               | Done res -> 
                    match res.code with 
                    | FullCode code -> 
                        let mutable  index = code.IndexOf(highTxt, 0, StringComparison.Ordinal)                
                        let mutable k = 0
                        while index >= 0 do        
                            k <- k+1                        
                            let st =  index + highTxt.Length // endOffset // TODO or just +1 ???????
                            if st >= code.Length then 
                                index <- -99
                                printfn "index  %d in %d ??" st code.Length    
                            else
                                index <- code.IndexOf(highTxt, st, StringComparison.Ordinal)
                                   
                        SelectedTextTracer.Instance.ChangeInfoText(highTxt, k  )          // will update status bar             
                    | PartialCode _ -> ()
            )
        SelectedTextTracer.Instance


