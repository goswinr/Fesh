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

    let color = Brushes.LightSkyBlue

    member this.HighlightText  with get() = highTxt and set v = highTxt <- v

    /// This gets called for every visvble line on any view change
    override this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) =       
        //  from https://stackoverflow.com/questions/9223674/highlight-all-occurrences-of-selected-word-in-avalonedit
        
        if not <| isNull highTxt  then             

            let  lineStartOffset = line.Offset;
            let  text = ed.Document.GetText(line)
            let mutable  start = 0
            let mutable  index = text.IndexOf(highTxt, start, StringComparison.Ordinal)

            while (index >= 0) do            
                base.ChangeLinePart( 
                    lineStartOffset + index, // startOffset
                    lineStartOffset + index + highTxt.Length, // endOffset
                    fun element -> element.TextRunProperties.SetBackgroundBrush(color))
                start <- index + 1 // search for next occurrence
                index <- text.IndexOf(highTxt, start, StringComparison.Ordinal)
        
    
    static member Setup(ed:TextEditor) = 
        let ta = ed.TextArea
        let oh = new OccurenceHighlighter(ed)
        ta.TextView.LineTransformers.Add(oh)

        ta.SelectionChanged.Add ( fun a -> 
            let ft = ed.SelectedText
            let t = ft.Trim()
            if t.Length > 1 && not <| t.Contains("\n") then // minimum 2 non whitecpace characters?
                oh.HighlightText <- t  
            else
                oh.HighlightText <- null 
            ta.TextView.Redraw()
            )
        oh


