namespace Seff.Editor

open System
open System.Linq // for First() and Last() on read only collections //TODO replace
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Collections.Generic

open FSharp.Compiler
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.EditorServices

open AvalonEditB
open AvalonEditB.Document
open AvalonEditB.Rendering

open AvalonLog.Brush

open Seff
open Seff.Util
open Seff.Model



//read: http://danielgrunwald.de/coding/AvalonEdit/rendering.php

// originally taken from //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
// better would be https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/Textsegmentservice.cs


module ErrorStyle= 
    let errSquiggle     = Pen(  Brushes.Red     |> darker 20      |> freeze, 1.0) |> Pen.freeze
    let errBackGr       =       Brushes.Red     |> brighter 220   |> freeze

    let warnSquiggle    = Pen(  Brushes.Yellow  |> darker 40      |> freeze, 1.0) |> Pen.freeze
    let warnBackGr      =       Brushes.Yellow  |> brighter 200   |> freeze

    let infoSquiggle    = Pen(  Brushes.Green  |> darker 5       |> freeze, 1.0) |> Pen.freeze
    let infoBackGr      =       Brushes.Green  |> brighter 220   |> freeze


/// ISegment: This segment also contains back and foreground color and diagnostic display text
type SegmentToMark (startOffset,  endOffset, e:FSharpDiagnostic)  = 

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
   
    member s.Offset      = startOffset
    member s.EndOffset   = endOffset
    member s.Length      = endOffset - startOffset

    member _.Message           =  msg
    member _.Diagnostic        =  e
    member _.Severity          =  e.Severity 
    member _.UnderlinePen      =  underlinePen
    member _.BackgroundBrush   =  backgroundBrush

    interface ISegment with 
        member s.Offset      = startOffset
        member s.EndOffset   = endOffset
        member s.Length      = endOffset - startOffset 

module ErrorUtil =    
        
    /// for clicking through the errors in the status bar 
    let mutable private scrollToIdx = -1

    /// split errors by severity and sort by line number 
    let getBySeverity(checkRes:CodeAnalysis.FSharpCheckFileResults) :ErrorsBySeverity =
        scrollToIdx <- -1 // reset first scroll to error when clicking in status bar
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

    let linesStartAtOne i = if i<1 then 1 else i 

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
    
    

    let getNextErrorIdx( ews:ResizeArray<FSharpDiagnostic> ) =
        if ews.Count=0 then 
            -1
        elif scrollToIdx >= ews.Count-1 then // || scrollToIdx >= maxAmountOfErrorsToDraw then 
            scrollToIdx <- 0   
            0
        else         
            scrollToIdx <- scrollToIdx + 1            
            scrollToIdx

    let rec getNextSegment(ied:IEditor) =         
        match ied.FileCheckState with
        | Checking  ->  None 
        | Done res ->
            let ews = res.errors.errorsAndWarnings
            if ews.Count=0 then 
                None 
            else  
                let i = getNextErrorIdx ews
                if i < 0 then 
                    None
                elif i=0 then 
                    getSegment ied.AvaEdit.Document ews[i]
                else 
                    let p = ews[i-1]
                    let t = ews[i  ]
                    if p.StartLine = t.StartLine then // loop on if not first and same line as last
                        getNextSegment(ied)
                    else                
                        getSegment ied.AvaEdit.Document t
   
 
