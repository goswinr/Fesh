// https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/EnhancedScrollBar.cs


open System
open System.Collections.Generic
open System.Windows
open System.Windows.Controls
open System.Windows.Controls.Primitives
open System.Windows.Documents
open System.Windows.Input
open System.Windows.Media
open System.Windows.Media.Animation
open System.Windows.Threading
open ICSharpCode.AvalonEdit.Rendering
open ICSharpCode.SharpDevelop
open ICSharpCode.SharpDevelop.Editor
open ICSharpCode.SharpDevelop.Gui
namespace ICSharpCode.AvalonEdit.AddIn
[<TextEditorService>]
type EnhancedScrollBar() =
    inherit IDisposable()
    member val editor = Unchecked.defaultof<TextEditor>
    member val textMarkerService = Unchecked.defaultof<TextMarkerService>
    member val changeWatcher = Unchecked.defaultof<IChangeWatcher>
    member val trackAdorner = Unchecked.defaultof<TrackAdorner> with get, set
    new(editor : TextEditor, textMarkerService : TextMarkerService, changeWatcher : IChangeWatcher) as this = 
        (EnhancedScrollBar ())
        then
            if editor = Unchecked.defaultof<_>
            then raise (new ArgumentNullException("editor") :> System.Exception)
            this.editor <- editor
            this.textMarkerService <- textMarkerService
            this.changeWatcher <- changeWatcher
            editor.Loaded <- editor.Loaded + editor_Loaded
            if editor.IsLoaded
            then this.editor_Loaded (Unchecked.defaultof<_>, Unchecked.defaultof<_>)
    member this.Dispose() = 
        this.editor.Loaded <- this.editor.Loaded - editor_Loaded
        if this.trackAdorner :> obj <> Unchecked.defaultof<_>
        then 
            this.trackAdorner.Remove ()
            this.trackAdorner <- Unchecked.defaultof<_>
        ()
    member val isUIInitialized = Unchecked.defaultof<System.Boolean> with get, set
    member this.editor_Loaded(sender : System.Object, e : RoutedEventArgs) = 
        if this.isUIInitialized
        then ()
        this.isUIInitialized <- true
        this.editor.ApplyTemplate ()
        let mutable scrollViewer = (this.editor.Template.FindName ("PART_ScrollViewer", this.editor)) :> ScrollViewer
        if scrollViewer = Unchecked.defaultof<_>
        then ()
        scrollViewer.ApplyTemplate ()
        let mutable vScrollBar = (scrollViewer.Template.FindName ("PART_VerticalScrollBar", scrollViewer)) :> ScrollBar
        if vScrollBar = Unchecked.defaultof<_>
        then ()
        let mutable (track : Track) = vScrollBar.Template.FindName ("PART_Track", vScrollBar)
        if track = Unchecked.defaultof<_>
        then ()
        let mutable (grid : Grid) = VisualTreeHelper.GetParent (track) :?> Grid
        if grid = Unchecked.defaultof<_>
        then ()
        let mutable layer = AdornerLayer.GetAdornerLayer (grid)
        if layer = Unchecked.defaultof<_>
        then ()
        this.trackAdorner <- new TrackAdorner(this, grid)
        layer.Add (this.trackAdorner)
    static member GetBrush(markerColor : Color) = 
        let mutable (brush : SolidColorBrush) = new SolidColorBrush(markerColor)
        brush.Freeze ()
        brush
    type TrackAdorner() =
        inherit Adorner()
        static member val triangleGeometry = (TrackAdorner.CreateTriangleGeometry ())
        static member CreateTriangleGeometry() = 
            let mutable triangleGeometry = new StreamGeometry()
            let __ = 
                use ctx = triangleGeometry.Open ()
                let mutable (triangleSize : float) = 6.5
                let mutable (right : float) = triangleSize * 0.866 / 2
                let mutable (left : float) = - right
                ctx.BeginFigure (new Point(left, (triangleSize / 2)), true, true)
                ctx.LineTo (new Point(left, (- triangleSize / 2)), true, false)
                ctx.LineTo (new Point(right, 0), true, false)
            triangleGeometry.Freeze ()
            triangleGeometry
        member val editor = Unchecked.defaultof<TextEditor>
        member val textMarkerService = Unchecked.defaultof<TextMarkerService>
        new(enhanchedScrollBar : EnhancedScrollBar, trackGrid : Grid) as this = 
            (TrackAdorner ())
            then
                this.editor <- enhanchedScrollBar.editor
                this.textMarkerService <- enhanchedScrollBar.textMarkerService
                this.Cursor <- Cursors.Hand
                this.ToolTip <- string.Empty
                this.textMarkerService.RedrawRequested <- this.textMarkerService.RedrawRequested + RedrawRequested
                this.editor.TextArea.TextView.VisualLinesChanged <- this.editor.TextArea.TextView.VisualLinesChanged + VisualLinesChanged
        member this.Remove() = 
            this.textMarkerService.RedrawRequested <- this.textMarkerService.RedrawRequested - RedrawRequested
            this.editor.TextArea.TextView.VisualLinesChanged <- this.editor.TextArea.TextView.VisualLinesChanged - VisualLinesChanged
            let mutable layer = AdornerLayer.GetAdornerLayer (AdornedElement)
            if layer <> Unchecked.defaultof<_>
            then layer.Remove (this)
            ()
        member this.RedrawRequested(sender : System.Object, e : EventArgs) = 
            InvalidateVisual ()
        member this.VisualLinesChanged(sender : System.Object, e : EventArgs) = 
            InvalidateVisual ()
        override this.OnRender(drawingContext : DrawingContext) = 
            let mutable renderSize = this.RenderSize
            let mutable document = this.editor.Document
            let mutable textView = this.editor.TextArea.TextView
            let mutable (documentHeight : float) = textView.DocumentHeight
            for marker in this.textMarkerService.TextMarkers do
                if not (IsVisibleInAdorner (marker))
                then (* ERROR ContinueNotSupported "ContinueStatementSyntax" continue; *)
                let mutable location = document.GetLocation (marker.StartOffset)
                let mutable (visualTop : float) = textView.GetVisualTopByDocumentLine (location.Line)
                let mutable (renderPos : float) = visualTop / documentHeight * renderSize.Height
                let mutable brush = GetBrush (marker.MarkerColor)
                let mutable (isLineOrCircle : System.Boolean) = false
                if marker.MarkerTypes & TextMarkerTypes.LineInScrollBar <> 0
                then 
                    drawingContext.DrawRectangle (brush, Unchecked.defaultof<_>, new Rect(3, (renderPos - 1), (renderSize.Width - 6), 2))
                    isLineOrCircle <- true
                if marker.MarkerTypes & TextMarkerTypes.CircleInScrollBar <> 0
                then 
                    let mutable (radius : float) = 3
                    drawingContext.DrawEllipse (brush, Unchecked.defaultof<_>, new Point((renderSize.Width / 2), renderPos), radius, radius)
                    isLineOrCircle <- true
                if not isLineOrCircle
                then 
                    let mutable translateTransform = new TranslateTransform(6, renderPos)
                    translateTransform.Freeze ()
                    drawingContext.PushTransform (translateTransform)
                    if marker.MarkerTypes & TextMarkerTypes.ScrollBarLeftTriangle <> 0
                    then 
                        let mutable scaleTransform = new ScaleTransform((- 1), 1)
                        scaleTransform.Freeze ()
                        drawingContext.PushTransform (scaleTransform)
                        drawingContext.DrawGeometry (brush, Unchecked.defaultof<_>, TrackAdorner.triangleGeometry)
                        drawingContext.Pop ()
                    if marker.MarkerTypes & TextMarkerTypes.ScrollBarRightTriangle <> 0
                    then drawingContext.DrawGeometry (brush, Unchecked.defaultof<_>, TrackAdorner.triangleGeometry)
                    drawingContext.Pop ()
            ()
        member this.IsVisibleInAdorner(marker : ITextMarker) = 
            marker.MarkerTypes & TextMarkerTypes.ScrollBarLeftTriangle | TextMarkerTypes.ScrollBarRightTriangle | TextMarkerTypes.LineInScrollBar | TextMarkerTypes.CircleInScrollBar <> 0
        override this.OnMouseDown(e : MouseButtonEventArgs) = 
            ``base``.OnMouseDown (e)
            let mutable marker = FindNextMarker (e.GetPosition (this))
            if marker <> Unchecked.defaultof<_>
            then 
                let mutable location = this.editor.Document.GetLocation (marker.StartOffset)
                let mutable textEditor = this.editor.TextArea.GetService (typeof<ITextEditor>) :?> ITextEditor
                if textEditor <> Unchecked.defaultof<_>
                then textEditor.JumpTo (location.Line, location.Column)
                else this.editor.ScrollTo (location.Line, location.Column)
                e.Handled <- true
            ()
        member this.FindNextMarker(mousePos : Point) = 
            let mutable renderSize = this.RenderSize
            let mutable document = this.editor.Document
            let mutable textView = this.editor.TextArea.TextView
            let mutable (documentHeight : float) = textView.DocumentHeight
            let mutable (bestMarker : ITextMarker) = Unchecked.defaultof<_>
            let mutable (bestDistance : float) = double.PositiveInfinity
            for marker in this.textMarkerService.TextMarkers do
                if not (IsVisibleInAdorner (marker))
                then (* ERROR ContinueNotSupported "ContinueStatementSyntax" continue; *)
                let mutable location = document.GetLocation (marker.StartOffset)
                let mutable (visualTop : float) = textView.GetVisualTopByDocumentLine (location.Line)
                let mutable (renderPos : float) = visualTop / documentHeight * renderSize.Height
                let mutable (distance : float) = Math.Abs (renderPos - mousePos.Y)
                if distance < bestDistance
                then 
                    bestDistance <- distance
                    bestMarker <- marker
            bestMarker
        override this.OnToolTipOpening(e : ToolTipEventArgs) = 
            ``base``.OnToolTipOpening (e)
            let mutable marker = FindNextMarker (Mouse.GetPosition (this))
            if marker <> Unchecked.defaultof<_> && marker.ToolTip <> Unchecked.defaultof<_>
            then this.ToolTip <- marker.ToolTip
            else e.Handled <- true