namespace Seff.Editor

open System
open System.Linq // for First() and Last() on read only collections //TODO replace
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Collections.Generic

open FSharp.Compiler
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

    let infoSquiggle    = Pen(  Brushes.Green  |> darker 5      |> freeze, 1.0) |> Pen.freeze
    let infoBackGr      =       Brushes.Green  |> brighter 220   |> freeze

module ErrorUtil = 
    
    type ErrorsBySeverity = {
        erros    : ResizeArray<FSharpDiagnostic>
        warnings : ResizeArray<FSharpDiagnostic>
        infos    : ResizeArray<FSharpDiagnostic>
        hiddens  : ResizeArray<FSharpDiagnostic>        }
        
    /// split errors by severity and sort by line number 
    let getBySeverity(checkRes:CodeAnalysis.FSharpCheckFileResults)  =
        let was = ResizeArray()  // Warnings
        let ers = ResizeArray()  // Errors
        let ins = ResizeArray()  // Infos        
        let his = ResizeArray()  // Hidden
        for e in checkRes.Diagnostics do
            match e.Severity with
            | FSharpDiagnosticSeverity.Error   -> ers.Add e
            | FSharpDiagnosticSeverity.Warning -> was.Add e
            | FSharpDiagnosticSeverity.Hidden  -> his.Add e
            | FSharpDiagnosticSeverity.Info    -> ins.Add e
        // make sure they are sorted , the tools below will then truncate this list to only mark the first 9 or so errors in the UI (for performance)
        was.Sort(fun x y -> Operators.compare x.StartLine y.StartLine)
        ers.Sort(fun x y -> Operators.compare x.StartLine y.StartLine)
        ins.Sort(fun x y -> Operators.compare x.StartLine y.StartLine)
        his.Sort(fun x y -> Operators.compare x.StartLine y.StartLine)
        { erros = ers; warnings = was; infos = ins; hiddens = his }


    /// because FSharpDiagnostic might have line number 0 form Parse-and-check-file-in-project errors, but avalonedit starts at 1
    let inline linesStartAtOne i = if i<1 then 1 else i 

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

   
    let getSquiggleLine(r:Rect):StreamGeometry = 
        let startPoint = r.BottomLeft
        let endPoint = r.BottomRight
        let offset = 2.5
        let count = max 4 (int((endPoint.X - startPoint.X)/offset) + 1)
        let geometry = new StreamGeometry()
        use ctx = geometry.Open()
        ctx.BeginFigure(startPoint, false, false)
        ctx.PolyLineTo(
            [| for i=0 to count - 1 do yield new Point( startPoint.X + (float i * offset) , startPoint.Y - if (i + 1) % 2 = 0 then offset else 0.) |] , /// for Squiggly line
            true, 
            false)
        geometry.Freeze()
        geometry

    let getFirstError(iEditor:IEditor)= 
        match iEditor.FileCheckState with
        | GettingCode _  | Checking _ | NotStarted  | Failed -> None
        | Done res -> 
            res.checkRes.Diagnostics |> Array.tryHead

    let getFirstSegment(iEditor:IEditor) =
        getFirstError(iEditor) 
        |> Option.map ( getSegment iEditor.AvaEdit.Document)
        
/// This segment also contains back and foreground color and dignostic display text
type SegmentToMark (startOffset, length, e:FSharpDiagnostic)  = 
    inherit TextSegment()

    let undelinePen = 
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
    let msg = 
        match e.Severity with
        | FSharpDiagnosticSeverity.Hidden  -> sprintf "• Hidden Info: %s: %s"  e.ErrorNumberText e.Message 
        | FSharpDiagnosticSeverity.Info    -> sprintf "• Info: %s: %s"         e.ErrorNumberText e.Message 
        | FSharpDiagnosticSeverity.Warning -> sprintf "• Warning: %s: %s"      e.ErrorNumberText e.Message 
        | FSharpDiagnosticSeverity.Error   -> sprintf "• Error: %s: %s"        e.ErrorNumberText e.Message 
  
    do
        base.StartOffset <- startOffset
        base.Length      <- length

    member _.Message           =  msg
    member _.Diagnostic        =  e
    member _.Severity          =  e.Severity 
    member _.UnderlinePen      =  undelinePen
    member _.BackgroundBrush   =  backgroundBrush


