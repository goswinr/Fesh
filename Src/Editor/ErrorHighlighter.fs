namespace Seff.Editor

open System
open System.Linq // for First() and Last() on read only collections //TODO replace
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Collections.Generic

open FSharp.Compiler
open FSharp.Compiler.Diagnostics

open AvalonEditB
open AvalonEditB.Document
open AvalonEditB.Rendering

open AvalonLog.Brush

open Seff
open Seff.Util
open Seff.Model


//read: http://danielgrunwald.de/coding/AvalonEdit/rendering.php

// taken from //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
// better would be https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/Textsegmentservice.cs


module ErrorStyle= 
    let errSquiggle     = Pen(  Brushes.Red     |> darker 20      |> freeze, 1.0) |> Pen.freeze
    let errBackGr       =       Brushes.Red     |> brighter 220   |> freeze

    let warnSquiggle    = Pen(  Brushes.Yellow  |> darker 40      |> freeze, 1.0) |> Pen.freeze
    let warnBackGr      =       Brushes.Yellow  |> brighter 200   |> freeze

    let infoSquiggle    = Pen(  Brushes.Green  |> darker 5      |> freeze, 1.0) |> Pen.freeze
    let infoBackGr      =       Brushes.Green  |> brighter 220   |> freeze

module ErrorUtil =    
        
    let mutable private scrollToIdx = -1

    /// because the UI gets a lag if there are 100 of errors to draw
    let maxAmountOfErrorsToDraw = 12 

    /// split errors by severity and sort by line number 
    let getBySeverity(checkRes:CodeAnalysis.FSharpCheckFileResults) :ErrorsBySeverity =
        scrollToIdx <- -1
        let was = ResizeArray()  // Warnings
        let ers = ResizeArray()  // Errors
        let ins = ResizeArray()  // Infos        
        let his = ResizeArray()  // Hidden
        let erWs = ResizeArray()  // Errors and Warnings
        for e in checkRes.Diagnostics do
            match e.Severity with
            | FSharpDiagnosticSeverity.Error   -> ers.Add e ; erWs.Add e
            | FSharpDiagnosticSeverity.Warning -> was.Add e ; erWs.Add e
            | FSharpDiagnosticSeverity.Hidden  -> his.Add e
            | FSharpDiagnosticSeverity.Info    -> if e.ErrorNumber <> 3370 then ins.Add e   //  exclude infos about ref cell incrementing ??

        // make sure they are sorted , the tools below will then truncate this list to only mark the first 9 or so errors in the UI (for performance)
        was.Sort( fun x y -> Operators.compare x.StartLine y.StartLine)
        ers.Sort( fun x y -> Operators.compare x.StartLine y.StartLine)
        ins.Sort( fun x y -> Operators.compare x.StartLine y.StartLine)
        his.Sort( fun x y -> Operators.compare x.StartLine y.StartLine)
        erWs.Sort(fun x y -> Operators.compare x.StartLine y.StartLine)
        { errors = ers; warnings = was; infos = ins; hiddens = his; errorsAndWarnings = erWs }
    

   

    /// because FSharpDiagnostic might have line number 0 form Parse-and-check-file-in-project errors, but Avalonedit starts at 1
    let inline linesStartAtOne i = if i<1 then 1 else i 

    let getSegment (doc:TextDocument) ( e:FSharpDiagnostic) =
        try
            let s = TextSegment()
            let st = doc.GetOffset(new TextLocation(linesStartAtOne e.StartLine, e.StartColumn + 1 ))
            let en = doc.GetOffset(new TextLocation(linesStartAtOne e.EndLine  , e.EndColumn   + 1 ))
            if st<en then 
                s.StartOffset <- st
                s.EndOffset  <-  en
            elif st>en then // should never happen // this FCS bug has happened in the past, for Parse-and-check-file-in-project errors the segments can be wrong
                s.StartOffset <- en
                s.EndOffset  <-  st 
            else // st=en  // should never happen
                s.StartOffset <- st
                s.EndOffset   <- st + 1 // just in case, so it is at least on char long
            Some s 
         with 
            //In a rare race condition the segment is beyond the end of the document because it was just deleted:
            | :? ArgumentOutOfRangeException -> 
                None 
            | e -> 
                raise e

    
    let getSquiggleLine(r:Rect):StreamGeometry = 
        let startPoint = r.BottomLeft
        let endPoint = r.BottomRight
        let offset = 2.5
        let count = max 4 (int((endPoint.X - startPoint.X)/offset) + 1)
        let geometry = new StreamGeometry()
        use ctx = geometry.Open()
        ctx.BeginFigure(startPoint, false, false)
        ctx.PolyLineTo(
            [| for i=0 to count - 1 do yield new Point( startPoint.X + (float i * offset) , startPoint.Y - if (i + 1) % 2 = 0 then offset else 0.) |] , // for Squiggly line
            true, 
            false)
        geometry.Freeze()
        geometry  
    

    let getNextErrrorIdx( ews:ResizeArray<FSharpDiagnostic> ) =
        if ews.Count=0 then 
            -1
        elif scrollToIdx >= ews.Count-1  || scrollToIdx >= maxAmountOfErrorsToDraw then 
            scrollToIdx <- 0   
            0
        else         
            scrollToIdx <- scrollToIdx + 1            
            scrollToIdx

    let rec getNextSegment(ed:IEditor)=         
        match ed.FileCheckState with 
        | Done res ->
            let ews = res.errors.errorsAndWarnings
            if ews.Count=0 then 
                None 
            else  
                let i = getNextErrrorIdx ews
                if i < 0 then 
                    None
                elif i=0 then 
                    getSegment ed.AvaEdit.Document ews[i]
                else 
                    let p = ews[i-1]
                    let t = ews[i  ]
                    if p.StartLine = t.StartLine then // loop on if not first and same line as last
                        getNextSegment(ed)
                    else                
                        getSegment ed.AvaEdit.Document t

        | NotStarted |  GettingCode _ | Checking _| CheckFailed -> 
            None           

