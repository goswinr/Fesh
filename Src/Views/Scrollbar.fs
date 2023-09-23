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
    open System

  
    // see // https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/EnhancedScrollBar.cs

    type Marks = ResizeArray<int*SolidColorBrush>

    [<AllowNullLiteral>]
    type ScrollbarAdorner(ed:TextEditor, state:InteractionState, errs:ErrorHighlighter, track: Track)  as this =        
        inherit Adorner(track)

        let textView = ed.TextArea.TextView

        let mutable idOfMarks = 0L

        let mutable isTrackShowing = false

        let markLineNos = ref (Marks())

        let setLineNos (v: Marks) = 
            if not <| Util.General.areSameBy fst v markLineNos.Value then // compare by fst, that is only the line number
                markLineNos.Value <- v
                idOfMarks <- state.DocChangedId.Value
                ed.Dispatcher.Invoke (fun _ -> this.InvalidateVisual())      

        let visualTopCache = Array.create (ErrorUtil.maxErrorCountToTrack*4) 0.0
                
        do             
            //base.Cursor <- Cursors.Hand // https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/EnhancedScrollBar.cs
            //base.ToolTip <- "empty"
            
            ed.TextArea.TextView.VisualLinesChanged.Add (fun _ -> if isTrackShowing then this.InvalidateVisual() )   
            errs.FoundErrors.Add (fun _                        -> if isTrackShowing then setLineNos errs.ErrorsLines.Value )
        
        member this.IsTrackShowing 
            with get() = isTrackShowing 
            and set(v) = isTrackShowing <- v

        override this.OnRender(drawingContext : DrawingContext) =   
            if isTrackShowing  then
                //textView.EnsureVisualLines() // this is needed to get the correct visual line count
                let renderSize = base.RenderSize
                let lineHeight = textView.DefaultLineHeight
                let documentHeight = textView.DocumentHeight
                let lnNos = markLineNos.Value // this iteration never fails, even if the value in the ref gets replaced while looping                            
                for i = 0 to lnNos.Count - 1 do 
                    let lnNo, brush = lnNos.[i]
                    let visualTop =
                        if state.IsLatest idOfMarks then 
                            // GetVisualTopByDocumentLine fails with null ref exception if lnNo is bigger than document 
                            // (lines where deleted but Marks still has the bigger count because checker has not updated yet)
                            let vt = textView.GetVisualTopByDocumentLine (lnNo) 
                            visualTopCache.[i] <- vt
                            vt
                        else
                            visualTopCache.[i]

                    if visualTop  < documentHeight then   // so that markers are not drawn below the bottom of the scroll track
                        let visualMiddle = visualTop + lineHeight * 0.5      // *0.5 to get text middle              
                        let trackHeight = renderSize.Height 
                        let renderPos = ((visualMiddle / documentHeight) * trackHeight) 
                        let boxHeight = max 1. ((lineHeight / documentHeight) * trackHeight) 
                        
                        let x = 0. //3.
                        let y = renderPos - boxHeight * 0.5 
                        let width = renderSize.Width //- 6.                    
                        let rect = new Rect(x, y, width, boxHeight)
                        drawingContext.DrawRectangle (brush, null, rect)       
                  

    type ScrollBarEnhancer(ed:TextEditor, state:InteractionState, errs:ErrorHighlighter) = 
    
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
                    adorner <- new ScrollbarAdorner(ed,state, errs, track)
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
            