/// IBackgroundRenderer and IVisualLineTransformer
type ErrorRenderer (ed:TextEditor, folds:Folding.FoldingManager, log:ISeffLog) = 

    let doc = ed.Document
    let txA = ed.TextArea
    let segments = new TextSegmentCollection<SegmentToMark>(doc)

    /// Draw the error sqiggle  on the code
    member _.Draw (textView:TextView , drawingContext:DrawingContext) = // for IBackgroundRenderer
        try
            let vls = textView.VisualLines
            if textView.VisualLinesValid  && vls.Count > 0 then
                let  viewStart = vls.First().FirstDocumentLine.Offset
                let  viewEnd =   vls.Last().LastDocumentLine.EndOffset
                for segment in segments.FindOverlappingSegments(viewStart, viewEnd - viewStart) do
                    // background color:
                    let geoBuilder = new BackgroundGeometryBuilder (AlignToWholePixels = true, CornerRadius = 0.)
                    geoBuilder.AddSegment(textView, segment)
                    let boundaryPolygon= geoBuilder.CreateGeometry() // creates one boundary round the text
                    drawingContext.DrawGeometry(segment.BackgroundBrush, null, boundaryPolygon)

                    //foreground,  squiggels:
                    for rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment) do
                        let geo = ErrorUtil.getSquiggleLine(rect)
                        drawingContext.DrawGeometry(Brushes.Transparent, segment.UnderlinePen, geo)
                        //break //TODO why break in original code on //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
        with ex ->
            log.PrintfnAppErrorMsg "ERROR in ErrorRenderer.Draw: %A" ex

    member _.Layer = KnownLayer.Selection // for IBackgroundRenderer
    member _.Transform(context:ITextRunConstructionContext , elements:IList<VisualLineElement>)=() // needed ? // for IVisualLineTransformer

    /// Update list of Segments to actually mark (first nine only per Severity) and ensure drawing the error sqiggle on the surrounding folding box too
    member _.AddSegments( res: CheckResults )= 
        let mark(e:FSharpDiagnostic) = 
            let seg = ErrorUtil.getSegment doc e  
            let segToMark = SegmentToMark ( seg.StartOffset, seg.Length, e )
            segments.Add (segToMark)
            for fold in folds.GetFoldingsContaining(seg.StartOffset) do
                //if fold.IsFolded then // do on all folds !
                //fold.BackbgroundColor  <- ErrorStyle.errBackGr // done via ctx.DrawRectangle(ErrorStyle.errBackGr
                fold.DecorateRectangle <- 
                    Action<Rect,DrawingContext>( fun rect ctx ->
                        let geo = ErrorUtil.getSquiggleLine(rect)
                        if isNull fold.BackbgroundColor then ctx.DrawRectangle(segToMark.BackgroundBrush, null, rect) // in case of selection highlighting skip brush only use Pen                        
                        ctx.DrawGeometry(Brushes.Transparent, segToMark.UnderlinePen, geo)
                        )        
        let bySev = ErrorUtil.getBySeverity(res.checkRes)
        for i in bySev.hiddens  |> Seq.truncate 9  do mark(i)                
        for i in bySev.infos    |> Seq.truncate 9  do mark(i)                
        for w in bySev.warnings |> Seq.truncate 9  do mark(w)                
        for e in bySev.erros    |> Seq.truncate 9  do mark(e)   // draw error last, after warning, to be on top !   
            
        txA.TextView.Redraw()

    member _.Clear()= 
        if segments.Count > 0 then
            segments.Clear()
            for fold in folds.AllFoldings do fold.DecorateRectangle <- null
            txA.TextView.Redraw()

    member _.GetsegmentsAtOffset(offset) = segments.FindSegmentsContaining(offset)

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

    let showErrorToolTip(mouse:Input.MouseEventArgs) = 
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
                //tb.Foreground <- Media.SolidColorBrush(if seg.IsWarning then Colors.DarkRed else Colors.DarkGreen)
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
                //e.Handled <- true // don't, MouseHover might still trigger type info tooltip

    do
        tView.BackgroundRenderers.Add(renderer)
        tView.LineTransformers.Add(   renderer)
        tView.Services.AddService(typeof<ErrorRenderer> , renderer) // TODO, what for?

        tView.MouseHover.Add        (showErrorToolTip)
        tView.MouseHoverStopped.Add ( fun e ->  tip.IsOpen <- false ) //; e.Handled <- true) )
        tView.VisualLinesChanged.Add( fun e ->  tip.IsOpen <- false ) // on scroll  or resize?

    [<CLIEvent>] member this.OnDrawn = drawnEv.Publish

    /// draws underlines
    /// threadsafe
    member this.Draw( iEditor: IEditor ) = // this is used as Checker.OnChecked event handler
        match iEditor.FileCheckState with
        | Done res ->
            renderer.Clear()
            renderer.AddSegments(res)
            drawnEv.Trigger(iEditor) // to update foldings now
        | NotStarted | GettingCode _ | Checking _ | Failed -> ()

    member this.ToolTip = tip




