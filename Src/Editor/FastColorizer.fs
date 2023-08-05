namespace Seff.Editor

open System
open AvalonEditB
open AvalonEditB.Rendering
open Seff.Model
open Seff.Util.General

type ChangeReason = Semantic | Selection | BadIndent | MatchingBrackets | CurrentBracketPair | CheckerError 

/// Given Start Offset and End Offset from Document
[<Struct>]
type LinePartChange =   {
    from: int // Offset 
    till: int // Offset 
    act: Action<VisualLineElement>
    }

[<Struct>]
type Shift = {
    from:int // Offset 
    amount: int 
    }


/// For accessing the highlighting of a line in constant time
type LineTransformers<'T>() =    // generic so it can work for LinePartChange and Shift an SegmentToMark

    let mutable lines = ResizeArray<ResizeArray<'T>>(256)// for approx 256 lines on screen

    let empty = ResizeArray<'T>()

    let mutable shift = {from=0; amount=0}    

    member _.AdjustOneShift(s:Shift) = shift <- {from = min shift.from s.from  ; amount = shift.amount + s.amount }

    member _.ResetOneShift() = shift <- {from=0; amount=0}

    member _.Shift = shift

    member _.LineCount = lines.Count

    /// provide the new list.
    /// when done call update with this new list   
    member _.Insert(lineList:ResizeArray<ResizeArray<'T>>,lineNumber:int, x:'T) =         
        
        // fill up missing lines
        for _ = lineList.Count to lineNumber-1 do            
            lineList.Add null

        if lineNumber = lineList.Count  then 
            // add a new line        
            let n = ResizeArray<'T>(4)
            n.Add x
            lineList.Add n            
       
        else            
            // add to existing line:            
            let ln = lineList.[lineNumber] 
            if isNull ln then 
                let n = ResizeArray(4)
                lineList.[lineNumber] <- n
                n.Add x 
            else                
                ln.Add x           
           
    member _.Update(lineList:ResizeArray<ResizeArray<'T>>) =        
        lines <- lineList
        shift <- {from=0; amount=0}

    /// Safely gets a Line returns empty if index is out of range
    member _.GetLine(lineNumber) =
        if lineNumber>=0 && lineNumber<lines.Count then 
            let ln = lines.[lineNumber] 
            if isNull ln then 
                empty 
            else 
                ln            
        else 
            empty
    
    member _.Range =

        let mutable first = Unchecked.defaultof<'T>
        let mutable last  = Unchecked.defaultof<'T>
        let mutable lastLine   = 0
        let mutable firstLine  = Int32.MaxValue

        let rec findFirst i = 
            if i < lines.Count then 
                let ln = lines.[i] 
                if notNull ln then 
                    firstLine <- i
                    first     <- ln.[0]
                else 
                    findFirst (i+1)
            else ()
        
        let rec findLast i = 
            if i >= 0 then 
                let ln = lines.[i] 
                if notNull ln then 
                    lastLine <- i
                    last     <- ln.[ln.Count-1]
                else 
                    findLast (i-1)
            else ()
        
        findFirst 0
        if firstLine = Int32.MaxValue  then 
            None
        else
            findLast (lines.Count-1)
            Some (first,last) 
 


/// An efficient DocumentColorizingTransformer using line number indices into a line transformer list.
type FastColorizer(transformers:LineTransformers<LinePartChange> [], ed:TextEditor) = 
    inherit Rendering.DocumentColorizingTransformer()  

    member _.AdjustShifts(s:Shift) = 
        for i = 0 to transformers.Length-1 do
            let lts = transformers.[i]
            lts.AdjustOneShift(s) 
    
    member _.ResetShifts() = 
        for i = 0 to transformers.Length-1 do
            let lts = transformers.[i]
            lts.ResetOneShift() 

    /// This gets called for every visible line on every Redraw
    override _.ColorizeLine(line:Document.DocumentLine) =   
        let lineNo = line.LineNumber
        let offSt  = line.Offset    
        let offEn  = line.EndOffset 

        for j = 0 to transformers.Length-1 do
            let lts = transformers.[j]
            if lineNo >= lts.LineCount then 
                //ISeffLog.log.PrintfnAppErrorMsg $"Cant get line index {lineNo} from {lts.LineCount} lines in LineTransformer"
                ()
            else
                let lpcs = lts.GetLine(lineNo) 
                for i=0 to lpcs.Count-1 do  
                    if i < lpcs.Count then // because it might get reset while iterating ?
                        let lpc = lpcs[i]
                        if notNull lpc.act then // because for coloring brackets it may be null to keep xshd coloring
                            let shift = lts.Shift
                            let shiftChecked = if lpc.from > shift.from then shift.amount else 0
                            let from = lpc.from + shiftChecked
                            let till = lpc.till + shiftChecked
                            if from >= till then () // negative length
                                //let tx = ed.Document.GetText(line)
                                //let seg = ed.Document.GetText(till, from-till)
                                //ISeffLog.log.PrintfnAppErrorMsg $"*LineChangePart1 {from} >= {till}; DocLine {offSt}-{offEn} on line: {lineNo}; (shift:{shiftChecked})"           
                                //ISeffLog.log.PrintfnAppErrorMsg $"   '{seg}' in {lineNo}:'{tx}'"           
                            elif till > offEn then () // ISeffLog.log.PrintfnAppErrorMsg $"**LineChangePart2 {from}-{till}; DocLine {offSt}-{offEn} on line: {lineNo}; (shift:{shiftChecked})" 
                            elif from < offSt then () // ISeffLog.log.PrintfnAppErrorMsg $"***LineChangePart3 {from}-{till}; DocLine {offSt}-{offEn} on line: {lineNo}; (shift:{shiftChecked})"           
                            else
                                //ISeffLog.log.PrintfnDebugMsg $"{from}-{till}; DocLine {offSt}-{offEn} on line: {lineNo}; doc.Text.Length {ed.Document.TextLength} (shift:{shiftChecked})" 
                                base.ChangeLinePart(from, till, lpc.act)
                                                            
                  

/// An efficient DocumentColorizingTransformer using line number indices into a line transformer list.
type DebugColorizer() = 
    inherit Rendering.DocumentColorizingTransformer()  
    let t = Diagnostics.Stopwatch()

    /// This gets called for every visible line on every Redraw
    override _.ColorizeLine(line:Document.DocumentLine) =   
        let lineNo = line.LineNumber
        if t.ElapsedMilliseconds > 1000L then 
            t.Restart()
            ISeffLog.log.PrintfnIOErrorMsg $"after 1s DebugColorizer on %d{lineNo}"
        elif lineNo % 10 = 0  then            
            ISeffLog.log.PrintfnDebugMsg $"%d{lineNo} from DebugColorizer"
            t.Restart()
        //elif lineNo % 2= 0  then 
        else
            ISeffLog.log.PrintfFsiErrorMsg $"%d{lineNo}, "
            
        



