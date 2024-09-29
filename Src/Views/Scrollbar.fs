namespace Fesh.Editor


open System
open System.Windows
open System.Windows.Media
open System.Windows.Controls
open System.Windows.Controls.Primitives
open System.Windows.Documents
open System.Windows.Input

open AvalonEditB
open AvalonEditB.Utils

open Fesh
open Fesh.Util.General

module MagicScrollbar =



    // see // https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/EnhancedScrollBar.cs

    type Marks = ResizeArray<int*SolidColorBrush>

    [<AllowNullLiteral>]
    type ScrollbarAdorner(ed:TextEditor,  errs:ErrorHighlighter, track: Track)  as this = //state:InteractionState,
        inherit Adorner(track)

        let textView = ed.TextArea.TextView

        let mutable isTrackShowing = false

        let markLineNos = ref (Marks())

        let setLineNos (v: Marks) =
            if not <| Util.General.areSameBy fst v markLineNos.Value then // compare by fst, that is only the line number
                markLineNos.Value <- v
                ed.Dispatcher.Invoke (fun _ -> this.InvalidateVisual())

        let visualTopCache = Array.create (ErrorUtil.maxErrorCountToTrack * 4 ) 0.0

        let pixelSize = PixelSnapHelpers.GetPixelSize(textView)

        do
            ed.TextArea.TextView.VisualLinesChanged.Add (fun _ -> if isTrackShowing then this.InvalidateVisual() )
            errs.FoundErrors.Add (fun _                        -> setLineNos errs.ErrorsLines.Value )

        member this.IsTrackShowing
            with get() = isTrackShowing
            and set(v) = isTrackShowing <- v

        override this.OnRender(drawingContext : DrawingContext) =
            if isTrackShowing  then
                //textView.EnsureVisualLines()
                let renderSize = base.RenderSize
                let lineHeight = textView.DefaultLineHeight
                let documentHeight = textView.DocumentHeight
                let lnNos = markLineNos.Value // this iteration never fails, even if the value in the ref gets replaced while looping
                //eprintfn $"ScrollbarAdorner.OnRender: {lnNos.Count} lines to draw"
                for i = 0 to lnNos.Count - 1 do
                    let lnNo, brush = lnNos.[i]
                    let visualTop =
                        try
                            // GetVisualTopByDocumentLine fails with null ref exception if lnNo is bigger than document last line.
                            // Lines where deleted but Marks still has the bigger count because checker has not updated yet.
                            let vt = textView.GetVisualTopByDocumentLine (lnNo)
                            visualTopCache.[i] <- vt
                            vt
                        with _ ->
                            if i < visualTopCache.Length then  // if the line is not visible anymore, use the last known visual top
                                visualTopCache.[i]
                            else
                                9e9 // just skip it

                    if visualTop < documentHeight then   // so that markers are not drawn below the bottom of the scroll track
                        let visualMiddle = visualTop + lineHeight * 0.5      // *0.5 to get text middle
                        let trackHeight = renderSize.Height
                        //eprintfn $"error {i} on line {lnNo}: visualMiddle:{visualMiddle} documentHeight:{documentHeight} trackHeight:{trackHeight}"
                        let renderPos0 = ((visualMiddle / documentHeight) * trackHeight)
                        let renderPos = PixelSnapHelpers.PixelAlign(renderPos0, pixelSize.Height)
                        //let boxHeight = max 2. ((lineHeight / documentHeight) * trackHeight) // to have the line thickness relative to the document height, but min 2.0
                        let boxHeight = pixelSize.Height * 2.0

                        let y = renderPos - boxHeight * 0.5
                        let x = pixelSize.Width
                        let width = renderSize.Width - 2.0 * x
                        let rect = new Rect(x, y, width, boxHeight)
                        drawingContext.DrawRectangle (brush, null, rect)

            //else printfn $"ScrollbarAdorner.OnRender: not showing"


    type ScrollBarEnhancer(ed:TextEditor,  errs:ErrorHighlighter) = // state:InteractionState,

        let vertScrollBar : ScrollBar =
            ed.ApplyTemplate ()  |> ignore
            let scrollViewer = ed.Template.FindName ("PART_ScrollViewer", ed) :?> ScrollViewer
            scrollViewer.ApplyTemplate ()|> ignore
            let vScrollBar = scrollViewer.Template.FindName ("PART_VerticalScrollBar", scrollViewer) :?> ScrollBar
            if isNull vScrollBar then failwithf $"scrollViewer.Template.FindName (\"PART_VerticalScrollBar\")  is null" // never happens
            vScrollBar

        let mutable adorner: ScrollbarAdorner = null

        let setAdorner() =
            if isNull adorner then
                let track =  vertScrollBar.Template.FindName ("PART_Track", vertScrollBar) :?> Track
                if notNull track then
                    //let trackGrid = VisualTreeHelper.GetParent (track) :?> Grid // sharp develop uses this for the adorner layer
                    let layer = AdornerLayer.GetAdornerLayer (track)
                    adorner <- new ScrollbarAdorner(ed, errs, track)
                    layer.Add (adorner)


        do
            setAdorner()
            if notNull adorner then
                adorner.IsTrackShowing <- true
                adorner.InvalidateVisual()

            vertScrollBar.IsVisibleChanged.Add (fun _ -> // when the text is small no scrollbar is visible.
                //eprintfn $"vScrollBar.VisibleChanged: {vertScrollBar.IsVisible}" // this event even happens while normal scrolling ! why ?
                if vertScrollBar.IsVisible then
                    setAdorner()
                    if notNull adorner then
                        adorner.IsTrackShowing <- true
                        adorner.InvalidateVisual()
                elif notNull adorner then
                    adorner.IsTrackShowing <- false
                    adorner.InvalidateVisual()
            )
