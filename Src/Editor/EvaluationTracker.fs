namespace Fesh.Editor

open System
open Avalonia.Media
open Avalonia.Media.Immutable

open AvaloniaEdit
open AvaloniaEdit.Document
open AvaloniaEdit.Rendering

open AvaloniaLog.ImmBrush

open Fesh.Util.General
open Fesh.Model
open Fesh

module private EvaluationTrackerRendererUtil =

    //let backGround = Brushes.Teal |> brighter 230
    //let backGround = Brushes.Ivory |> brighter 5
    let backGround = Brushes.Gray |> brighter 110
    // let backGround = ImmutableSolidColorBrush(Color.FromArgb(120uy,239uy,239uy,239uy)) // a=0 : fully transparent A=255 opaque

    let border = ImmutablePen(Brushes.Gray |> darker 20  , 1.0) // new Pen(Brushes.Gray |> darker 20  , 1.0)

    //let border = new Pen( Brushes.Teal  , 0.7)  |> Pen.freeze


open EvaluationTrackerRendererUtil

/// IBackgroundRenderer
type EvaluationTrackerRenderer (ed:TextEditor, state:InteractionState ) =

    /// the first line number as literal
    let [<Literal>] ``1`` = 1

    let mutable evalFromLine = ``1``

    let recomputeEvalFromLineByIndent(changedLineIdx) =
        if changedLineIdx <= ``1`` then
            evalFromLine <- ``1``
        else
            let lns = state.CodeLines
            let id = state.DocChangedId
            let rec findIndent i =
                if i <= ``1`` then ``1``
                else
                    match lns.GetLine(i,id.Value) with // checks for 0 index
                    | ValueSome ln ->
                        if ln.indent = 0 && ln.len > 0 then // a non empty line with 0 indent
                            i
                        else
                            findIndent (i-1)
                    | ValueNone ->
                        ``1``

            let noIndentLine = findIndent(min lns.LastLineIdx changedLineIdx)
            //printfn "noIndentLine: %d"  noIndentLine

            let rec nextNonWhiteLine i =
                if i <= ``1`` then ``1``
                else
                    match lns.GetLineText(i,id.Value) with
                    | ValueSome txt ->
                        if String.IsNullOrWhiteSpace(txt) then nextNonWhiteLine (i-1)
                        else i+1
                    | ValueNone ->
                        IFeshLog.log.PrintfnDebugMsg "nextNonWhiteLine: Line not found %d" i
                        ``1``

            evalFromLine <- nextNonWhiteLine (noIndentLine-1)

    /// Triggered on each document changed
    member _.SetLastChangeAt(changedLineIdx) =
        if changedLineIdx < evalFromLine then
            recomputeEvalFromLineByIndent(changedLineIdx)

    member _.ClearMarking() =
        evalFromLine <- ``1``
        ed.TextArea.TextView.Redraw()

    /// provide the index of the last evaluated Line
    member _.MarkEvaluatedTillLine(changedLineIdx) =
        evalFromLine <- changedLineIdx + 1

    member _.MarkAllEvaluated() =
        let cLns = state.CodeLines
        let mutable li = cLns.LastLineIdx
        let inline isWhite(i) =
            match cLns.GetLineText(i, state.DocChangedId.Value) with
            | ValueSome txt -> String.IsNullOrWhiteSpace(txt)
            | ValueNone -> false
        while li > ``1`` && isWhite(li)  do // to exclude empty lines at end
            li <- li-1
        evalFromLine <- li + 1
        ed.TextArea.TextView.Redraw()

    /// Line Number where evaluation should continue from
    member _.EvaluateFromLine = evalFromLine

    // for IBackgroundRenderer
    member _.Draw(textView:TextView , drawingContext:DrawingContext) =
        if evalFromLine > ``1`` then
            try
                if textView.VisualLinesValid then //to avoid above error.
                    let vls = textView.VisualLines
                    if vls.Count > 0 then // check needed !
                        let topLine = vls[0].FirstDocumentLine
                        let topIdx= topLine.LineNumber
                        // let botLine   = vls[vls.Count-1].LastDocumentLine.LineNumber
                        let tillLineIdx = evalFromLine-1
                        if tillLineIdx >= topIdx then
                            let mutable endIdx = 0
                            while endIdx < vls.Count-1 && vls[endIdx].FirstDocumentLine.LineNumber < tillLineIdx do
                                endIdx <- endIdx + 1
                            let endLine = vls[endIdx]
                            let y = endLine.VisualTop - textView.VerticalOffset + endLine.Height
                            let rect = RectangleGeometry(new Avalonia.Rect(1.0, -1.0, textView.Width - 1.0, y + 2.0))
                            drawingContext.DrawGeometry(backGround, border, rect) // pen could be null too


                // let geoBuilder = new BackgroundGeometryBuilder (AlignToWholePixels = true, CornerRadius = 0.0 )
                // let seg = ISegment.FormTill(0, textView.Document.GetLineByNumber(evalFromLine-1).EndOffset)
                // geoBuilder.AddSegment(textView, seg) // TODO: what happens if the code became shorter and this segment is now bigger than the document ?
                // let boundaryPolygon = geoBuilder.CreateGeometry() // creates one boundary round the text
                // //drawingContext.DrawGeometry(backGround, null, boundaryPolygon) // pen could be null too
                // drawingContext.DrawGeometry(backGround, border, boundaryPolygon) // pen could be null too

                // TODO draw a draggable separator instead:
                // http://www.fssnip.net/9N/title/Drag-move-for-GUI-controls
            with ex ->
                IFeshLog.log.PrintfnAppErrorMsg "ERROR in EvaluationTrackerRenderer.Draw(): %A" ex

    // for IBackgroundRenderer
    member _.Layer =
        // KnownLayer.Caret // to draw over all text a transparent layer
        // KnownLayer.Selection
        KnownLayer.Background

    interface IBackgroundRenderer with
        member this.Draw(tv,dc) = this.Draw(tv,dc)
        member this.Layer = this.Layer


