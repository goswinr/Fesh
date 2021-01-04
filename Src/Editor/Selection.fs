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
    

    /// a DU represneting all possible kinds of the current selection
    type Sel = 
        |NoSel
        |RegSel      
        |RectSel 

    let getSelType (ta:TextArea) = 
        match ta.Selection with               
        | null -> failwithf "Unknown selection class in getSelection: null"
        | :? EmptySelection   -> NoSel
        | :? SimpleSelection  -> RegSel
        | :? RectangleSelection -> RectSel
        | x -> failwithf "Unknown selection class in getSelection: %A" x          
    
    /// returns selpos order top to left bottom right
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
            let v1,v2 = sorted s.VisualColumn e.VisualColumn
            if s.Line <= e.Line then               
               {stp = TextViewPosition(s.Line, s.Column, v1)  
                enp = TextViewPosition(e.Line, e.Column, v2) }
            else
               {stp = TextViewPosition(e.Line, e.Column, v1) 
                enp = TextViewPosition(s.Line, s.Column, v2) }

               
        | x -> failwithf "Unknown selection class in makeTopDown: %A" x
     
           

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
   
    let setNewEmpty (ta:TextArea, s:SelPos, vcol, checkWithColInSelpos) = 
        let st , en = 
            if checkWithColInSelpos then 
                TextViewPosition( s.stp.Line,  min (vcol + 1) s.stp.Column , vcol) , // use min function in case the  Visual coloumn is in virtual whitespace
                TextViewPosition( s.enp.Line,  min (vcol + 1) s.enp.Column , vcol)
            else
                TextViewPosition( s.stp.Line,  vcol + 1   , vcol), // even if the Visual coloumn was in virtual whitespace, spaces to fill it wher added if len is bigger than 0
                TextViewPosition( s.enp.Line,  vcol + 1   , vcol)
        ta.Selection <- new RectangleSelection(ta, st, en)             
        ta.Caret.VisualColumn <- vcol

    
    let insert (ed:IEditor, s:SelPos,text:string) =
        let doc = ed.AvaEdit.Document        
        let visCol = s.stp.VisualColumn 
        doc.BeginUpdate()
        for li = s.enp.Line downto s.stp.Line do // move from bottom up
            let ln = doc.GetLineByNumber(li)
            let len = ln.Length
            let spacesToAdd = visCol - len
            let stOff = ln.Offset
            if spacesToAdd > 0 then // in case this line is shorten than the visual colum with virtual white space                
                doc.Insert(stOff + len , new String(' ', spacesToAdd) )
                doc.Insert(stOff + len + spacesToAdd , text)
            else
                doc.Insert(stOff + visCol , text)        
        doc.EndUpdate()         
        setNewEmpty (ed.AvaEdit.TextArea, s, visCol + text.Length, false)
       

    let replace (ed:IEditor, s:SelPos,text:string)  =
        let doc = ed.AvaEdit.Document 
        let minVisCol = s.stp.VisualColumn 
        let maxVisCol = s.enp.VisualColumn        
        let delLen    = maxVisCol - minVisCol
        
        doc.BeginUpdate()
        for li = s.enp.Line downto s.stp.Line do // move from bottom up
            let ln = doc.GetLineByNumber(li)
            let len = ln.Length
            let delLenLoc =  min (len - minVisCol) delLen // in case if line is shorter than block selection
            let stOff = ln.Offset            
            if delLenLoc > 0 then 
                doc.Remove(stOff + minVisCol , delLenLoc)
            
            let spacesToAdd = minVisCol - len
            if spacesToAdd > 0 then // in case this line is shorten than the visual colum with virtual white space
                doc.Insert(stOff + len                , new String(' ', spacesToAdd) )
                doc.Insert(stOff + len + spacesToAdd  , text)
            else
                doc.Insert(stOff + minVisCol , text)        
        doc.EndUpdate() // finsh doc update beforee recreating selecltion
        setNewEmpty (ed.AvaEdit.TextArea, s, minVisCol + text.Length, false)

    
    let delete (ed:IEditor, s:SelPos) = 
        let doc = ed.AvaEdit.Document        
        let minVisCol = s.stp.VisualColumn 
        let maxVisCol = s.enp.VisualColumn        
        let delLen    = maxVisCol - minVisCol

        doc.BeginUpdate()
        for li = s.enp.Line downto s.stp.Line do // move from bottom up
            let ln = doc.GetLineByNumber(li) 
            let delLenLoc =  min (ln.Length - minVisCol) delLen // in case if line is shorter than block selection            
            if delLenLoc > 0 then 
                doc.Remove(ln.Offset + minVisCol , delLenLoc)
        doc.EndUpdate() // finsh doc update before recreating selection        
        setNewEmpty (ed.AvaEdit.TextArea, s, minVisCol,true)


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
            doc.EndUpdate()          
            setNewEmpty (avaEdit.TextArea, s, nvcol,true)
                
    
    //TODO add check for beeing over folded block

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

    let complete (ed:IEditor, completionSegment:ISegment, txt:string) =
        let len = completionSegment.Length
        let s = makeTopDown ed.AvaEdit.TextArea.Selection
        let p = {s with stp = TextViewPosition( s.stp.Line,  s.stp.Column - len , s.stp.VisualColumn - len) }
        replace (ed, p, txt)