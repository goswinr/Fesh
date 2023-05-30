namespace Seff.Editor

open System
open System.Collections.Generic
open System.Windows.Media

open AvalonEditB
open AvalonEditB.Rendering
open AvalonEditB.Folding
open AvalonLog.Brush
open Seff.Model


[<Struct>]
type Fold = {foldStartOff:int; foldEndOff:int; linesInFold: int ; nestingLevel:int}

[<Struct>]
type FoldStart = {indent: int; lineEndOff:int; line: int; indexInFolds:int; nestingLevel:int}

[<Struct>]
type Indent = { indent: int; wordStartOff:int }

[<Struct>]
type NonStandardIndent = { badIndent: int; lineStartOffset:int; lineNo: int }



type Foldings(manager:Folding.FoldingManager, state:InteractionState, getFilePath:unit->FilePath) = 
    let foldStatus = state.Config.FoldingStatus

    /// for brackets and bad indentation
    let transformers = state.TransformersAllBrackets

    let defaultIndenting = state.Editor.Options.IndentationSize

    let badIndentBrush =        
        //Color.FromArgb(30uy,255uy,140uy,0uy) // a very light transparent Orange, transparent to show column rulers behind
        Color.FromArgb(40uy,255uy,255uy,0uy) // a very light transparent Yellow, transparent to show column rulers behind
        |> SolidColorBrush
        |> freeze
    
    let badIndentAction  = Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(badIndentBrush))       
    
    /// maximum amount of nested foldings
    let maxDepth = 1 

    /// minimum line count for outer folding
    let minLinesOutside = 2 

    /// minimum line count for inner folding
    let minLinesNested = 3 

    /// if inner folding is just 9 line shorter than outer folding don't do it    
    let minLineCountDiffToOuter = 9    
   

    let saveFoldingStatus() = foldStatus.Set(getFilePath(),manager)
 
    // the color for folding box is set in SelectedTextHighlighter

    let FoldingStack = Stack<FoldStart>()
    let Folds = ResizeArray<Fold>()  

    let mutable isInitialLoad = true

    let mutable lastBadIndentSize = 0

    let findFoldings (clns:CodeLineTools.CodeLines, id) :bool = 
        
        FoldingStack.Clear() // Collections.Generic.Stack<FoldStart>
        Folds.Clear() // ResizeArray<Fold>  

        
        let rec loopLines prevLnNo (prev:CodeLineTools.LineInfo) (lnNo:int) = 
            if lnNo > clns.LastLineIdx then //end of file
                true
            else
                match clns.GetLine(lnNo, id) with 
                |ValueNone -> false // did not reach end of code lines
                |ValueSome this -> 
                    if this.indent=this.len then // skip all white lines
                        loopLines prevLnNo prev (lnNo+1)
                    
                    elif this.indent > prev.indent then 
                        


        
        
        match clns.GetLine(1,id) with 
        |ValueNone -> false // did not reach end of code lines
        |ValueSome li -> loopLines 1 li 2
        
        //----------------------------------------
        //----------------------------------------
        
        
        let mutable endOfPrevLineWithChars = -1 // will not get set on only whitespace lines empty lines 

        let rec loopLinesOLD (ind:int) (lnNo:int) = 
            if lnNo > clns.LastLineIdx then //end of file
                true
            else
                match clns.GetLine(lnNo,id) with 
                |ValueNone -> false // did not reach end of code lines
                |ValueSome li -> 
                    if li.indent=li.len then // skip all white lines
                        loopLines ind (lnNo+1)
                    else
                        // (1) find bad indents:
                        if li.indent % defaultIndenting <> 0 then 
                            transformers.Insert(lnNo,{from=li.offStart; till=li.offStart+li.indent  ; act=badIndentAction })
                    
                        // (2) check folds: 
                        if li.indent > ind then
                            //ISeffLog.log.PrintfnDebugMsg $"le.indent > (*ind*): offset of first VisibleChar: le.indent={le.indent} (indent {ind}) in line {no} till {lineNo}"
                            let nestingLevel = FoldingStack.Count
                            if nestingLevel <= maxDepth then
                                let index = Folds.Count
                                Folds.Add  {foldStartOff = -99; foldEndOff = -99 ; linesInFold = -99 ; nestingLevel = nestingLevel} // dummy value to be mutated later, if not mutated it will be filter out in foldEditor function.
                                FoldingStack.Push {indent= ind; lineEndOff = endOfPrevLineWithChars ; line = lnNo ; indexInFolds = index; nestingLevel = nestingLevel}
                                //ISeffLog.log.PrintfnAppErrorMsg  " line: %d: indent %d start" no ind                

                        elif li.indent < ind then
                            //ISeffLog.log.PrintfnFsiErrorMsg $"le.indent < ind: offset of first VisibleChar: le.indent={le.indent} (indent {ind}) in line {no} till {lineNo}"                   
                            let mutable take = true
                            while FoldingStack.Count > 0 && take do
                                let st = FoldingStack.Peek()
                                if st.indent >= li.indent then
                                    FoldingStack.Pop()  |> ignore
                                    let lines = lnNo - st.line
                                    if (st.nestingLevel = 0 && lines >= minLinesOutside)
                                    || (st.nestingLevel > 0 && lines >= minLinesNested ) then // only add if block has enough lines outer wise leave dummy inside list
                                        let foldStart = st.lineEndOff                                       
                                        Folds.[st.indexInFolds] <- {foldStartOff = foldStart; foldEndOff = endOfPrevLineWithChars; linesInFold = lines ;nestingLevel = st.nestingLevel}
                                        //ISeffLog.log.PrintfnAppErrorMsg  "line: %d : indent %d end of %d lines " no st.indent lines
                                else
                                    take <- false
                        
                        endOfPrevLineWithChars <- li.offStart + li.len // the last charcater on this line before\r\n

                        loopLines li.indent (lnNo+1)
                    
        loopLines 0 1 



    let textInFoldBox(count:int) = sprintf " ... %d folded lines " count

    // save folding id just as its characters length.
    // there is a risk for collision but it is small
    let collapseStatus = Dictionary<int,bool>()

    let ed = state.Editor

    // get hash of first line of folding segmnet
    let getHash(off) =
        let d = ed.Document
        let mutable hash = off 
        let rec loop i =
            let c = d.GetCharAt(i)
            if c = '\r' then hash
            else
                hash <- hash  + (97 * int c) 
                loop (i+1)
        loop off

    let updateCollapseStatus()=
        collapseStatus.Clear()
        for f in  manager.AllFoldings do             
            collapseStatus.[getHash f.StartOffset] <- f.IsFolded 
    
    let foundBadIndentsEv = new Event<unit>() 

    ///Get foldings at every line that is followed by an indent
    let foldEditor (id:int64) = 
        async{                
            if findFoldings (state.CodeLines, id) then             
                eprintfn $"found {Folds.Count} Folds"
                let foldings =
                    foundBadIndentsEv.Trigger()
                    let l = Folds.Count-1
                    let fs = ResizeArray(max 0 l)// would be -1 if no foldings
                    let mutable lastOuter = {foldStartOff = -99; foldEndOff = -99 ; linesInFold = -99 ; nestingLevel = -99}
                    for i=0 to l do
                        let f = Folds.[i]
                        if f.foldEndOff > 0 then // filter out to short blocks that are left as dummy
                            if  f.nestingLevel = 0 then
                                lastOuter <- f
                                fs.Add f
                            elif f.linesInFold + minLineCountDiffToOuter < lastOuter.linesInFold then // filter out inner blocks that are almost the size of the outer block
                                fs.Add f
                            else
                                printfn $"skip1 {f}"
                        else
                             printfn $"skip2 {f}"
                    eprintfn $"added {fs.Count} Folds"
                    fs       
                
                if isInitialLoad then                                
                    while foldStatus.WaitingForFileRead do
                        // check like this because reading of file data happens async
                        // ISeffLog.log.PrintfnDebugMsg "waiting to load last code folding status.. "
                        do! Async.Sleep 50
                    let vs = foldStatus.Get(getFilePath())
                    
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                    for i = 0 to foldings.Count-1 do
                        let f = foldings.[i]
                        let folded = if  i < vs.Length then  vs.[i]  else false
                        let fs = manager.CreateFolding(f.foldStartOff, f.foldEndOff)
                        fs.Tag <- box f.nestingLevel
                        fs.IsFolded <- folded
                        fs.Title <- textInFoldBox f.linesInFold                        

                    updateCollapseStatus()
                    isInitialLoad <- false
                

                elif state.DocChangedId.Value = id then
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context  
                    
                    let folds=ResizeArray<NewFolding>()                                
                    for f in foldings do
                        //ISeffLog.log.PrintfnDebugMsg "Foldings from %d to %d  that is  %d lines" f.foldStartOff  f.foldEndOff f.linesInFold
                        let fo = new NewFolding(f.foldStartOff, f.foldEndOff) 
                        fo.Name <- textInFoldBox f.linesInFold
                        folds.Add(fo) //if NewFolding type is created async a waiting symbol appears on top of it
                        
                    
                    // Existing foldings starting after this offset will be kept even if they don't appear in newFoldings. Use -1 for this parameter if there were no parse errors)
                    let firstErrorOffset = -1 //The first position of a parse error. 
                    manager.UpdateFoldings(folds, firstErrorOffset)
                                
                    // restore state after caret , because state gets lost after an auto complete insertion                             
                    let co = ed.CaretOffset
                    for f in manager.AllFoldings do 
                        if f.StartOffset > co then                                         
                            match collapseStatus.TryGetValue (getHash f.StartOffset) with 
                            |false , _ -> ()
                            |true , isCollapsed -> 
                                f.IsFolded <- isCollapsed 
                                //let d = iEditor.AvaEdit.Document
                                //let ln = d.GetLineByOffset f.StartOffset
                                //if isCollapsed <> f.IsFolded then 
                                //    if isCollapsed then  ISeffLog.printnColor 200 0 0 $"try collapse {ln.LineNumber}: {d.GetText ln}"
                                //    else                 ISeffLog.printnColor 0 200 0 $"try open {ln.LineNumber}:{d.GetText ln}"
                    //ISeffLog.printnColor 100 100 100 $"---------end of try collapse---------------------"
                    state.Editor.TextArea.TextView.Redraw()
                    saveFoldingStatus() // so that when new foldings appeared they are saved immediately

            } |>  Async.Start

    let margin = 
        ed.TextArea.LeftMargins
        |> Seq.tryFind ( fun m -> m :? Folding.FoldingMargin )
        |> Option.defaultWith (fun () -> failwithf "Failed to find Folding.FoldingMargin")
        :?> Folding.FoldingMargin

    // When the full text gets replaced ( eg via git branch change).
    // manager.UpdateFoldings(..) cannot remember old locations and keep state
    do 
        // set up intial state:
        let vs = state.Config.FoldingStatus.Get(getFilePath())
        for f,s in Seq.zip manager.AllFoldings vs do f.IsFolded <- s
        
        margin.MouseUp.Add (fun e -> 
            updateCollapseStatus()
            state.Config.FoldingStatus.Set(getFilePath(), manager)
            )

    /// runs first part async
    member _.UpdateFoldsAndBadIndents( id) = foldEditor( id)
    
    member _.Manager = manager

    [<CLIEvent>] 
    member _.FoundBadIndents = foundBadIndentsEv.Publish
    
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

