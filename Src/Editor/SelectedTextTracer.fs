namespace Seff.Editor

open Seff
open Seff.Config
open Seff.Util
open Seff.Util.General
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
open ICSharpCode.AvalonEdit.Search

/// Highlight-all-occurrences-of-selected-text in Text View
type SelectedTextHighlighter (ed:TextEditor) = 
    inherit AvalonEdit.Rendering.DocumentColorizingTransformer()    

    let mutable highTxt = null
    let mutable curSelStart = -1

    static member val ColorHighlight =      Brushes.PaleTurquoise |> brighter 30
    static member val ColorHighlightInBox = Brushes.PaleTurquoise |> brighter 30
    static member val ColorFoldBox =        Brushes.Gray   //|> darker 30


    member this.HighlightText  with get() = highTxt and set v = highTxt <- v
    member this.CurrentSelectionStart  with get() = curSelStart and set v = curSelStart <- v

    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) =       
        //  from https://stackoverflow.com/questions/9223674/highlight-all-occurrences-of-selected-word-in-avalonedit
        
        //printfn "Sel in %s " <|  ed.Document.GetText(line)   
        if notNull highTxt  then             

            let  lineStartOffset = line.Offset
            let  text = ed.Document.GetText(line)            
            let mutable  index = text.IndexOf(highTxt, 0, StringComparison.Ordinal)

            while index >= 0 do      
                let st = lineStartOffset + index  // startOffset
                let en = lineStartOffset + index + highTxt.Length // endOffset   

                if curSelStart <> st  then // skip the actual current selection
                    //printfn "Sel %d to %d for %s" st en highTxt
                    base.ChangeLinePart( st,en, fun el -> el.TextRunProperties.SetBackgroundBrush(SelectedTextHighlighter.ColorHighlight))
                let start = index + highTxt.Length // search for next occurrence // TODO or just +1 ???????
                index <- text.IndexOf(highTxt, start, StringComparison.Ordinal)
                  

/// Highlight-all-occurrences-of-selected-text in Text View and Statusbar
type SelectedTextTracer () =   
    
    let highlightChangedEv  = new Event<string*int>()
    [<CLIEvent>]
    member this.HighlightChanged = highlightChangedEv.Publish
    member this.ChangeInfoText(newInfoText,i) = highlightChangedEv.Trigger(newInfoText,i)  // will update status bar             
    
    static member val Instance = SelectedTextTracer() // singelton pattern

    static member Setup(ed:IEditor,folds:Foldings,config:Config) = 
        Folding.FoldingElementGenerator.TextBrush <- SelectedTextHighlighter.ColorFoldBox
        let ta = ed.AvaEdit.TextArea
        let oh = new SelectedTextHighlighter(ed.AvaEdit)
        ta.TextView.LineTransformers.Add(oh)

        ta.SelectionChanged.Add ( fun a ->             
            
            // for text view:
            let highTxt = ed.AvaEdit.SelectedText            
            let checkTx = highTxt.Trim()
            let doHighlight = 
                checkTx.Length > 1 // minimum 2 non whitecpace characters?
                && not <| highTxt.Contains("\n")  //no line beaks          
                && not <| highTxt.Contains("\r")  //no line beaks
                && config.Settings.SelectAllOccurences
            
            if doHighlight then 
                oh.HighlightText <- highTxt
                oh.CurrentSelectionStart <- ed.AvaEdit.SelectionStart
                //ta.TextView.Redraw()

 
                // for status bar :            
                match ed.FileCheckState.FullCodeAndId with 
                | NoCode ->() //OccurencesTracer.Instance.InfoText <- ""
                | CodeID (code,_) ->
                    let mutable  index = code.IndexOf(highTxt, 0, StringComparison.Ordinal)                
                    let mutable k = 0
                    let mutable anyInFolding = false
                    while index >= 0 do        
                        k <- k+1  

                        // check for ttext that is folded away:
                        let infs = folds.Manager.GetFoldingsContaining(index) 
                        for inf in infs do 
                            // if && infs.[0].IsFolded then 
                            inf.BackbgroundColor <-  SelectedTextHighlighter.ColorHighlightInBox
                            anyInFolding <- true
                                
                        let st =  index + highTxt.Length // endOffset // TODO or just +1 ???????
                        if st >= code.Length then 
                            index <- -99 // this happens wen wor to highlight ia at document end
                            //eprintfn "index  %d in %d ??" st code.Length    
                        else
                            index <- code.IndexOf(highTxt, st, StringComparison.Ordinal)
                                   
                    SelectedTextTracer.Instance.ChangeInfoText(highTxt, k  )    // will update status bar 
                    //if anyInFolding then ta.TextView.Redraw()
                    

            else
                if notNull oh.HighlightText then // to ony redraw if it was not null before
                    oh.HighlightText <- null 
                    for f in folds.Manager.AllFoldings do  f.BackbgroundColor <- null 
                    ///ta.TextView.Redraw() // to clear highlight
            
            ta.TextView.Redraw() //do just once at end ?
           
               
            )
  
