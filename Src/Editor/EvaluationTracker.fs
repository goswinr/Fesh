namespace Seff.Editor

open System
open System.Windows.Media

open AvalonEditB
open AvalonEditB.Document
open AvalonEditB.Rendering

open AvalonLog.Brush

open Seff.Util.General
open Seff.Model
open Seff

module private EvaluationTrackerRendererUtil = 

    //let backGround = Brushes.Teal |> brighter 230 |> freeze
    //let backGround = Brushes.Ivory |> brighter 5   |> freeze
    //let backGround = Brushes.LightGray |> brighter 30 |> freeze
    let backGround = SolidColorBrush(Color.FromArgb(200uy,220uy,220uy,220uy))|> freeze
    

    //let pen = new Pen(Brushes.Black |> freeze , 0.5)  |> Pen.freeze
    //let border = new Pen( Brushes.Teal |> freeze , 0.7)  |> Pen.freeze

    let inline newSegmentTill(endOff) = 
        if endOff <= 0 then
            null
        else
            let s = TextSegment()
            s.StartOffset <- 0
            s.EndOffset <-endOff
            s

    /// c<>' ' &&  c <> '\r' &&  c <> '\n'
    let inline isNonWhite(c:Char) = 
        c     <> ' '
        &&  c <> '\r'
        &&  c <> '\n'

    /// c=' '  ||  c = '\r'  ||  c = '\n'
    let inline isWhite(c:Char) = 
        c     = ' '
        ||  c = '\r'
        ||  c = '\n'

open EvaluationTrackerRendererUtil

/// IBackgroundRenderer
type EvaluationTrackerRenderer (ed:TextEditor) = 
    let doc = ed.Document

    let mutable evaluatedCodeSeg: ISegment = null

    /// Offset of where evaluation should continue from
    /// Holds the index of the first unevaluated offset, this might be out of bound too, if all doc is evaluated.
    let mutable topMostUnEvaluated = 0

    let formatChar (ch:char) = 
        if   ch = '\n'then "\\n" 
        elif ch = '\r'then "\\r" 
        else ch.ToString()

    let computeParagraphAndDraw() = 
        if topMostUnEvaluated = 0 then
            if isNull evaluatedCodeSeg then () // nothing evaluated yet, do nothing
            else evaluatedCodeSeg <-null // happens when there is a segment but the first char gets deleted
        else
            let len = doc.TextLength
            if topMostUnEvaluated >= len then
                evaluatedCodeSeg <- newSegmentTill(len-1)// recalculate just to be sure it hasn't changed
                ed.TextArea.TextView.Redraw()

            elif isNull evaluatedCodeSeg || topMostUnEvaluated-1 <> evaluatedCodeSeg.EndOffset then
                let lastInEval = doc.GetCharAt(topMostUnEvaluated-1)
                //ISeffLog.log.PrintfnColor 144 222 100 "computeParagraphAndDraw:\r\n'%s'" ed.Text

                // first try to look ahead if there is only white space till a paragraph starts.
                // in that case keep the current evaluated marking
                let rec keepMark prev i = 
                    if i = len then
                        true 
                    else
                        let ch = doc.GetCharAt(i)
                        //ISeffLog.log.PrintfnColor 200 0 100 "prev: '%s' ch: '%s'" (formatChar prev)(formatChar ch)
                        if isNonWhite ch then // non whitespace
                            if prev = '\n' then // find a line that does not start with white space
                                true //i-1 // there is only white space till 'i' where a paragraph starts
                            else
                                false //a non white but not at beginning of line
                        else
                            keepMark ch (i+1)
                let keep = keepMark lastInEval topMostUnEvaluated
                //ISeffLog.log.PrintfnColor 55 99 100 "keep is: '%b'" keep

                //now search back needed since the next non white is at position 0 in line
                if keep then 
                    let mutable j = topMostUnEvaluated-1
                    while j>0 && isWhite (doc.GetCharAt(j)) do // to remove white space too
                        j <- j-1
                    topMostUnEvaluated <- j+2
                    evaluatedCodeSeg   <- newSegmentTill(j+1)
                    ed.TextArea.TextView.Redraw()
                else
                    let rec searchBack after i = 
                        if i = -1 then 0
                        else
                            let this = doc.GetCharAt(i)
                            if this = '\n' && isNonWhite after then // find a line that does not start with white space
                                //i // end segment will include line return
                                let mutable j = i-1
                                while j>0 && isWhite (doc.GetCharAt(j)) do // to remove white space too
                                    j <- j-1
                                j+1
                            else
                                searchBack this (i-1)

                    //ISeffLog.log.PrintfnColor 0 200 100 "topMostUnEvaluated-1: %d" (topMostUnEvaluated-1)
                    let segmentEnd = 
                        let segEnd = searchBack lastInEval topMostUnEvaluated   // GetCharAt     cant be -1 because there is a check at the top
                        
                        /// now include any attributes and comments in the lines above , and skip whitespace again                       
                        let rec moveUp (ln:DocumentLine) =                             
                            if ln.LineNumber = 1 then 
                                min segEnd ln.EndOffset // min() because segEnd might be smaller than ln.EndOffset
                            else
                                let st2 = doc.GetText(ln.Offset,2)
                                let st5 = doc.GetText(ln.Offset,5)                                
                                if st2 = "[<" || st2 = "//"   then // for attributes comments  
                                    moveUp(ln.PreviousLine)                                
                                elif String.IsNullOrWhiteSpace(doc.GetText(ln)) then 
                                    moveUp(ln.PreviousLine)
                                elif st5 = "open " || st5 = "#if I" then // TODO: detect correctly if current end segment is inside a #if #elif #else block !
                                    moveUp(ln.PreviousLine)  
                                else
                                    ln.EndOffset 
                        moveUp(doc.GetLineByOffset(segEnd))
                        

                    if segmentEnd = 0 then
                        topMostUnEvaluated <- 0
                        evaluatedCodeSeg <- null
                    else
                        topMostUnEvaluated <- segmentEnd+1
                        evaluatedCodeSeg   <- newSegmentTill(segmentEnd)

                    ed.TextArea.TextView.Redraw()


    /// Triggered on each document changed
    member _.SetLastChangeAt(offset) = 
        if offset < topMostUnEvaluated then
            //ISeffLog.log.PrintfnColor 0 200 0 "Doc change topMostUnEvaluated offset : %d '%s' " offset (formatChar<| doc.GetCharAt(offset))
            topMostUnEvaluated <-  offset            
            computeParagraphAndDraw()

    member _.MarkNoneEvaluated() = 
        topMostUnEvaluated <- 0
        evaluatedCodeSeg <- null
        ed.TextArea.TextView.Redraw()

    /// provide the index of the first unevaluated offset, this might be out of bound too if all doc is evaluated and will be checked
    member _.MarkEvaluatedTillOffset(offset) = 
        let len = doc.TextLength // off might be bigger than doc.TextLength because expandSelectionToFullLines adds 2 chars at end.
        topMostUnEvaluated <- min len offset
        computeParagraphAndDraw()

    member _.MarkAllEvaluated() = 
        let mutable len = doc.TextLength-1
        while len > 0 && isWhite (doc.GetCharAt(len)) do // to exclude empty lines at end
            len <- len-1
        topMostUnEvaluated <- len + 2
        evaluatedCodeSeg <- newSegmentTill(len+1)
        ed.TextArea.TextView.Redraw()

    /// Offset of where evaluation should continue from
    member _.EvaluateFrom = topMostUnEvaluated

    // for IBackgroundRenderer
    member _.Draw (textView:TextView , drawingContext:DrawingContext) =  
        if notNull evaluatedCodeSeg then            
            try
                let geoBuilder = new BackgroundGeometryBuilder (AlignToWholePixels = true, CornerRadius = 0.)
                geoBuilder.AddSegment(textView, evaluatedCodeSeg) // TODO: what happens if the code became shorter and this segment is now bigger than the document ?
                let boundaryPolygon = geoBuilder.CreateGeometry() // creates one boundary round the text
                drawingContext.DrawGeometry(backGround, null, boundaryPolygon) // pen could be null too
                //drawingContext.DrawGeometry(backGround, border, boundaryPolygon) // pen could be null too

                // TODO draw a dragable separator instead:
                // http://www.fssnip.net/9N/title/Drag-move-for-GUI-controls
            with ex ->
                ISeffLog.log.PrintfnAppErrorMsg "ERROR in EvaluationTrackerRenderer.Draw(): %A" ex

    // for IBackgroundRenderer
    member _.Layer = KnownLayer.Caret// KnownLayer.Background

    interface IBackgroundRenderer with
        member this.Draw(tv,dc) = this.Draw(tv,dc)
        member this.Layer = this.Layer



