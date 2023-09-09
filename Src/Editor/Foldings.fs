namespace Seff.Editor

open System
open System.Collections.Generic

open AvalonEditB
open AvalonEditB.Rendering
open AvalonEditB.Folding
open AvalonLog.Brush
open Seff.Model

[<Struct>]
type Fold = {
    foldStartOff:int;
    foldEndOff:int
    linesInFold: int  
    nestingLevel:int
    }

[<Struct>]
type FoldFrom = {
    lineNo: int 
    info: CodeLineTools.LineInfo
    }


type Foldings(manager:Folding.FoldingManager, state:InteractionState, getFilePath:unit->FilePath) = 
    let foldStatus = state.Config.FoldingStatus

    // ----------------------------------
    // ------- foldings ----------
    // ----------------------------------

    /// maximum amount of nested foldings
    let maxDepth = 1 // 1 = means one primary and one secondary nested

    /// minimum line count for outer folding
    let minLinesOutside = 2 

    /// minimum line count for inner folding
    let minLinesNested = 3 

    // if inner folding is just 9 line shorter than outer folding don't do it    
    // let minLineCountDiffToOuter = 9    
   

    let saveFoldingStatus() = foldStatus.Set(getFilePath(),manager)
 
    // the color for folding box is set in SelectedTextHighlighter

    let mutable isInitialLoad = true
   

    let findFoldings (cLns:CodeLineTools.CodeLines, id) :ResizeArray<Fold> option = 
        let FoldingStack = Stack<FoldFrom>()
        let Folds = ResizeArray<Fold>()         

        //for i=0 to cLns.LastLineIdx do 
        //    match cLns.GetLine(i, id) with 
        //    |ValueNone -> ()
        //    |ValueSome l -> 
        //        if l.len=l.indent then eprintfn $"line {i}: skip empty"
        //        else printfn $"line {i}: indent {l.indent}"            
        
        let rec loopLines prevLnNo (prev:CodeLineTools.LineInfo) (lnNo:int) = 
            if lnNo > cLns.LastLineIdx then //end of file
                while  FoldingStack.Count > 0 do // at file end close off items on stack
                    let st = FoldingStack.Pop()                    
                    let lineCount = prevLnNo - st.lineNo
                    let nestingLevel = FoldingStack.Count 
                            
                    if (nestingLevel = 0 && lineCount >= minLinesOutside)
                    || (nestingLevel > 0 && lineCount >= minLinesNested ) then
                        //printfn $"on END ({lnNo}) popped prev line {st.lineNo} to {prevLnNo}"
                        Folds.Add{
                            foldStartOff = st.info.offStart + st.info.len
                            foldEndOff   = prev.offStart + prev.len
                            linesInFold  = lineCount
                            nestingLevel = nestingLevel
                            }               
                Some Folds
            else
                match cLns.GetLine(lnNo, id) with 
                |ValueNone -> None // did not reach end of code lines
                |ValueSome this ->                     
                    if this.indent=this.len then // skip all white lines
                        loopLines prevLnNo prev (lnNo+1)
                    
                    elif this.indent > prev.indent then 
                        if FoldingStack.Count <= maxDepth then 
                            FoldingStack.Push {lineNo=prevLnNo ; info=prev}
                            //printfn $"pushed prev line {prevLnNo} because {this.indent} > {prev.indent} : this.indent > prev.indent"
                            loopLines lnNo this (lnNo+1)
                        else
                            loopLines lnNo this (lnNo+1)
                    
                    elif this.indent < prev.indent && FoldingStack.Count > 0 then 
                        let mutable top = FoldingStack.Peek()
                        //printfn $"on line {lnNo} TRY popped prev line because {this.indent} < {prev.indent}; {top.info.indent} : this.indent < prev.indent; top.info.indent"
                        while this.indent <= top.info.indent  && FoldingStack.Count > 0 do 
                            let st = FoldingStack.Pop()
                            let lineCount = prevLnNo - st.lineNo
                            let nestingLevel = FoldingStack.Count 
                            
                            if (nestingLevel = 0 && lineCount >= minLinesOutside)
                            || (nestingLevel > 0 && lineCount >= minLinesNested ) then
                                //printfn $"on line {lnNo} popped prev line {st.lineNo} to {prevLnNo}"
                                Folds.Add{
                                    foldStartOff = st.info.offStart + st.info.len
                                    foldEndOff   = prev.offStart + prev.len
                                    linesInFold  = lineCount
                                    nestingLevel = nestingLevel
                                    }
                            if FoldingStack.Count > 0 then 
                                top <- FoldingStack.Peek()
                        
                        loopLines lnNo this (lnNo+1)
                            
                    else
                        loopLines lnNo this (lnNo+1)
        
        
        match cLns.GetLine(1,id) with 
        |ValueNone -> None // did not reach end of code lines
        |ValueSome li -> loopLines 1 li 2
    
    let textInFoldBox(count:int) = sprintf " ... %d folded lines " count

    // save folding the first line, there is a risk for collision but it is small
    let collapseStatus = Dictionary<string,bool>()

    let ed = state.Editor

    let updateCollapseStatus()=
        collapseStatus.Clear()
        let doc = ed.Document
        for f in  manager.AllFoldings do             
            let ln = doc.GetLineByOffset f.StartOffset            
            collapseStatus.[doc.GetText(ln)] <- f.IsFolded 
    

    ///Get foldings at every line that is followed by an indent
    let foldEditor (id:int64) = 
        async{                
            match findFoldings (state.CodeLines, id) with
            |None -> ()
            |Some folds ->                 
                folds|> Seff.Util.General.sortInPlaceBy ( fun f -> f.foldStartOff, f.linesInFold) 
                
                // only applies when opening a new file
                if isInitialLoad then                                
                    while foldStatus.WaitingForFileRead do
                        // check like this because reading of file data happens async
                        // ISeffLog.log.PrintfnDebugMsg "waiting to load last code folding status.. "
                        do! Async.Sleep 50
                    let vs = foldStatus.Get(getFilePath())
                    
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                    for i = 0 to folds.Count-1 do
                        let f = folds.[i]
                        if f.foldStartOff < f.foldEndOff then // TODO this seems to not always be the case
                            let folded = if  i < vs.Length then  vs.[i]  else false
                            let fs = manager.CreateFolding(f.foldStartOff, f.foldEndOff)
                            fs.Tag <- box f.nestingLevel
                            fs.IsFolded <- folded
                            fs.Title <- textInFoldBox f.linesInFold   
                        else
                            let lno = ed.Document.GetLineByOffset f.foldStartOff
                            ISeffLog.log.PrintfnAppErrorMsg $"manager.CreateFolding was given a negative folding from offset {f.foldStartOff} to {f.foldEndOff} on line {lno.LineNumber}"

                    updateCollapseStatus()
                    isInitialLoad <- false
                
                // for any change after initial opening of the file
                elif state.IsLatest id then                     
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context  
                    let edFolds = manager.AllFoldings
                   
                    // (1) first find out if a folding update is needed at all                    
                    use enum = edFolds.GetEnumerator()                    
                    let rec zip i = // returns true if a folding update is  needed
                        match enum.MoveNext(), i < folds.Count with
                        |false, false -> false // reached end,  both collections have the same length
                        |false, true  -> true // reached end, edFolds is shorter than folds
                        |true,  false -> true // reached end, edFolds is longer than folds
                        |true,  true  -> 
                            let f = folds.[i]
                            let fEdi = enum.Current
                            if fEdi.StartOffset <> f.foldStartOff || fEdi.EndOffset <> f.foldEndOff then 
                                ISeffLog.log.PrintfnDebugMsg $"changeId: {id} foldings differ: {fEdi.StartOffset-f.foldStartOff} and {fEdi.EndOffset-f.foldEndOff}"
                                true // existing, foldings are different
                            else
                                zip (i+1) // loop on
                    
                    let updateNeeded = zip 0
                    enum.Dispose()
                    if updateNeeded then
                        // if edFolds.Count <> folds.Count then    ISeffLog.log.PrintfnDebugMsg $"****changeId: {id} foldings differ: {edFolds.Count} and {folds.Count}"
                        // else                                    ISeffLog.log.PrintfnDebugMsg $"but count same"
                        
                        
                        // (2) Update of foldings is needed:
                        // (2.1) find firstError offset for Update Foldings function
                        // search backwards from end of file to find the last folding that needs a change
                        let edFoldsArr = edFolds |> Array.ofSeq
                        let rec findBack i j = 
                            if i<0 || j<0 then 
                                -1 // firstErrorOffset: Use -1 for this parameter if there were no parse errors)
                            else
                                let feDi = edFoldsArr[i]
                                let fNew = folds[j]                                
                                if feDi.StartOffset <> fNew.foldStartOff || feDi.EndOffset <> fNew.foldEndOff then 
                                    max  feDi.EndOffset  fNew.foldEndOff 
                                else
                                    findBack (i-1) (j-1)                        
                        let firstErrorOffset = findBack  (edFoldsArr.Length-1) (folds.Count-1)

                        // (2.2) create new Foldings
                        let docLen = ed.Document.TextLength
                        //eprintfn $" firstErrorOffset: {firstErrorOffset}, docLen: {docLen}" 
                        let nFolds=ResizeArray<NewFolding>() 
                        let rec collect i =
                            if i < folds.Count then 
                                let f = folds.[i]
                                if firstErrorOffset = -1 || f.foldStartOff <= firstErrorOffset then 
                                    if f.foldEndOff <= docLen then // in case of deleting at the end of file
                                        //ISeffLog.log.PrintfnDebugMsg "Foldings from %d to %d  that is  %d lines" f.foldStartOff  f.foldEndOff f.linesInFold
                                        let fo = new NewFolding(f.foldStartOff, f.foldEndOff) 
                                        fo.Name <- textInFoldBox f.linesInFold
                                        nFolds.Add(fo) //if NewFolding type is created async a waiting symbol appears on top of it
                                        collect (i+1)
                                    //else eprintfn $"too long: f.foldEndOff: {f.foldEndOff} > docLen: {docLen}"
                                //else eprintfn $"not added: f.foldStartOff: {f.foldStartOff} to {f.foldEndOff}"
                                
                        collect 0
                        
                        //eprintfn $"{nFolds.Count} new foldings created. firstErrorOffset: {firstErrorOffset}" 

                        // (2.3) update foldings
                        // Existing foldings starting after this firstErrorOffset will be kept even if they don't appear in newFoldings. 
                        // Use -1 for this parameter if there were no parse errors.
                        manager.UpdateFoldings(nFolds, firstErrorOffset)
                        
                        // (2.4) save collapsed status again
                        // so that when new foldings appeared they are saved immediately
                        saveFoldingStatus() 
                    
                    else
                        //printfn $"{folds.Count} folds. no folding update needed"
                        enum.Dispose()

            } |>  Async.Start

    let margin = 
        ed.TextArea.LeftMargins
        |> Seq.tryFind ( fun m -> m :? Folding.FoldingMargin )
        |> Option.defaultWith (fun () -> failwithf "Failed to find Folding.FoldingMargin")
        :?> Folding.FoldingMargin

    // When the full text gets replaced ( eg via git branch change).
    // manager.UpdateFoldings(..) cannot remember old locations and keep state
    do 
        // set up initial state:
        let vs = state.Config.FoldingStatus.Get(getFilePath())
        for f,s in Seq.zip manager.AllFoldings vs do 
            f.IsFolded <- s
        
        margin.MouseUp.Add (fun e -> 
            updateCollapseStatus()
            state.Config.FoldingStatus.Set(getFilePath(), manager)
            )

    /// runs first part async
    member _.UpdateFolds( id) = foldEditor(id)
    
    member _.Manager = manager


    (*
    member _.Margin = 
        ed.TextArea.LeftMargins
        |> Seq.tryFind ( fun m -> m :? Folding.FoldingMargin )
        |> Option.defaultWith (fun () -> failwithf "Failed to find Folding.FoldingMargin")
        :?> Folding.FoldingMargin
    *)

    member _.ExpandAll() = 
        for f in manager.AllFoldings do
            f.IsFolded <- false
        updateCollapseStatus()
        saveFoldingStatus() // so that they are saved immediately

    member _.CollapseAll() = 
        for f in manager.AllFoldings do
            f.IsFolded <- true
        updateCollapseStatus()
        saveFoldingStatus() // so that they are saved immediately

    member _.CollapsePrimary() = 
        for f in manager.AllFoldings do            
            match f.Tag with // cast might fail ??
             | :? int as tag ->
                    if tag  = 0 then // nestingLevel
                        f.IsFolded <- true
             | _ -> // because only foldings at opening get a tag, not later ones
                let ln = ed.Document.GetLineByOffset(f.StartOffset)    
                let st = ed.Document.GetCharAt(ln.Offset)
                if st<> ' ' then 
                    f.IsFolded <- true             
        updateCollapseStatus()
        saveFoldingStatus() // so that they are saved immediately
    
    /// Open any foldings if required and optionally select at location
    member _.GoToOffsetAndUnfold(offset, length, selectText) =         
        let mutable unfoldedOneOrMore = false
        for fold in manager.GetFoldingsContaining(offset) do
            if fold.IsFolded then
                fold.IsFolded <- false
                unfoldedOneOrMore <- true
        let ln = ed.Document.GetLineByOffset(offset)
        ed.ScrollTo(ln.LineNumber,1)
        //ed.CaretOffset<- loc.EndOffset // done by ed.Select(..) too
        if selectText then 
            ed.Select(offset, length)
        else 
            ed.CaretOffset<-offset 

        if unfoldedOneOrMore then
            updateCollapseStatus()
            saveFoldingStatus() // so that they are saved immediately

