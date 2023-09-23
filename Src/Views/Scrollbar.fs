namespace Seff.Editor


open System
open System.Windows
open System.Windows.Media
open System.Windows.Input
open System.Threading
open System.Windows.Controls
open System.Windows.Controls.Primitives
open System.Windows.Documents
open System.Windows.Media.Animation

open AvalonEditB
open AvalonEditB.Utils
open AvalonEditB.Document
open AvalonEditB.Rendering
open AvalonLog
open FsEx.Wpf

open Seff
open Seff.Model
open Seff.Config
open Seff.Util.Str


module MagicScrollbar =      
    open Seff.Util.General

  
    // see // https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/EnhancedScrollBar.cs

    type Marks = ResizeArray<int*SolidColorBrush>

    [<AllowNullLiteral>]
    type ScrollbarAdorner(ed:TextEditor, errs:ErrorHighlighter, trackGrid:Grid)  as this =
        inherit Adorner(trackGrid)

        let mutable isActive = false

        let markLineNos = ref (Marks())

        let setLineNos (v: Marks) = 
            if not <| Util.General.areSameBy fst v markLineNos.Value then // compare by fst, that is only the line number ??
                markLineNos.Value <- v
                ed.Dispatcher.Invoke (fun _ -> this.InvalidateVisual()
                )
        
        /// the size of the arrows at the top and bottom of the scrollbar
        let scrollbarArrowsSize = 17. 
                
        do             
            //base.Cursor <- Cursors.Hand
            //base.ToolTip <- "empty"
            
            ed.TextArea.TextView.VisualLinesChanged.Add (fun _ -> if isActive then this.InvalidateVisual())   
            errs.FoundErrors.Add (fun _ -> setLineNos errs.ErrorsLines.Value )
        
        member this.IsActive 
            with get() = isActive 
            and set(v) = isActive <- v

        override this.OnRender(drawingContext : DrawingContext) =   
            if isActive then                
                let renderSize = base.RenderSize                
                let textView = ed.TextArea.TextView
                let documentHeight = textView.DocumentHeight
                let lnNos = markLineNos.Value // this iteration never fails, even if the value in the ref gets replaced while looping
                for i = 0 to lnNos.Count - 1 do 
                    let lnNo, brush = lnNos.[i]

                    let visualTop = textView.GetVisualTopByDocumentLine (lnNo) 
                    let trackHeight = renderSize.Height - scrollbarArrowsSize * 2.
                    let renderPos = ((visualTop / documentHeight) * trackHeight) + scrollbarArrowsSize
                    //eprintfn $"renderPos: {renderPos} visualTop: {visualTop} documentHeight: {documentHeight} trackHeight: {trackHeight} renderSize.Width{renderSize.Width}"
                    
                    let x = 1. //3.
                    let y = renderPos - 1.
                    let width = renderSize.Width //- 6.
                    let height = 2.
                    let rect = new Rect(x, y, width, height)
                    drawingContext.DrawRectangle (brush, null, rect)                          

    type ScrollBarEnhancer(ed:TextEditor, errs:ErrorHighlighter) = 
    
        let vertScrollBar : ScrollBar =
            ed.ApplyTemplate ()  |> ignore                 
            let scrollViewer = ed.Template.FindName ("PART_ScrollViewer", ed) :?> ScrollViewer
            scrollViewer.ApplyTemplate ()|> ignore
            let vScrollBar = scrollViewer.Template.FindName ("PART_VerticalScrollBar", scrollViewer) :?> ScrollBar        
            if isNull vScrollBar then 
                failwithf $"scrollViewer.Template.FindName (\"PART_VerticalScrollBar\")  is null"
            vScrollBar
        
        let mutable adorner: ScrollbarAdorner = null

        let setAdorner() = 
            if isNull adorner then                
                let track =  vertScrollBar.Template.FindName ("PART_Track", vertScrollBar) :?> Track    
                if notNull track then    
                    let trackGrid = VisualTreeHelper.GetParent (track) :?> Grid       
                    let layer = AdornerLayer.GetAdornerLayer (trackGrid) 
                    adorner <- new ScrollbarAdorner(ed, errs, trackGrid)
                    layer.Add (adorner)                    

        
        do 
            setAdorner()
            if notNull adorner then adorner.InvalidateVisual()  

            vertScrollBar.IsVisibleChanged.Add (fun _ -> // when the text is small no scrollbar is visible.
                //eprintfn $"vScrollBar.VisibleChanged: {vertScrollBar.IsVisible}" // this event even happens while normal scrolling ! why ?
                if vertScrollBar.IsVisible then 
                    setAdorner()
                    if notNull adorner then
                        adorner.IsActive <- true
                        adorner.InvalidateVisual()            
                elif notNull adorner then
                    adorner.IsActive <- false
                    adorner.InvalidateVisual()
            )
            