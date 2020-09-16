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
            let mutable  index = text.IndexOf(highTxt, 0, StringComparison.Ordinal)

            while index >= 0 do      
                let st = lineStartOffset + index  // startOffset
                let en = lineStartOffset + index + highTxt.Length // endOffset
                if selStart <> st  then // skip the actual current selection
                    base.ChangeLinePart( st,en, fun el -> el.TextRunProperties.SetBackgroundBrush(color))
                let start = index + highTxt.Length // search for next occurrence // TODO or just +1 ???????
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
            let highTxt = ft.Trim()
            let doHighlight = highTxt.Length > 1 && not <| highTxt.Contains("\n") // minimum 2 non whitecpace characters?
            if doHighlight then 
                oh.HighlightText <- highTxt
                oh.SelectionStart <- sst
            else
                oh.HighlightText <- null 
            ta.TextView.Redraw()

If dohighlt then 
            (*



            Try dup[licat the code string so it is on this thredad ?????

            match ch.Status with        
            | NotStarted | Running _ | Failed -> 
                () //OccurencesTracer.Instance.InfoText <- ""
            | Done res -> 
                match res.code with 
                | FullCode code ->                     
                    printfn "code.Length %d" code.Length
                    //OccurencesTracer.Instance.Count <- k                    
                    //OccurencesTracer.Instance.InfoText <- sprintf "%d x \"%s\"" k highTxt
                    //OccurencesTracer.Instance.HighlightChangedEvent.Trigger()
                | PartialCode _ -> ()
            
            
            /// for status bar :
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
                        else
                            index <- code.IndexOf(highTxt, st, StringComparison.Ordinal)
                    printfn "found %d" k
                    //OccurencesTracer.Instance.Count <- k                    
                    //OccurencesTracer.Instance.InfoText <- sprintf "%d x \"%s\"" k highTxt
                    //OccurencesTracer.Instance.HighlightChangedEvent.Trigger()
                | PartialCode _ -> ()
            *)
            
            )



