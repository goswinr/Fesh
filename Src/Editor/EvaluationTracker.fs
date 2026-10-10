namespace Fesh.Editor

open System
open System.Windows.Media

open AvalonEditB
open AvalonEditB.Document
open AvalonEditB.Rendering

open AvalonLog.Brush

open Fesh.Util.General
open Fesh.Model
open Fesh

module private EvaluationTrackerRendererUtil =

    //let backGround = Brushes.Teal |> brighter 230 |> freeze
    //let backGround = Brushes.Ivory |> brighter 5   |> freeze
    let backGround = Brushes.Gray |> brighter 110 |> freeze
    // let backGround = SolidColorBrush(Color.FromArgb(120uy,239uy,239uy,239uy))|> freeze // a=0 : fully transparent A=255 opaque


    let border =
        let p = new Pen(Brushes.Gray |> darker 20 |> freeze , 1.0)
        p.Freeze()
        p

    //let border = new Pen( Brushes.Teal |> freeze , 0.7)  |> Pen.freeze


open EvaluationTrackerRendererUtil

/// IBackgroundRenderer
type EvaluationTrackerRenderer (ed:TextEditor) =

    /// the first line number as literal
    let [<Literal>] ``1`` = 1

    let mutable evalFromLine = ``1``

    let isWhite (ln:DocumentLine) =
        TextUtilities.GetLeadingWhitespace(ed.Document, ln).Length = ln.Length

    /// a non empty line with 0 indent
    let isNotIndented (ln:DocumentLine) =
        ln.Length > 0 && TextUtilities.GetLeadingWhitespace(ed.Document, ln).Length = 0

    let recomputeEvalFromLineByIndent(changedLineIdx) =
        if changedLineIdx <= ``1`` then
            evalFromLine <- ``1``
        else
            let doc = ed.Document
            let rec findNotIndented (ln:DocumentLine) =
                if ln.LineNumber <= ``1`` || isNotIndented ln then ln
                else findNotIndented ln.PreviousLine

            let noIndentLine = findNotIndented (doc.GetLineByNumber(min doc.LineCount changedLineIdx))
            //printfn "noIndentLine: %d"  noIndentLine.LineNumber

            // include the white lines directly above it too:
            let rec firstOfWhiteLinesAbove (ln:DocumentLine) =
                if ln.LineNumber <= ``1`` then ``1``
                elif isWhite ln.PreviousLine then firstOfWhiteLinesAbove ln.PreviousLine
                else ln.LineNumber

            evalFromLine <- firstOfWhiteLinesAbove noIndentLine

    /// Call in sync on each document change, with the line number where the change starts.
    member _.SetLastChangeAt(changedLineIdx) =
        if changedLineIdx < evalFromLine then
            let prev = evalFromLine
            recomputeEvalFromLineByIndent(changedLineIdx)
            if evalFromLine <> prev then
                ed.TextArea.TextView.InvalidateLayer(KnownLayer.Background) // to redraw the gray background

    member _.ClearMarking() =
        evalFromLine <- ``1``
        ed.TextArea.TextView.Redraw()

    /// provide the index of the last evaluated Line
    member _.MarkEvaluatedTillLine(changedLineIdx) =
        evalFromLine <- changedLineIdx + 1

    member _.MarkAllEvaluated() =
        let mutable ln = ed.Document.GetLineByNumber(ed.Document.LineCount)
        while ln.LineNumber > ``1`` && isWhite ln do // to exclude empty lines at end
            ln <- ln.PreviousLine
        evalFromLine <- ln.LineNumber + 1
        ed.TextArea.TextView.Redraw()

    /// Marks the evaluated code, moved to where it is in the current document.
    /// Then applies the changes made while it was evaluating again, they are not evaluated.
    /// fromVersion: the version of the document the evaluated code was taken from.
    member this.MarkEvaluated(amount:FsiCodeAmount, fromVersion:ITextSourceVersion) =
        let doc = ed.Document
        let current = doc.Version
        match amount with
        |All |ContinueFromChanges -> this.MarkAllEvaluated()
        |FsiSegment s ->
            let stOff = fromVersion.MoveOffsetTo(current, s.startOffset, AnchorMovementType.AfterInsertion)
            let stLine = doc.GetLineByOffset(stOff).LineNumber // not s.startLine, it is the last line if the selection was made upwards
            if stLine <= evalFromLine then // only mark if the code before was evaluated already
                let endOff = fromVersion.MoveOffsetTo(current, s.startOffset + s.length, AnchorMovementType.BeforeInsertion)
                this.MarkEvaluatedTillLine(doc.GetLineByOffset(endOff).LineNumber)
                ed.TextArea.TextView.Redraw()
            else
                IFeshLog.log.PrintfnDebugMsg "FsiSegment start line > EvaluateFromLine: %d > %d" stLine evalFromLine

        // The first changed offset is enough, a change only moves the offsets after it.
        // So the smallest offset of all changes is also the first changed offset in the current document:
        let firstChangeOff = fromVersion.GetChangesTo(current) |> Seq.fold (fun m c -> min m c.Offset) Int32.MaxValue
        if firstChangeOff < Int32.MaxValue then
            this.SetLastChangeAt(doc.GetLineByOffset(min firstChangeOff doc.TextLength).LineNumber)

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
                            let rect = RectangleGeometry(new Windows.Rect(1.0, -1.0, textView.ActualWidth - 1.0, y + 2.0))
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


