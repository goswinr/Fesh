namespace Seff.Editor

open System
open AvalonEditB
open AvalonEditB.Rendering
open Seff.Model

/// Given Start Offset and End Offset from Document
[<Struct>]
type LinePartChange = {
    form: int
    till: int
    action: Action<VisualLineElement>
    }

/// For accessing the highlighting of a line in constant time
type LineTransformers() =    

    let lines = ResizeArray<ResizeArray<LinePartChange>>(256)// for approx 256 lines on screen

    member _.Lines = lines

    member _.Insert(line,c) =         
        while lines.Count <= line  do // fill up missing lines
            lines.Add ( new ResizeArray<LinePartChange>(16))// for approx 16 tokens per line
        lines[line].Add c
        
    member _.ClearLine(line) =
        lines[line].Clear()
    
    member _.ClearAllLines() =
        for line in lines do 
            line.Clear()


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
        
        if lineNo >= lts.Lines.Count then 
            ISeffLog.log.PrintfnDebugMsg $"Cant get line index {lineNo} from {lts.Lines.Count} lines in LineTransformer"
        else
            for ch in lts.Lines[lineNo] do  
                let from = ch.form + shift
                let till = ch.till + shift
                if   from > offEn then ISeffLog.log.PrintfnDebugMsg $"ch.form {ch.form} + shift {shift} > offEn {offEn} in LineTransformer"
                elif till > offEn then ISeffLog.log.PrintfnDebugMsg $"ch.till {ch.till} + shift {shift} > offEn {offEn} in LineTransformer"
                elif till < offSt then ISeffLog.log.PrintfnDebugMsg $"ch.till {ch.till} + shift {shift} < offSt {offSt} in LineTransformer"
                elif from < offSt then ISeffLog.log.PrintfnDebugMsg $"ch.form {ch.form} + shift {shift} < offSt {offSt} in LineTransformer"            
                else
                    base.ChangeLinePart(from, till, ch.action) 