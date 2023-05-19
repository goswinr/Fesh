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

/// For accessing the highlighting of a line in constant time
type LineTransformers<'T>() =    

    let lines = ResizeArray<ResizeArray<'T>>(256)// for approx 512 lines on screen

    let empty = ResizeArray<'T>()
    
    
    let mutable first = Unchecked.defaultof<'T>
    let mutable last  = Unchecked.defaultof<'T>
    let mutable lastLine   = 0
    let mutable firstLine  = Int32.MaxValue

    member _.LineCount = lines.Count
       
    member _.Insert(lineNumber, x) =         
        // fill up missing lines
        while lineNumber > lines.Count   do 
            lines.Add empty        
        
        if lineNumber = lines.Count  then 
            // add a new line        
            let n = ResizeArray(4)
            n.Add x
            lines.Add n
        else
            // add to existing line
            lines.[lineNumber].Add x 
        
        if lineNumber < firstLine then 
            firstLine <- lineNumber
            first <- x
        if lineNumber > lastLine then 
            lastLine <- lineNumber
            last <- x
        
        
    /// does nothing if clear already
    member _.ClearAllLines() =        
        if lastLine > 0  then 
            for line in lines do 
                line.Clear()
            //lines.Clear() // dont do this too, so that allocated Lists stay alive and can be refilled fast        
            lastLine  <- 0      
            firstLine <- Int32.MaxValue

    /// Safely gets a Line returns empty if index is out of range
    member _.Line(lineNumber) =
        if lineNumber>=0 && lineNumber<lines.Count then lines.[lineNumber]
        else empty

    member _.IsEmpty = lastLine = 0 

    member _.IsNotEmpty = lastLine > 0 

    member _.Range = if lastLine > 0  then Some (first,last) else None

    member this.TotalCount = 
        let mutable k =  0
        for i=0 to lines.Count do
            k <- k + this.Line(i).Count
        k
   


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
        
        for j = 0 to ltss.Length-1 do
            let lts = ltss.[j]
            if lineNo >= lts.LineCount then 
                //ISeffLog.log.PrintfnAppErrorMsg $"Cant get line index {lineNo} from {lts.LineCount} lines in LineTransformer"
                ()
            else
                let lpcs = lts.Line(lineNo) 
                for i=0 to lpcs.Count-1 do  
                    if i < lpcs.Count then // becaus it might get reset while iterating ?
                        let lpc = lpcs[i]
                        let from = lpc.from + shift
                        let till = lpc.till + shift
                        if   from > offEn then ISeffLog.log.PrintfnAppErrorMsg $"ch.form {lpc.from} + shift {shift} > offEn {offEn} in LineTransformer"
                        elif till > offEn then ISeffLog.log.PrintfnAppErrorMsg $"ch.till {lpc.till} + shift {shift} > offEn {offEn} in LineTransformer"
                        elif till < offSt then ISeffLog.log.PrintfnAppErrorMsg $"ch.till {lpc.till} + shift {shift} < offSt {offSt} in LineTransformer"
                        elif from < offSt then ISeffLog.log.PrintfnAppErrorMsg $"ch.form {lpc.from} + shift {shift} < offSt {offSt} in LineTransformer"            
                        else
                            base.ChangeLinePart(from, till, lpc.act)
                            //base.CurrentContext.VisualLine.
                    else
                        ISeffLog.log.PrintfnAppErrorMsg $"Line Count {lpcs.Count} was reset while iterating index{i}"