type RedrawSegment(startOffset,  endOffset)  = 
    member s.Offset      = startOffset
    member s.EndOffset   = endOffset
    member s.Length      = endOffset - startOffset
    
    override s.ToString() = $"RedrawSegment form: {s.Offset}, len:{s.Length}"

    interface ISegment with 
        member s.Offset      = startOffset
        member s.EndOffset   = endOffset
        member s.Length      = endOffset - startOffset  
        
    member t.Merge (o:RedrawSegment) = 
        new RedrawSegment(
            min t.Offset o.Offset, 
            max t.EndOffset o.EndOffset )
        
/// This segment also contains back and foreground color and diagnostic display text
type SegmentToMark (startOffset, length, e:FSharpDiagnostic)  = 
    inherit TextSegment()

    let underlinePen = 
        match e.Severity with
        | FSharpDiagnosticSeverity.Info    -> ErrorStyle.infoSquiggle
        | FSharpDiagnosticSeverity.Hidden  -> ErrorStyle.infoSquiggle
        | FSharpDiagnosticSeverity.Warning -> ErrorStyle.warnSquiggle
        | FSharpDiagnosticSeverity.Error   -> ErrorStyle.errSquiggle 
    let backgroundBrush =
        match e.Severity with
        | FSharpDiagnosticSeverity.Hidden  -> ErrorStyle.infoBackGr
        | FSharpDiagnosticSeverity.Info    -> ErrorStyle.infoBackGr
        | FSharpDiagnosticSeverity.Warning -> ErrorStyle.warnBackGr
        | FSharpDiagnosticSeverity.Error   -> ErrorStyle.errBackGr 
    let msg = 
        match e.Severity with
        | FSharpDiagnosticSeverity.Hidden  -> sprintf "• Hidden Info: %s: %s"  e.ErrorNumberText e.Message 
        | FSharpDiagnosticSeverity.Info    -> sprintf "• Info: %s: %s"         e.ErrorNumberText e.Message 
        | FSharpDiagnosticSeverity.Warning -> sprintf "• Warning: %s: %s"      e.ErrorNumberText e.Message 
        | FSharpDiagnosticSeverity.Error   -> sprintf "• Error: %s: %s"        e.ErrorNumberText e.Message 
  
    do
        base.StartOffset <- startOffset
        base.EndOffset   <- startOffset + length
        base.Length      <- length

    member _.Message           =  msg
    member _.Diagnostic        =  e
    member _.Severity          =  e.Severity 
    member _.UnderlinePen      =  underlinePen
    member _.BackgroundBrush   =  backgroundBrush


