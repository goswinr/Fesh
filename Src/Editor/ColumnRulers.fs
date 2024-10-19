namespace Fesh.Editor

open System
open System.Windows
open System.Windows.Media
open System.Collections.Generic

open AvalonEditB
open AvalonEditB.Rendering
open AvalonEditB.Utils
open AvalonEditB.Editing
open AvalonEditB.Folding

open AvalonLog.Brush



type ColumnRulers (editor:TextEditor)  as this =
    //https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/Rendering/ColumnRulerRenderer.cs

    let columnsInit =
        [0 .. 10] |> List.map ( fun i -> i * editor.Options.IndentationSize)
        //[ 0; 4; 8; 12 ; 16 ; 20 ; 24 ; 28 ; 32 ; 36]

    let mutable color = Brushes.White |> darker 24

    // make it more transparent
    let addTransparency (amount:int) (br:SolidColorBrush)  =
        SolidColorBrush(Color.FromArgb(clampToByte (int br.Color.A - amount), br.Color.R, br.Color.G, br.Color.B))


    let pens =
        [
            for _ in columnsInit do
                let p = new Pen(color, 1.1 )
                // color <- brighter 2 color   // fade out next ruler
                color <- addTransparency 20 color   // fade out next ruler
                p.Freeze()
                p
        ]

    let columns = ResizeArray(columnsInit)

    let pixelSize = PixelSnapHelpers.GetPixelSize(editor.TextArea.TextView)

    do
        editor.TextArea.TextView.BackgroundRenderers.Add(this)

        // set color in Margins:
        editor.ShowLineNumbers <- true //needs to be done before iterating margins
        for uiElm in editor.TextArea.LeftMargins do
            let marginColor =  Brushes.White |> darker 8 // set color
            match uiElm with
            | :? LineNumberMargin as lnm ->  lnm.BackgroundColor <- marginColor
            | :? FoldingMargin    as fm  ->  fm.BackgroundColor  <- marginColor
            | _-> ()//log.PrintfnAppErrorMsg "other left margin: %A" uiElm // TODO other left margin: System.Windows.Shapes.Line


    member this.Layer =
        // KnownLayer.Background
        KnownLayer.Selection

    member this.Draw(textView:TextView, drawingContext:DrawingContext) =
        let width = textView.WideSpaceWidth
        for column,pen in Seq.zip columns pens do
            let offset = width * float column
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
