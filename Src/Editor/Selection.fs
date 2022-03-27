namespace Seff.Editor

open System

open AvalonEditB
open AvalonEditB.Editing
open AvalonEditB.Document

open Seff.Model

module Selection = 

    /// a segment defined by start and end offset
    [<Struct>]
    type Seg = 
        {st:int; en:int}
        member s.len = s.en - s.st
    
    /// In this Selection Position stPos is always smaller than enPo. 
    /// Which is not always the case in TextArea.Selection
    type SelectionPos = 
        {stPos:TextViewPosition ; enPos:TextViewPosition; caret:TextViewPosition}

        member this.LineCount = this.enPos.Line - this.stPos.Line + 1
        member this.stOffset(d:TextDocument) = d.GetOffset(this.stPos.Location)
        member this.enOffset(d:TextDocument) = d.GetOffset(this.enPos.Location)
        member this.caretOffset(d:TextDocument) = d.GetOffset(this.caret.Location)

    /// ensure first is smaller or equal to second
    let inline sorted a b = if a>b then b,a else a,b


    /// a DU represneting all possible kinds of the current selection
    type Sel = 
        |NoSel
        |RegSel
        |RectSel

    let getSelType (ta:TextArea) = 
        match ta.Selection with
        | null -> failwithf "Unknown selection class in getSelection: null"
        | :? EmptySelection     -> NoSel
        | :? SimpleSelection    -> RegSel
        | :? RectangleSelection -> RectSel
        | x -> failwithf "Unknown selection class in getSelection: %A" x

    /// Returns SelectionPos, order top to left bottom right
    let getSelectionOrdered(ta:TextArea) : SelectionPos= 
        match ta.Selection with
        | null -> failwithf "Unknown selection class in makeTopDown: null"
        | :? EmptySelection  ->
            let p = ta.Caret.Position
            {stPos = p ; enPos = p; caret = p }

        | :? SimpleSelection  as ss ->
            let st = ss.StartPosition
            let en = ss.EndPosition
            let car = ta.Caret.Position
            if st.Line > en.Line then                              {stPos = en ; enPos = st; caret = car } // reversed order
            elif st.Line = en.Line && en.Column < st.Column then   {stPos = en ; enPos = st; caret = car  } // reversed order
            else                                                   {stPos = st ; enPos = en; caret = car  }

        | :? RectangleSelection as rs ->
            let s = rs.StartPosition
            let e = rs.EndPosition
            let car = ta.Caret.Position
            //ISeffLog.log.PrintfnDebugMsg "caret0: %A "car
            let v1,v2 = sorted s.VisualColumn e.VisualColumn
            if s.Line <= e.Line then
               {stPos = TextViewPosition(s.Line, s.Column, v1)
                enPos = TextViewPosition(e.Line, e.Column, v2)
                caret = car }
            else
               {stPos = TextViewPosition(e.Line, e.Column, v1)
                enPos = TextViewPosition(s.Line, s.Column, v2)
                caret = car }


        | x -> failwithf "Unknown selection class in makeTopDown: %A" x

    /// any letter, digit, underscore or dot
    let inline internal isFsLetter (c:char) = Char.IsLetterOrDigit c || c='_' || c='.'

    let inline internal areLetters (doc:TextDocument) fromIdx toIdx = 
        let rec loop i = 
            if  i>toIdx then true
            elif i |> doc.GetCharAt |>  isFsLetter then loop (i+1)
            else false
        loop fromIdx

    /// treats start and end of file also as non letter
    let inline internal isNonLetter (doc:TextDocument) i = 
        i = -1 // file start
        || i = doc.TextLength //file end
        || i |> doc.GetCharAt |>  isFsLetter |> not


    /// checks if the current selection is exactly one word
    let isOneWord (ed:TextEditor) : option<SelectionSegment> = 
        match ed.TextArea.Selection with
        | :? EmptySelection   | :? RectangleSelection -> None
        | :? SimpleSelection as sel  ->
            let doc = ed.Document
            let seg = Seq.head sel.Segments
            if     isNonLetter doc <| seg.StartOffset - 1
                && areLetters  doc    seg.StartOffset  (seg.EndOffset-1)  // seg.EndOffset is the first chareaxter after the selection
                && isNonLetter doc <| seg.EndOffset then
                    Some seg
            else
                    //ISeffLog.log.PrintfnDebugMsg "not Word: %s" (sel.GetText())
                    None

            //let startOffset = TextUtilities.GetNextCaretPosition(doc,offset, LogicalDirection.Backward, CaretPositioningMode.WordBorderOrSymbol)// TODO fails on ´´ backtick names
            //let endOffset =   TextUtilities.GetNextCaretPosition(doc,offset, LogicalDirection.Forward,  CaretPositioningMode.WordBorderOrSymbol)// returns -1 on empty lines;

        | _ ->  None


    /// Returns true if nothin is selected in textarea
    let hasNoSelection (ta:TextArea) = 
        match ta.Selection with
        | null -> true  // does this actually happen?
        | :? EmptySelection -> true
        | :? RectangleSelection -> false
        | :? SimpleSelection  -> false
        | x -> failwithf "Unknown selection class in hasNoSelection: %A" x

    let selectAll(avaEdit:TextEditor) = 
        let doc = avaEdit.Document
        avaEdit.Select(0,doc.TextLength)

    /// text of line at current Caret
    /// Returns start line umber and line  text
    /// Does not select anything
    let currentLine (avaEdit:TextEditor) : int*string= 
        let offset = avaEdit.CaretOffset
        let  line = avaEdit.Document.GetLineByOffset(offset)
        line.LineNumber, avaEdit.Document.GetText(line.Offset, line.Length)

    /// Offset at line End ( exluding \r and \n that probably follow
    let currentLineEnd (avaEdit:TextEditor) : int= 
        let offset = avaEdit.CaretOffset
        let  line = avaEdit.Document.GetLineByOffset(offset)
        line.Offset + line.Length

