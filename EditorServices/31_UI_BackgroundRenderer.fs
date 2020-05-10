namespace Seff

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


module ColumnRulers = 
    type ColumnRulers (columnsInit: seq<int>) =
        //https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/Rendering/ColumnRulerRenderer.cs
        let pen = 
            let g = 245uy // grey value
            let p = new Pen(new SolidColorBrush(Color.FromRgb(g,g,g)), 1.0) //Default color
            p.Freeze()
            p
        
        let columns = ResizeArray(columnsInit)        

        member this.Layer = KnownLayer.Background
        
        member this.Draw(textView:TextView, drawingContext:DrawingContext) =
            for column in columns do                
                let offset = textView.WideSpaceWidth * float column
                let pixelSize = PixelSnapHelpers.GetPixelSize(textView)
                let markerXPos = PixelSnapHelpers.PixelAlign(offset, pixelSize.Width) - textView.ScrollOffset.X                
                let start = new Point(markerXPos, 0.0);
                let ende =  new Point(markerXPos, Math.Max(textView.DocumentHeight, textView.ActualHeight))            
                drawingContext.DrawLine(pen, start, ende)
        
        member this.SetRulers(editor:AvalonEdit.TextEditor, columnsNew: seq<int>) = // to be able to change them later
            if HashSet(columnsNew).SetEquals(columns) then 
                columns.Clear()
                columns.AddRange(columnsNew)
                editor.TextArea.TextView.InvalidateLayer(this.Layer)

        interface IBackgroundRenderer with  // needed in F#: implementing the interface members as properties too.
            member this.Draw(v,c) = this.Draw(v,c)
            member this.Layer     = this.Layer
