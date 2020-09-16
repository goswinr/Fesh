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


type OccurenceHighlighter (ed:TextEditor) = 
    inherit AvalonEdit.Rendering.DocumentColorizingTransformer()    

    let mutable highTxt = null
    let mutable selStart = -1

    let color = Brushes.Yellow

    member this.HighlightText  with get() = highTxt and set v = highTxt <- v
    member this.SelectionStart  with get() = selStart and set v = selStart <- v

    /// This gets called for every visvble line on any view change
    override this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) =       
        //  from https://stackoverflow.com/questions/9223674/highlight-all-occurrences-of-selected-word-in-avalonedit
        
        if not <| isNull highTxt  then             

            let  lineStartOffset = line.Offset;
            let  text = ed.Document.GetText(line)
            let mutable  start = 0
            let mutable  index = text.IndexOf(highTxt, start, StringComparison.Ordinal)

            while index >= 0 do      
                let st = lineStartOffset + index  // startOffset
                let en = lineStartOffset + index + highTxt.Length // endOffset
                if selStart <> st  then // skip the actual current selection
                    base.ChangeLinePart( st,en, fun el -> el.TextRunProperties.SetBackgroundBrush(color))
                start <- index + highTxt.Length // search for next occurrence
                index <- text.IndexOf(highTxt, start, StringComparison.Ordinal)
            
       


type OccurencesTracer () =   
    
    let highlightChangedEv  = new Event<unit>() 

    //member val Count  = -1 with get , set 

    member val InfoText  = "" with get , set 


    [<CLIEvent>]
    member this.HighlightChanged = highlightChangedEv.Publish
    member this.HighlightChangedEvent = highlightChangedEv

    static member val Instance = OccurencesTracer() // singelton pattern

    static member Setup(ed:TextEditor,ch:Checker) = 
        let ta = ed.TextArea
        let oh = new OccurenceHighlighter(ed)
        ta.TextView.LineTransformers.Add(oh)

        ta.SelectionChanged.Add ( fun a -> 
            let ft = ed.SelectedText
            let sst= ed.SelectionStart
            let t = ft.Trim()
            let doHighlight = t.Length > 1 && not <| t.Contains("\n") // minimum 2 non whitecpace characters?
            if doHighlight then 
                oh.HighlightText <- t
                oh.SelectionStart <- sst
            else
                oh.HighlightText <- null 
            ta.TextView.Redraw()
            
            (*
            /// for status bar :
            match ch.Status with        
            | NotStarted | Running _ | Failed -> 
                () //OccurencesTracer.Instance.InfoText <- ""
            | Done res -> 
                match res.code with 
                | FullCode code -> 
                    let mutable  index = code.IndexOf(t, 0, StringComparison.Ordinal)                
                    let mutable k = 0
                    while index >= 0 do        
                        k <- k+1                        
                        let en =  index + t.Length // endOffset                        
                        index <- code.IndexOf(t, en, StringComparison.Ordinal)
                    //OccurencesTracer.Instance.Count <- k
                    OccurencesTracer.Instance.InfoText <- sprintf "%d x \"%s\"" k t
                    OccurencesTracer.Instance.HighlightChangedEvent.Trigger()
                | PartialCode _ -> ()
      
            *)
            )



