namespace Seff.Editor

open Seff
open Seff.Util.General
open System
open System.Windows
open System.Windows.Media
open System.Collections.Generic
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.Document
open ICSharpCode.AvalonEdit.Rendering
open FSharp.Compiler.SourceCodeServices
open System.Linq // for First() and Last() on read only collections
open System.Windows.Controls
open System.Text


//read: http://danielgrunwald.de/coding/AvalonEdit/rendering.php

// taken from //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
// better would be https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/Textsegmentservice.cs

    
type SegmentToMark private (startOffset, length, message:string, undelineColor:Media.Color option, backbroundColor:Media.Color option,isWarning:bool) as this =
    inherit TextSegment()
    do
        this.StartOffset <- startOffset
        this.Length <- length
    member val Message =                 message 
    member val IsWarning =          isWarning
    member val UnderlineColor    =  undelineColor
    member val BackgroundColor   =  backbroundColor

    static member CreateForError( startOffset, length, message) = 
             SegmentToMark (startOffset, length, message, Some Colors.Red , Some Colors.LightSalmon, false )
    
    static member CreateForWarning (startOffset, length, message) = 
               SegmentToMark (startOffset, length, message, Some Colors.Green , Some (Colors.LightSeaGreen |> changeLuminace 50) , true)


type ErrorRenderer (textEditor:TextEditor, log:ISeffLog) = 
        
    let segments = new TextSegmentCollection<SegmentToMark>(textEditor.Document)
    let createPoints(start:Point , offset, count)=
        [| for i=0 to count - 1 do yield new Point( start.X + (float i * offset) , 
                                                    start.Y - if (i + 1) % 2 = 0 then offset else 0.) |]

    member this.Draw (textView:TextView , drawingContext:DrawingContext) = // for IBackgroundRenderer
        try
            let vls = textView.VisualLines  
            if textView.VisualLinesValid  && vls.Count > 0 then
                let  viewStart = vls.First().FirstDocumentLine.Offset
                let  viewEnd =   vls.Last().LastDocumentLine.EndOffset
            
                for segment in segments.FindOverlappingSegments(viewStart, viewEnd - viewStart) do
                    // background
                    match segment.BackgroundColor with 
                    |None ->()
                    |Some backgroundColor ->                 
                        let geoBuilder = new BackgroundGeometryBuilder (AlignToWholePixels = true, CornerRadius = 3.)
                        geoBuilder.AddSegment(textView, segment) // TODO loop only over this line ,not the others ?
                        let geometry = geoBuilder.CreateGeometry()
                        let brush = new SolidColorBrush(backgroundColor)
                        brush.Freeze()
                        drawingContext.DrawGeometry(brush, null, geometry)

                    //foreground
                    match segment.UnderlineColor with 
                    |None ->()
                    |Some underlineColor ->   
                    for r in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment) do
                        let  startPoint = r.BottomLeft
                        let  endPoint = r.BottomRight
                        let usedPen = new Pen(new SolidColorBrush(underlineColor), 1.)
                        usedPen.Freeze()
                        let offset = 2.5
                        let count = max 4 (int((endPoint.X - startPoint.X)/offset) + 1)

                        let  geometry = new StreamGeometry()
                        
                        use ctx = geometry.Open()
                        ctx.BeginFigure(startPoint, false, false)
                        ctx.PolyLineTo(createPoints(startPoint, offset, count), true, false)                    
                        geometry.Freeze()
                        drawingContext.DrawGeometry(Brushes.Transparent, usedPen, geometry)
                        //break //TODO why break in original code ? //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
        with ex -> 
            log.PrintAppErrorMsg "ERROR in ErrorRenderer.Draw: %A" ex
            
    member this.Layer = KnownLayer.Selection // for IBackgroundRenderer
    member this.Transform(context:ITextRunConstructionContext , elements:IList<VisualLineElement>)=() // needed ? // for IVisualLineTransformer
        
    member this.AddSegments( res: CheckResults )=        
        for e in res.checkRes.Errors |> Seq.truncate 5 do // TODO Only highligth the first 3 Errors, Otherwise UI becomes unresponsive at 100 errors ( eg when pasting bad text)// TODO Test again        
            //TODO as an alternative use Visualline transformers like in Log view, do they perform better ?
            let startOffset = textEditor.Document.GetOffset(new TextLocation(e.StartLineAlternate, e.StartColumn + 1 ))
            let endOffset   = textEditor.Document.GetOffset(new TextLocation(e.EndLineAlternate,   e.EndColumn   + 1 ))
            let length      = endOffset-startOffset
            match e.Severity with 
            | FSharpErrorSeverity.Error   -> segments.Add ( SegmentToMark.CreateForError  ( startOffset, length, e.Message+"\r\nError: "   + (string e.ErrorNumber) ))
            | FSharpErrorSeverity.Warning -> segments.Add ( SegmentToMark.CreateForWarning( startOffset, length, e.Message+"\r\nWarning: " + (string e.ErrorNumber) )) 
        
        textEditor.TextArea.TextView.Redraw()// or just redraw each segment one by one while adding them?
        
    member this.Clear()= 
        if segments.Count > 0 then 
            segments.Clear()
            textEditor.TextArea.TextView.Redraw() // redraw all instead of just renderer ISegment  ?            

    member this.GetsegmentsAtOffset(offset) = segments.FindSegmentsContaining(offset)
      
    interface IBackgroundRenderer with
        member this.Draw(tv,dc) = this.Draw(tv,dc)
        member this.Layer = this.Layer
        
    interface IVisualLineTransformer with // needed ?
        member this.Transform(ctx,es) = this.Transform(ctx,es)


type ErrorHighlighter (ed:TextEditor, log:ISeffLog) = 

    let tView= ed.TextArea.TextView
    let renderer = ErrorRenderer(ed,log)
    let tip = new ToolTip(IsOpen=false) // replace with something that can be pinned// TODO use popup instead of tooltip so it can be pinned?

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
                let seg = segmentsAtOffset.[0]
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
        tView.Services.AddService(typeof<ErrorRenderer> , renderer) // what for?
        tView.MouseHover.Add (showTip)
        
        tView.MouseHoverStopped.Add ( fun e ->  tip.IsOpen <- false ) //; e.Handled <- true) )
        tView.VisualLinesChanged.Add( fun e ->  tip.IsOpen <- false ) // on scroll  or resize?
    
    [<CLIEvent>] member this.OnDrawn = drawnEv.Publish

    /// draws underlines
    /// theadsafe
    member this.Draw( iEditor: IEditor ) = // this is used as Checker.OnChecked event handler         
        match iEditor.CheckState with        
        | Done res -> 
            renderer.Clear()
            renderer.AddSegments(res)
            drawnEv.Trigger(iEditor) // to update foldings now
        | NotStarted | Running _ | Failed -> ()
        
       

    member this.ToolTip = tip
