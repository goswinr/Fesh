namespace Seff.Editor

open AvalonEditB
open Seff.Util

/// Commenting:  turning code into Comments and back
module Commenting = 

    let comment(avaEdit:TextEditor) = 
        let doc = avaEdit.Document
        let start = doc.GetLineByOffset(avaEdit.SelectionStart)
        let endeNext = doc.GetLineByOffset(avaEdit.SelectionStart + avaEdit.SelectionLength).NextLine

        let rec findIndent ln minInd= 
            if ln <> endeNext then
                let dl = doc.GetText(ln).TrimEnd()
                match Seq.tryFindIndex (fun c -> c <>' ')  dl with
                | None ->
                    findIndent ln.NextLine minInd // do not comment empty lines
                | Some i ->
                    findIndent ln.NextLine (min minInd i)
            else
                minInd
        let indent = findIndent start 99

        let rec comm ln = 
            if ln <> endeNext then
                let dl = doc.GetText(ln)
                match Seq.tryFindIndex (fun c -> c <>' ')  dl with
                | None ->
                    comm ln.NextLine // do not comment empty lines
                | Some i ->
                    doc.Insert(ln.Offset + indent, "//")
                    comm ln.NextLine
        doc.BeginUpdate() 
        comm start
        doc.EndUpdate()
        

    let unComment(avaEdit:TextEditor) = 
        let doc = avaEdit.Document
        let start = doc.GetLineByOffset(avaEdit.SelectionStart)
        let endeNext = doc.GetLineByOffset(avaEdit.SelectionStart + avaEdit.SelectionLength).NextLine
        let rec ucomm ln = 
            if ln <> endeNext then
                let dl = doc.GetText(ln).TrimEnd()
                match Seq.tryFindIndex (fun c -> c <>' ')  dl with
                | None -> ucomm ln.NextLine // do not comment empty lines
                | Some i ->
                    if  dl.Length > i && // there must be 2 chars min.
                        dl.[i]  ='/' &&
                        dl.[i+1]='/' then doc.Remove(ln.Offset + i , 2)
                    ucomm ln.NextLine
        doc.BeginUpdate()
        ucomm start
        doc.EndUpdate()
        

    let toggleComment(avaEdit:TextEditor) = 
        let doc = avaEdit.Document
        let start = doc.GetLineByOffset(avaEdit.SelectionStart)
        let endeNext = doc.GetLineByOffset(avaEdit.SelectionStart + avaEdit.SelectionLength).NextLine
        let rec toggle ln = 
            if ln <> endeNext then
                let dl = doc.GetText(ln).TrimEnd()
                match Seq.tryFindIndex (fun c -> c <>' ')  dl with
                | None -> toggle ln.NextLine // do not comment empty lines
                | Some i ->
                    if  dl.Length > i && // there must be 2 chars min.
                        dl.[i]  ='/' &&
                        dl.[i+1]='/' then doc.Remove(ln.Offset + i , 2)
                    else
                        let indent = Str.spacesAtStart dl
                        doc.Insert(ln.Offset + indent, "//")
                    toggle ln.NextLine
        doc.BeginUpdate()
        toggle start
        doc.EndUpdate()
        
