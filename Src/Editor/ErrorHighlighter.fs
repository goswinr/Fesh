namespace Seff.Editor

open System
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

// Error colors are defined in FastColorizer.fs

module ErrorUtil =    
        
    /// for clicking through the errors in the status bar 
    let mutable private scrollToIdx = -1

    /// split errors by severity and sort by line number 
    let getBySeverity(checkRes:CodeAnalysis.FSharpCheckFileResults) :ErrorsBySeverity =
        scrollToIdx <- -1 // reset first scroll to error when clicking in status bar
        let was  = ResizeArray()  // Warnings
        let ers  = ResizeArray()  // Errors
        let ins  = ResizeArray()  // Infos        
        let his  = ResizeArray()  // Hidden
        let erWs = ResizeArray()  // Errors and Warnings
        let all = checkRes.Diagnostics |> Array.sortBy( fun e -> e.StartLine) // sort before filtering out duplicates
        for i = 0 to all.Length - 1 do
            let  e = all.[i]
            if i=0 // to filter out duplicate errors, a bug in FCS !
            ||( let p = all.[i-1] in p.StartLine <> e.StartLine || p.StartColumn <> e.StartColumn || p.EndLine <> e.EndLine || p.EndColumn <> e.EndColumn) then                             
                match e.Severity with
                | FSharpDiagnosticSeverity.Error   -> ers.Add e ; erWs.Add e
                | FSharpDiagnosticSeverity.Warning -> was.Add e ; erWs.Add e
                | FSharpDiagnosticSeverity.Hidden  -> his.Add e
                | FSharpDiagnosticSeverity.Info    -> if e.ErrorNumber <> 3370 then ins.Add e   //  exclude infos about ref cell incrementing ??
        
        //printfn $"Errors: {ers.Count} Warnings: {was.Count} Infos: {insCount} Hidden: {his.Count} "
        { errors = ers; warnings = was; infos = ins; hiddens = his; errorsAndWarnings = erWs }

    let linesStartAtOne i = if i<1 then 1 else i 

    let makeSeg(from,till) =
        Some {new ISegment with
                    member _.Offset      = from
                    member _.EndOffset   = till
                    member _.Length      = till - from      }

    let getSegment (doc:TextDocument) ( e:FSharpDiagnostic) : ISegment option =
        try            
            let st = doc.GetOffset(new TextLocation(linesStartAtOne e.StartLine, e.StartColumn + 1 ))
            let en = doc.GetOffset(new TextLocation(linesStartAtOne e.EndLine  , e.EndColumn   + 1 ))
            if st<en then 
                makeSeg(st,en)
            elif st>en then // should never happen // this FCS bug has happened in the past, for Parse-and-check-file-in-project errors the segments can be wrong
                makeSeg(en,st)
            else // st=en  // should never happen
                makeSeg(st,st+1) // just in case, so it is at least on char long            
        with 
            //In a rare race condition the segment is beyond the end of the document because it was just deleted:
            | :? ArgumentOutOfRangeException -> 
                None 
            | e -> 
                raise e 
    
    let getSquiggleLine(r:Rect):StreamGeometry = 
        let startPoint = r.BottomLeft
        let endPoint = r.BottomRight
        let offset = 3.0 // originally 2.5
        let count = max 4 (int((endPoint.X - startPoint.X)/offset) + 1) // at least 4 squiggles visible
        let geometry = new StreamGeometry()
        use ctx = geometry.Open()
        ctx.BeginFigure(startPoint, false, false)
        ctx.PolyLineTo(
            [| for i=0 to count - 1 do 
                let x = startPoint.X + (float i * offset)
                let y = startPoint.Y - if (i + 1) % 2 = 0 then offset else 0.
                Point(x,y) |] , // for Squiggly line
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
type ErrorRenderer (state: InteractionState) = 
    
    // based on //https://stackoverflow.com/questions/11149907/showing-invalid-xml-syntax-with-avalonedit
    // better would be https://github.com/icsharpcode/SharpDevelop/blob/master/src/AddIns/DisplayBindings/AvalonEdit.AddIn/Src/Textsegmentservice.cs

    /// Draw the error squiggle on the code
    member _.Draw (textView:TextView , drawingContext:DrawingContext) = // for IBackgroundRenderer         
        //AvalonEditB.Rendering.VisualLinesInvalidException: Exception of type 'AvalonEditB.Rendering.VisualLinesInvalidException' was thrown.
        //    at AvalonEditB.Rendering.TextView.get_VisualLines()
        //    at Seff.Editor.ErrorRenderer.Draw(TextView textView, DrawingContext drawingContext) in D:\Git\Seff\Src\Editor\ErrorHighlighter.fs:line 138
        //    at AvalonEditB.Rendering.TextView.RenderBackground(DrawingContext drawingContext, KnownLayer layer)
        //    at AvalonEditB.Editing.CaretLayer.OnRender(DrawingContext drawingContext)
        //    at System.Windows.UIElement.Arrange(Rect finalRect)
        //    at System.Windows.ContextLayoutManager.UpdateLayout()
        if textView.VisualLinesValid then //to avoid above error.
            let vls = textView.VisualLines                
            let fromLine = vls[0].FirstDocumentLine.LineNumber
            let toLine   = vls[vls.Count-1].LastDocumentLine.LineNumber
            let segms = state.ErrSegments
            for lnNo = fromLine to toLine do
                let segs = segms.GetLine(lnNo)
                for i = 0 to segs.Count-1 do                    
                    let seg = segs.[i]                        
                    let segShift = seg.Shifted(segms.Shift) 

                    // background color: // when drawing on Caret layer background must be disabled.
                    // let geoBuilder = new BackgroundGeometryBuilder (AlignToWholePixels = true, CornerRadius = 0.)
                    // geoBuilder.AddSegment(textView, segShift )                       
                    // let boundaryPolygon = geoBuilder.CreateGeometry() // creates one boundary round the text
                    // drawingContext.DrawGeometry(seg.BackgroundBrush, null, boundaryPolygon)

                    //foreground, squiggles:
                    for rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segShift) do 
                        let geo = ErrorUtil.getSquiggleLine(rect)
                        drawingContext.DrawGeometry(Brushes.Transparent, seg.UnderlinePen, geo)
                                
                //let e = seg.Diagnostic in ISeffLog.log.PrintfnDebugMsg $"IBackgroundRenderer: DocLine {lnNo}: ErrLines{e.StartLine}.{e.StartColumn}-{e.EndLine}.{e.EndColumn}"   
            
         
    member _.Layer = 
        // when drawing on Caret layer the  background change must be disabled
        KnownLayer.Caret// .Selection// for IBackgroundRenderer

    interface IBackgroundRenderer with
        member this.Draw(tv,dc) = this.Draw(tv,dc)
        member this.Layer = this.Layer


