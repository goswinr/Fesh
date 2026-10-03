namespace Fesh.Editor

open Fesh.Model
open AvaloniaEdit.Editing

module SwapLines =
    open Selection

    type SwapPair =
        |ThisAndOther of Seg * Seg
        |EndOrStartOfDoc

    /// TODO jump over folded block too !

    let swapLinesUp(ed:IEditor) =
        let avaEdit = ed.AvaEdit
        let doc=avaEdit.Document
        let ta = avaEdit.TextArea
        let caret = avaEdit.CaretOffset
        let sel = getSelType (ta)
        let sp = Selection.getSelectionOrdered ta

        let pair =
            match sel with
            | NoSel ->
                let thisLn = doc.GetLineByOffset(caret)
                let prevLn = thisLn.PreviousLine
                if isNull prevLn then
                    EndOrStartOfDoc
                else
                    // TODO finish up swapping over folded lines
                    //let foldLine =
                    //    let fs = ed.Folds.Manager.GetFoldingsContaining(prevLn.Offset) |> Seq.filter ( fun f -> f.IsFolded)
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

            | RectSel
            | RegSel  ->
                let firstLn = doc.GetLineByNumber(sp.stPos.Line)
                let lastLn =  doc.GetLineByNumber(sp.enPos.Line)
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
            let txtThis  = doc.GetText(lnsThis.st, lnsThis.len)

            doc.BeginUpdate()
            ta.ClearSelection()
            let lineBreak = doc.GetText(lnAbove.en, lnsThis.st - lnAbove.en) // the line break between the two, "\r\n" or just "\n"
            doc.Remove(lnAbove.st, lnAbove.len + lineBreak.Length + lnsThis.len)
            doc.Insert(lnAbove.st, txtThis + lineBreak + txtAbove)
            avaEdit.CaretOffset <- caret - (lnAbove.len + lineBreak.Length)

            match sel with
            | NoSel -> () // just change caret below

            | RegSel ->
                let mutable startPos = sp.stPos
                let mutable endPos   = sp.enPos
                startPos.Line <-  startPos.Line - 1
                endPos.Line   <-  endPos.Line   - 1
                let newSel = new SimpleSelection(ta, startPos, endPos)
                ta.Selection <- newSel

            | RectSel ->
                let mutable startPos = sp.stPos
                let mutable endPos   = sp.enPos
                startPos.Line <-  startPos.Line - 1
                endPos.Line   <-  endPos.Line   - 1
                ta.Selection <- new RectangleSelection(ta,startPos,endPos)
            doc.EndUpdate()

    let swapLinesDown(ed:IEditor) =
        let avaEdit = ed.AvaEdit
        let doc=avaEdit.Document
        let ta = avaEdit.TextArea
        let caret = avaEdit.CaretOffset
        let sel = getSelType (ta)
        let sp = Selection.getSelectionOrdered ta

        let pair =
            match sel with
            | NoSel ->
                let thisLn = doc.GetLineByOffset(caret)
                let nextLn = thisLn.NextLine
                if isNull nextLn then
                    EndOrStartOfDoc
                else
                    let segBelow = { st = nextLn.Offset; en = nextLn.EndOffset}
                    let segThis  = { st = thisLn.Offset; en = thisLn.EndOffset}
                    ThisAndOther (segThis,segBelow)
            | RectSel
            | RegSel  ->
                let firstLn = doc.GetLineByNumber(sp.stPos.Line)
                let lastLn =  doc.GetLineByNumber(sp.enPos.Line)
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
            let txtThis  = doc.GetText(lnsThis.st, lnsThis.len)

            doc.BeginUpdate()
            ta.ClearSelection()

            let lineBreak = doc.GetText(lnsThis.en, lnBelow.st - lnsThis.en) // the line break between the two, "\r\n" or just "\n"
            doc.Remove(lnsThis.st, lnsThis.len + lineBreak.Length + lnBelow.len)
            doc.Insert(lnsThis.st, txtBelow + lineBreak + txtThis)
            avaEdit.CaretOffset <- caret + (lnBelow.len + lineBreak.Length)

            match sel with
            | NoSel -> () // just change carte below
            | RegSel  ->
                let mutable startPos = sp.stPos
                let mutable endPos   = sp.enPos
                startPos.Line <-  startPos.Line + 1
                endPos.Line   <-  endPos.Line   + 1
                let newSel = new SimpleSelection(ta,startPos,endPos)
                ta.Selection <- newSel

            | RectSel  ->
                let mutable startPos = sp.stPos
                let mutable endPos   = sp.enPos
                startPos.Line <-  startPos.Line + 1
                endPos.Line   <-  endPos.Line   + 1
                ta.Selection <- new RectangleSelection(ta,startPos,endPos)
            doc.EndUpdate()


