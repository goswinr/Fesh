﻿namespace Fesh.Editor

open System
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Collections.Generic

open FSharp.Compiler
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.EditorServices

open AvalonEditB
open AvalonEditB.Document
open AvalonEditB.Rendering

open AvalonLog.Brush

open Fesh
open Fesh.Util
open Fesh.Model
open System.Windows.Media

// Error colors are defined in FastColorizer.fs

module ErrorUtil =

    /// for clicking through the errors in the status bar
    let mutable private scrollToIdx = -1

    let maxErrorCountToTrack = 5

    /// split errors by severity and sort by line number
    let getBySeverity(checkRes:CodeAnalysis.FSharpCheckFileResults) :ErrorsBySeverity =
        AutoFixErrors.references checkRes // check for missing references
        scrollToIdx <- -1 // reset first scroll to error when clicking in status bar
        let was  = ResizeArray()  // Warnings
        let ers  = ResizeArray()  // Errors
        let ins  = ResizeArray()  // Infos
        let his  = ResizeArray()  // Hidden
        let erWs = ResizeArray()  // Errors and Warnings
        let all = checkRes.Diagnostics |> Array.sortBy( fun e -> e.StartLine) // sort before filtering out duplicates
        let m = maxErrorCountToTrack
        // let m2 = maxErrorCountToTrack * 2
        for i = 0 to all.Length - 1 do
            let  e = all.[i]
            // to filter out duplicate errors, a bug in FCS !
            if i=0 || ( let p = all.[i-1] in p.StartLine <> e.StartLine || p.StartColumn <> e.StartColumn || p.EndLine <> e.EndLine || p.EndColumn <> e.EndColumn) then
                match e.Severity with
                | FSharpDiagnosticSeverity.Error   -> if ers.Count < m then ers.Add e ;  erWs.Add e
                | FSharpDiagnosticSeverity.Warning -> if was.Count < m then was.Add e ;  erWs.Add e
                | FSharpDiagnosticSeverity.Hidden  -> if his.Count < m then his.Add e
                | FSharpDiagnosticSeverity.Info    -> if ins.Count < m && e.ErrorNumber <> 3370 then ins.Add e   //  exclude infos about ref cell incrementing ??

        //printfn $"Errors: {ers.Count} Warnings: {was.Count} Infos: {ins.Count} Hidden: {his.Count} "
        { errors = ers; warnings = was; infos = ins; hiddens = his; errorsAndWarnings = erWs }

    let linesStartAtOne i = if i < 1 then 1 else i

    let makeSeg(from,till) =
        Some {new ISegment with
                    member _.Offset      = from
                    member _.EndOffset   = till
                    member _.Length      = till - from      }

    let getSegment (doc:TextDocument) ( e:FSharpDiagnostic) : ISegment option =
        try
            let st = doc.GetOffset(new TextLocation(linesStartAtOne e.StartLine, e.StartColumn + 1 ))
            let en = doc.GetOffset(new TextLocation(linesStartAtOne e.EndLine  , e.EndColumn   + 1 ))
            if st<en then
                makeSeg(st,en)
            elif st>en then // should never happen // this FCS bug has happened in the past, for Parse-and-check-file-in-project errors the segments can be wrong
                makeSeg(en,st)
            else // st=en  // should never happen
                makeSeg(st,st+1) // just in case, so it is at least on char long
        with
            //In a rare race condition the segment is beyond the end of the document because it was just deleted:
            | :? ArgumentOutOfRangeException ->
                None
            | e ->
                raise e

    let getSquiggleLine(r:Rect, yOffset):StreamGeometry =
        let startPoint = r.BottomLeft
        let endPoint = r.BottomRight
        let offset = 3.0 // originally 2.5
        let count = max 4 (int((endPoint.X - startPoint.X)/offset) + 1) // at least 4 squiggles visible
        let geometry = new StreamGeometry()
        use ctx = geometry.Open()
        ctx.BeginFigure(startPoint, false, false)
        ctx.PolyLineTo(
            [| for i=0 to count - 1 do
                let x = startPoint.X + (float i * offset)
                let y = startPoint.Y - if (i + 1) % 2 = 0 then offset + yOffset else yOffset
                Point(x,y) |] , // for Squiggly line
            true,
            false)
        geometry.Freeze()
        geometry


    let getNextErrorIdx( ews:ResizeArray<FSharpDiagnostic> ) =
        if ews.Count=0 then
            -1
        elif scrollToIdx >= ews.Count-1 then // || scrollToIdx >= maxAmountOfErrorsToDraw then
            scrollToIdx <- 0
            0
        else
            scrollToIdx <- scrollToIdx + 1
            scrollToIdx

    let rec getNextSegment(ied:IEditor) =
        match ied.FileCheckState with
        | NotChecked | WaitForCompl _ | WaitForErr _->
            None
        | Done res ->
            let ews = res.errors.errorsAndWarnings
            if ews.Count=0 then
                None
            else
                let i = getNextErrorIdx ews
                if i < 0 then
                    None
                elif i=0 then
                    getSegment ied.AvaEdit.Document ews[i]
                else
                    let p = ews[i-1]
                    let t = ews[i  ]
                    if p.StartLine = t.StartLine then // loop on if not first and same line as last
                        getNextSegment(ied)
                    else
                        getSegment ied.AvaEdit.Document t


