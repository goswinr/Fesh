namespace Seff.Editor

open System
open System.Text

open AvalonEditB
open AvalonEditB.Editing
open AvalonEditB.Document

open AvalonLog.Util

open Seff.Model


module RectangleSelection = 
    
    open Selection

    //all this functions is neded because ReplaceSelectionWithText of rectangular selection does nort work wel on all font sizes e.g. consolas 17.5
   
    let private setNewEmpty (ta:TextArea, s:SelPos, vcol, checkWithColInSelpos) = 
        //ISeffLog.log.PrintfnDebugMsg "caret2: %A "ta.Caret.Position
        let st , en = 
            if checkWithColInSelpos then 
                TextViewPosition( s.stp.Line,  min (vcol + 1) s.stp.Column , vcol) , // use min function in case the  Visual coloumn is in virtual whitespace
                TextViewPosition( s.enp.Line,  min (vcol + 1) s.enp.Column , vcol)
            else
                TextViewPosition( s.stp.Line,  vcol + 1   , vcol), // even if the Visual coloumn was in virtual whitespace, spaces to fill it where added, if len is bigger than 0
                TextViewPosition( s.enp.Line,  vcol + 1   , vcol)
        ta.Selection <- new RectangleSelection(ta, st, en)        
        if s.caret.Line = s.stp.Line then 
            ta.Caret.Position <- st
        else
            ta.Caret.Position <- en
        //ISeffLog.log.PrintfnDebugMsg "caret3: %A "ta.Caret.Position


    let private insert (ed:IEditor, s:SelPos,text:string) =
        let doc = ed.AvaEdit.Document        
        let visCol = s.stp.VisualColumn           
        doc.BeginUpdate()
        if s.LineCount > 1 then 
            let stOff = doc.GetLineByNumber(s.stp.Line).Offset
            let enOff = doc.GetLineByNumber(s.enp.Line-1).EndOffset
            let len = enOff-stOff
            let txt = doc.GetText(stOff, len)        
            let sb = StringBuilder()       
            let rec loop i pos = 
                if i<len then 
                    let c = txt.[i]
                    if pos = visCol then 
                        sb.Append(text) |> ignoreObj
                
                    if c = '\r' then
                        if pos < visCol then //position is in virtual space
                            sb.Append(String(' ',visCol-pos)) |> ignoreObj // fill whitesace
                            sb.Append(text) |> ignoreObj
                        sb.Append(Environment.NewLine) |> ignoreObj
                        loop (i+2) 0
                    else
                        sb.Append(c) |> ignoreObj 
                        loop (i+1) (pos+1)
                else
                    if pos = visCol then 
                        sb.Append(text)|> ignoreObj  // so it works after last caracter too
                    elif pos < visCol then 
                        sb.Append(String(' ',visCol-pos)) |> ignoreObj // fill whitesace
                        sb.Append(text)|> ignoreObj  // so it works after last caracter too
            loop 0 0
            let nt = sb.ToString()        
            doc.Replace(stOff,len,nt)  
        
        //do last line individual to trigger potential autocompletion:
        let ln = doc.GetLineByNumber(s.enp.Line)
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
      
    let private replace (ed:IEditor, s:SelPos, text:string)  =
        let doc = ed.AvaEdit.Document 
        let minVisCol = s.stp.VisualColumn 
        let maxVisCol = s.enp.VisualColumn             
        doc.BeginUpdate()
        if s.LineCount > 1 then 
            let stOff = doc.GetLineByNumber(s.stp.Line).Offset
            let enOff = doc.GetLineByNumber(s.enp.Line-1).EndOffset //do last line individualy to trigger potential autocompletion:
            let len = enOff-stOff
            let txt = doc.GetText(stOff, len)        
            let sb = StringBuilder()       
            let rec loop i pos = 
                if i<len then 
                    let c = txt.[i]
                    if pos = minVisCol then 
                        sb.Append(text) |> ignoreObj
                       
                    if c = '\r' then
                        if pos < minVisCol then //position is in virtual space
                            sb.Append(String(' ',minVisCol-pos)) |> ignoreObj // fill whitesace
                            sb.Append(text) |> ignoreObj
                        sb.Append(Environment.NewLine) |> ignoreObj
                        loop (i+2) 0
                    else
                        if  pos < minVisCol || pos >= maxVisCol then // to delete
                            sb.Append(c) |> ignoreObj 
                        loop (i+1) (pos+1)
                else
                    if pos = minVisCol then 
                        sb.Append(text)|> ignoreObj  // so it works after last caracter too
                    elif pos < minVisCol then 
                        sb.Append(String(' ',minVisCol-pos)) |> ignoreObj // fill whitesace
                        sb.Append(text)|> ignoreObj  // so it works after last caracter too
            loop 0 0
            let nt = sb.ToString()        
            doc.Replace(stOff,len,nt) 
        
        //do last line individual to trigger potential autocompletion:
        let ln = doc.GetLineByNumber(s.enp.Line)
        let delLen    = maxVisCol - minVisCol
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

      
    let private delete (ed:IEditor, s:SelPos) = 
        let doc = ed.AvaEdit.Document        
        let minVisCol = s.stp.VisualColumn 
        let maxVisCol = s.enp.VisualColumn 
        let stOff = doc.GetLineByNumber(s.stp.Line).Offset
        let enOff = doc.GetLineByNumber(s.enp.Line-1).EndOffset // -1 to do last line individual to trigger potential autocompletion:
        doc.BeginUpdate()  
        if s.LineCount > 1 then
            let len = enOff-stOff
            let txt = doc.GetText(stOff, len)        
            let sb = StringBuilder()       
            let rec loop i pos = 
                if i<len then 
                    let c = txt.[i]                              
                    if c = '\r' then                    
                        sb.Append(Environment.NewLine) |> ignoreObj
                        loop (i+2) 0
                    else
                        if  pos < minVisCol || pos >= maxVisCol then // to delete
                            sb.Append(c) |> ignoreObj 
                        loop (i+1) (pos+1)            
            loop 0 0
            let nt = sb.ToString()        
            doc.Replace(stOff,len,nt)
        
        //do last line individual to trigger potential autocompletion:
        let delLen    = maxVisCol - minVisCol
        let ln = doc.GetLineByNumber(s.enp.Line) 
        let delLenLoc =  min (ln.Length - minVisCol) delLen // in case if line is shorter than block selection            
        if delLenLoc > 0 then 
            doc.Remove(ln.Offset + minVisCol , delLenLoc)        
        doc.EndUpdate()   
        setNewEmpty (ed.AvaEdit.TextArea, s, minVisCol,true)


    /// when pressing delete key on empty rect selection, delet on char on right      
    let private deleteRight (ed:IEditor, s:SelPos) =
        let doc = ed.AvaEdit.Document
        let col = s.stp.VisualColumn 
        let stOff = doc.GetLineByNumber(s.stp.Line).Offset
        let enOff = doc.GetLineByNumber(s.enp.Line-1).EndOffset // -1 to do last line individual to trigger potential autocompletion: 
        doc.BeginUpdate()
        if s.LineCount > 1 then            
            let len = enOff-stOff
            let txt = doc.GetText(stOff, len)        
            let sb = StringBuilder()       
            let rec loop i pos = 
                if i<len then 
                    let c = txt.[i]                              
                    if c = '\r' then                    
                        sb.Append(Environment.NewLine) |> ignoreObj
                        loop (i+2) 0
                    else
                        if  pos < col || pos > col then // to delete
                            sb.Append(c) |> ignoreObj 
                        loop (i+1) (pos+1)            
            loop 0 0
            let nt = sb.ToString()           
            doc.Replace(stOff,len,nt) 
        //do last line individual to trigger potential autocompletion:
        let ln = doc.GetLineByNumber(s.enp.Line) 
        if ln.Length - col > 0 then // in case if line is shorter than block selection
            doc.Remove(ln.Offset + col , 1) 
        doc.EndUpdate() 
        setNewEmpty (ed.AvaEdit.TextArea, s, col,true)// neede in manual version



    let private deleteLeft (ed:IEditor, s:SelPos) =        
        let doc = ed.AvaEdit.Document
        let vcol = s.stp.VisualColumn
        let nvcol = vcol - 1
        if vcol > 0 then           
            let stOff = doc.GetLineByNumber(s.stp.Line).Offset
            let enOff = doc.GetLineByNumber(s.enp.Line-1).EndOffset // -1 to do last line individual to trigger potential autocompletion:                 
            doc.BeginUpdate()
            if s.LineCount > 1 then
                let len = enOff-stOff
                let txt = doc.GetText(stOff, len)        
                let sb = StringBuilder()       
                let rec loop i pos = 
                    if i<len then 
                        let c = txt.[i]                              
                        if c = '\r' then                    
                            sb.Append(Environment.NewLine) |> ignoreObj
                            loop (i+2) 0
                        else
                            if  pos < nvcol || pos > nvcol then // to delete
                                sb.Append(c) |> ignoreObj 
                            loop (i+1) (pos+1)            
                loop 0 0
                let nt = sb.ToString()                  
                doc.Replace(stOff,len,nt)  
            //do last line individual to trigger potential autocompletion:
            let ln = doc.GetLineByNumber(s.enp.Line) 
            if ln.Length - vcol >= 0 then// in case if line is shorter than block selection
                doc.Remove(ln.Offset + nvcol , 1)
            doc.EndUpdate() 
            setNewEmpty (ed.AvaEdit.TextArea, s, nvcol, true)
                
    
    /// when block selection is pasted into block selection do line by line 
    let private pasteLinebyLine (ed:IEditor, fullText:string) =  
        let lines = fullText.Split('\n') |> Array.map ( fun t -> t.Replace("\r",""))
        let ta = ed.AvaEdit.TextArea
        let doc = ed.AvaEdit.Document
        doc.BeginUpdate()
        for i,seg in ta.Selection.Segments |> Seq.indexed |> Seq.rev do // do from bottom up so tht the segments are alwas correct, otherwise they would need incrementing too
            let newTxt = if lines.Length > i then lines.[i] else ""
            doc.Replace(seg,newTxt)
        doc.EndUpdate() 


    let expandDown(ed:IEditor) =() //TODO
    
    let expandUp(ed:IEditor) =() //TODO        


    //TODO add check for beeing over folded block

    let deleteKey (ed:IEditor) =  
        let s = getSelectionOrdered ed.AvaEdit.TextArea
        if s.stp.VisualColumn = s.enp.VisualColumn then 
            deleteRight (ed, s)
        else 
            delete (ed, s)
    
    let backspaceKey (ed:IEditor) = 
        let s = getSelectionOrdered ed.AvaEdit.TextArea
        if s.stp.VisualColumn = s.enp.VisualColumn then 
            deleteLeft (ed, s)
        else 
            delete (ed, s)
   
    
    let insertText (ed:IEditor, txt: string) = 
        match txt with 
        | null | "" | "\x1b" | "\b" -> ()  // see avalonedit scource
        // ASCII 0x1b = ESC. 
        // also see TextArea.OnTextInput implementation 
        // WPF produces a TextInput event with that old ASCII control char
        // when Escape is pressed. We'll just ignore it.
        // A deadkey followed by backspace causes a textinput event for the BS character.
        // Similarly, some shortcuts like Alt+Space produce an empty TextInput event.
        // We have to ignore those (not handle them) to keep the shortcut working.   
        
        | _ -> 
            let s = getSelectionOrdered ed.AvaEdit.TextArea
            if s.stp.VisualColumn = s.enp.VisualColumn then 
                insert (ed, s, txt)
            else             
                replace (ed, s, txt)
    
    let paste(ed:IEditor, txt: string, txtIsFromOtherRectSel:bool)=
        if not txtIsFromOtherRectSel then 
            insertText (ed, txt)
        else
            if not <| txt.Contains("\n") && not <| txt.Contains("\r") then // TODO maybe only do line by line paste if the lines count in selection and text to paste is same or similar ??
                insertText (ed, txt)  
            else
                pasteLinebyLine (ed, txt) 

    let complete (ed:IEditor, completionSegment:ISegment, txt:string) =
        let len = completionSegment.Length
        let s = getSelectionOrdered ed.AvaEdit.TextArea
        let p = {s with stp = TextViewPosition( s.stp.Line,  s.stp.Column - len , s.stp.VisualColumn - len) }
        replace (ed, p, txt)


