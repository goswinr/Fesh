namespace Seff.Editor

open System
open ICSharpCode.AvalonEdit

module Commenting =

    //----------------------
    //-------- Commenting---  turning code into Comments and back
    //----------------------
    
    let comment(avaEdit:TextEditor) =
        let doc = avaEdit.Document
        let start = doc.GetLineByOffset(avaEdit.SelectionStart)
        let endeNext = doc.GetLineByOffset(avaEdit.SelectionStart + avaEdit.SelectionLength).NextLine            
        let rec comm ln pos lineK= 
            if ln <> endeNext then 
                let dl = doc.GetText(ln)
                match Seq.tryFindIndex (fun c -> c <>' ')  dl with
                | None ->
                    comm ln.NextLine 0 lineK // do not comment empty lines
                | Some i -> 
                    let ii = if lineK = 0 || i < pos then i else pos // use the indent on first line for position of comments markers unles line is more inside
                    doc.Insert(ln.Offset + ii, "//")
                    comm ln.NextLine ii (lineK+1)           
        doc.BeginUpdate() //avaEdit.Document.RunUpdate
        comm start 0 0
        doc.EndUpdate()
        //FsService.textChanged (FsService.OtherChange, tab) // TODO needed ?        
        
    let unComment(avaEdit:TextEditor) =
        let doc = avaEdit.Document
        let start = doc.GetLineByOffset(avaEdit.SelectionStart)
        let endeNext = doc.GetLineByOffset(avaEdit.SelectionStart + avaEdit.SelectionLength).NextLine            
        let rec ucomm ln = 
            if ln <> endeNext then 
                let dl = doc.GetText(ln)                    
                match Seq.tryFindIndex (fun c -> c <>' ')  dl with
                | None -> ucomm ln.NextLine // do not comment empty lines
                | Some i -> 
                    if  dl.Length > i && // ther must be 2 chars min.
                        dl.[i]  ='/' &&
                        dl.[i+1]='/' then doc.Remove(ln.Offset + i , 2)                        
                    ucomm ln.NextLine            
        doc.BeginUpdate()//avaEdit.Document.RunUpdate
        ucomm start 
        doc.EndUpdate()
        //FsService.textChanged (FsService.OtherChange, tab)// needed ?

module Selection =
    
    let expandSelectionToFullLines(avaEdit:TextEditor) =
        let doc = avaEdit.Document
        let st = doc.GetLineByOffset(avaEdit.SelectionStart)
        let en = doc.GetLineByOffset(avaEdit.SelectionStart + avaEdit.SelectionLength)
        let stoff = st.Offset
        avaEdit.Select(stoff,en.EndOffset-stoff)
        avaEdit.SelectedText

    let linesTillCursor(avaEdit:TextEditor) =
        let doc = avaEdit.Document        
        let en = doc.GetLineByOffset(avaEdit.SelectionStart + avaEdit.SelectionLength)
        avaEdit.Select(0,en.EndOffset)
        avaEdit.SelectedText

    let linesFromCursor(avaEdit:TextEditor) =
        let doc = avaEdit.Document
        let st = doc.GetLineByOffset(avaEdit.SelectionStart)        
        avaEdit.Select(st.Offset,avaEdit.Document.TextLength-1)
        avaEdit.SelectedText

    let selectAll(avaEdit:TextEditor) = 
        let doc = avaEdit.Document
        avaEdit.Select(0,doc.TextLength)
 