/// IBackgroundRenderer only needed because
type ErrorRenderer (state: InteractionState) =

    // based on //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
    // better would be https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/Textsegmentservice.cs

    /// Draw the error squiggle on the code

    member _.Draw(textView:TextView , drawingContext:DrawingContext) = // for IBackgroundRenderer
        //AvalonEditB.Rendering.VisualLinesInvalidException: Exception of type 'AvalonEditB.Rendering.VisualLinesInvalidException' was thrown.
        //    at AvalonEditB.Rendering.TextView.get_VisualLines()
        //    at Fesh.Editor.ErrorRenderer.Draw(TextView textView, DrawingContext drawingContext) in D:\Git\Fesh\Src\Editor\ErrorHighlighter.fs:line 138
        //    at AvalonEditB.Rendering.TextView.RenderBackground(DrawingContext drawingContext, KnownLayer layer)
        //    at AvalonEditB.Editing.CaretLayer.OnRender(DrawingContext drawingContext)

        if textView.VisualLinesValid then //to avoid above error.
            let vls = textView.VisualLines
            if vls.Count > 0 then // check needed !
                let allSegments = state.ErrSegments
                let shift = allSegments.Shift
                for i=0 to vls.Count-1 do
                    let vl = vls[i]
                    let lineNo = vl.LastDocumentLine.LineNumber
                    let segments = allSegments.GetLine lineNo
                    for i=0 to segments.Count-1 do
                        let seg = segments[i]
                        // adjust offset to shifts:
                        let mutable till = seg.EndOffset
                        let from =
                            if seg.Offset >= shift.fromOff  then
                                let shifted = seg.Offset + shift.amountOff
                                if shifted < shift.fromOff then // after shifting the offset moved before the changed area
                                    Int32.MaxValue // to skip this segment
                                else
                                    till <- till + shift.amountOff
                                    shifted
                            else
                                seg.Offset

                        if from >= till then   () // eprintfn "from >= till" // negative length or Int32.MaxValue in from value
                        //elif till > offEn then () // eprintfn "till > offEn " // avoid jumping to next line
                        //elif from < offSt then () // eprintfn "from < offSt" // avoid jumping to previous line
                        else
                            // background color:
                            // when drawing on Caret layer background must be disabled.
                            // let geoBuilder = new BackgroundGeometryBuilder (AlignToWholePixels = true, CornerRadius = 0.)
                            // geoBuilder.AddSegment(textView, segShift )
                            // let boundaryPolygon = geoBuilder.CreateGeometry() // creates one boundary round the text
                            // drawingContext.DrawGeometry(seg.BackgroundBrush, null, boundaryPolygon)

                            //foreground, squiggles:
                            let iSeg = ISegment.FormTill(from,till)
                            let rects = BackgroundGeometryBuilder.GetRectsForSegment(textView, iSeg) |> ResizeArray
                            if rects.Count = 1 then // skip if line overflows and there is more than one rect
                                let geo = ErrorUtil.getSquiggleLine(rects[0], -1.1) // neg offset to move down , // move a bit lower than the line so that the squiggle is not hidden by a selection highlighting
                                drawingContext.DrawGeometry(Brushes.Transparent, seg.UnderlinePen, geo)
    member _.Layer =
        // when drawing on Caret layer  This method is called on every blink of the CaretLayer
        // KnownLayer.Caret
        // KnownLayer.Text // seems to not show  ??
        KnownLayer.Selection // seems OK ?

    interface IBackgroundRenderer with
        member this.Draw(tv,dc) = this.Draw(tv,dc)
        member this.Layer = this.Layer


