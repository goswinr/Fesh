namespace Seff.Editor

open Seff
open Seff.Model
open ICSharpCode.AvalonEdit
open Seff.Util.String
open Seff.Util
open System.Windows
open System
open System.Windows.Media
open ICSharpCode.AvalonEdit.Editing
open ICSharpCode.AvalonEdit.Document
open Seff.Util
open Seff.Util.General
open ICSharpCode.AvalonEdit.Folding

module SwapLines =
    type Seg = 
        {st:int;en:int}
        member s.len = s.en - s.st
  
    type SelPos = {stp:TextViewPosition ; enp:TextViewPosition}
            
    type Sel = 
        |NoSel
        |RegSel  of SelPos
        |RectSel of SelPos
  
    type SwapPair = 
        |ThisAndOther of Seg * Seg
        |EndOrStartOfDoc
  
    /// TODO jump over folded block !


    let getSelection (ta:TextArea,log:ISeffLog) = 
        match ta.Selection with
        | null -> NoSel   
        | :? EmptySelection -> NoSel   
        | :? RectangleSelection as rs -> 
            let st = rs.StartPosition
            let en = rs.EndPosition 
            if st.Line <= en.Line then  RectSel {stp = st ; enp = en }
            else                        RectSel {stp = en ; enp = st } // for a block selection that has the caret at the beginning
        | :? SimpleSelection    as ss -> RegSel  {stp = ss.StartPosition ; enp = ss.EndPosition }
        | x ->             
            log.PrintfnAppErrorMsg "Unknown selction class in swapLinesUp: %A" x
            NoSel


    let swapLinesUp(ed:Editor) =
        let avaEdit = ed.AvaEdit
        let doc=avaEdit.Document
        let ta = avaEdit.TextArea
        let caret = avaEdit.CaretOffset
        
        let selection = getSelection (ta,ed.Log)
        
        let pair =
            match selection with
            | NoSel -> 
                let thisLn = doc.GetLineByOffset(caret)
                let prevLn = thisLn.PreviousLine
                if isNull prevLn then 
                    EndOrStartOfDoc
                else  
                    //let foldLine =
                    //    let fs = ed.Folds.Manager.GetFoldingsContaining(prevLn.Offset) |> Seq.filter ( fun f -> f.IsFolded)  // TODO finsh up swaping over folded lines
                    //    if Seq.isEmpty fs then 
                    //        None
                    //    else
                    //        fs
                    //        |> Seq.map (fun f  -> f.StartOffset)
                    //        |> Seq.min
                    //        |> doc.GetLineByOffset
                    //        |> Some
                    //
                    //match foldLine with 
                    let segAbove = { st = prevLn.Offset; en = prevLn.EndOffset}
                    let segThis  = { st = thisLn.Offset; en = thisLn.EndOffset}
                    ThisAndOther (segThis,segAbove)
            | RectSel s 
            | RegSel s ->
                let firstLn = doc.GetLineByNumber(s.stp.Line)
                let lastLn =  doc.GetLineByNumber(s.enp.Line)
                let prevLn = firstLn.PreviousLine
                if isNull prevLn then 
                    EndOrStartOfDoc
                else
                    let segAbove = { st = prevLn.Offset;  en = prevLn.EndOffset}
                    let segThis  = { st = firstLn.Offset; en = lastLn.EndOffset}
                    ThisAndOther (segThis,segAbove)
        
        match pair with
        |EndOrStartOfDoc -> () // swap not possible
        |ThisAndOther ( lnsThis, lnAbove) -> 
            let txtAbove = doc.GetText(lnAbove.st, lnAbove.len)
            //log.PrintfnDebugMsg "doc.GetText(lnsThis.st %d, lnsThis.len %d)" lnsThis.st lnsThis.len
            let txtThis  = doc.GetText(lnsThis.st, lnsThis.len)
            
            doc.BeginUpdate()
            ta.ClearSelection()        
            doc.Remove(lnAbove.st, lnAbove.len + 2 + lnsThis.len)            
            doc.Insert(lnAbove.st, txtThis + "\r\n" + txtAbove)
            avaEdit.CaretOffset <- caret - (lnAbove.len + 2)
            
            match selection with
            | NoSel -> () // just change carte below
            | RegSel sel ->  
                let mutable startPos = sel.stp
                let mutable endPos   = sel.enp
                startPos.Line <-  startPos.Line - 1
                endPos.Line   <-  endPos.Line   - 1
                let newSel = new SimpleSelection(ta,startPos,endPos)
                //log.PrintfnDebugMsg "new selection: %A" newSel
                ta.Selection <- newSel
            | RectSel rectSel ->
                let mutable startPos = rectSel.stp
                let mutable endPos   = rectSel.enp
                startPos.Line <-  startPos.Line - 1
                endPos.Line   <-  endPos.Line   - 1
                ta.Selection <- new RectangleSelection(ta,startPos,endPos) 
            doc.EndUpdate()
      
    let swapLinesDown(ed:Editor) =
        let avaEdit = ed.AvaEdit
        let doc=avaEdit.Document
        let ta = avaEdit.TextArea
        let caret = avaEdit.CaretOffset
        
        let selection = getSelection (ta,ed.Log)
        
        let pair =
            match selection with
            | NoSel -> 
                let thisLn = doc.GetLineByOffset(caret)
                let nextLn = thisLn.NextLine
                if isNull nextLn then 
                    EndOrStartOfDoc
                else                                      
                    let segBelow= { st = nextLn.Offset; en = nextLn.EndOffset}
                    let segThis  = { st = thisLn.Offset; en = thisLn.EndOffset}
                    ThisAndOther (segThis,segBelow)
            | RectSel s 
            | RegSel s ->
                let firstLn = doc.GetLineByNumber(s.stp.Line)
                let lastLn =  doc.GetLineByNumber(s.enp.Line)
                let nextLn = lastLn.NextLine
                if isNull nextLn then 
                    EndOrStartOfDoc
                else
                    let segAbove = { st = nextLn.Offset;  en = nextLn.EndOffset}
                    let segThis  = { st = firstLn.Offset; en = lastLn.EndOffset}
                    ThisAndOther (segThis,segAbove)
        
        match pair with
        |EndOrStartOfDoc -> () // swap not possible
        |ThisAndOther ( lnsThis, lnBelow) -> 
            let txtBelow = doc.GetText(lnBelow.st, lnBelow.len)
            //log.PrintfnDebugMsg "doc.GetText(lnsThis.st %d, lnsThis.len %d)" lnsThis.st lnsThis.len
            let txtThis  = doc.GetText(lnsThis.st, lnsThis.len)
            
            doc.BeginUpdate()
            ta.ClearSelection() 

            doc.Remove(lnsThis.st, lnsThis.len + 2 + lnBelow.len)            
            doc.Insert(lnsThis.st, txtBelow + "\r\n" + txtThis)
            avaEdit.CaretOffset <- caret + (lnBelow.len + 2)
            
            match selection with
            | NoSel -> () // just change carte below
            | RegSel sel ->  
                let mutable startPos = sel.stp
                let mutable endPos   = sel.enp
                startPos.Line <-  startPos.Line + 1
                endPos.Line   <-  endPos.Line   + 1
                let newSel = new SimpleSelection(ta,startPos,endPos)
                //log.PrintfnDebugMsg "new selection: %A" newSel
                ta.Selection <- newSel
            | RectSel rectSel ->
                let mutable startPos = rectSel.stp
                let mutable endPos   = rectSel.enp
                startPos.Line <-  startPos.Line + 1
                endPos.Line   <-  endPos.Line   + 1
                ta.Selection <- new RectangleSelection(ta,startPos,endPos) 
            doc.EndUpdate()
        
        