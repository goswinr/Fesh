namespace Seff.Editor

open System
open System.Text

open Seff.Model
open Seff.Util


module Formating = 
    
    let findChar (c:Char) (fromIndex:int) (sb:StringBuilder) = 
        let rec find i = 
            if i >= sb.Length then -1
            else 
                if sb.[i] = c then i 
                else find ( i+1)
        find fromIndex

    /// align code by any of these characters
    let isAlignmentChar (c:char) = 
        c <>'.' // skip dots
        && (Char.IsPunctuation c 
         || Char.IsSymbol      c ) 


    let alignByNonLetters(ed:IEditor) = 
        let s = Selection.getSelectionOrdered(ed.AvaEdit.TextArea)
        if s.enp.Line > s.stp.Line then 
            let doc = ed.AvaEdit.Document
            let stOff = doc.GetLineByNumber(s.stp.Line).Offset
            let enOff = doc.GetLineByNumber(s.enp.Line).EndOffset
            let lns =   [| for i = s.stp.Line to s.enp.Line do  yield doc.GetText(doc.GetLineByNumber(i)) |]
            let spChars = 
                lns 
                |> Array.map ( fun s -> s.ToCharArray() |> Array.filter isAlignmentChar) // get special chars only, but not dot '.'
                |> Array.maxBy Array.length
            
            let sbs = lns |> Array.map ( fun ln -> StringBuilder(ln))

            let mutable serachFrom = 0 

            for sc in spChars do 
                let offs = sbs |> Array.map ( findChar sc serachFrom)
                let maxOff = Array.max offs
                //ed.Log.PrintfnDebugMsg "Char: '%c' at maxOff: %d" sc maxOff                
                for i,sb in Seq.indexed sbs do 
                    //ed.Log.PrintfIOErrorMsg "Ln:%d" (i+s.stp.Line)
                    let foundPos = findChar sc serachFrom sb
                    let diff = maxOff - foundPos
                    if diff>0 && foundPos>0 then 
                        //ed.Log.PrintfnAppErrorMsg " insert:%d spaces at max (from %d ,  pos %d)" diff from p
                        sb.Insert(max serachFrom foundPos,String(' ', diff)) |> ignoreObj 
                    //else ed.Log.PrintfnFsiErrorMsg " NO insert:%d spaces at max (from %d ,  pos %d)" diff from p

                serachFrom <- maxOff

            sbs
            |> Array.map ( fun sb -> sb.ToString())
            |> String.concat "\r\n"
            |> fun t -> doc.Replace(stOff,enOff-stOff, t)


                    
                
                