type EvaluationTracker (ed:TextEditor, state, config:Config.Config) =

    let isActive = config.Settings.GetBool(EvaluationTracker.SettingsStr, EvaluationTracker.onByDefault)

    let renderer = EvaluationTrackerRenderer(ed,state)

    //TODO on tab change and "EvalInteractionNonThrowing returned Error:" reset too !

    do
        if isActive then
            ed.TextArea.TextView.BackgroundRenderers.Add renderer
            let fsi =Fsi.GetOrCreate config
            fsi.OnReset.Add       (fun _ -> renderer.ClearMarking()) // reset for all editors
            fsi.OnCanceled.Add    (fun _ -> if IEditor.isCurrent ed then renderer.ClearMarking())
            fsi.OnRuntimeError.Add(fun _ -> if IEditor.isCurrent ed then renderer.ClearMarking())
            // fsi.OnFsiEvalError.Add(fun _ -> if IEditor.isCurrent ed then renderer.ClearMarking())
            fsi.OnCompletedOk.Add (fun evc ->
                if IEditor.isCurrent ed then  //this event will be hooked up for each tab so check id too
                    //IFeshLog.log.PrintfnColor 150 150 150  "Fsi.OnCompletedOk:%A" evc
                    //IFeshLog.log.PrintfnFsiErrorMsg "Fsi.OnCompletedOk:renderer.EvaluateFrom:%d" renderer.EvaluateFrom
                    match evc.amount with
                    |All |ContinueFromChanges -> renderer.MarkAllEvaluated()
                    |FsiSegment s ->
                        if s.startLine <= renderer.EvaluateFromLine then // only mark if the code before was evaluated already
                            let li = ed.Document.GetLineByOffset(s.startOffset + s.length).LineNumber
                            renderer.MarkEvaluatedTillLine li
                            ed.TextArea.TextView.Redraw()
                        else
                            IFeshLog.log.PrintfnDebugMsg "FsiSegment.startLine > renderer.EvaluateFromLine: %d > %d" s.startLine renderer.EvaluateFromLine
                )

    member _.SetLastChangeAt(lineIdx) =
        if isActive then
            renderer.SetLastChangeAt(lineIdx)

    /// Line where evaluation should continue from
    member _.EvaluateFromLine =
        renderer.EvaluateFromLine

    member _.MarkEvaluatedTillLineRedraw(lineIdx) =
        if isActive then
            renderer.MarkEvaluatedTillLine(lineIdx)
            ed.TextArea.TextView.Redraw()


    /// just the string literal used for settings file: "TrackEvaluatedCode"
    static member SettingsStr = "TrackEvaluatedCode"

    static member onByDefault = false



    // let recomputeTopMostUnEvaluatedByIndent() =
    //     if topMostUnEvaluated = 0 then
    //         if isNull evaluatedCodeSeg then () // nothing evaluated yet, do nothing
    //         else evaluatedCodeSeg <- null // happens when there is a segment but the first char gets deleted
    //     else
    //         let len = doc.TextLength
    //         if topMostUnEvaluated >= len then
    //             evaluatedCodeSeg <- newSegmentTill(len-1)// recalculate just to be sure it hasn't changed
    //             ed.TextArea.TextView.Redraw()

    //         elif isNull evaluatedCodeSeg || topMostUnEvaluated-1 <> evaluatedCodeSeg.EndOffset then
    //             let lastInEval = doc.GetCharAt(topMostUnEvaluated-1)
    //             //IFeshLog.log.PrintfnColor 144 222 100 "computeParagraphAndDraw:\r\n'%s'" ed.Text

    //             // first try to look ahead if there is only white space till a paragraph starts.
    //             // in that case keep the current evaluated marking
    //             let rec keepMark prev i =
    //                 if i = len then
    //                     true
    //                 else
    //                     let ch = doc.GetCharAt(i)
    //                     //IFeshLog.log.PrintfnColor 200 0 100 "prev: '%s' ch: '%s'" (formatChar prev)(formatChar ch)
    //                     if isNonWhite ch then // non whitespace
    //                         if prev = '\n' then // find a line that does not start with white space
    //                             true //i-1 // there is only white space till 'i' where a paragraph starts
    //                         else
    //                             false //a non white but not at beginning of line
    //                     else
    //                         keepMark ch (i+1)
    //             let keep = keepMark lastInEval topMostUnEvaluated
    //             //IFeshLog.log.PrintfnColor 55 99 100 "keep is: '%b'" keep

    //             //now search back needed since the next non white is at position 0 in line
    //             if keep then
    //                 let mutable j = topMostUnEvaluated-1
    //                 while j>0 && isWhite (doc.GetCharAt(j)) do // to remove white space too
    //                     j <- j-1
    //                 topMostUnEvaluated <- j+2
    //                 evaluatedCodeSeg   <- newSegmentTill(j+1)
    //             else
    //                 let rec searchBack after i =
    //                     if i = -1 then 0
    //                     else
    //                         let this = doc.GetCharAt(i)
    //                         if this = '\n' && isNonWhite after then // find a line that does not start with white space
    //                             //i // end segment will include line return
    //                             let mutable j = i-1
    //                             while j>0 && isWhite (doc.GetCharAt(j)) do // to remove white space too
    //                                 j <- j-1
    //                             j+1
    //                         else
    //                             searchBack this (i-1)

    //                 //IFeshLog.log.PrintfnColor 0 200 100 "topMostUnEvaluated-1: %d" (topMostUnEvaluated-1)
    //                 let segmentEnd =
    //                     let segEnd = searchBack lastInEval topMostUnEvaluated   // GetCharAt     cant be -1 because there is a check at the top

    //                     /// now include any attributes and comments in the lines above , and skip whitespace again
    //                     let rec moveUp (ln:DocumentLine) =
    //                         if ln.LineNumber = 1 then
    //                             min segEnd ln.EndOffset // min() because segEnd might be smaller than ln.EndOffset
    //                         else
    //                             let st2 = doc.GetText(ln.Offset,2)
    //                             let st5 = doc.GetText(ln.Offset,5)
    //                             if st2 = "[<" || st2 = "//"   then // for attributes comments
    //                                 moveUp(ln.PreviousLine)
    //                             elif String.IsNullOrWhiteSpace(doc.GetText(ln)) then
    //                                 moveUp(ln.PreviousLine)
    //                             elif st5 = "open " || st5 = "#if I" then // TODO: detect correctly if current end segment is inside a #if #elif #else block !
    //                                 moveUp(ln.PreviousLine)
    //                             else
    //                                 ln.EndOffset
    //                     moveUp(doc.GetLineByOffset(segEnd))


    //                 if segmentEnd = 0 then
    //                     topMostUnEvaluated <- 0
    //                     evaluatedCodeSeg <- null
    //                 else
    //                     topMostUnEvaluated <- segmentEnd+1
    //                     evaluatedCodeSeg   <- newSegmentTill(segmentEnd)

    //             ed.TextArea.TextView.Redraw()