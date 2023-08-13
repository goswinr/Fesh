namespace Seff.Editor

open System
open AvalonEditB
open AvalonEditB.Rendering
open Seff.Model
open Seff.Util.General

open System.Windows
open System.Windows.Controls
open System.Windows.Media

open AvalonLog.Brush

open Seff
open Seff.Util
open Seff.Model

open FSharp.Compiler
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.EditorServices

open AvalonEditB
open AvalonEditB.Document
open AvalonEditB.Rendering



/// Given Start Offset and End Offset from Document
[<Struct>]
type LinePartChange =   {
    from: int // Offset 
    till: int // Offset 
    act: Action<VisualLineElement>
    }

    
/// index change needed from document change
[<Struct>]
type Shift = {
    fromOff      : int // Offset 
    fromLine     : int // Line number
    amountOff    : int 
    amountLines : int // change in line number
    }
    

module ErrorStyle= 
    let errSquiggle     = Pen(  Brushes.Red     |> darker 20      |> freeze, 1.0) |> Pen.freeze
    let errBackGr       =       Brushes.Red     |> brighter 220   |> freeze

    let warnSquiggle    = Pen(  Brushes.Yellow  |> darker 40      |> freeze, 1.0) |> Pen.freeze
    let warnBackGr      =       Brushes.Yellow  |> brighter 200   |> freeze

    let infoSquiggle    = Pen(  Brushes.Green  |> darker 5       |> freeze, 1.0) |> Pen.freeze
    let infoBackGr      =       Brushes.Green  |> brighter 220   |> freeze


/// an ISegment: This segment also contains back and foreground color and diagnostic display text
type SegmentToMark (startOffset:int,  endOffset:int , e:FSharpDiagnostic)  = 

    let underlinePen = 
        match e.Severity with
        | FSharpDiagnosticSeverity.Info    -> ErrorStyle.infoSquiggle
        | FSharpDiagnosticSeverity.Hidden  -> ErrorStyle.infoSquiggle
        | FSharpDiagnosticSeverity.Warning -> ErrorStyle.warnSquiggle
        | FSharpDiagnosticSeverity.Error   -> ErrorStyle.errSquiggle 
    let backgroundBrush =
        match e.Severity with
        | FSharpDiagnosticSeverity.Hidden  -> ErrorStyle.infoBackGr
        | FSharpDiagnosticSeverity.Info    -> ErrorStyle.infoBackGr
        | FSharpDiagnosticSeverity.Warning -> ErrorStyle.warnBackGr
        | FSharpDiagnosticSeverity.Error   -> ErrorStyle.errBackGr 
       
    member _.Offset      = startOffset
    member _.EndOffset   = endOffset
    member _.Length      = endOffset - startOffset

    member _.Message  =  
        match e.Severity with
        | FSharpDiagnosticSeverity.Hidden  -> sprintf "• Hidden Info: %s: %s"  e.ErrorNumberText e.Message 
        | FSharpDiagnosticSeverity.Info    -> sprintf "• Info: %s: %s"         e.ErrorNumberText e.Message 
        | FSharpDiagnosticSeverity.Warning -> sprintf "• Warning: %s: %s"      e.ErrorNumberText e.Message 
        | FSharpDiagnosticSeverity.Error   -> sprintf "• Error: %s: %s"        e.ErrorNumberText e.Message   

    member _.Diagnostic        =  e
    member _.Severity          =  e.Severity 
    member _.UnderlinePen      =  underlinePen
    member _.BackgroundBrush   =  backgroundBrush

    interface ISegment with 
        member _.Offset      = startOffset
        member _.EndOffset   = endOffset
        member _.Length      = endOffset - startOffset 

    member s.Shifted (x:Shift)= 
        let o = if startOffset < x.fromOff then startOffset else startOffset + x.amountOff  
        let e = if endOffset   < x.fromOff then endOffset   else endOffset   + x.amountOff
        {new ISegment with
            member _.Offset      = o
            member _.EndOffset   = e
            member _.Length      = e - o
            }



/// For accessing the highlighting of a line in constant time
type LineTransformers<'T>() =    // generic so it can work for LinePartChange and SegmentToMark

    let mutable lines = ResizeArray<ResizeArray<'T>>(256)// for approx 256 lines on screen

    let empty = ResizeArray<'T>()

    let mutable shift = { fromOff=Int32.MaxValue; fromLine=Int32.MaxValue; amountOff=0;  amountLines=0}   

    member _.AdjustOneShift(s:Shift) = 
        shift <- {  fromOff      = min shift.fromOff   s.fromOff  
                    fromLine     = min shift.fromLine  s.fromLine 
                    amountOff    =     shift.amountOff     + s.amountOff
                    amountLines =     shift.amountLines  + s.amountLines
                    }
   

    member _.Shift = shift

    member _.LineCount = lines.Count

    /// provide the new list.
    /// when done call update with this new list   
    static member Insert(lineList:ResizeArray<ResizeArray<'T>>, lineNumber:int, x:'T) =         
        
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
    
    /// Replaces the Linetransformers or Segments with a new list and resets the shift       
    member _.Update(lineList:ResizeArray<ResizeArray<'T>>) =        
        lines <- lineList
        shift <- { fromOff=Int32.MaxValue; fromLine=Int32.MaxValue; amountOff=0;  amountLines=0}   

    /// Safely gets a Line returns empty List  if index is out of range
    /// also applies the shift for line numbers if present
    member _.GetLine(lineNumber) =
        let lNo = 
            if lineNumber > shift.fromLine then 
                lineNumber - shift.amountLines // use minus to actually get the line that was there before the shift
            else 
                lineNumber 
       
        if lNo>=0 && lNo<lines.Count then 
            let ln = lines[lNo] 
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
    
    //member _.ResetShifts() = // DELETE
    //    for i = 0 to transformers.Length-1 do
    //        let lts = transformers.[i]
    //        lts.ResetOneShift() 

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
                            let shiftChecked = if lpc.from >= shift.fromOff then shift.amountOff else 0
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
            
        