/// IBackgroundRenderer and IVisualLineTransformer
type ErrorRenderer (ed:TextEditor, folds:Folding.FoldingManager, log:ISeffLog) = 

    let doc = ed.Document
    let txA = ed.TextArea
    let segments = new TextSegmentCollection<SegmentToMark>(doc)
    let mutable prevHash = 0L 
    let mutable prevSeg = None


    /// Draw the error squiggle  on the code
    member _.Draw (textView:TextView , drawingContext:DrawingContext) = // for IBackgroundRenderer
        try
            let vls = textView.VisualLines
            if textView.VisualLinesValid  && vls.Count > 0 then
                let  viewStart = vls.First().FirstDocumentLine.Offset
                let  viewEnd =   vls.Last().LastDocumentLine.EndOffset
                for segment in segments.FindOverlappingSegments(viewStart, viewEnd - viewStart) do
                    // background color:
                    let geoBuilder = new BackgroundGeometryBuilder (AlignToWholePixels = true, CornerRadius = 0.)
                    geoBuilder.AddSegment(textView, segment)
                    let boundaryPolygon= geoBuilder.CreateGeometry() // creates one boundary round the text
                    drawingContext.DrawGeometry(segment.BackgroundBrush, null, boundaryPolygon)

                    //foreground,  squiggles:
                    for rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment) do
                        let geo = ErrorUtil.getSquiggleLine(rect)
                        drawingContext.DrawGeometry(Brushes.Transparent, segment.UnderlinePen, geo)
                        //break //TODO why break in original code on //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
        with ex ->
            log.PrintfnAppErrorMsg "ERROR in ErrorRenderer.Draw: %A" ex

    member _.Layer = KnownLayer.Selection // for IBackgroundRenderer
    member _.Transform(context:ITextRunConstructionContext , elements:IList<VisualLineElement>)=() // TODO needed ? // for IVisualLineTransformer

    /// Update list of Segments to actually mark (first nine only per Severity) and ensure drawing the error squiggle on the surrounding folding box too
    member _.AddSegments( res: CheckResults ) =         
        let mutable hash = 0L
        let mutable firstOff = -1
        let mutable lastOff = -1
        let mark(e:FSharpDiagnostic) =             
            match ErrorUtil.getSegment doc e with 
            |None -> ()
            |Some seg -> 
                let st = seg.StartOffset
                firstOff <- min firstOff st
                lastOff  <- max lastOff seg.EndOffset
                let segToMark = SegmentToMark ( st, seg.Length, e )
                hash <- hash <<< 7
                hash <- hash + int64 st
                hash <- hash <<< 7
                hash <- hash + int64 seg.Length
                segments.Add (segToMark)
                for fold in folds.GetFoldingsContaining(st) do
                    //if fold.IsFolded then // do on all folds !
                    //fold.BackbgroundColor  <- ErrorStyle.errBackGr // done via ctx.DrawRectangle(ErrorStyle.errBackGr
                    fold.DecorateRectangle <- 
                        Action<Rect,DrawingContext>( fun rect ctx ->
                            let geo = ErrorUtil.getSquiggleLine(rect)
                            if isNull fold.BackgroundColor then ctx.DrawRectangle(segToMark.BackgroundBrush, null, rect) // in case of selection highlighting skip brush only use Pen                        
                            ctx.DrawGeometry(Brushes.Transparent, segToMark.UnderlinePen, geo)
                            )  
        // first clear:
        if segments.Count > 0 then
            segments.Clear()
            for fold in folds.AllFoldings do fold.DecorateRectangle <- null
        // then refill:
        let es = res.errors
        for h in es.hiddens  |> Seq.truncate ErrorUtil.maxAmountOfErrorsToDraw  do mark(h)    // TODO only highlight the first 9 ?            
        for i in es.infos    |> Seq.truncate ErrorUtil.maxAmountOfErrorsToDraw  do mark(i)                
        for w in es.warnings |> Seq.truncate ErrorUtil.maxAmountOfErrorsToDraw  do mark(w)                
        for e in es.errors   |> Seq.truncate ErrorUtil.maxAmountOfErrorsToDraw  do mark(e)   // draw errors last, after warnings, to be on top of them!   
        
        // to only redraw on chnages:
        if prevHash <> hash then 
            prevHash <- hash
            if lastOff > 0 then 
                let seg = RedrawSegment(firstOff,lastOff)
                match prevSeg with 
                |Some s -> 
                    let m = seg.Merge(s)                    
                    //ISeffLog.printnColor 200 0 0 "Err Segs:prev seg merged redraw"
                    txA.TextView.Redraw(m)
                    prevSeg <- Some seg
                |None ->
                    //ISeffLog.printnColor 200 0 0 "Err Segs:segredraw"
                    txA.TextView.Redraw(seg)
                    prevSeg <- Some seg
                
            
            else // no errors found, clear if not done yet
                match prevSeg with 
                |Some s -> 
                    //ISeffLog.printnColor 200 0 0 "Err Segs: prev seg redraw"
                    txA.TextView.Redraw(s)
                    prevSeg <- None
                |None ->
                    //ISeffLog.printnColor 0 222 0 "Err Segs:no AddSegments redraw"
                    ()
        //else
            //ISeffLog.printnColor 0 222 0 "Err Segs:no AddSegments redraw no hash"


    member _.GetSegmentsAtOffset(offset) = segments.FindSegmentsContaining(offset)

    interface IBackgroundRenderer with
        member this.Draw(tv,dc) = this.Draw(tv,dc)
        member this.Layer = this.Layer

    interface IVisualLineTransformer with // needed ?
        member this.Transform(ctx,es) = this.Transform(ctx,es)


