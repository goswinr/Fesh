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

    type ScrollbarAdorner(ed:TextEditor, errs:ErrorHighlighter) =  //, trackGrid:Grid)  as this =
        inherit Adorner(trackGrid)

        let mutable setupPending = true


        let mutable isVisible = false

        let rec init() = 
            if setupPending then 
                ed.ApplyTemplate ()  |> ignore 
                
                let scrollViewer = ed.Template.FindName ("PART_ScrollViewer", ed) :?> ScrollViewer
                scrollViewer.ApplyTemplate ()|> ignore 
                
                let vScrollBar = scrollViewer.Template.FindName ("PART_VerticalScrollBar", scrollViewer) :?> ScrollBar        
                if isNull vScrollBar then 
                    eprintfn $"ScrollbarAdorner: vScrollBar is null"
                else
                    vScrollBar.IsVisibleChanged.Add (fun _ -> isVisible <- vScrollBar.IsVisible)
                    let track =  vScrollBar.Template.FindName ("PART_Track", vScrollBar) :?> Track    
                    if isNull track then 
                        eprintfn $"ScrollbarAdorner: track is null, setting loaded callback"
                        vScrollBar.Loaded.Add(fun _ -> init() )
                    else                 
                        let trackGrid = VisualTreeHelper.GetParent (track) :?> Grid       
                        let layer = AdornerLayer.GetAdornerLayer (trackGrid) 
                        let trackArdorner = ScrollbarAdorners(ed, errs, trackGrid)
                        layer.Add (this)
                        setupPending <- false

        let markLineNos = ref (Marks())

        let setLineNos (v: Marks) = 
            if not <| Util.General.areSameBy fst v markLineNos.Value then // compare by fst, that is only the line number ??
                markLineNos.Value <- v
                ed.Dispatcher.Invoke (fun _ -> this.InvalidateVisual()
                )
                
        do 
            init()
            //base.Cursor <- Cursors.Hand
            //base.ToolTip <- "empty"
            
            ed.TextArea.TextView.VisualLinesChanged.Add (fun _ -> if isVisible then this.InvalidateVisual())   
            errs.FoundErrors.Add (fun _ -> setLineNos errs.ErrorsLines.Value )
                           
        override this.OnRender(drawingContext : DrawingContext) =   
            if isVisible then
                let renderSize = base.RenderSize
                let document = ed.Document
                let textView = ed.TextArea.TextView
                let documentHeight = textView.DocumentHeight
                let lnNos = markLineNos.Value // this iteration never fails, even if the value in the ref gets replaced while looping
                for i = 0 to lnNos.Count - 1 do 
                    let lnNo, brush = lnNos.[i]
                    let visualTop = textView.GetVisualTopByDocumentLine (lnNo)
                    let renderPos = visualTop / documentHeight * renderSize.Height                
                    let rect = new Rect(3, renderPos - 1., renderSize.Width - 6., 2.)
                    drawingContext.DrawRectangle (brush, null, rect)                          