type EvaluationTracker (ed:TextEditor, config:Config.Config) =

    let isActive = config.Settings.GetBool(EvaluationTracker.SettingsStr, EvaluationTracker.onByDefault)

    let renderer = EvaluationTrackerRenderer(ed)

    /// The Fsi events are hooked up for each tab, so check if the evaluated code is from this editor.
    /// (It might not be the current one anymore when an async evaluation completes)
    let isThisEditor (req:EvalRequest) = Object.ReferenceEquals(req.editor.AvaEdit, ed)

    /// The evaluation of this editor that is compiling or running now,
    /// with the version of the document its code was taken from.
    let mutable running : option<EvalRequest * ITextSourceVersion> = None

    let clearIfThisEditor (req:EvalRequest) =
        if isThisEditor req then
            running <- None
            renderer.ClearMarking()

    //TODO on tab change and "EvalInteractionNonThrowing returned Error:" reset too !

    do
        if isActive then
            ed.TextArea.TextView.BackgroundRenderers.Add renderer
            let fsi =Fsi.GetOrCreate config
            fsi.OnCompiling.Add   (fun evd -> running <- if isThisEditor evd.request then Some (evd.request, evd.docVersion) else None)
            fsi.OnReset.Add       (fun _ -> running <- None; renderer.ClearMarking()) // reset for all editors
            fsi.OnCanceled.Add    (fun req      -> clearIfThisEditor req)
            fsi.OnRuntimeError.Add(fun (req, _) -> clearIfThisEditor req)
            // fsi.OnFsiEvalError.Add(fun _ -> if IEditor.isCurrent ed then renderer.ClearMarking())
            fsi.OnCompletedOk.Add (fun evc ->
                if isThisEditor evc then
                    //IFeshLog.log.PrintfnColor 150 150 150  "Fsi.OnCompletedOk:%A" evc
                    //IFeshLog.log.PrintfnFsiErrorMsg "Fsi.OnCompletedOk:renderer.EvaluateFrom:%d" renderer.EvaluateFrom
                    match running with
                    | Some (req, ver) when Object.ReferenceEquals(req, evc) && ver.BelongsToSameDocumentAs ed.Document.Version ->
                        running <- None
                        renderer.MarkEvaluated(evc.amount, ver)
                    | _ ->
                        running <- None
                        IFeshLog.log.PrintfnDebugMsg "EvaluationTracker: the document version for the completed evaluation is unknown, it is not marked as evaluated."
                )

    /// Call in sync on each document change, with the line number where the change starts.
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