/// to also draw the full line in a red  background, not only the squiggle, but on the background layer
type ErrorLineRenderer (state: InteractionState) =
    member _.Draw(textView:TextView , drawingContext:DrawingContext) = // for IBackgroundRenderer
        if textView.VisualLinesValid then
            let vls = textView.VisualLines
            if vls.Count > 0 then // check needed !
                let allSegments = state.ErrSegments
                for i=0 to vls.Count-1 do
                    let vl = vls[i]
                    let lineNo = vl.LastDocumentLine.LineNumber
                    let segments = allSegments.GetLine lineNo
                    if segments.Count > 0 then
                        let rect = BackgroundGeometryBuilder.GetRectsFromVisualSegment(textView, vl, 0, 1000)  |> Seq.head
                        drawingContext.DrawRectangle(segments[0].BackgroundBrush, null, rect)
    member _.Layer = KnownLayer.Background
    interface IBackgroundRenderer with
        member this.Draw(tv,dc) = this.Draw(tv,dc)
        member this.Layer = this.Layer


type ErrorHighlighter ( state:InteractionState, folds:Folding.FoldingManager, isComplWinOpen: unit-> bool) =

    //  let actionError   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(ErrorStyle.errBackGr))
    //  let actionWarning = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(ErrorStyle.warnBackGr))
    //  let actionInfo    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(ErrorStyle.infoBackGr))
    //  let actionHidden  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(ErrorStyle.infoBackGr))

    let foundErrorsEv = new Event<int64>()
    let tip = new ToolTip(IsOpen=false)

    let ed = state.Editor
    let tView = ed.TextArea.TextView


    let insert (marginMarks:ResizeArray<int*SolidColorBrush>) (newSegments:ResizeArray<ResizeArray<SegmentToMark>>) id (e:FSharpDiagnostic) : unit =
        let stLn = max 1 e.StartLine // because FSharpDiagnostic might have line number 0 form Parse-and-check-file-in-project errors, but Avalonedit starts at 1
        let enLn = max 1 e.EndLine
        if stLn > enLn then // this actually can happen
            // IFeshLog.log.PrintfnAppErrorMsg $"FSharp Checker reported an invalid error position: e.EndLine < e.StartLine:\r\n {e}"
            ()
        elif e.EndLine = e.StartLine && e.StartColumn > e.EndColumn then // this actually can happen
            // IFeshLog.log.PrintfnAppErrorMsg $"FSharp Checker reported an invalid error position: e.StartColumn <= e.EndColumn:\r\n {e}"
            ()
        else
            let rec insert lnNo =
                if lnNo > enLn then
                    () //true
                elif lnNo-stLn > 2 then // don't insert more than 2 lines of errors, because the are costly to draw
                    () // true
                else
                    match state.CodeLines.GetLine(lnNo,id) with
                    | ValueNone -> () //false
                    | ValueSome cln ->
                        //if cln.len > cln.indent then // Don't skip just whitespace lines, they might also have errors when code is expected but missing.
                        let st  = if lnNo = stLn then cln.offStart + e.StartColumn else cln.offStart
                        let en  = if lnNo = enLn then cln.offStart + e.EndColumn   else cln.offStart + cln.len
                        // e.StartColumn = e.EndColumn // this may actually happens as a result from fs checker:
                        let fixedEn =  if st = en then cln.offStart + max cln.len 1 else en
                        let seg = SegmentToMark(st ,fixedEn , e)
                        LineTransformers.Insert(newSegments, lnNo, seg)
                        marginMarks.Add(lnNo, seg.Underline) // for status bar
                        insert (lnNo+1)
            insert stLn


    let updateFolds id brush pen (e:FSharpDiagnostic): bool = // TODO in theory this could run async, can it ??
        let lnNo =  max 1 e.StartLine // because FSharpDiagnostic might have line number 0
        match state.CodeLines.GetLine(lnNo,id) with
        | ValueNone -> false
        | ValueSome cln ->
            let offset = cln.offStart + e.StartColumn
            for fold in folds.GetFoldingsContaining offset do
                //if fold.IsFolded then // do on all folds, even open ones, so they show correctly when collapsing !
                //fold.BackgroundColor  <- ErrorStyle.errBackGr // done via ctx.DrawRectangle(ErrorStyle.errBackGr
                fold.DecorateRectangle <-
                    Action<Rect,DrawingContext>( fun rect ctx ->
                        let geo = ErrorUtil.getSquiggleLine(rect, 0.1) // move a bit lower than the line so that the squiggle is not hidden by a selection highlighting
                        if isNull fold.BackgroundColor then // in case of selection highlighting skip brush, only use Pen
                            ctx.DrawRectangle(brush, null, rect)
                        ctx.DrawGeometry(Brushes.Transparent, pen, geo)
                        )
            true


    let showErrorToolTip(mouse:Input.MouseEventArgs) =
        if not <| isComplWinOpen() then // don't show tooltip when completion window is open
            let pos = tView.GetPositionFloor(mouse.GetPosition(tView) + tView.ScrollOffset)
            if pos.HasValue then
                let loc = pos.Value.Location
                let offset = ed.Document.GetOffset(loc)
                state.ErrSegments.GetLine(loc.Line)
                |> Seq.tryFind( fun s ->  offset >= s.Offset && offset <= s.EndOffset )
                |> Option.iter(fun segm ->
                    let tb = new TextBlock()
                    tb.Text <- segm.Message       //TODO move styling out of event handler ?
                    tb.FontSize <- StyleState.fontSize * 0.9
                    tb.FontFamily <- StyleState.fontToolTip //TODO use another monospace font ?
                    tb.TextWrapping <- TextWrapping.Wrap
                    //tb.Foreground <- Media.SolidColorBrush(if seg.IsWarning then Colors.DarkRed else Colors.DarkGreen)
                    tip.Content <- tb

                    let pos = ed.Document.GetLocation(segm.Offset)
                    let tvpos = new TextViewPosition(pos.Line,pos.Column)
                    let pt = tView.GetVisualPosition(tvpos, Rendering.VisualYPosition.LineTop)
                    let ptInclScroll = pt - tView.ScrollOffset
                    tip.PlacementTarget <- ed.TextArea
                    tip.PlacementRectangle <- new Rect(ptInclScroll.X, ptInclScroll.Y, 0., 0.)
                    tip.Placement <- Primitives.PlacementMode.Top // Type info Tooltip is on Bottom //https://docs.microsoft.com/en-us/dotnet/framework/wpf/controls/popup-placement-behavior
                    tip.VerticalOffset <- -5.0

                    tip.IsOpen <- true
                    )

    do
        tView.BackgroundRenderers.Add(new ErrorRenderer(state))
        tView.BackgroundRenderers.Add(new ErrorLineRenderer(state))

        tView.MouseHover.Add        ( showErrorToolTip)
        tView.MouseHoverStopped.Add ( fun _->  tip.IsOpen <- false ) //; e.Handled <- true) )
        //tView.VisualLinesChanged.Add( fun e ->  tip.IsOpen <- false ) // done in Editor.setup: avaEdit.TextArea.TextView.VisualLinesChanged.Add (fun _ ->    closeToolTips() )// close type info on typing


    [<CLIEvent>]
    member _.FoundErrors = foundErrorsEv.Publish

    member val ErrorsLines = ref (ResizeArray<int*SolidColorBrush>()) // line numbers of errors, for status bar

    /// triggers foundErrorsEv
    member this.UpdateErrs(errs:ErrorsBySeverity, id) =
        if state.IsLatest id then
            let nSegs = ResizeArray<ResizeArray<SegmentToMark>>(state.ErrSegments.LineCount + 2 )
            let marginMarks = ResizeArray<int*SolidColorBrush>(errs.errors.Count + errs.warnings.Count)
            // first insert in to LineTransformer
            for e in errs.hiddens  do insert marginMarks nSegs id e
            for e in errs.infos    do insert marginMarks nSegs id e
            for e in errs.warnings do insert marginMarks nSegs id e
            for e in errs.errors   do insert marginMarks nSegs id e
            if state.IsLatest id then
                state.ErrSegments.Update nSegs
                this.ErrorsLines.Value <- marginMarks
                foundErrorsEv.Trigger(id)

                // second mark folding boxes if an error is inside, even open ones, so that it shows when collapsed:
                async{
                    do! Async.SwitchToContext Fittings.SyncWpf.context
                    for fold in folds.AllFoldings do  fold.DecorateRectangle <- null   // first clear
                    for e in errs.hiddens  do updateFolds id ErrorStyle.infoBackGr ErrorStyle.infoSquigglePen e  |> ignore<bool>
                    for e in errs.infos    do updateFolds id ErrorStyle.infoBackGr ErrorStyle.infoSquigglePen e  |> ignore<bool>
                    for e in errs.warnings do updateFolds id ErrorStyle.warnBackGr ErrorStyle.warnSquigglePen e  |> ignore<bool>
                    for e in errs.errors   do updateFolds id ErrorStyle.errBackGr  ErrorStyle.errSquigglePen  e  |> ignore<bool>
                } |> Async.Start

    member this.ToolTip = tip




