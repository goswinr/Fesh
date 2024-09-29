namespace Fesh.Editor

open System
open AvalonEditB
open AvalonEditB.Rendering
open Fesh.Model
open Fesh.Util.General

open System.Windows
open System.Windows.Controls
open System.Windows.Media

open AvalonLog.Brush

open Fesh
open Fesh.Util
open Fesh.Model

open FSharp.Compiler
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.EditorServices

open AvalonEditB
open AvalonEditB.Document
open AvalonEditB.Rendering
open System
open System



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

    let penSize = 1.2

    let errBackGr       = Brushes.Red     |> brighter 220   |> freeze
    let errSquiggle     = Brushes.Red     |> darker 10      |> freeze
    let errSquigglePen     = Pen(errSquiggle, penSize) |> Pen.freeze

    //let warnSquiggle    = Pen(  Brushes.Yellow  |> darker 40      |> freeze, penSize) |> Pen.freeze
    let warnBackGr      =  Brushes.Yellow  |> brighter 200   |> freeze
    let warnSquiggle    =  Brushes.Gold                      |> freeze
    let warnSquigglePen = Pen(warnSquiggle, penSize) |> Pen.freeze

    let infoBackGr      =  Brushes.Green  |> brighter 220   |> freeze
    let infoSquiggle    =  Brushes.Green  |> darker 5       |> freeze
    let infoSquigglePen = Pen( infoSquiggle, penSize) |> Pen.freeze


