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
    action: Action<VisualLineElement>
    reason: ChangeReason
    }

    static member make (from,till,action,reason) = 
        {
        from  =from  
        till  =till  
        action=action
        reason=reason
        }

/// For accessing the highlighting of a line in constant time
type LineTransformers<'T>() =    

    let lines = ResizeArray<ResizeArray<'T>>(512)// for approx 512 lines on screen

    let empty = ResizeArray<'T>(0)

    //member _.Lines = lines // don make public

    member _.LineCount = lines.Count

    /// removes all items from one line that have a given transformer reason
    member _.RemoveByPredicate (lineNo, predicate: 'T -> bool) = 
        if lineNo >= 0 && lineNo < lines.Count then 
            let ts = lines[lineNo]
            ts.RemoveAll(predicate)  |> ignore<int>

    member _.Insert(line,c) =         
        while lines.Count <= line  do // fill up missing lines
            lines.Add ( new ResizeArray<'T>(8))// for approx 16 tokens per line
        lines[line].Add c
        
    member _.ClearLine(line) =
        lines[line].Clear()
    
    member _.ClearAllLines() =
        for line in lines do 
            line.Clear()

    /// Safely gets a Line returns emppty if index out of range
    member _.Line(lineNumber) =
        if lineNumber<lines.Count then  lines.[lineNumber]
        else empty



/// An efficient DocumentColorizingTransformer using line number indices into a line transformer list.
type FastColorizer () = 
    inherit Rendering.DocumentColorizingTransformer()   

    let lts = LineTransformers() 
    
    let mutable shift = 0

    member _.AdjustShift (s:int) = shift <- shift + s

    member _.ResetShift () = shift <- 0

    member _.Transformers = lts

    /// This gets called for every visible line on every Redraw
    override this.ColorizeLine(line:Document.DocumentLine) =   
        let lineNo = line.LineNumber
        let offSt  = line.Offset    
        let offEn  = line.EndOffset        
        
        if lineNo >= lts.LineCount then 
            ISeffLog.log.PrintfnDebugMsg $"Cant get line index {lineNo} from {lts.LineCount} lines in LineTransformer"
        else
            for ch in lts.Line(lineNo) do  
                let from = ch.from + shift
                let till = ch.till + shift
                if   from > offEn then ISeffLog.log.PrintfnDebugMsg $"ch.form {ch.from} + shift {shift} > offEn {offEn} in LineTransformer"
                elif till > offEn then ISeffLog.log.PrintfnDebugMsg $"ch.till {ch.till} + shift {shift} > offEn {offEn} in LineTransformer"
                elif till < offSt then ISeffLog.log.PrintfnDebugMsg $"ch.till {ch.till} + shift {shift} < offSt {offSt} in LineTransformer"
                elif from < offSt then ISeffLog.log.PrintfnDebugMsg $"ch.form {ch.from} + shift {shift} < offSt {offSt} in LineTransformer"            
                else
                    base.ChangeLinePart(from, till, ch.action)
                    //base.CurrentContext.VisualLine.