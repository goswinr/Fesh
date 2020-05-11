namespace Seff.Editor

open System
open System.Windows
open System.Windows.Media
open System.Collections.Generic
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.Document
open ICSharpCode.AvalonEdit.Rendering
open FSharp.Compiler.SourceCodeServices
open System.Linq // for First() and Last() on read only collections


//read: http://danielgrunwald.de/coding/AvalonEdit/rendering.php

// taken from //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
// better would be https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/TextMarkerService.cs

    
//[<AllowNullLiteral>] //TODO needed?
type HighlightingSegment private (startOffset, length, message:string, undelineColor:Media.Color option, backbroundColor:Media.Color option) as this =
    inherit TextSegment()
    do
        this.StartOffset <- startOffset
        this.Length <- length
    member val Message =                 message 
    member val UnderlineColor    =  undelineColor
    member val BackgroundColor   =  backbroundColor

    static member CreateForError( startOffset, length, message) = 
             HighlightingSegment (startOffset, length, message, Some Colors.Red , Some Colors.LightSalmon )
    
    static member CreateForWarning (startOffset, length, message) = 
               HighlightingSegment (startOffset, length, message, Some Colors.Green , Some Colors.LightSeaGreen)


    
//[<AllowNullLiteral>] //TODO needed?
type ErrorMarker (textEditor:TextEditor) = 
        
    let markers = new TextSegmentCollection<HighlightingSegment>(textEditor.Document)
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
                match marker.BackgroundColor with 
                |None ->()
                |Some backgroundColor ->                 
                    let geoBuilder = new BackgroundGeometryBuilder (AlignToWholePixels = true, CornerRadius = 3.)
                    geoBuilder.AddSegment(textView, marker) // TODO loop only over this line ,not the others ?
                    let geometry = geoBuilder.CreateGeometry()
                    let brush = new SolidColorBrush(backgroundColor)
                    brush.Freeze()
                    drawingContext.DrawGeometry(brush, null, geometry)

                //foreground
                match marker.UnderlineColor with 
                |None ->()
                |Some underlineColor ->   
                for r in BackgroundGeometryBuilder.GetRectsForSegment(textView, marker) do
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
        
    member this.Layer = KnownLayer.Selection // for IBackgroundRenderer
    member this.Transform(context:ITextRunConstructionContext , elements:IList<VisualLineElement>)=() // needed ? // for IVisualLineTransformer
        
    member this.AddSegments(errs:FSharpErrorInfo[])=        
        for e in errs |> Seq.truncate 5 do // TODO Only highligth the first 3 Errors, Otherwise UI becomes unresponsive at 100 errors ( eg when pasting bad text)// TODO Test again        
            //TODO as an alternative use Visualline transformers like in Log view, do they perform better ?
            let startOffset = textEditor.Document.GetOffset(new TextLocation(e.StartLineAlternate, e.StartColumn + 1 ))
            let endOffset   = textEditor.Document.GetOffset(new TextLocation(e.EndLineAlternate,   e.EndColumn   + 1 ))
            let length      = endOffset-startOffset
            match e.Severity with 
            | FSharpErrorSeverity.Error   -> markers.Add ( HighlightingSegment.CreateForError  ( startOffset, length, e.Message+"\r\nError: "   + (string e.ErrorNumber) ))
            | FSharpErrorSeverity.Warning -> markers.Add ( HighlightingSegment.CreateForWarning( startOffset, length, e.Message+"\r\nWarning: " + (string e.ErrorNumber) )) 
        
        textEditor.TextArea.TextView.Redraw()// or just redraw each segment one by one while adding them?
        
    member this.Clear()= 
        markers.Clear()
        textEditor.TextArea.TextView.Redraw() // redraw all instead of just marker ISegment  ?            

    member this.GetMarkersAtOffset(offset) = markers.FindSegmentsContaining(offset)
        

    interface IBackgroundRenderer with
        member this.Draw(tv,dc) = this.Draw(tv,dc)
        member this.Layer = this.Layer
        
    interface IVisualLineTransformer with // needed ?
        member this.Transform(ctx,es) = this.Transform(ctx,es)
