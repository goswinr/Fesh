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
                //if st.Column <> en.Column then log.PrintfnAppErrorMsg "empty rect selection columns dont match:\r\n%A\r\n%A :" st  en // happens whe rectangular selection is beyond line end, then only use visual columns
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

module RectangleSelection = 
    
    /// when pressing backspace key on empty rect selection 
    /// deal with bug that at some fontsizes (like 17.5) deleting rect selction jumps up line by line 
    let backSpaceEmpty (s:Selection.SelPos, avaEdit:TextEditor, log:ISeffLog) =
        let doc = avaEdit.Document
        //log.PrintfnDebugMsg "\r\nback: \r\n%A" s        

        let col = min s.stp.VisualColumn s.enp.VisualColumn
        if col > 0 then  

            doc.BeginUpdate()
            for li = s.enp.Line downto s.stp.Line do // move from bottom up
                let ln = doc.GetLineByNumber(li) 
                if ln.Length - col >= 0 then// in case if line is shorter than block selection
                    doc.Remove(ln.Offset + col - 1 , 1)
            doc.EndUpdate() // finsh doc update beforee recreating selecltion
        
            // collapse selection too
            let mutable st = s.stp
            let mutable en = s.enp
            st.VisualColumn <-  col - 1
            en.VisualColumn <-  col - 1
            st.Column <- min col st.Column
            en.Column <- min col en.Column
            //log.PrintfnDebugMsg "new pos: \r\n%A \r\n%A" st en 
            avaEdit.TextArea.Selection <- new RectangleSelection(avaEdit.TextArea,st,en)             
            avaEdit.TextArea.Caret.VisualColumn <- col - 1

    /// when pressing delet key on empty rect selection 
    /// deal with bug that at some fontsizes (like 17.5) deleting rect selction jumps up line by line            
    let deleteKeyEmpty (s:Selection.SelPos, avaEdit:TextEditor, log:ISeffLog) =                     
        //log.PrintfnDebugMsg "\r\del: \r\n%A" s 
        let doc = avaEdit.Document
        let col = min s.stp.VisualColumn s.enp.VisualColumn
        doc.BeginUpdate()
        for li = s.enp.Line downto s.stp.Line do // move from bottom up
            let ln = doc.GetLineByNumber(li) 
            if ln.Length - col > 0 then// in case if line is shorter than block selection
                doc.Remove(ln.Offset + col , 1)
        doc.EndUpdate() // finsh doc update beforee recreating selection
       
    /// when pressing delet or backspace key on regular rect selection 
    /// deal with bug that at some fontsizes (like 17.5) deleting rect selction jumps up line by line 
    let deleteNonEmpty (s:Selection.SelPos, avaEdit:TextEditor, log:ISeffLog) =
        let doc = avaEdit.Document
        //log.PrintfnDebugMsg "\r\nback: \r\n%A" s        

        let minVisCol = min s.stp.VisualColumn s.enp.VisualColumn
        let maxVisCol = max s.stp.VisualColumn s.enp.VisualColumn        
        let delLen    = maxVisCol - minVisCol

        //log.PrintfnDebugMsg "minCol:%d, maxCol %d " minCol maxCol

        doc.BeginUpdate()
        for li = s.enp.Line downto s.stp.Line do // move from bottom up
            let ln = doc.GetLineByNumber(li) 
            let delLenLoc =  min (ln.Length - minVisCol) delLen // in case if line is shorter than block selection
            //log.PrintfnDebugMsg "delLenLoc:%d, ln %d " delLenLoc li
            if delLenLoc > 0 then 
                doc.Remove(ln.Offset + minVisCol , delLenLoc)
        doc.EndUpdate() // finsh doc update beforee recreating selecltion
        
        // collapse selection too
        let mutable st = s.stp
        let mutable en = s.enp
        st.VisualColumn <-  minVisCol
        en.VisualColumn <-  minVisCol
        st.Column <- min (minVisCol+1) st.Column
        en.Column <- min (minVisCol+1) en.Column
        //log.PrintfnDebugMsg "new pos: \r\n%A \r\n%A" st en 
        avaEdit.TextArea.Selection <- new RectangleSelection(avaEdit.TextArea,st,en) 
        avaEdit.TextArea.Caret.VisualColumn <- minVisCol
        
      