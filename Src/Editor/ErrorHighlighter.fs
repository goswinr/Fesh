namespace Seff.Editor

open System
open System.Linq // for First() and Last() on read only collections //TODO replace
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Collections.Generic

open FSharp.Compiler.Diagnostics

open AvalonEditB
open AvalonEditB.Document
open AvalonEditB.Rendering

open AvalonLog.Brush

open Seff
open Seff.Util
open Seff.Model


//read: http://danielgrunwald.de/coding/AvalonEdit/rendering.php

// taken from //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
// better would be https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/Textsegmentservice.cs


module ErrorStyle= 
    let errSquiggle     = Pen(  Brushes.Red     |> darker 20      |> freeze, 1.0) |> Pen.freeze
    let errBackGr       =       Brushes.Red     |> brighter 220   |> freeze

    let warnSquiggle    = Pen(  Brushes.Yellow  |> darker 40      |> freeze, 1.0) |> Pen.freeze
    let warnBackGr      =       Brushes.Yellow  |> brighter 200   |> freeze


module ErrorUtil = 
    
    let inline linesStartAtOne i = if i<1 then 1 else i // because FSharpDiagnostic might have line number 0 form Parse-and-check-file-in-project errors, but avalonedit starts at 1

    let getSegment (doc:TextDocument) ( e:FSharpDiagnostic) =
        let s = TextSegment()
        let st = doc.GetOffset(new TextLocation(linesStartAtOne e.StartLine, e.StartColumn + 1 ))
        let en = doc.GetOffset(new TextLocation(linesStartAtOne e.EndLine  , e.EndColumn   + 1 ))
        if st<en then 
            s.StartOffset <- st
            s.EndOffset  <-  en
        elif st>en then // should never happen // this FCS bug has happened in the past, for Parse-and-check-file-in-project errors the segments can be wrong
            s.StartOffset <- en
            s.EndOffset  <-  st 
        else // st=en  // should never happen
            s.StartOffset <- st
            s.EndOffset   <- st + 1 // just in case, so it is at least on char long
        s

    let getFirstError(iEditor:IEditor)= 
        match iEditor.FileCheckState with
        | GettingCode _  | Checking _ | NotStarted  | Failed -> None
        | Done res -> 
            res.checkRes.Diagnostics |> Array.tryHead

    let getFirstSegment(iEditor:IEditor) =
        getFirstError(iEditor) 
        |> Option.map ( getSegment iEditor.AvaEdit.Document)
        

type SegmentToMark private (startOffset, length, message:string, undelinePen:Pen, backbroundColor:SolidColorBrush, isWarning:bool)  = 
    inherit TextSegment()
    do
        base.StartOffset <- startOffset
        base.Length      <- length
    member val Message           =  message
    member val IsWarning         =  isWarning
    member val UnderlinePen      =  undelinePen
    member val BackgroundBrush   =  backbroundColor

    static member CreateForError( startOffset, length, message) = 
        SegmentToMark (startOffset, length, message, ErrorStyle.errSquiggle, ErrorStyle.errBackGr , false )

    static member CreateForWarning (startOffset, length, message) = 
        SegmentToMark (startOffset, length, message, ErrorStyle.warnSquiggle, ErrorStyle.warnBackGr, true)

/// IBackgroundRenderer and IVisualLineTransformer
type ErrorRenderer (ed:TextEditor, folds:Folding.FoldingManager, log:ISeffLog) = 

    let doc = ed.Document
    let txA = ed.TextArea
    let segments = new TextSegmentCollection<SegmentToMark>(doc)

    /// for Squiggly line
    let createPoints(start:Point , offset, count)= 
        [| for i=0 to count - 1 do yield new Point( start.X + (float i * offset) ,
                                                    start.Y - if (i + 1) % 2 = 0 then offset else 0.) |]

    let getSquiggle(r:Rect):StreamGeometry = 
        let  startPoint = r.BottomLeft
        let  endPoint = r.BottomRight
        let offset = 2.5
        let count = max 4 (int((endPoint.X - startPoint.X)/offset) + 1)
        let  geometry = new StreamGeometry()
        use ctx = geometry.Open()
        ctx.BeginFigure(startPoint, false, false)
        ctx.PolyLineTo(createPoints(startPoint, offset, count), true, false)
        geometry.Freeze()
        geometry


    member this.Draw (textView:TextView , drawingContext:DrawingContext) = // for IBackgroundRenderer
        try
            let vls = textView.VisualLines
            if textView.VisualLinesValid  && vls.Count > 0 then
                let  viewStart = vls.First().FirstDocumentLine.Offset
                let  viewEnd =   vls.Last().LastDocumentLine.EndOffset

                for segment in segments.FindOverlappingSegments(viewStart, viewEnd - viewStart) do

                    // background
                    let geoBuilder = new BackgroundGeometryBuilder (AlignToWholePixels = true, CornerRadius = 0.)
                    geoBuilder.AddSegment(textView, segment)
                    let boundaryPolygon= geoBuilder.CreateGeometry() // creates one boundary round the text
                    drawingContext.DrawGeometry(segment.BackgroundBrush, null, boundaryPolygon)

                    //foreground, red squiggels below
                    for rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment) do
                        let geo = getSquiggle(rect)
                        drawingContext.DrawGeometry(Brushes.Transparent, segment.UnderlinePen, geo)
                        //break //TODO why break in original code ? //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
        with ex ->
            log.PrintfnAppErrorMsg "ERROR in ErrorRenderer.Draw: %A" ex

    member this.Layer = KnownLayer.Selection // for IBackgroundRenderer
    member this.Transform(context:ITextRunConstructionContext , elements:IList<VisualLineElement>)=() // needed ? // for IVisualLineTransformer

    member this.AddSegments( res: CheckResults )= 
        res.checkRes.Diagnostics|> Array.sortInPlaceBy (fun e -> e.StartLine)
        for e in res.checkRes.Diagnostics |> Seq.truncate 9 do
            // TODO Only highligth the first 9 Errors, Otherwise UI becomes unresponsive at 100 or more errors ( eg when pasting bad text)
            let seg = ErrorUtil.getSegment doc e
            match e.Severity with
            | FSharpDiagnosticSeverity.Error   -> segments.Add ( SegmentToMark.CreateForError  ( seg.StartOffset, seg.Length, sprintf "• Error: %s: %s"   e.ErrorNumberText e.Message ))
            | FSharpDiagnosticSeverity.Warning -> segments.Add ( SegmentToMark.CreateForWarning( seg.StartOffset, seg.Length, sprintf "• Warning: %s: %s" e.ErrorNumberText e.Message ))
            | FSharpDiagnosticSeverity.Hidden -> () //TODO show ??
            | FSharpDiagnosticSeverity.Info   -> ()

            for fold in folds.GetFoldingsContaining(seg.StartOffset) do
                //if fold.IsFolded then // do on all folds !
                //fold.BackbgroundColor  <- ErrorStyle.errBackGr // done via ctx.DrawRectangle(ErrorStyle.errBackGr
                fold.DecorateRectangle <- Action<Rect,DrawingContext>( fun rect ctx ->
                    let geo = getSquiggle(rect)
                    if isNull fold.BackbgroundColor then // in case of selection highlighting skip brush only use Pen
                        ctx.DrawRectangle(ErrorStyle.errBackGr, null, rect)
                    ctx.DrawGeometry(Brushes.Transparent, ErrorStyle.errSquiggle, geo)
                    )
        txA.TextView.Redraw()

    member this.Clear()= 
        if segments.Count > 0 then
            segments.Clear()
            for fold in folds.AllFoldings do fold.DecorateRectangle <- null
            txA.TextView.Redraw()

    member this.GetsegmentsAtOffset(offset) = segments.FindSegmentsContaining(offset)

    interface IBackgroundRenderer with
        member this.Draw(tv,dc) = this.Draw(tv,dc)
        member this.Layer = this.Layer

    interface IVisualLineTransformer with // needed ?
        member this.Transform(ctx,es) = this.Transform(ctx,es)


