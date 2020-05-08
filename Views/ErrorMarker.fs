namespace Seff.Views

open System
open System.Windows
open System.Windows.Media
open System.Collections.Generic

open ICSharpCode
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.Document
open ICSharpCode.AvalonEdit.Rendering
open ICSharpCode.AvalonEdit.Utils

open System.Linq // for First() and Last() on read only colections

//read: http://danielgrunwald.de/coding/AvalonEdit/rendering.php

// taken from //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
// better would be https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/TextMarkerService.cs
    
[<AllowNullLiteral>]
type ErrorSegment (startOffset, length, erMsg:string) as this =
    inherit TextSegment ()
    do
        this.StartOffset <- startOffset
        this.Length <- length
    member val Msg =                 erMsg with get , set
    member val MarkerColor    = Colors.Red with get , set
    member val BackgroundColor=  Colors.LightSalmon with get , set //Colors.MistyRose
    
[<AllowNullLiteral>]
type ErrorMarker (textEditor:TextEditor) = 
        
    let markers = new TextSegmentCollection<ErrorSegment>(textEditor.Document)
    let createPoints(start:Point , offset, count)=
        [| for i=0 to count - 1 do yield new Point( start.X + (float i * offset) , 
                                                    start.Y - if (i + 1) % 2 = 0 then offset else 0.) |]

    member this.Draw (textView:TextView , drawingContext:DrawingContext) = // for IBackgroundRenderer
        let vls = textView.VisualLines  
        if textView.VisualLinesValid  && vls.Count > 0 then
            let  viewStart = vls.First().FirstDocumentLine.Offset
            let  viewEnd =   vls.Last().LastDocumentLine.EndOffset
            
            for marker in markers.FindOverlappingSegments(viewStart, viewEnd - viewStart) do
                // background
                let geoBuilder = new BackgroundGeometryBuilder (AlignToWholePixels = true, CornerRadius = 3.)
                geoBuilder.AddSegment(textView, marker) // TODO loop only over this line ,not the others ?
                let geometry = geoBuilder.CreateGeometry()
                let brush = new SolidColorBrush(marker.BackgroundColor)
                brush.Freeze()
                drawingContext.DrawGeometry(brush, null, geometry)

                //foreground
                for r in BackgroundGeometryBuilder.GetRectsForSegment(textView, marker) do
                    let  startPoint = r.BottomLeft
                    let  endPoint = r.BottomRight
                    let usedPen = new Pen(new SolidColorBrush(marker.MarkerColor), 1.)
                    usedPen.Freeze()
                    let offset = 2.5
                    let count = max 4 (int((endPoint.X - startPoint.X)/offset) + 1)

                    let  geometry = new StreamGeometry()
                        
                    use ctx = geometry.Open()
                    ctx.BeginFigure(startPoint, false, false)
                    ctx.PolyLineTo(createPoints(startPoint, offset, count), true, false)                    
                    geometry.Freeze()
                    drawingContext.DrawGeometry(Brushes.Transparent, usedPen, geometry)
                    //break //TODO why in original code ? //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
        
    member this.Layer = KnownLayer.Selection // for IBackgroundRenderer
    member this.Transform(context:ITextRunConstructionContext , elements:IList<VisualLineElement>)=() // needed ? // for IVisualLineTransformer
        
    member this.Create(offset, length, message)=
        let m = new ErrorSegment(offset, length, message)
            
        markers.Add(m)
        //m.MarkerColor <- // TODO chose color from severity
        textEditor.TextArea.TextView.Redraw(m)
        
    member this.Clear()= 
        markers.Clear()
        textEditor.TextArea.TextView.Redraw() // redraw all instead of just marker ISegment  ?            

    member this.GetMarkersAtOffset(offset) = markers.FindSegmentsContaining(offset)
        

    interface IBackgroundRenderer with
        member this.Draw(tv,dc) = this.Draw(tv,dc)
        member this.Layer = this.Layer
        
    interface IVisualLineTransformer with // needed ?
        member this.Transform(ctx,es) = this.Transform(ctx,es)
