namespace Seff.Editor

open Seff.Model
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.Editing
open ICSharpCode.AvalonEdit.Document
open Seff.Util

module Selection =
    
    /// a segment defined by start and end offset
    [<Struct>]
    type Seg = 
        {st:int;en:int}
        member s.len = s.en - s.st
         
    type SelPos = {stp:TextViewPosition ; enp:TextViewPosition}
    
    /// a DU represneting all possible kinds of the current selection
    type Sel = 
        |NoSel
        |RegSel  of SelPos
        |RectSel of SelPos 
        |RectSelEmpty of SelPos 
         

    let getSelection (ta:TextArea,log:ISeffLog) = 
        match ta.Selection with
               
        | null -> 
            log.PrintfnAppErrorMsg "null selction class in Text Area"
            NoSel  
               
        | :? EmptySelection -> NoSel   
               
        | :? RectangleSelection as rs -> 
            let st = rs.StartPosition
            let en = rs.EndPosition 
                   
            if rs.IsEmpty then 
                if st.Column <> en.Column then log.PrintfnAppErrorMsg "empty rect selection st.Column %d <> en.Column %d" st.Column  en.Column
                if st.Line > en.Line then  RectSelEmpty {stp = en ; enp = st } // reverse order 
                else                       RectSelEmpty {stp = st ; enp = en }
            else
                if st.Line > en.Line then                              RectSel {stp = en ; enp = st } // reverse order 
                elif st.Line = en.Line && en.Column < st.Column then   RectSel {stp = en ; enp = st } // reverse order 
                else                                                   RectSel {stp = st ; enp = en }

        | :? SimpleSelection    as ss -> 
            let st = ss.StartPosition
            let en = ss.EndPosition 
            if st.Line > en.Line then                              RegSel {stp = en ; enp = st } // reverse order 
            elif st.Line = en.Line && en.Column < st.Column then   RegSel {stp = en ; enp = st } // reverse order 
            else                                                   RegSel {stp = st ; enp = en }
                                         
               
        | x ->             
            log.PrintfnAppErrorMsg "Unknown selction class in swapLinesUp: %A" x
            NoSel

    /// retuns true if nothin is selected in textarea
    let hasNoSelection (ta:TextArea) =
        match ta.Selection with
        | null -> true  // does this actually happen?
        | :? EmptySelection -> true  
        | :? RectangleSelection -> false
        | :? SimpleSelection  -> false
        | x ->             
            eprintf "Unknown selction class in CursorBehaviour.hasNoSelection: %A" x
            false

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
 