/// IBackgroundRenderer only needed because 
type ErrorRenderer (state: InteractionState, segms:LineTransformers<SegmentToMark>) = 
    
    /// Draw the error squiggle  on the code
    member _.Draw (textView:TextView , drawingContext:DrawingContext) = // for IBackgroundRenderer        
        //eprintfn $"Drawing : {segms.TotalCount} errs"
        let vls = textView.VisualLines
        for vl in vls do 
            let ln = vl.FirstDocumentLine                
            let segs = segms.Line(ln.LineNumber)
            for i = 0 to segs.Count-1 do
                if segs.Count > i then // saftey check because collection might get reset while iterating
                    let seg = segs.[i]
                    let e = seg.Diagnostic
                    ISeffLog.log.PrintfnDebugMsg $"IBackgRe: DocLine {ln.LineNumber}: ErrLines{e.StartLine}.{e.StartColumn}-{e.EndLine}.{e.EndColumn}"   
                    // background color: Done in Error Highlighter
                    //let geoBuilder = new BackgroundGeometryBuilder (AlignToWholePixels = true, CornerRadius = 0.)
                    //geoBuilder.AddSegment(textView, seg)
                    //let boundaryPolygon= geoBuilder.CreateGeometry() // creates one boundary round the text
                    //drawingContext.DrawGeometry(seg.BackgroundBrush, null, boundaryPolygon)

                    //foreground,  squiggles:
                    for rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, seg) do
                        let geo = ErrorUtil.getSquiggleLine(rect)
                        drawingContext.DrawGeometry(Brushes.Transparent, seg.UnderlinePen, geo)
                        //based on //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
       

    member _.Layer = KnownLayer.Selection // for IBackgroundRenderer

    interface IBackgroundRenderer with
        member this.Draw(tv,dc) = this.Draw(tv,dc)
        member this.Layer = this.Layer


