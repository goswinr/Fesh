namespace Seff


open System
open ICSharpCode.AvalonEdit

module ModifyCode =

    //----------------------
    //-------- Commenting-----turning code into Comments and back
    //----------------------
    // 
    let comment(tab: FsxTab) =
        let doc = tab.Editor.Document
        let start = doc.GetLineByOffset(tab.Editor.SelectionStart)
        let endeNext = doc.GetLineByOffset(tab.Editor.SelectionStart + tab.Editor.SelectionLength).NextLine            
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
        doc.BeginUpdate() //tab.Editor.Document.RunUpdate
        comm start 0 0
        doc.EndUpdate()
        FsService.textChanged (FsService.OtherChange, tab) // TODO needed ?        
        
    let unComment(tab: FsxTab) =
        let doc = tab.Editor.Document
        let start = doc.GetLineByOffset(tab.Editor.SelectionStart)
        let endeNext = doc.GetLineByOffset(tab.Editor.SelectionStart + tab.Editor.SelectionLength).NextLine            
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
        doc.BeginUpdate()//tab.Editor.Document.RunUpdate
        ucomm start 
        doc.EndUpdate()
        FsService.textChanged (FsService.OtherChange, tab)// needed ?

 