type ErrorHighlighter (ed:TextEditor, folds:Folding.FoldingManager, log:ISeffLog) = 

    let tView= ed.TextArea.TextView
    let renderer = ErrorRenderer(ed,folds,log)
    let tip = new ToolTip(IsOpen=false) // TODO replace with something that can be pinned// TODO use popup instead of tooltip so it can be pinned?

    let drawnEv = new Event<IEditor>()

    let showTip(mouse:Input.MouseEventArgs) = 
        let pos = tView.GetPositionFloor(mouse.GetPosition(tView) + tView.ScrollOffset)
        if pos.HasValue then
            let logicalPosition = pos.Value.Location
            let offset = ed.Document.GetOffset(logicalPosition)
            let segmentsAtOffset = renderer.GetsegmentsAtOffset(offset)
            //let seg = segmentsAtOffset.FirstOrDefault(fun renderer -> renderer.Message <> null)//LINQ ??
            //if notNull seg && notNull tab.ErrorToolTip then
            if segmentsAtOffset.Count > 0 then
                let seg = segmentsAtOffset.[0] // TODO show all Errors at this segment not just first ?
                let tb = new TextBlock()
                tb.Text <- seg.Message       //TODO move styling out of event handler ?
                tb.FontSize <- Style.fontSize
                tb.FontFamily <- Style.fontToolTip //TODO use another monospace font ?
                tb.TextWrapping <- TextWrapping.Wrap
                tb.Foreground <- Media.SolidColorBrush(if seg.IsWarning then Colors.DarkRed else Colors.DarkGreen)
                tip.Content <- tb

                let pos = ed.Document.GetLocation(seg.StartOffset)
                let tvpos = new TextViewPosition(pos.Line,pos.Column)
                let pt = tView.GetVisualPosition(tvpos, Rendering.VisualYPosition.LineTop)
                let ptInclScroll = pt - tView.ScrollOffset
                tip.PlacementTarget <- ed.TextArea
                tip.PlacementRectangle <- new Rect(ptInclScroll.X, ptInclScroll.Y, 0., 0.)
                tip.Placement <- Primitives.PlacementMode.Top //https://docs.microsoft.com/en-us/dotnet/framework/wpf/controls/popup-placement-behavior
                tip.VerticalOffset <- -6.0

                tip.IsOpen <- true
                //e.Handled <- true // don't, it might still trigger type info

    do
        tView.BackgroundRenderers.Add(renderer)
        tView.LineTransformers.Add(   renderer)
        tView.Services.AddService(typeof<ErrorRenderer> , renderer) // TODO, what for?
        tView.MouseHover.Add (showTip)

        tView.MouseHoverStopped.Add ( fun e ->  tip.IsOpen <- false ) //; e.Handled <- true) )
        tView.VisualLinesChanged.Add( fun e ->  tip.IsOpen <- false ) // on scroll  or resize?

    [<CLIEvent>] member this.OnDrawn = drawnEv.Publish

    /// draws underlines
    /// theadsafe
    member this.Draw( iEditor: IEditor ) = // this is used as Checker.OnChecked event handler
        match iEditor.FileCheckState with
        | Done res ->
            renderer.Clear()
            renderer.AddSegments(res)
            drawnEv.Trigger(iEditor) // to update foldings now
        | NotStarted | GettingCode _ | Checking _ | Failed -> ()

    member this.ToolTip = tip




