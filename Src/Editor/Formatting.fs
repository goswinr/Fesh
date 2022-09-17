namespace Seff.Editor

open System
open System.Text



open Seff.Model
open Seff.Util

module Formatting = 

    let findCharOld (c:Char) (fromIndex:int) (sb:StringBuilder) = 
        let rec find i = 
            if i >= sb.Length then -1
            else
                if sb.[i] = c then i
                else find ( i+1)
        find fromIndex

    let findCharExcludeInStringLiterals (c:Char) (fromIndex:int) (sb:StringBuilder) = 
        let rec find inStr  i= 
            if i >= sb.Length then -1 // exit recursion
            else                
                if inStr then 
                    match sb.[i] with 
                    |'\\' ->  find true  (i+2) // escaped quote in string literal
                    |'"'  ->  find false (i+1) // end of string literal
                    | _   ->  find true (i+1)  // next char in string literal
                else
                    match sb.[i] with                     
                    |'"'  ->  find true (i+1) // start of string literal
                    | ci  when ci = c ->  i // found
                    | _   ->  find false (i+1)  // next char (NOT in string literal)
        find false fromIndex



    /// align code by any of these characters
    let isAlignmentChar (c:char) = 
        //c <>'.' // skip dots
        //&& (    Char.IsPunctuation c
        //     || Char.IsSymbol      c )
        match c with 
        | '=' | ',' | ';' | '(' | ')' | '[' | ']' | '{' | '}' -> true
        | _ -> false      
        

    let getAlignmentCharsExcludeInStringLiterals(ln:string) =
        let res = ResizeArray()
        let rec find inStr  i= 
            if i >= ln.Length then ()// exit recursion
            else                
                if inStr then 
                    match ln.[i] with 
                    |'\\' ->  find true  (i+2) // escaped quote in string literal
                    |'"'  ->  find false (i+1) // end of string literal
                    | _   ->  find true (i+1)  // next char in string literal
                else
                    match ln.[i] with                     
                    |'"'  ->  find true (i+1) // start of string literal
                    | ci  when isAlignmentChar ci ->  res.Add ci // found
                    | _   ->  find false (i+1)  // next char (NOT in string literal)
        find false 0
        res


    let alignByNonLetters(ed:IEditor) = 
        let s = Selection.getSelectionOrdered(ed.AvaEdit.TextArea)
        if s.enPos.Line > s.stPos.Line then
            let doc = ed.AvaEdit.Document
            let stOff = doc.GetLineByNumber(s.stPos.Line).Offset
            let enOff = doc.GetLineByNumber(s.enPos.Line).EndOffset
            let lns =   [| for i = s.stPos.Line to s.enPos.Line do  yield doc.GetText(doc.GetLineByNumber(i)) |]
            let alignChars = 
                lns
                |> Array.map getAlignmentCharsExcludeInStringLiterals // get special chars 
                |> Array.maxBy ( fun rarr -> rarr.Count)

            let stringBuilders = lns |> Array.map ( fun ln -> StringBuilder(ln))

            let mutable serachFrom = 0

            for alignChr in alignChars do
                let offs = stringBuilders |> Array.map ( findCharExcludeInStringLiterals alignChr serachFrom)
                let maxOff = Array.max offs
                //ed.Log.PrintfnDebugMsg "Char: '%c' at maxOff: %d" sc maxOff
                for sb in stringBuilders do
                    //ed.Log.PrintfIOErrorMsg "Ln:%d" (i+s.stPos.Line)
                    let foundPos = findCharExcludeInStringLiterals alignChr serachFrom sb
                    let diff = maxOff - foundPos
                    if diff > 0 && foundPos > 0 then
                        //ed.Log.PrintfnAppErrorMsg " insert:%d spaces at max (from %d ,  pos %d)" diff from p
                        sb.Insert(max serachFrom foundPos,String(' ', diff)) |> ignore

                    //else ed.Log.PrintfnFsiErrorMsg " NO insert:%d spaces at max (from %d ,  pos %d)" diff from p

                serachFrom <- maxOff

            stringBuilders
            |> Seq.map ( fun sb -> sb.ToString())
            |> String.concat "\r\n"
            |> fun t -> doc.Replace(stOff,enOff-stOff, t)





