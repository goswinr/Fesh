namespace Seff.Editor

open System
open System.Linq // for First() and Last() on read only collections //TODO replace
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Collections.Generic

open AvalonEditB
open AvalonEditB.Document
open AvalonEditB.Rendering

open FSharp.Compiler.SourceCodeServices

open Seff
open Seff.Model
open Seff.Util.Media


//read: http://danielgrunwald.de/coding/AvalonEdit/rendering.php

// taken from //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
// better would be https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/Textsegmentservice.cs


module ErrorStyle  = 
    let errSquiggle     = Pen(  Brushes.Red     |> darker 20      |> freeze, 1.0) |> freezePen
    let warnSquiggle    = Pen(  Brushes.Yellow  |> darker 40      |> freeze, 1.0) |> freezePen
    let errBackGr       =       Brushes.Red     |> brighter 200   |> freeze
    let warnBackGr      =       Brushes.Yellow  |> brighter 90    |> freeze   

    
type SegmentToMark private (startOffset, length, message:string, undelinePen:Pen, backbroundColor:SolidColorBrush, isWarning:bool)  =
    inherit TextSegment()
    do
        base.StartOffset <- startOffset
        base.Length      <- length
    member val Message           =  message 
    member val IsWarning         =  isWarning
    member val UnderlinePen    =  undelinePen
    member val BackgroundBrush   =  backbroundColor

    static member CreateForError( startOffset, length, message) = 
        SegmentToMark (startOffset, length, message, ErrorStyle.errSquiggle, ErrorStyle.errBackGr , false )
    
    static member CreateForWarning (startOffset, length, message) = 
        SegmentToMark (startOffset, length, message, ErrorStyle.warnSquiggle, ErrorStyle.warnBackGr, true)

/// IBackgroundRenderer and IVisualLineTransformer
type ErrorRenderer (ied:IEditor, log:ISeffLog) = 
   
    let doc = ied.AvaEdit.Document
    let txA = ied.AvaEdit.TextArea
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
        res.checkRes.Errors|> Array.sortInPlaceBy (fun e -> e.StartLineAlternate)
        for e in res.checkRes.Errors |> Seq.truncate 9 do 
            // TODO Only highligth the first 9 Errors, Otherwise UI becomes unresponsive at 100 or more errors ( eg when pasting bad text)            
            let startOffset = doc.GetOffset(new TextLocation(e.StartLineAlternate, e.StartColumn + 1 ))
            let endOffset   = doc.GetOffset(new TextLocation(e.EndLineAlternate,   e.EndColumn   + 1 ))
            let length      = endOffset-startOffset
            match e.Severity with 
            | FSharpErrorSeverity.Error   -> segments.Add ( SegmentToMark.CreateForError  ( startOffset, length, e.Message+"\r\nError: "   + (string e.ErrorNumber) ))
            | FSharpErrorSeverity.Warning -> segments.Add ( SegmentToMark.CreateForWarning( startOffset, length, e.Message+"\r\nWarning: " + (string e.ErrorNumber) )) 
                        
            for fold in ied.FoldingManager.GetFoldingsContaining(startOffset) do
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
            for fold in ied.FoldingManager.AllFoldings do fold.DecorateRectangle <- null
            txA.TextView.Redraw()       

    member this.GetsegmentsAtOffset(offset) = segments.FindSegmentsContaining(offset)
      
    interface IBackgroundRenderer with
        member this.Draw(tv,dc) = this.Draw(tv,dc)
        member this.Layer = this.Layer
        
    interface IVisualLineTransformer with // needed ?
        member this.Transform(ctx,es) = this.Transform(ctx,es)


type ErrorHighlighter (ied:IEditor, log:ISeffLog) = 

    let tView= ied.AvaEdit.TextArea.TextView
    let renderer = ErrorRenderer(ied,log)
    let tip = new ToolTip(IsOpen=false) // TODO replace with something that can be pinned// TODO use popup instead of tooltip so it can be pinned?

    let drawnEv = new Event<IEditor>()

    let showTip(mouse:Input.MouseEventArgs) = 
        let pos = tView.GetPositionFloor(mouse.GetPosition(tView) + tView.ScrollOffset)
        if pos.HasValue then
            let logicalPosition = pos.Value.Location
            let offset = ied.AvaEdit.Document.GetOffset(logicalPosition)
            let segmentsAtOffset = renderer.GetsegmentsAtOffset(offset)
            //let seg = segmentsAtOffset.FirstOrDefault(fun renderer -> renderer.Message <> null)//LINQ ??
            //if notNull seg && notNull tab.ErrorToolTip then
            if segmentsAtOffset.Count > 0 then 
                let seg = segmentsAtOffset.[0]
                let tb = new TextBlock()
                tb.Text <- seg.Message       //TODO move styling out of event handler ?
                tb.FontSize <- Style.fontSize
                tb.FontFamily <- Style.fontToolTip //TODO use another monospace font ?
                tb.TextWrapping <- TextWrapping.Wrap
                tb.Foreground <- Media.SolidColorBrush(if seg.IsWarning then Colors.DarkRed else Colors.DarkGreen) 
                tip.Content <- tb
            
                let pos = ied.AvaEdit.Document.GetLocation(seg.StartOffset) 
                let tvpos = new TextViewPosition(pos.Line,pos.Column) 
                let pt = tView.GetVisualPosition(tvpos, Rendering.VisualYPosition.LineTop)
                let ptInclScroll = pt - tView.ScrollOffset
                tip.PlacementTarget <- ied.AvaEdit.TextArea
                tip.PlacementRectangle <- new Rect(ptInclScroll.X, ptInclScroll.Y, 0., 0.)                    
                tip.Placement <- Primitives.PlacementMode.Top //https://docs.microsoft.com/en-us/dotnet/framework/wpf/controls/popup-placement-behavior      
                tip.VerticalOffset <- -6.0                   
            
                tip.IsOpen <- true
                //e.Handled <- true // don't, it might still trigger type info 
                
    do 
        tView.BackgroundRenderers.Add(renderer)
        tView.LineTransformers.Add(   renderer)
        tView.Services.AddService(typeof<ErrorRenderer> , renderer) // what for?
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


   
        