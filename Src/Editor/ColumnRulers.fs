﻿namespace Seff.Editor

open Seff
open System
open System.Windows
open System.Windows.Media
open System.Collections.Generic
open Seff.Util.General
open ICSharpCode

open ICSharpCode.AvalonEdit.Rendering
open ICSharpCode.AvalonEdit.Utils
open ICSharpCode.AvalonEdit.Editing
open ICSharpCode.AvalonEdit.Folding


type ColumnRulers (editor:AvalonEdit.TextEditor, log: ISeffLog)  as this =
    //https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/Rendering/ColumnRulerRenderer.cs
    
    let columnsInit = [ 0 ; 4; 8; 12 ; 16 ; 20 ; 24 ; 28 ; 32] 
    
    let mutable color = Brushes.White |> darker 35

    let pens =
        [ for _ in columnsInit do             
            let p = new Pen(color, 1.2)
            p.Freeze()
            color <- brighter 3 color   // fade out next ruler        
            p
        ]
        
    let columns = ResizeArray(columnsInit)        


    //TODO
    //let updateMargins _ =
    //    log.PrintInfoMsg "LineNumberMargin.TextView.VisualLinesChanged"
    //    for uiElm in editor.TextArea.LeftMargins do
    //        match uiElm with 
    //        | :? LineNumberMargin as lnm  -> for ln in lnm.TextView.VisualLines do for el in ln.Elements do el.BackgroundBrush <- Brushes.LightGray /// this colors the editor
    //        | :? FoldingMargin as fm ->      for ln in fm.TextView.VisualLines  do for el in ln.Elements do el.BackgroundBrush <- Brushes.LightGray
    //        | x -> ()

    do
        editor.TextArea.TextView.BackgroundRenderers.Add(this)
        //for uiElm in editor.TextArea.LeftMargins do
        //    match uiElm with 
        //    | :? LineNumberMargin as lnm  ->  lnm.TextView.VisualLinesChanged.Add updateMargins
        // e.ShowLineNumbers <- true needs to be done before this event is attached
        //    | x -> ()

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