namespace Seff.Editor

open System
open System.Windows.Media

open AvalonEditB
open AvalonEditB.Document
open AvalonEditB.Rendering

open AvalonLog.Brush

open Seff
open Seff.Util
open Seff.Util.General
open Seff.Model


module private EvaluationTrackerRendererUtil =
    
    //let backGround = Brushes.Teal |> brighter 230 |> freeze      
    let backGround = Brushes.Ivory |> brighter 5   |> freeze    

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
        c<>' ' 
        &&  c <> '\r' 
        &&  c <> '\n'

    /// c=' '  ||  c = '\r'  ||  c = '\n'
    let inline isWhite(c:Char) = 
        c=' ' 
        ||  c = '\r' 
        ||  c = '\n'
    
open EvaluationTrackerRendererUtil

/// IBackgroundRenderer 
type EvaluationTrackerRenderer (ed:TextEditor) = 
    let doc = ed.Document
    

    let mutable evaluatedCodeSeg: ISegment = null    
    
    /// Offset of where evaluatiuon should continue from 
    /// Holds the index of the first unevaluated offest, this might be out of bound too, if all doc is evaluated.
    let mutable topMostUnEvaluated = 0    


    let computeParagraphAndDraw() = 
        if topMostUnEvaluated = 0 then 
            if isNull evaluatedCodeSeg then () // nothing evaluated yet, do nothing
            else evaluatedCodeSeg <-null /// happens when thre is a segment but the forst char gets deleted
        else
            let len = doc.TextLength
            if topMostUnEvaluated >= len then
                evaluatedCodeSeg <- newSegmentTill(len-1)// recalcultat just to be sure it hasn't chnaged
                ed.TextArea.TextView.Redraw() 
        
            elif isNull evaluatedCodeSeg || topMostUnEvaluated-1 <> evaluatedCodeSeg.EndOffset then         
                let lastInEval = doc.GetCharAt(topMostUnEvaluated-1)
                
                // first try to look ahead if there is only white space till a paragraph starts
                let rec keepMark prev i = 
                    if i = len then 
                        true // -99
                    else                        
                        let this = doc.GetCharAt(i)   
                        if isNonWhite this then // non whitespace
                            if prev = '\n' then // find a line that does not start with white space
                                true //i-1 // there is only white space till 'i' wher a paragraph starts
                            else
                                false //-999 //a non whit but not at beginning of line                            
                        else
                            keepMark this (i+1)
                let keep = keepMark lastInEval topMostUnEvaluated
                
                if keep then //no search back needed since the next non white is at position 0 in line 
                    let mutable j = topMostUnEvaluated-1
                    while j>0 && isWhite (doc.GetCharAt(j)) do // to remove white space too
                        j <- j-1
                    topMostUnEvaluated <- j+2
                    evaluatedCodeSeg   <- newSegmentTill(j+1)                      
                    ed.TextArea.TextView.Redraw()                
                else             
                    let rec searchback after i = 
                        if i = -1 then 0
                        else 
                            let this = doc.GetCharAt(i)                    
                            if this = '\n' && isNonWhite after then // find a line that does not start with white space
                                //i // end segment wil include line return
                                let mutable j = i-1
                                while j>0 && isWhite (doc.GetCharAt(j)) do // to remove white space too
                                    j <- j-1
                                j+1

                            else 
                                searchback this (i-1)             
            
                    //ISeffLog.log.PrintfnColor 0 200 100 "topMostUnEvaluated-1: %d" topMostUnEvaluated-1
                    let segEnd = searchback lastInEval topMostUnEvaluated   // GetCharAt     cant be -1 because ther is a check at the top    
                      
                    if segEnd = 0 then 
                        topMostUnEvaluated <- 0                
                        evaluatedCodeSeg <- null 
                    else
                        topMostUnEvaluated <- segEnd+1
                        evaluatedCodeSeg   <- newSegmentTill(segEnd)
            
                    ed.TextArea.TextView.Redraw() 
            
    
    /// Triggered on each document changed
    member _.SetLastChangeAt(off) =         
        if off < topMostUnEvaluated then         
            //ISeffLog.log.PrintfnColor 0 200 0 "Doc change topMostUnEvaluated: %d" topMostUnEvaluated
            topMostUnEvaluated <-  off 
            computeParagraphAndDraw()       
       

    member _.MarkNoneEvaluated() =
        topMostUnEvaluated <- 0
        evaluatedCodeSeg <- null
        ed.TextArea.TextView.Redraw()      
    
    /// provide the index of the first unevaluted offest, this might be out of bound too if all doc is evaluated and will be checked
    member _.MarkEvalutedTillOffset(off) =        
        let len = doc.TextLength // off might be bigger than doc.TextLength because expandSelectionToFullLines adds 2 chars at end.        
        topMostUnEvaluated <- min len off  
        computeParagraphAndDraw()
        
    member _.MarkAllEvaluated() =
        let mutable len = doc.TextLength-1
        while len > 0 && isWhite (doc.GetCharAt(len)) do // to exlude empty lines at end
            len <- len-1        
        topMostUnEvaluated <- len + 2
        evaluatedCodeSeg <- newSegmentTill(len+1)
        ed.TextArea.TextView.Redraw() 
        
    
    /// Offset of where evaluatiuon should continue from 
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
            with ex -> 
                ISeffLog.log.PrintfnAppErrorMsg "ERROR in EvaluationTrackerRenderer.Draw(): %A" ex
    
    // for IBackgroundRenderer  
    member _.Layer = KnownLayer.Selection        

    interface IBackgroundRenderer with
        member this.Draw(tv,dc) = this.Draw(tv,dc)
        member this.Layer = this.Layer
      


type EvaluationTracker (ed:TextEditor, checker:Checker, edId:Guid) =
    
    let renderer = EvaluationTrackerRenderer(ed)   
    
    do 
        ed.TextArea.TextView.BackgroundRenderers.Add(renderer)        
        checker.Fsi.OnReset.Add       (fun evc -> renderer.MarkNoneEvaluated()) // rest for all editors
        checker.Fsi.OnCanceled.Add    (fun evc -> if evc.editor.Id = edId then renderer.MarkNoneEvaluated())
        checker.Fsi.OnCompletedOk.Add (fun evc ->
            if evc.editor.Id = edId then  //this event will be hooked up for each tab so check id too
                //ISeffLog.log.PrintfnColor 150 150 150  "Fsi.OnCompletedOk:%A" evc
                //ISeffLog.log.PrintfnFsiErrorMsg "Fsi.OnCompletedOk:renderer.EvaluateFrom:%d" renderer.EvaluateFrom
                match evc.amount with 
                |All |ContinueFromChanges -> renderer.MarkAllEvaluated()
                |FsiSegment s ->                     
                    let endOffset = s.startOffset + s.length
                    if s.startOffset <= renderer.EvaluateFrom  // check start is in already evaluated region
                    && endOffset > renderer.EvaluateFrom then
                        renderer.MarkEvalutedTillOffset(endOffset) 
                        //ISeffLog.log.PrintfnFsiErrorMsg "Fsi.OnCompletedOk,FsiSegment:renderer.EvaluateFrom:%d" renderer.EvaluateFrom
                )             
      
    member _.SetLastChangeAt(off) =     renderer.SetLastChangeAt(off)

    /// Offset of where evaluatiuon should continue from 
    member _.EvaluateFrom =             renderer.EvaluateFrom
    
    /// provide the index of the first unevaluted offest, this might be out of bound too if all doc is evaluated and will be checked
    member _.MarkEvalutedTillOffset(off) =      renderer.MarkEvalutedTillOffset(off)



    
