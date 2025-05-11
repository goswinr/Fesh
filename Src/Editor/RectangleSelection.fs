namespace Fesh.Editor

open System
open System.Text

open AvaloniaEdit
open AvaloniaEdit.Editing
open AvaloniaEdit.Document

module RectangleSelection =

    open Selection

    //all this functions is needed because ReplaceSelectionWithText of rectangular selection does not work well on all font sizes e.g. consolas 17.5

    let private setNewEmpty (ta:TextArea, s:SelectionPos, vcol, checkWithColInSelpos) =
        //IFeshLog.log.PrintfnDebugMsg "caret2: %A "ta.Caret.Position
        let st , en =
            if checkWithColInSelpos then
                TextViewPosition( s.stPos.Line,  min (vcol + 1) s.stPos.Column , vcol) , // use min function in case the  Visual column is in virtual whitespace
                TextViewPosition( s.enPos.Line,  min (vcol + 1) s.enPos.Column , vcol)
            else
                TextViewPosition( s.stPos.Line,  vcol + 1   , vcol), // even if the Visual column was in virtual whitespace, spaces to fill it where added, if len is bigger than 0
                TextViewPosition( s.enPos.Line,  vcol + 1   , vcol)
        ta.Selection <- new RectangleSelection(ta, st, en)
        if s.caret.Line = s.stPos.Line then
            ta.Caret.Position <- st
        else
            ta.Caret.Position <- en
        //IFeshLog.log.PrintfnDebugMsg "caret3: %A "ta.Caret.Position


    let private insert (ed:TextEditor, s:SelectionPos,text:string) =
        let doc = ed.Document
        let visCol = s.stPos.VisualColumn
        doc.BeginUpdate()
        if s.LineCount > 1 then
            let stOff = doc.GetLineByNumber(s.stPos.Line).Offset
            let enOff = doc.GetLineByNumber(s.enPos.Line-1).EndOffset
            let len = enOff-stOff
            let txt = doc.GetText(stOff, len)
            let sb = StringBuilder()
            let rec loop i pos =
                if i<len then
                    let c = txt.[i]
                    if pos = visCol then
                        sb.Append(text) |> ignore

                    if c = '\r' then
                        if pos < visCol then //position is in virtual space
                            sb.Append(String(' ',visCol-pos)) |> ignore// fill whitespace
                            sb.Append(text) |> ignore
                        sb.Append(Environment.NewLine) |> ignore
                        loop (i+2) 0
                    else
                        sb.Append(c) |> ignore
                        loop (i+1) (pos+1)
                else
                    if pos = visCol then
                        sb.Append(text)|> ignore // so it works after last character too
                    elif pos < visCol then
                        sb.Append(String(' ',visCol-pos)) |> ignore// fill whitespace
                        sb.Append(text)|> ignore // so it works after last character too
            loop 0 0
            let nt = sb.ToString()
            doc.Replace(stOff,len,nt)

        //do last line individual to trigger potential autocompletion:
        let ln = doc.GetLineByNumber(s.enPos.Line)
        let len = ln.Length
        let spacesToAdd = visCol - len
        let stOff = ln.Offset
        if spacesToAdd > 0 then // in case this line is shorten than the visual column with virtual white space
            doc.Insert(stOff + len , new String(' ', spacesToAdd) )
            doc.Insert(stOff + len + spacesToAdd , text)
        else
            doc.Insert(stOff + visCol , text)
        doc.EndUpdate()
        setNewEmpty (ed.TextArea, s, visCol + text.Length, false)

    let private replace (ta:TextArea, s:SelectionPos, text:string)  =
        let doc = ta.Document
        let minVisCol = s.stPos.VisualColumn
        let maxVisCol = s.enPos.VisualColumn
        doc.BeginUpdate()
        if s.LineCount > 1 then
            let stOff = doc.GetLineByNumber(s.stPos.Line).Offset
            let enOff = doc.GetLineByNumber(s.enPos.Line-1).EndOffset //do last line individually to trigger potential autocompletion:
            let len = enOff-stOff
            let txt = doc.GetText(stOff, len)
            let sb = StringBuilder()
            let rec loop i pos =
                if i<len then
                    let c = txt.[i]
                    if pos = minVisCol then
                        sb.Append(text) |> ignore

                    if c = '\r' then
                        if pos < minVisCol then //position is in virtual space
                            sb.Append(String(' ',minVisCol-pos)) |> ignore// fill whitespace
                            sb.Append(text) |> ignore
                        sb.Append(Environment.NewLine) |> ignore
                        loop (i+2) 0
                    else
                        if  pos < minVisCol || pos >= maxVisCol then // to delete
                            sb.Append(c) |> ignore
                        loop (i+1) (pos+1)
                else
                    if pos = minVisCol then
                        sb.Append(text)|> ignore // so it works after last character too
                    elif pos < minVisCol then
                        sb.Append(String(' ',minVisCol-pos)) |> ignore// fill whitespace
                        sb.Append(text)|> ignore // so it works after last character too
            loop 0 0
            let nt = sb.ToString()
            doc.Replace(stOff,len,nt)

        //do last line individual to trigger potential autocompletion:
        let ln = doc.GetLineByNumber(s.enPos.Line)
        let delLen    = maxVisCol - minVisCol
        let len = ln.Length
        let delLenLoc =  min (len - minVisCol) delLen // in case if line is shorter than block selection
        let stOff = ln.Offset
        if delLenLoc > 0 then
            doc.Remove(stOff + minVisCol , delLenLoc)

        let spacesToAdd = minVisCol - len
        if spacesToAdd > 0 then // in case this line is shorten than the visual column with virtual white space
            doc.Insert(stOff + len                , new String(' ', spacesToAdd) )
            doc.Insert(stOff + len + spacesToAdd  , text)
        else
            doc.Insert(stOff + minVisCol , text)
        doc.EndUpdate() // finish doc update before recreating selection
        setNewEmpty (ta, s, minVisCol + text.Length, false)


    let private delete (ed:TextEditor, s:SelectionPos) =
        let doc = ed.Document
        let minVisCol = s.stPos.VisualColumn
        let maxVisCol = s.enPos.VisualColumn
        let stOff = doc.GetLineByNumber(s.stPos.Line).Offset
        let enOff = doc.GetLineByNumber(s.enPos.Line-1).EndOffset // -1 to do last line individual to trigger potential autocompletion:
        doc.BeginUpdate()
        if s.LineCount > 1 then
            let len = enOff-stOff
            let txt = doc.GetText(stOff, len)
            let sb = StringBuilder()
            let rec loop i pos =
                if i<len then
                    let c = txt.[i]
                    if c = '\r' then
                        sb.Append(Environment.NewLine) |> ignore
                        loop (i+2) 0
                    else
                        if  pos < minVisCol || pos >= maxVisCol then // to delete
                            sb.Append(c) |> ignore
                        loop (i+1) (pos+1)
            loop 0 0
            let nt = sb.ToString()
            doc.Replace(stOff,len,nt)

        //do last line individual to trigger potential autocompletion:
        let delLen    = maxVisCol - minVisCol
        let ln = doc.GetLineByNumber(s.enPos.Line)
        let delLenLoc =  min (ln.Length - minVisCol) delLen // in case if line is shorter than block selection
        if delLenLoc > 0 then
            doc.Remove(ln.Offset + minVisCol , delLenLoc)
        doc.EndUpdate()
        setNewEmpty (ed.TextArea, s, minVisCol,true)


    /// when pressing delete key on empty rect selection, delete on char on right
    let private deleteRight (ed:TextEditor, s:SelectionPos) =
        let doc = ed.Document
        let col = s.stPos.VisualColumn
        let stOff = doc.GetLineByNumber(s.stPos.Line).Offset
        let enOff = doc.GetLineByNumber(s.enPos.Line-1).EndOffset // -1 to do last line individual to trigger potential autocompletion:
        doc.BeginUpdate()
        if s.LineCount > 1 then
            let len = enOff-stOff
            let txt = doc.GetText(stOff, len)
            let sb = StringBuilder()
            let rec loop i pos =
                if i<len then
                    let c = txt.[i]
                    if c = '\r' then
                        sb.Append(Environment.NewLine) |> ignore
                        loop (i+2) 0
                    else
                        if  pos < col || pos > col then // to delete
                            sb.Append(c) |> ignore
                        loop (i+1) (pos+1)
            loop 0 0
            let nt = sb.ToString()
            doc.Replace(stOff,len,nt)
        //do last line individual to trigger potential autocompletion:
        let ln = doc.GetLineByNumber(s.enPos.Line)
        if ln.Length - col > 0 then // in case if line is shorter than block selection
            doc.Remove(ln.Offset + col , 1)
        doc.EndUpdate()
        setNewEmpty (ed.TextArea, s, col,true)// needed in manual version


    let private deleteLeft (ed:TextEditor, s:SelectionPos) =
        let doc = ed.Document
        let vcol = s.stPos.VisualColumn
        let nvcol = vcol - 1
        if vcol > 0 then
            let stOff = doc.GetLineByNumber(s.stPos.Line).Offset
            let enOff = doc.GetLineByNumber(s.enPos.Line-1).EndOffset // -1 to do last line individual to trigger potential autocompletion:
            doc.BeginUpdate()
            if s.LineCount > 1 then
                let len = enOff-stOff
                let txt = doc.GetText(stOff, len)
                let sb = StringBuilder()
                let rec loop i pos =
                    if i<len then
                        let c = txt.[i]
                        if c = '\r' then
                            sb.Append(Environment.NewLine) |> ignore
                            loop (i+2) 0
                        else
                            if  pos < nvcol || pos > nvcol then // to delete
                                sb.Append(c) |> ignore
                            loop (i+1) (pos+1)
                loop 0 0
                let nt = sb.ToString()
                doc.Replace(stOff,len,nt)
            //do last line individual to trigger potential autocompletion:
            let ln = doc.GetLineByNumber(s.enPos.Line)
            if ln.Length - vcol >= 0 then// in case if line is shorter than block selection
                doc.Remove(ln.Offset + nvcol , 1)
            doc.EndUpdate()
            setNewEmpty (ed.TextArea, s, nvcol, true)


    /// when block selection is pasted into block selection do line by line
    let private pasteLineByLine (ed:TextEditor, fullText:string) =
        let lines = fullText.Split('\n') |> Array.map ( fun t -> t.Replace("\r",""))
        let ta = ed.TextArea
        let doc = ed.Document
        doc.BeginUpdate()
        for i,seg in ta.Selection.Segments |> Seq.indexed |> Seq.rev do // do from bottom up so tht the segments are always correct, otherwise they would need incrementing too
            let newTxt = if lines.Length > i then lines.[i] else ""
            doc.Replace(seg,newTxt)
        doc.EndUpdate()


    //TODO add checks for being over folded block !

    let deleteKey (ed:TextEditor) =
        let s = getSelectionOrdered ed.TextArea
        if s.stPos.VisualColumn = s.enPos.VisualColumn then
            deleteRight (ed, s)
        else
            delete (ed, s)

    let backspaceKey (ed:TextEditor) =
        let s = getSelectionOrdered ed.TextArea
        if s.stPos.VisualColumn = s.enPos.VisualColumn then
            deleteLeft (ed, s)
        else
            delete (ed, s)

    /// The replacement for the OnTextInput handler on TextArea of AvaloniaEdit
    let insertText (ed:TextEditor, txt: string) =
        match txt with
        | null | "" | "\x1b" | "\b" -> ()  // see avalonedit source OnTextInput event handler on Text Area
        // ASCII 0x1b = ESC.
        // also see TextArea.OnTextInput implementation
        // WPF produces a TextInput event with that old ASCII control char
        // when Escape is pressed. We'll just ignore it.
        // A deadkey followed by backspace causes a textinput event for the BS character.
        // Similarly, some shortcuts like Alt+Space produce an empty TextInput event.
        // We have to ignore those (not handle them) to keep the shortcut working.

        | _ ->
            let s = getSelectionOrdered ed.TextArea
            if s.stPos.VisualColumn = s.enPos.VisualColumn then
                insert (ed, s, txt)
            else
                replace (ed.TextArea, s, txt)

    let paste(ed:TextEditor, txt: string, txtIsFromOtherRectSel:bool)=
        if not txtIsFromOtherRectSel then
            insertText (ed, txt)
        else
            if not <| txt.Contains("\n") && not <| txt.Contains("\r") then // TODO maybe only do line by line paste if the lines count in selection and text to paste is same or similar ??
                insertText (ed, txt)
            else
                pasteLineByLine (ed, txt)

    let complete (ta:TextArea, completionSegment:ISegment, txt:string) =
        let len = completionSegment.Length
        let s = getSelectionOrdered ta
        let p = {s with stPos = TextViewPosition( s.stPos.Line,  s.stPos.Column - len , s.stPos.VisualColumn - len) }
        replace (ta, p, txt)


