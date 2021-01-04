namespace Seff.Editor

open System
open Seff.Model
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.Editing
open ICSharpCode.AvalonEdit.Document
open Seff.Util
open System.Windows
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit

module Selection =
    
    /// a segment defined by start and end offset
    [<Struct>]
    type Seg = 
        {st:int;en:int}
        member s.len = s.en - s.st
         
    type SelPos = {stp:TextViewPosition ; enp:TextViewPosition}
    
    /// ensure first is smaller or equal to second
    let inline sorted a b = if a>b then b,a else a,b

    let setPos maxCol vcol (p:TextViewPosition) =  
        TextViewPosition(
            p.Line, 
            min (vcol + 1) maxCol , 
            vcol
            )

    /// a DU represneting all possible kinds of the current selection
    type Sel = 
        |NoSel
        |RegSel      
        |RectSel 

    let makeTopDown(s:Selection)=
        match s with
        | null -> failwithf "Unknown selection class in makeTopDown: null"
        | :? EmptySelection as es -> 
            if es.StartPosition <> es.EndPosition  then eprintf "makeTopDown es.StartPosition <> es.EndPosition " //TODO Remove 
            {stp = es.StartPosition ; enp = es.EndPosition }
        
        | :? SimpleSelection    as ss -> 
            let st = ss.StartPosition
            let en = ss.EndPosition 
            if st.Line > en.Line then                              {stp = en ; enp = st } // reversed order 
            elif st.Line = en.Line && en.Column < st.Column then   {stp = en ; enp = st } // reversed order 
            else                                                   {stp = st ; enp = en }
                                   
        | :? RectangleSelection as rs -> 
            let s = rs.StartPosition
            let e = rs.EndPosition
            let l1,l2 = sorted s.Line         e.Line 
            let c1,c2 = sorted s.Column       e.Column
            let v1,v2 = sorted s.VisualColumn e.VisualColumn
            {stp = TextViewPosition(l1,c1,v1) ; 
             enp = TextViewPosition(l2,c2,v2) }
               
        | x -> failwithf "Unknown selection class in makeTopDown: %A" x
     
    let getSelType (ta:TextArea) = 
        match ta.Selection with               
        | null -> failwithf "Unknown selection class in getSelection: null"
        | :? EmptySelection   -> NoSel
        | :? SimpleSelection  -> RegSel
        | :? RectangleSelection -> RectSel
        | x -> failwithf "Unknown selection class in getSelection: %A" x          
           

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
    let insertAtCaretOrSelection (avaEdit:TextEditor, tx:string) = 
        
        
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



