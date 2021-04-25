namespace Seff.Editor

open System
open Seff.Model
open AvalonEditB
open AvalonEditB.Editing
open AvalonEditB.Document
open Seff.Util
open System.Windows
open System.Text

module Selection =
    
    /// a segment defined by start and end offset
    [<Struct>]
    type Seg = 
        {st:int;en:int}
        member s.len = s.en - s.st
         
    type SelPos = 
        {stp:TextViewPosition ; enp:TextViewPosition; caret:TextViewPosition}
        
        member this.LineCount = this.enp.Line - this.stp.Line + 1 
    
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
    
    /// returns selpos order top to left bottom right
    let getSelectionOrdered(ta:TextArea) : SelPos=
        match ta.Selection with
        | null -> failwithf "Unknown selection class in makeTopDown: null"
        | :? EmptySelection  ->             
            let p = ta.Caret.Position
            {stp = p ; enp = p; caret = p }
        
        | :? SimpleSelection  as ss -> 
            let st = ss.StartPosition
            let en = ss.EndPosition 
            let car = ta.Caret.Position
            if st.Line > en.Line then                              {stp = en ; enp = st; caret = car } // reversed order 
            elif st.Line = en.Line && en.Column < st.Column then   {stp = en ; enp = st; caret = car  } // reversed order 
            else                                                   {stp = st ; enp = en; caret = car  }
                                   
        | :? RectangleSelection as rs -> 
            let s = rs.StartPosition
            let e = rs.EndPosition
            let car = ta.Caret.Position
            //ISeffLog.log.PrintfnDebugMsg "caret0: %A "car
            let v1,v2 = sorted s.VisualColumn e.VisualColumn
            if s.Line <= e.Line then               
               {stp = TextViewPosition(s.Line, s.Column, v1)  
                enp = TextViewPosition(e.Line, e.Column, v2)
                caret = car }
            else
               {stp = TextViewPosition(e.Line, e.Column, v1) 
                enp = TextViewPosition(s.Line, s.Column, v2)
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


    /// retuns true if nothin is selected in textarea
    let hasNoSelection (ta:TextArea) =
        match ta.Selection with
        | null -> true  // does this actually happen?
        | :? EmptySelection -> true  
        | :? RectangleSelection -> false
        | :? SimpleSelection  -> false
        | x -> failwithf "Unknown selection class in hasNoSelection: %A" x            
            

    /// retuns start line umber and selected text, or "" if none
    let current (avaEdit:TextEditor) = // TODO move to other module ! not selection related
        let tx = avaEdit.SelectedText               
        if isNull tx then 0,""
        else 
            let  line = avaEdit.Document.GetLineByOffset(avaEdit.SelectionStart)
            line.LineNumber, tx

    /// text of line at current Caret 
    /// retuns start line umber and selected text
    let currentLine (avaEdit:TextEditor) = // TODO move to other module ! not selection related
        let offset = avaEdit.CaretOffset               
        let  line = avaEdit.Document.GetLineByOffset(offset)
        line.LineNumber, avaEdit.Document.GetText(line.Offset, line.Length)

    /// retuns start line umber and selected text
    let expandSelectionToFullLines(avaEdit:TextEditor) =
        let doc = avaEdit.Document
        let st = doc.GetLineByOffset(avaEdit.SelectionStart)
        let en = doc.GetLineByOffset(avaEdit.SelectionStart + avaEdit.SelectionLength)
        let stoff = st.Offset
        avaEdit.Select(stoff,en.EndOffset-stoff)
        st.LineNumber, avaEdit.SelectedText
    
    /// retuns start line umber and selected text
    let linesTillCursor(avaEdit:TextEditor) =
        let doc = avaEdit.Document        
        let en = doc.GetLineByOffset(avaEdit.SelectionStart + avaEdit.SelectionLength)
        avaEdit.Select(0,en.EndOffset)
        1,avaEdit.SelectedText
    
    /// retuns start line umber and selected text
    let linesFromCursor(avaEdit:TextEditor) =
        let doc = avaEdit.Document
        let st = doc.GetLineByOffset(avaEdit.SelectionStart)        
        avaEdit.Select(st.Offset,avaEdit.Document.TextLength-st.Offset)
        st.LineNumber,avaEdit.SelectedText

    let selectAll(avaEdit:TextEditor) = 
        let doc = avaEdit.Document
        avaEdit.Select(0,doc.TextLength)
    
    /// Fails on rectangular selection , use methods from RectangleSelection Module 
    let insertAtCaretOrSimpleSelectionUNUSED (avaEdit:TextEditor, tx:string) = //TODO delete
        if avaEdit.TextArea.Selection.IsEmpty then
            avaEdit.Document.Insert(avaEdit.TextArea.Caret.Offset, tx)
        else 
            avaEdit.Document.BeginUpdate()
            let mutable shift = 0
            for seg in avaEdit.TextArea.Selection.Segments do
                let segShifted = new SelectionSegment(seg.StartOffset+shift, seg.EndOffset+shift)
                avaEdit.Document.Replace(segShifted, tx) // becaus the next segment will not be correcty anymore after this insert
                shift <- shift - seg.Length + tx.Length
            avaEdit.Document.EndUpdate()

