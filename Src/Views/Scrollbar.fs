namespace Fesh.Editor


open System
open Avalonia
open Avalonia.Media
open Avalonia.Media.Immutable

open Avalonia.Controls
open Avalonia.Controls.Primitives
// open Avalonia.Documents
open Avalonia.Input
open Avalonia.VisualTree

open AvaloniaEdit
open AvaloniaEdit.Utils

open Fesh
open Fesh.Util.General

module MagicScrollbar =

    /// Finds a named part of a control template.
    /// control.FindControl<'T>(name) only works for names registered in a XAML name scope, not for template parts.
    /// Returns null if the template is not applied yet or the part does not exist.
    let findTemplatePart<'T when 'T :> Control and 'T : null> (name:string) (parent:Visual) : 'T =
        parent.GetVisualDescendants()
        |> Seq.tryPick (fun v ->
            match v with
            | :? Control as c when c.Name = name ->
                match box c with
                | :? 'T as t -> Some t
                | _ -> None
            | _ -> None)
        |> Option.toObj

    let forceScrollBarWidth18 (scrollViewer: ScrollViewer) =
        scrollViewer.TemplateApplied.Add(fun e ->
            // Find the scrollbars in the template
            let verticalScrollBar = e.NameScope.Find<ScrollBar>("PART_VerticalScrollBar")
            let horizontalScrollBar = e.NameScope.Find<ScrollBar>("PART_HorizontalScrollBar")

            match verticalScrollBar with
            | null -> ()
            | bar ->
                bar.Width <- 18.0
                bar.MinWidth <- 18.0

            match horizontalScrollBar with
            | null -> ()
            | bar ->
                bar.Height <- 18.0
                bar.MinHeight <- 18.0
        )


    // initially take from see https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/EnhancedScrollBar.cs
    //  adapted to Avalonia https://stackoverflow.com/questions/63598245/analogous-to-the-abstract-adorner-wpf-class-in-avalonia-ui

    type Marks = ResizeArray<int*ImmutableSolidColorBrush>


    [<AllowNullLiteral>]
    type ScrollbarAdorner(ed:TextEditor,  errs:ErrorHighlighter, track: Track)  as this = //state:InteractionState,
        // inherit Adorner(track)
        inherit Control() // Adorner is not available in Avalonia, so we use Control instead

        let textView = ed.TextArea.TextView

        let mutable isTrackShowing = false

        let markLineNos = ref (Marks())

        let setLineNos (v: Marks) =
            if not <| Util.General.areSameBy fst v markLineNos.Value then // compare by fst, that is only the line number
                markLineNos.Value <- v
                Fittings.SyncContext.post (fun _ -> this.InvalidateVisual())

        let visualTopCache = Array.create (ErrorUtil.maxErrorCountToTrack * 4 ) 0.0

        let pixelSize = PixelSnapHelpers.GetPixelSize(textView)

        let subscriptions : IDisposable list = [
            ed.TextArea.TextView.VisualLinesChanged.Subscribe (fun _ -> if isTrackShowing then this.InvalidateVisual() )
            errs.FoundErrors.Subscribe (fun _                        -> setLineNos errs.ErrorsLines.Value )
            ]

        /// removes the event handlers of this adorner from the editor and removes it from the adorner layer
        member this.Detach() =
            for s in subscriptions do s.Dispose()
            let adornerLayer = AdornerLayer.GetAdornerLayer track
            if notNull adornerLayer then
                adornerLayer.Children.Remove this |> ignore

        member this.IsTrackShowing
            with get() = isTrackShowing
            and set(v) = isTrackShowing <- v

        override this.Render(drawingContext : DrawingContext) =
            if isTrackShowing  then
                //textView.EnsureVisualLines()
                let renderSize = track.Bounds.Size
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

        override _.StyleKeyOverride = typeof<Control> // see https://github.com/AvaloniaUI/Avalonia/discussions/18697


    type ScrollBarEnhancer(ed:TextEditor,  errs:ErrorHighlighter) = // state:InteractionState,

        /// null if the templates are not applied yet.
        let vertScrollBar : ScrollBar =
            ed.ApplyTemplate()
            let scrollViewer = findTemplatePart<ScrollViewer> "PART_ScrollViewer" ed
            if isNull scrollViewer then
                null
            else
                scrollViewer.ApplyTemplate()
                findTemplatePart<ScrollBar> "PART_VerticalScrollBar" scrollViewer

        let mutable adorner: ScrollbarAdorner = null

        let setAdorner() =
            if isNull adorner && notNull vertScrollBar then
                vertScrollBar.ApplyTemplate()
                let track = findTemplatePart<Track> "PART_Track" vertScrollBar
                if notNull track then
                    let adornerLayer = AdornerLayer.GetAdornerLayer track //adornerElement
                    if notNull adornerLayer then
                        adorner <- new ScrollbarAdorner(ed, errs, track)
                        // https://stackoverflow.com/questions/63598245/analogous-to-the-abstract-adorner-wpf-class-in-avalonia-ui
                        adornerLayer.Children.Add(adorner)
                        AdornerLayer.SetAdornedElement(adorner, track) //adornerElement)

        do
            setAdorner()
            if notNull adorner then
                adorner.IsTrackShowing <- true
                adorner.InvalidateVisual()

        let visibilitySubscription : IDisposable =
            if isNull vertScrollBar then
                null
            else
                vertScrollBar.PropertyChanged.Subscribe (fun e -> // when the text is small no scrollbar is visible.
                    if e.Property = Visual.IsVisibleProperty then
                        if vertScrollBar.IsVisible then
                            setAdorner()
                            if notNull adorner then
                                adorner.IsTrackShowing <- true
                                adorner.InvalidateVisual()
                        elif notNull adorner then
                            adorner.IsTrackShowing <- false
                            adorner.InvalidateVisual()
                )

        /// Removes the adorner and all event handlers.
        /// Call this before creating a new ScrollBarEnhancer for the same editor.
        member _.Detach() =
            if notNull visibilitySubscription then
                visibilitySubscription.Dispose()
            if notNull adorner then
                adorner.Detach()
                adorner <- null