module SelectionForEval = 

    /// Returns start line umber and selected text, or "" if none
    let current (avaEdit:TextEditor) : CodeSegment = 
        let tx = avaEdit.SelectedText
        if isNull tx then
            {
            text = ""
            startLine = 0
            startOffset = 0
            length = 0
            }
        else
            let s = avaEdit.TextArea.Selection
            let t = avaEdit.SelectedText
            {
            text = t
            startLine = s.StartPosition.Line
            startOffset = avaEdit.SelectionStart
            length = t.Length
            }


    /// Returns start line umber and selected text
    let expandSelectionToFullLines(avaEdit:TextEditor) : CodeSegment = 
        let doc = avaEdit.Document
        let st = doc.GetLineByOffset(avaEdit.SelectionStart)
        let en = doc.GetLineByOffset(avaEdit.SelectionStart + avaEdit.SelectionLength)
        let stoff = st.Offset
        avaEdit.Select(stoff,en.EndOffset-stoff)
        let s = avaEdit.TextArea.Selection
        let t = avaEdit.SelectedText
        {
        text = t + Environment.NewLine // add line return so that EvaluationTracker.fs knows it is the full line and can jump to next line.
        startLine = s.StartPosition.Line // not st.LineNumber becaus this may juat as wel be the last line
        startOffset = avaEdit.SelectionStart
        length = t.Length + Environment.NewLine.Length
        }


    /// Returns start line umber and selected text
    let linesTillCursor(avaEdit:TextEditor) : CodeSegment = 
        let doc = avaEdit.Document
        let en = doc.GetLineByOffset(avaEdit.SelectionStart + avaEdit.SelectionLength)
        avaEdit.Select(0,en.EndOffset)
        let t = avaEdit.SelectedText
        {
        text = t + Environment.NewLine // add line return so that EvaluationTracker.fs knows it is the full line and can jump to next line.
        startLine = 1
        startOffset = 0
        length = t.Length + Environment.NewLine.Length
        }


    // returns start line umber and selected text, end offset
    //let linesFromCursor(avaEdit:TextEditor) = 
    //    let doc = avaEdit.Document
    //    let st = doc.GetLineByOffset(avaEdit.SelectionStart)
    //    avaEdit.Select(st.Offset,avaEdit.Document.TextLength-st.Offset)
    //    st.LineNumber,avaEdit.SelectedText