module RectangleSelection = 
    
    open Selection

    //all this functions is neded because ReplaceSelectionWithText of rectangular selection does nort work wel on all font sizes e.g. consolas 17.5

    
    let insert (text:string, s:Selection.SelPos, avaEdit:TextEditor, log:ISeffLog) =
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
            
            let spacesToAdd = minVisCol - ln.Length 
            if spacesToAdd > 0 then // in case this line is shorten than the visual colum with virtual white space
                doc.Insert(ln.EndOffset , new String(' ', spacesToAdd) )
                doc.Insert(ln.EndOffset + spacesToAdd , text)
            else
                doc.Insert(ln.Offset + minVisCol , text)
        
        doc.EndUpdate() 
        
        
        // move selection too
        let ncol = minVisCol + text.Length
        let st = shiftToVisCol ncol s.stp
        let en = shiftToVisCol ncol s.enp

        let mutable st = s.stp
        let mutable en = s.enp
        st.VisualColumn <-  minVisCol + text.Length
        en.VisualColumn <-  minVisCol + text.Length
        st.Column <- min (minVisCol + 1 + text.Length) st.Column
        en.Column <- min (minVisCol + 1 + text.Length) en.Column
        //log.PrintfnDebugMsg "new pos: \r\n%A \r\n%A" st en 
        avaEdit.TextArea.Selection <- new RectangleSelection(avaEdit.TextArea,st,en) 
        avaEdit.TextArea.Caret.VisualColumn <- minVisCol    

    let replace (text:string, s:Selection.SelPos, avaEdit:TextEditor, log:ISeffLog) =
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
            
            let spacesToAdd = minVisCol - ln.Length 
            if spacesToAdd > 0 then // in case this line is shorten than the visual colum with virtual white space
                doc.Insert(ln.EndOffset , new String(' ', spacesToAdd) )
                doc.Insert(ln.EndOffset + spacesToAdd , text)
            else
                doc.Insert(ln.Offset + minVisCol , text)
        
        doc.EndUpdate() // finsh doc update beforee recreating selecltion
        
        // collapse selection too
        let mutable st = s.stp
        let mutable en = s.enp
        st.VisualColumn <-  minVisCol + text.Length
        en.VisualColumn <-  minVisCol + text.Length
        st.Column <- min (minVisCol + 1 + text.Length) st.Column
        en.Column <- min (minVisCol + 1 + text.Length) en.Column
        //log.PrintfnDebugMsg "new pos: \r\n%A \r\n%A" st en 
        avaEdit.TextArea.Selection <- new RectangleSelection(avaEdit.TextArea,st,en) 
        avaEdit.TextArea.Caret.VisualColumn <- minVisCol    

    
    let delete (ed:IEditor, s:SelPos) = 
        let doc = ed.AvaEdit.Document
        //log.PrintfnDebugMsg "\r\nback: \r\n%A" s
        let minVisCol = s.stp.VisualColumn 
        let maxVisCol = s.enp.VisualColumn        
        let delLen    = maxVisCol - minVisCol

        doc.BeginUpdate()
        for li = s.enp.Line downto s.stp.Line do // move from bottom up
            let ln = doc.GetLineByNumber(li) 
            let delLenLoc =  min (ln.Length - minVisCol) delLen // in case if line is shorter than block selection
            //log.PrintfnDebugMsg "delLenLoc:%d, ln %d " delLenLoc li
            if delLenLoc > 0 then 
                doc.Remove(ln.Offset + minVisCol , delLenLoc)
        doc.EndUpdate() // finsh doc update before recreating selection
        
        // collapse selection too
        let st = shiftToVisCol minVisCol s.stp
        let en = shiftToVisCol minVisCol s.enp
        //log.PrintfnDebugMsg "new pos: \r\n%A \r\n%A" st en 
        ed.AvaEdit.TextArea.Selection <- new RectangleSelection(ed.AvaEdit.TextArea,st,en) 
        ed.AvaEdit.TextArea.Caret.VisualColumn <- minVisCol


    /// when pressing delete key on empty rect selection, delet on char on right      
    let deleteRight (avaEdit:TextEditor, s:SelPos) =
        let doc = avaEdit.Document
        let col = s.stp.VisualColumn 
        doc.BeginUpdate()
        for li = s.enp.Line downto s.stp.Line do // move from bottom up
            let ln = doc.GetLineByNumber(li) 
            if ln.Length - col > 0 then // in case if line is shorter than block selection
                doc.Remove(ln.Offset + col , 1)
        doc.EndUpdate() 

    let deleteLeft (avaEdit:TextEditor, s:SelPos) =
        let doc = avaEdit.Document
        let vcol = s.stp.VisualColumn
        let nvcol = vcol - 1
        if vcol > 0 then
            doc.BeginUpdate()
            for li = s.enp.Line downto s.stp.Line do // move from bottom up
                let ln = doc.GetLineByNumber(li) 
                if ln.Length - vcol >= 0 then// in case if line is shorter than block selection
                    doc.Remove(ln.Offset + nvcol , 1)
            doc.EndUpdate() // finsh doc update before recreating selection
    
            // move selection too
            let st = shiftToVisCol ncol s.stp
            let en = shiftToVisCol ncol s.enp
            avaEdit.TextArea.Selection <- new RectangleSelection(avaEdit.TextArea, st, en)             
            avaEdit.TextArea.Caret.VisualColumn <- nvcol
    
    
    let deleteKey (ed:IEditor) =  
        let s = makeTopDown ed.AvaEdit.TextArea.Selection
        if s.stp.VisualColumn = s.enp.VisualColumn then 
            deleteRight (ed.AvaEdit, s)
        else 
            delete (ed, s)
    
    let backspaceKey (ed:IEditor) = 
        let s = makeTopDown ed.AvaEdit.TextArea.Selection
        if s.stp.VisualColumn = s.enp.VisualColumn then 
            deleteLeft (ed.AvaEdit, s)
        else 
            delete (ed, s)
   
    
    let insertText (ed:IEditor, txt: string) = 
        let s = makeTopDown ed.AvaEdit.TextArea.Selection
        if s.stp.VisualColumn = s.enp.VisualColumn then 
            insert (ed, s, txt)
        else 
            replace (ed, s, txt)