type ErrorHighlighter (ed:TextEditor, folds:Folding.FoldingManager, log:ISeffLog) = 

    let tView= ed.TextArea.TextView
    let renderer = ErrorRenderer(ed,folds,log)
    let tip = new ToolTip(IsOpen=false) // TODO replace with something that can be pinned// TODO use popup instead of tooltip so it can be pinned?

    let drawnEv = new Event<IEditor>()

    let showErrorToolTip(mouse:Input.MouseEventArgs) = 
        let pos = tView.GetPositionFloor(mouse.GetPosition(tView) + tView.ScrollOffset)
        if pos.HasValue then
            let logicalPosition = pos.Value.Location
            let offset = ed.Document.GetOffset(logicalPosition)
            let segmentsAtOffset = renderer.GetSegmentsAtOffset(offset)
            //let seg = segmentsAtOffset.FirstOrDefault(fun renderer -> renderer.Message <> null)//LINQ ??
            //if notNull seg && notNull tab.ErrorToolTip then
            if segmentsAtOffset.Count > 0 then
                let seg = segmentsAtOffset.[0] // TODO show all Errors at this segment not just first ?
                let tb = new TextBlock()
                tb.Text <- seg.Message       //TODO move styling out of event handler ?
                tb.FontSize <- Style.fontSize
                tb.FontFamily <- Style.fontToolTip //TODO use another monospace font ?
                tb.TextWrapping <- TextWrapping.Wrap
                //tb.Foreground <- Media.SolidColorBrush(if seg.IsWarning then Colors.DarkRed else Colors.DarkGreen)
                tip.Content <- tb

                let pos = ed.Document.GetLocation(seg.StartOffset)
                let tvpos = new TextViewPosition(pos.Line,pos.Column)
                let pt = tView.GetVisualPosition(tvpos, Rendering.VisualYPosition.LineTop)
                let ptInclScroll = pt - tView.ScrollOffset
                tip.PlacementTarget <- ed.TextArea
                tip.PlacementRectangle <- new Rect(ptInclScroll.X, ptInclScroll.Y, 0., 0.)
                tip.Placement <- Primitives.PlacementMode.Top //https://docs.microsoft.com/en-us/dotnet/framework/wpf/controls/popup-placement-behavior
                tip.VerticalOffset <- -6.0

                tip.IsOpen <- true
                //e.Handled <- true // don't, MouseHover might still trigger type info tooltip

    do
        tView.BackgroundRenderers.Add(renderer)
        tView.LineTransformers.Add(   renderer)
        tView.Services.AddService(typeof<ErrorRenderer> , renderer) // TODO, what for?

        tView.MouseHover.Add        (showErrorToolTip)
        tView.MouseHoverStopped.Add ( fun e ->  tip.IsOpen <- false ) //; e.Handled <- true) )
        tView.VisualLinesChanged.Add( fun e ->  tip.IsOpen <- false ) // on scroll  or resize?

    [<CLIEvent>] member this.OnDrawn = drawnEv.Publish

    /// draws underlines
    /// threadsafe
    member this.Draw( iEditor: IEditor ) = // this is used as Checker.OnChecked event handler
        match iEditor.FileCheckState with
        | Done res ->            
            renderer.AddSegments(res)
            drawnEv.Trigger(iEditor) // to update foldings now
        | NotStarted |  GettingCode _ | Checking _ | CheckFailed -> ()

    member this.ToolTip = tip