type EvaluationTracker (ed:TextEditor, config:Seff.Config.Config) = 

    let renderer = EvaluationTrackerRenderer(ed)

    //TODO on tab change and "EvalInteractionNonThrowing returned Error:" reset too !

    do
        ed.TextArea.TextView.BackgroundRenderers.Add(renderer)
        let fsi =Fsi.GetOrCreate(config)
        fsi.OnReset.Add       (fun evc -> renderer.MarkNoneEvaluated()) // reset for all editors
        fsi.OnCanceled.Add    (fun evc -> if IEditor.isCurrent ed then renderer.MarkNoneEvaluated())
        fsi.OnCompletedOk.Add (fun evc ->
            if IEditor.isCurrent ed then  //this event will be hooked up for each tab so check id too
                //ISeffLog.log.PrintfnColor 150 150 150  "Fsi.OnCompletedOk:%A" evc
                //ISeffLog.log.PrintfnFsiErrorMsg "Fsi.OnCompletedOk:renderer.EvaluateFrom:%d" renderer.EvaluateFrom
                match evc.amount with
                |All |ContinueFromChanges -> renderer.MarkAllEvaluated()
                |FsiSegment s ->
                    let endOffset = s.startOffset + s.length
                    if s.startOffset <= renderer.EvaluateFrom  // check start is in already evaluated region
                    && endOffset > renderer.EvaluateFrom then
                        renderer.MarkEvaluatedTillOffset(endOffset)
                        //ISeffLog.log.PrintfnFsiErrorMsg "Fsi.OnCompletedOk,FsiSegment:renderer.EvaluateFrom:%d" renderer.EvaluateFrom
                )

    member _.SetLastChangeAt(off) =  renderer.SetLastChangeAt(off)

    /// Offset of where evaluation should continue from
    member _.EvaluateFrom =    renderer.EvaluateFrom

    /// provide the index of the first unevaluated offset, this might be out of bound too if all doc is evaluated and will be checked
    member _.MarkEvaluatedTillOffset(off) =  renderer.MarkEvaluatedTillOffset(off)





