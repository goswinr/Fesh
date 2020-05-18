namespace Seff.Editor

open Seff
open System
open System.Windows
open System.Windows.Media
open System.Collections.Generic
open Seff.Util.General
open ICSharpCode

open ICSharpCode.AvalonEdit.Rendering
open ICSharpCode.AvalonEdit.Utils


//TODO this si currently not used

type ColumnRulers (editor:AvalonEdit.TextEditor, columnsInit: seq<int>)  as this =
    //https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/Rendering/ColumnRulerRenderer.cs
    
    let columnsInit = [ 0 ; 4; 8; 12 ; 16] 
    
    let mutable color = Brushes.White |> darker 20

    let pens =
        [ for col in columnsInit do             
            let p = new Pen(color, 1.0)
            color <- color |>  brighter 4 // fade out rulers
            p.Freeze()
            p
        ]
        
    let columns = ResizeArray(columnsInit)        

    do
        editor.TextArea.TextView.BackgroundRenderers.Add(this)

    member this.Layer = KnownLayer.Background
        
    member this.Draw(textView:TextView, drawingContext:DrawingContext) =
        for column,pen in Seq.zip columns pens do                
            let offset = textView.WideSpaceWidth * float column
            let pixelSize = PixelSnapHelpers.GetPixelSize(textView)
            let markerXPos = PixelSnapHelpers.PixelAlign(offset, pixelSize.Width) - textView.ScrollOffset.X                
            let start = new Point(markerXPos, 0.0);
            let ende =  new Point(markerXPos, Math.Max(textView.DocumentHeight, textView.ActualHeight)) 
            drawingContext.DrawLine(pen, start, ende)
        
    member this.SetRulers( columnsNew: seq<int>) = // to be able to change them later
        if HashSet(columnsNew).SetEquals(columns) then 
            columns.Clear()
            columns.AddRange(columnsNew)
            editor.TextArea.TextView.InvalidateLayer(this.Layer)


    interface IBackgroundRenderer with  // needed in F#: implementing the interface members as properties too.
        member this.Draw(v,c) = this.Draw(v,c)
        member this.Layer     = this.Layer