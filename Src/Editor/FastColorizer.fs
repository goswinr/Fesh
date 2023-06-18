namespace Seff.Editor

open System
open AvalonEditB
open AvalonEditB.Rendering
open Seff.Model
open Seff.Util.General
open Microsoft.VisualBasic.DateAndTime

type ChangeReason = Semantic | Selection | BadIndent | MatchingBrackets | CurrentBracketPair | CheckerError 

/// Given Start Offset and End Offset from Document
[<Struct>]
type LinePartChange =   {
    from: int
    till: int
    act: Action<VisualLineElement>
    }

[<Struct>]
type Shift = {
    from:int
    amount: int
    }

/// For accessing the highlighting of a line in constant time
type LineTransformers<'T>() =    

    let lines = ResizeArray<ResizeArray<'T>>(256)// for approx 512 lines on screen

    let mutable first = Unchecked.defaultof<'T>
    let mutable last  = Unchecked.defaultof<'T>
    let mutable lastLine   = 0
    let mutable firstLine  = Int32.MaxValue

    let empty = ResizeArray<'T>()

    member _.LineCount = lines.Count
       
    member _.Insert(lineNumber:int, x:'T) =         
        
        // fill up missing lines
        for _ = lines.Count to lineNumber-1 do            
            lines.Add null

        if lineNumber = lines.Count  then 
            // add a new line        
            let n = ResizeArray(4)
            n.Add x
            lines.Add n            
       
        else
            // add to existing line:            
            let ln = lines.[lineNumber] 
            if isNull ln then 
                let n = ResizeArray(4)
                lines.[lineNumber] <- n
                n.Add x 
            else                
                ln.Add x
            
        
        // remember the first and last line that has content to then only redraw those 
        if lineNumber < firstLine then 
            firstLine <- lineNumber
            first     <- x
        if lineNumber > lastLine then 
            lastLine <- lineNumber
            last     <- x
           
    /// does nothing if clear already
    member _.ClearAllLines() =        
        if firstLine < Int32.MaxValue  then 
            for i=0 to lines.Count-1 do
                let ln = lines.[i] 
                if not <| isNull ln then ln.Clear()
                
            //lines.Clear() // don't do this too, so that allocated Lists stay alive and can be refilled faster.       
            lastLine  <- 0      
            firstLine <- Int32.MaxValue

    /// Safely gets a Line returns empty if index is out of range
    member _.Line(lineNumber) =
        if lineNumber>=0 && lineNumber<lines.Count then 
            let ln = lines.[lineNumber] 
            if isNull ln then 
                empty 
            else 
                ln            
        else empty

    member _.IsEmpty = firstLine = Int32.MaxValue 

    member _.IsNotEmpty = firstLine  < Int32.MaxValue

    member _.Range = 
        if firstLine = Int32.MaxValue  then 
            None
        else
            Some (first,last) 

    member this.TotalCount = 
        let mutable k =  0
        for i=0 to lines.Count-1 do
            k <- k + this.Line(i).Count
        k  

/// An efficient DocumentColorizingTransformer using line number indices into a line transformer list.
type FastColorizer(transformers:LineTransformers<LinePartChange> [], ed:TextEditor) = 
    inherit Rendering.DocumentColorizingTransformer()  


    let ltss = transformers
    
    let mutable shift = {from=0; amount=0}    

    member _.AdjustShift(s:Shift) = shift <- {from = s.from  ; amount = s.amount + shift.amount}

    member _.ResetShift() = 
        //ISeffLog.printnColor 100 200 100 $"reset shift (from {shift.amount})"
        shift <- {from=0; amount=0}

    member _.Shift = shift

    member _.Transformers = ltss
    
    /// This gets called for every visible line on every Redraw
    override _.ColorizeLine(line:Document.DocumentLine) =   
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
                    if i < lpcs.Count then // because it might get reset while iterating ?
                        let lpc = lpcs[i]
                        if notNull lpc.act then // because for coloring brackets it may be null to keep xshd coloring
                            let shiftChecked = if lpc.from > shift.from then shift.amount else 0
                            let from = lpc.from + shiftChecked
                            let till = lpc.till + shiftChecked
                            if from >= till then () // negative length
                                //let tx = ed.Document.GetText(line)
                                //let seg = ed.Document.GetText(till, from-till)
                                //ISeffLog.log.PrintfnAppErrorMsg $"*LineChangePart1 {from} >= {till}; Docline {offSt}-{offEn} on line: {lineNo}; (shift:{shiftChecked})"           
                                //ISeffLog.log.PrintfnAppErrorMsg $"   '{seg}' in {lineNo}:'{tx}'"           
                            elif till > offEn then () // ISeffLog.log.PrintfnAppErrorMsg $"**LineChangePart2 {from}-{till}; Docline {offSt}-{offEn} on line: {lineNo}; (shift:{shiftChecked})" 
                            elif from < offSt then () // ISeffLog.log.PrintfnAppErrorMsg $"***LineChangePart3 {from}-{till}; Docline {offSt}-{offEn} on line: {lineNo}; (shift:{shiftChecked})"           
                            else
                                //ISeffLog.log.PrintfnDebugMsg $"{from}-{till}; Docline {offSt}-{offEn} on line: {lineNo}; doc.Text.Length {ed.Document.TextLength} (shift:{shiftChecked})" 
                                base.ChangeLinePart(from, till, lpc.act)
                                                            
                    //else  ISeffLog.log.PrintfnAppErrorMsg $"Line Count {lpcs.Count} was reset while iterating index{i}"  // DELETE

/// An efficient DocumentColorizingTransformer using line number indices into a line transformer list.
type DebugColorizer() = 
    inherit Rendering.DocumentColorizingTransformer()  
    let t = Diagnostics.Stopwatch()

    /// This gets called for every visible line on every Redraw
    override _.ColorizeLine(line:Document.DocumentLine) =   
        let lineNo = line.LineNumber
        if lineNo % 10 = 0 then
            if t.ElapsedMilliseconds > 1000L then 
                ISeffLog.log.PrintfnIOErrorMsg $"DebugColorizer.ColorizeLine %d{lineNo}"
            else
                ISeffLog.log.PrintfnDebugMsg $"DebugColorizer.ColorizeLine %d{lineNo}"
            t.Restart()
        



