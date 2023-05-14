namespace Seff.Editor

open System
open AvalonEditB
open AvalonEditB.Rendering
open Seff.Model



type ChangeReason = Semantic | Selection | BadIndent | MatchingBrackets | CurrentBracketPair | CheckerError 

/// Given Start Offset and End Offset from Document
[<Struct>]
type LinePartChange = 
    {
    from: int
    till: int
    act: Action<VisualLineElement>
    }

// Given Start Offset and End Offset from Document  // DELETE
//[<Struct>]
//type LinePartChangeEx = 
//    {
//    from: int
//    till: int
//    action: Action<VisualLineElement>
//    reason: ChangeReason
//    }
//
//    static member make (from,till,action,reason) = 
//        {
//        from  =from  
//        till  =till  
//        action=action
//        reason=reason
//        }

/// For accessing the highlighting of a line in constant time
type LineTransformers<'T>(capacityPerLine:int) =    

    let lines = ResizeArray<ResizeArray<'T>>(256)// for approx 512 lines on screen

    let empty = ResizeArray<'T>(0)

    
    let mutable firtsIsSet = false
    let mutable first = Unchecked.defaultof<'T>
    let mutable last  = Unchecked.defaultof<'T>

    member _.LineCount = lines.Count

    member _.Insert(line, x) =         
        while lines.Count <= line  do // fill up missing lines
            lines.Add ( new ResizeArray<'T>(capacityPerLine))// for capacityPerLine tokens per line
        
        lines[line].Add x
        if not firtsIsSet then 
            first <- x
            firtsIsSet <- true
        last <- x        
        
 
    /// does nothing if clear already
    member _.ClearAllLines() =        
        if firtsIsSet then 
            for line in lines do 
                line.Clear()
            //lines.Clear() // dont do this so that allocated Lists stay alive and can be refilled fast        
            firtsIsSet <- false        
        

    /// Safely gets a Line returns empty if index out of range
    member _.Line(lineNumber) =
        if lineNumber>=0 && lineNumber<lines.Count then lines.[lineNumber]
        else empty

    member _.IsEmpty = not firtsIsSet

    member _.IsNotEmpty = firtsIsSet

    member _.Range = if firtsIsSet then Some (first,last) else None




/// An efficient DocumentColorizingTransformer using line number indices into a line transformer list.
type FastColorizer(transformers:LineTransformers<LinePartChange> []) = 
    inherit Rendering.DocumentColorizingTransformer()   

    let ltss = transformers
    
    let mutable shift = 0

    member _.AdjustShift (s:int) = shift <- shift + s

    member _.ResetShift () = shift <- 0

    member _.Transformers = ltss

    /// This gets called for every visible line on every Redraw
    override this.ColorizeLine(line:Document.DocumentLine) =   
        let lineNo = line.LineNumber
        let offSt  = line.Offset    
        let offEn  = line.EndOffset        
        
        for lts in ltss do 
            if lineNo >= lts.LineCount then 
                ISeffLog.log.PrintfnAppErrorMsg $"Cant get line index {lineNo} from {lts.LineCount} lines in LineTransformer"
            else
                for ch in lts.Line(lineNo) do  
                    let from = ch.from + shift
                    let till = ch.till + shift
                    if   from > offEn then ISeffLog.log.PrintfnAppErrorMsg $"ch.form {ch.from} + shift {shift} > offEn {offEn} in LineTransformer"
                    elif till > offEn then ISeffLog.log.PrintfnAppErrorMsg $"ch.till {ch.till} + shift {shift} > offEn {offEn} in LineTransformer"
                    elif till < offSt then ISeffLog.log.PrintfnAppErrorMsg $"ch.till {ch.till} + shift {shift} < offSt {offSt} in LineTransformer"
                    elif from < offSt then ISeffLog.log.PrintfnAppErrorMsg $"ch.form {ch.from} + shift {shift} < offSt {offSt} in LineTransformer"            
                    else
                        base.ChangeLinePart(from, till, ch.act)
                        //base.CurrentContext.VisualLine.