type ErrorHighlighter ( state:InteractionState, folds:Folding.FoldingManager) = 
    
    let actionError   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(ErrorStyle.errBackGr))
    let actionWarning = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(ErrorStyle.warnBackGr)) 
    let actionInfo    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(ErrorStyle.infoBackGr)) 
    let actionHidden  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(ErrorStyle.infoBackGr))
    
    let segments = LineTransformers<SegmentToMark>()
    let foundErrorsEv = new Event<unit>()
    let tip = new ToolTip(IsOpen=false) 

    let trans = state.TransformersSemantic
    let ed = state.Editor
    let tView = ed.TextArea.TextView
     

    let insert (e:FSharpDiagnostic, id, action) =         
        let lnNo =  max 1 e.StartLine // because FSharpDiagnostic might have line number 0 form Parse-and-check-file-in-project errors, but Avalonedit starts at 1
        match state.CodeLines.GetLine(lnNo,id) with 
        | ValueNone -> ()
        | ValueSome cln ->
            let st  = cln.offStart + e.StartColumn
            let en = 
                if e.EndLine = e.StartLine then
                    if e.StartColumn <= e.EndColumn then 
                        ISeffLog.log.PrintfnAppErrorMsg $"FSharp Checker reported an invalid error position: e.StartColumn <= e.EndColumn:\r\n {e}"
                    cln.offStart + e.EndColumn           
                
                elif e.EndLine > e.StartLine then // for multiline Errors only show first Line
                    cln.offStart + cln.len
                else 
                    ISeffLog.log.PrintfnAppErrorMsg $"FSharp Checker reported an invalid error position: e.EndLine < e.StartLine:\r\n {e}"                                    
                    cln.offStart + cln.len    
                    
            trans.Insert(lnNo, {from=st; till=en; act=action}) 
            segments.Insert(lnNo, SegmentToMark(st ,en , e))                      
    
    let updateFolds(e:FSharpDiagnostic, id, brush, pen) = 
        let lnNo =  max 1 e.StartLine // because FSharpDiagnostic might have line number 0 
        match state.CodeLines.GetLine(lnNo,id) with 
        | ValueNone -> ()
        | ValueSome cln ->
            let offset = cln.offStart + e.StartColumn     
            for fold in folds.GetFoldingsContaining(offset) do
                //if fold.IsFolded then // do on all folds, so they show correctly when collapsing !
                //fold.BackbgroundColor  <- ErrorStyle.errBackGr // done via ctx.DrawRectangle(ErrorStyle.errBackGr
                fold.DecorateRectangle <- 
                    Action<Rect,DrawingContext>( fun rect ctx ->
                        let geo = ErrorUtil.getSquiggleLine(rect)
                        if isNull fold.BackgroundColor then // in case of selection highlighting skip brush, only use Pen  
                            ctx.DrawRectangle(brush, null, rect)                       
                        ctx.DrawGeometry(Brushes.Transparent, pen, geo)
                        )      
    
    
    let showErrorToolTip(mouse:Input.MouseEventArgs) = 
        let pos = tView.GetPositionFloor(mouse.GetPosition(tView) + tView.ScrollOffset)
        if pos.HasValue then
            let loc = pos.Value.Location
            let offset = ed.Document.GetOffset(loc)
            segments.Line(loc.Line)
            |> Seq.tryFind( fun s ->  offset >= s.Offset && offset <= s.EndOffset )
            |> Option.iter(fun segm ->    
                let tb = new TextBlock()
                tb.Text <- segm.Message       //TODO move styling out of event handler ?
                tb.FontSize <- StyleState.fontSize * 0.9
                tb.FontFamily <- StyleState.fontToolTip //TODO use another monospace font ?
                tb.TextWrapping <- TextWrapping.Wrap
                //tb.Foreground <- Media.SolidColorBrush(if seg.IsWarning then Colors.DarkRed else Colors.DarkGreen)
                tip.Content <- tb

                let pos = ed.Document.GetLocation(segm.Offset)
                let tvpos = new TextViewPosition(pos.Line,pos.Column)
                let pt = tView.GetVisualPosition(tvpos, Rendering.VisualYPosition.LineTop)
                let ptInclScroll = pt - tView.ScrollOffset
                tip.PlacementTarget <- ed.TextArea
                tip.PlacementRectangle <- new Rect(ptInclScroll.X, ptInclScroll.Y, 0., 0.)
                tip.Placement <- Primitives.PlacementMode.Top //https://docs.microsoft.com/en-us/dotnet/framework/wpf/controls/popup-placement-behavior
                tip.VerticalOffset <- -6.0

                tip.IsOpen <- true                
                )    
    
    do
        tView.BackgroundRenderers.Add(new ErrorRenderer(state,segments)) 

        tView.MouseHover.Add        (showErrorToolTip)
        tView.MouseHoverStopped.Add ( fun e ->  tip.IsOpen <- false ) //; e.Handled <- true) )
        tView.VisualLinesChanged.Add( fun e ->  tip.IsOpen <- false ) // on scroll and resize


    [<CLIEvent>] 
    member _.FoundErrors = foundErrorsEv.Publish

    member _.TransformerLineCount = trans.LineCount
    
    member _.UpdateErrs(errs:ErrorsBySeverity, id) = 
        if state.DocChangedId.Value = id then
            //eprintfn $"clear and add {errs.errors.Count}"
            segments.ClearAllLines() // first clear. no dont! clear is done in DocChanged.fs
            for h in errs.hiddens  do insert(h, id ,actionHidden)
            for i in errs.infos    do insert(i, id ,actionInfo)              
            for w in errs.warnings do insert(w, id ,actionWarning)              
            for e in errs.errors   do insert(e, id ,actionError)
            //eprintfn $"inserted errs: {segments.TotalCount}"
            async{
                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                for fold in folds.AllFoldings do fold.DecorateRectangle <- null // first clear
                for h in errs.hiddens  do updateFolds(h, id, ErrorStyle.infoBackGr, ErrorStyle.infoSquiggle) 
                for i in errs.infos    do updateFolds(i, id, ErrorStyle.infoBackGr, ErrorStyle.infoSquiggle)
                for w in errs.warnings do updateFolds(w, id, ErrorStyle.warnBackGr, ErrorStyle.warnSquiggle)
                for e in errs.errors   do updateFolds(e, id, ErrorStyle.errBackGr , ErrorStyle.errSquiggle)
            } |> Async.RunSynchronously

            foundErrorsEv.Trigger()



    member this.ToolTip = tip