type ErrorHighlighter ( state:InteractionState, folds:Folding.FoldingManager, isComplWinOpen: unit-> bool) = 
    
    //  let actionError   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(ErrorStyle.errBackGr))
    //  let actionWarning = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(ErrorStyle.warnBackGr)) 
    //  let actionInfo    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(ErrorStyle.infoBackGr)) 
    //  let actionHidden  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(ErrorStyle.infoBackGr))
        
    let foundErrorsEv = new Event<int64>()
    let tip = new ToolTip(IsOpen=false) 

    let ed = state.Editor
    let tView = ed.TextArea.TextView
    
    /// returns true or false to indicate if CodeLines.GetLine was aborted because of a new state.
    let insert (newSegments:ResizeArray<ResizeArray<SegmentToMark>>) id (e:FSharpDiagnostic) : bool =         
        let stLn = max 1 e.StartLine // because FSharpDiagnostic might have line number 0 form Parse-and-check-file-in-project errors, but Avalonedit starts at 1
        let enLn = max 1 e.EndLine  
        if stLn > enLn then 
            ISeffLog.log.PrintfnAppErrorMsg $"FSharp Checker reported an invalid error position: e.EndLine < e.StartLine:\r\n {e}"  
        if e.EndLine = e.StartLine && e.StartColumn > e.EndColumn then 
            ISeffLog.log.PrintfnAppErrorMsg $"FSharp Checker reported an invalid error position: e.StartColumn <= e.EndColumn:\r\n {e}"
        
        let rec insert lnNo = 
            if lnNo > enLn then 
                true
            else 
                match state.CodeLines.GetLine(lnNo,id) with 
                | ValueNone -> false
                | ValueSome cln ->                    
                    let st  = if lnNo = stLn then cln.offStart + e.StartColumn else cln.offStart
                    let en  = if lnNo = enLn then cln.offStart + e.EndColumn   else cln.offStart + cln.len
                    if cln.len > cln.indent then // skip just whitespace lines
                        
                        let fixedEn = // e.StartColumn = e.EndColumn // this actually happens as a result from fs checker
                            if st = en then cln.offStart + max cln.len 1 else en
                            
                        LineTransformers.Insert(newSegments, lnNo, SegmentToMark(st ,fixedEn , e)) 
                    insert (lnNo+1) 
        
        insert stLn


    let updateFolds id brush pen (e:FSharpDiagnostic): bool = // TODO in theory this could run async, can it ??
        let lnNo =  max 1 e.StartLine // because FSharpDiagnostic might have line number 0 
        match state.CodeLines.GetLine(lnNo,id) with 
        | ValueNone -> false
        | ValueSome cln ->
            let offset = cln.offStart + e.StartColumn     
            for fold in folds.GetFoldingsContaining(offset) do
                //if fold.IsFolded then // do on all folds, even open ones, so they show correctly when collapsing !
                //fold.BackgroundColor  <- ErrorStyle.errBackGr // done via ctx.DrawRectangle(ErrorStyle.errBackGr
                fold.DecorateRectangle <- 
                    Action<Rect,DrawingContext>( fun rect ctx ->
                        let geo = ErrorUtil.getSquiggleLine(rect)
                        if isNull fold.BackgroundColor then // in case of selection highlighting skip brush, only use Pen  
                            ctx.DrawRectangle(brush, null, rect)                       
                        ctx.DrawGeometry(Brushes.Transparent, pen, geo)
                        ) 
            true     
    
    
    let showErrorToolTip(mouse:Input.MouseEventArgs) =         
        if not <| isComplWinOpen() then // don't show tooltip when completion window is open            
            let pos = tView.GetPositionFloor(mouse.GetPosition(tView) + tView.ScrollOffset)
            if pos.HasValue then
                let loc = pos.Value.Location
                let offset = ed.Document.GetOffset(loc)
                state.ErrSegments.GetLine(loc.Line)
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
                    tip.Placement <- Primitives.PlacementMode.Top // Type info Tooltip is on Bottom //https://docs.microsoft.com/en-us/dotnet/framework/wpf/controls/popup-placement-behavior
                    tip.VerticalOffset <- -5.0

                    tip.IsOpen <- true                
                    )    
    
    do
        tView.BackgroundRenderers.Add(new ErrorRenderer(state))

        tView.MouseHover.Add        ( showErrorToolTip)
        tView.MouseHoverStopped.Add ( fun e ->  tip.IsOpen <- false ) //; e.Handled <- true) )
        //tView.VisualLinesChanged.Add( fun e ->  tip.IsOpen <- false ) // on scroll and resize ?

    
   

    [<CLIEvent>] 
    member _.FoundErrors = foundErrorsEv.Publish
    
    /// triggers foundErrorsEv
    member _.UpdateErrs(errs:ErrorsBySeverity, id) =         
        if state.IsLatest id then  
            let nSegs = ResizeArray<ResizeArray<SegmentToMark>>(state.ErrSegments.LineCount + 2 )
            if // first insert in to LineTransformer
                General.traverse (insert nSegs id) errs.hiddens
                |> General.ifTrueDo General.traverse (insert nSegs id) errs.infos
                |> General.ifTrueDo General.traverse (insert nSegs id) errs.warnings
                |> General.ifTrueDo General.traverse (insert nSegs id) errs.errors            
                then                        
                    state.ErrSegments.Update nSegs 
                    foundErrorsEv.Trigger(id)
                    
                    // second mark folding boxes if an error is inside, even open ones, so that it shows when collapsed:
                    async{
                        do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                        for fold in folds.AllFoldings do // first clear   
                            fold.DecorateRectangle <- null                      
                        General.traverse (updateFolds id ErrorStyle.infoBackGr ErrorStyle.infoSquiggle)  errs.hiddens
                        |> General.ifTrueDo General.traverse (updateFolds id ErrorStyle.infoBackGr ErrorStyle.infoSquiggle) errs.infos
                        |> General.ifTrueDo General.traverse (updateFolds id ErrorStyle.warnBackGr ErrorStyle.warnSquiggle) errs.warnings
                        |> General.ifTrueDo General.traverse (updateFolds id ErrorStyle.errBackGr  ErrorStyle.errSquiggle ) errs.errors
                        |> ignore<bool>    
                    } |> Async.Start 

    member this.ToolTip = tip