/// an ISegment: This segment also contains back and foreground color and diagnostic display text
type SegmentToMark (startOffset:int,  endOffset:int , e:FSharpDiagnostic)  =

    let underlinePen =
        match e.Severity with
        | FSharpDiagnosticSeverity.Info    -> ErrorStyle.infoSquigglePen
        | FSharpDiagnosticSeverity.Hidden  -> ErrorStyle.infoSquigglePen
        | FSharpDiagnosticSeverity.Warning -> ErrorStyle.warnSquigglePen
        | FSharpDiagnosticSeverity.Error   -> ErrorStyle.errSquigglePen

    let underline =
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

    member _.Underline         =  underline

    member _.BackgroundBrush   =  backgroundBrush

    interface ISegment with
        member _.Offset      = startOffset
        member _.EndOffset   = endOffset
        member _.Length      = endOffset - startOffset


    member s.Shifted (x:Shift)=  // DELETE
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

    let mutable shift = {
        fromOff    = Int32.MaxValue
        fromLine   = Int32.MaxValue
        amountOff  = 0
        amountLines= 0
        }

    member _.AdjustOneShift(s:Shift) =
        shift <- {  fromOff      =     min shift.fromOff   s.fromOff
                    fromLine     =     min shift.fromLine  s.fromLine
                    amountOff    =     shift.amountOff    + s.amountOff
                    amountLines  =     shift.amountLines  + s.amountLines
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

    /// Replaces the LineTransformers or Segments with a new list and resets the shift
    member _.Update(lineList:ResizeArray<ResizeArray<'T>>) =
        lines <- lineList
        shift <- { fromOff=Int32.MaxValue; fromLine=Int32.MaxValue; amountOff=0;  amountLines=0}

    /// Safely gets a Line returns empty List if index is out of range
    /// also applies the shift for line numbers if present
    member _.GetLine(lineNumber) : ResizeArray<'T> =
        let lNo =
            if lineNumber > shift.fromLine then
                let shifted = lineNumber - shift.amountLines // use minus to actually get the line that was there before the shift
                if shifted < shift.fromLine then // this line has just been inserted. the shift is moving the line number before shift.fromLine
                    Int32.MaxValue // this line has just been inserted. if there are a few lines inserted. then they don't highlighting yet so make sure empty gets returned because of check below
                else
                    shifted
            else
                lineNumber

        if lNo>0 && lNo<lines.Count then // lNo>0  because if pasting several lines at the beginning of a file the lNo can actually go negative !
            let oneLineItems = lines[lNo]
            // let ln =
            //     try
            //         lines[lNo]
            //     with e ->
            //         IFeshLog.log.PrintfnAppErrorMsg $"draw line {lineNumber} trying to get color info from line {lNo} , shift.amountLines: {shift.amountLines} starting from line {shift.fromLine}"
            //         null

            if isNull oneLineItems then
                empty
            else
                oneLineItems
        else
            empty

/// An efficient DocumentColorizingTransformer using line number indices into a line transformer list.
type FastColorizer(transformers:LineTransformers<LinePartChange> []) = //, ed:TextEditor) =
    inherit DocumentColorizingTransformer()

    member _.AdjustShifts(s:Shift) =
        for i = 0 to transformers.Length-1 do
            let lts = transformers.[i]
            lts.AdjustOneShift(s)


    /// This gets called for every visible line on every Redraw
    override _.ColorizeLine(line:Document.DocumentLine) =
        let lineNo = line.LineNumber
        let offSt  = line.Offset
        let offEn  = line.EndOffset

        for j = 0 to transformers.Length-1 do // there are four
            let lts = transformers.[j]
            let shift = lts.Shift
            if lineNo < lts.LineCount then
                let linePartChanges = lts.GetLine(lineNo)
                for i=0 to linePartChanges.Count-1 do
                    let lpc = linePartChanges[i]

                    // adjust offset to shifts:
                    let mutable till = lpc.till
                    let from =
                        if lpc.from >= shift.fromOff  then
                            let shifted = lpc.from + shift.amountOff
                            if shifted < shift.fromOff then
                                Int32.MaxValue // to skip this segment
                            else
                                till <- till + shift.amountOff
                                shifted
                        else
                            lpc.from

                    if from >= offSt && till <= offEn && from < till && notNull lpc.act then
                        base.ChangeLinePart(from, till, lpc.act)


                    // if from >= till then () // negative length or skipped because of shift offset
                    //     //let tx = ed.Document.GetText(line)
                    //     //let seg = ed.Document.GetText(till, from-till)
                    //     //IFeshLog.log.PrintfnAppErrorMsg $"*LineChangePart1 {from} >= {till}; DocLine {offSt}-{offEn} on line: {lineNo}; (shift:{shiftChecked})"
                    //     //IFeshLog.log.PrintfnAppErrorMsg $"   '{seg}' in {lineNo}:'{tx}'"
                    // elif till > offEn then () // IFeshLog.log.PrintfnAppErrorMsg $"**LineChangePart2 {from}-{till}; DocLine {offSt}-{offEn} on line: {lineNo}; (shift:{shiftChecked})"
                    // elif from < offSt then () // IFeshLog.log.PrintfnAppErrorMsg $"***LineChangePart3 {from}-{till}; DocLine {offSt}-{offEn} on line: {lineNo}; (shift:{shiftChecked})"
                    // elif notNull lpc.act then // because for coloring brackets the action may be null to keep xshd coloring
                    //     //IFeshLog.log.PrintfnDebugMsg $"{from}-{till}; DocLine {offSt}-{offEn} on line: {lineNo}; doc.Text.Length {ed.Document.TextLength} (shift:{shiftChecked})"
                    //     base.ChangeLinePart(from, till, lpc.act)

(*

type DebugColorizer(transformers:LineTransformers<LinePartChange> [], ed:TextEditor) =
    inherit DocumentColorizingTransformer()
    let t = Diagnostics.Stopwatch()

    /// This gets called for every visible line on every Redraw
    override _.ColorizeLine(line:Document.DocumentLine) =
        let lineNo = line.LineNumber
        if t.ElapsedMilliseconds > 1000L then
            t.Restart()
            IFeshLog.log.PrintfnIOErrorMsg $"after 1s DebugColorizer on %d{lineNo}"
        elif lineNo % 10 = 0  then
            IFeshLog.log.PrintfnDebugMsg $"%d{lineNo} from DebugColorizer"
            t.Restart()
        //elif lineNo % 2= 0  then
        else
            IFeshLog.log.PrintfFsiErrorMsg $"%d{lineNo}, "

type DebugColorizer2(transformers:LineTransformers<LinePartChange> [], ed:TextEditor) =
    inherit DocumentColorizingTransformer()

    /// This gets called for every visible line on every Redraw
    override _.ColorizeLine(line:Document.DocumentLine) =
        let lineNo = line.LineNumber
        if lineNo = 1 then  eprintfn "draw line 1"

        if lineNo = -1 then
            let offSt  = line.Offset
            let offEn  = line.EndOffset

            for j = 0 to transformers.Length-1 do
                let lts = transformers.[j]
                let shift = lts.Shift
                if shift.fromOff = Int32.MaxValue then
                    eprintfn $"%d{lineNo} no shift"
                else
                    eprintfn $"%d{lineNo} shift: {shift.fromOff} {shift.fromLine} {shift.amountOff} {shift.amountLines} line from {offSt} to {offEn}"
*)
