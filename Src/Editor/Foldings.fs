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
type Fold = {
        foldStartOff:int;
        foldEndOff:int
        linesInFold: int  
        nestingLevel:int
        }

[<Struct>]
type FoldStart = {indent: int; lineEndOff:int; line: int; indexInFolds:int; nestingLevel:int}

[<Struct>]
type Indent = { indent: int; wordStartOff:int }

[<Struct>]
type NonStandardIndent = { badIndent: int; lineStartOffset:int; lineNo: int }

[<Struct>]
type FoldFrom = {lineNo: int ; info: CodeLineTools.LineInfo}


type Foldings(manager:Folding.FoldingManager, state:InteractionState, getFilePath:unit->FilePath) = 
    let foldStatus = state.Config.FoldingStatus

    // ----------------------------------
    // ------- bad indentation ----------
    // ----------------------------------

    /// for bad indentation
    let transformers = state.TransformersAllBrackets

    let defaultIndenting = state.Editor.Options.IndentationSize

    let badIndentBrush =        
        //Color.FromArgb(30uy,255uy,140uy,0uy) // a very light transparent Orange, transparent to show column rulers behind
        Color.FromArgb(40uy,255uy,255uy,0uy) // a very light transparent Yellow, transparent to show column rulers behind
        |> SolidColorBrush
        |> freeze
    
    let badIndentAction  = Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(badIndentBrush))       
    
    //let mutable lastBadIndentSize = 0

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

    let FoldingStack = Stack<FoldFrom>()
    let Folds = ResizeArray<Fold>()  

    let mutable isInitialLoad = true
   

    let findFoldings (clns:CodeLineTools.CodeLines, id) :bool = 
        
        FoldingStack.Clear() // Collections.Generic.Stack<FoldStart>
        Folds.Clear() // ResizeArray<Fold>  

        //for i=0 to clns.LastLineIdx do 
        //    match clns.GetLine(i, id) with 
        //    |ValueNone -> ()
        //    |ValueSome l -> 
        //        if l.len=l.indent then eprintfn $"line {i}: skip empty"
        //        else printfn $"line {i}: indent {l.indent}"
            
        
        let rec loopLines prevLnNo (prev:CodeLineTools.LineInfo) (lnNo:int) = 
            if lnNo > clns.LastLineIdx then //end of file
                while  FoldingStack.Count > 0 do // at file end close off items on stack
                    let st = FoldingStack.Pop()                    
                    let lineCount = prevLnNo - st.lineNo
                    let nestingLevel = FoldingStack.Count 
                            
                    if (nestingLevel = 0 && lineCount >= minLinesOutside)
                    || (nestingLevel > 0 && lineCount >= minLinesNested ) then
                        //printfn $"on END ({lnNo}) poped prevline {st.lineNo} to {prevLnNo}"
                        Folds.Add{
                            foldStartOff = st.info.offStart + st.info.len
                            foldEndOff   = prev.offStart + prev.len
                            linesInFold  = lineCount
                            nestingLevel = nestingLevel
                            }               
                true
            else
                match clns.GetLine(lnNo, id) with 
                |ValueNone -> false // did not reach end of code lines
                |ValueSome this -> 
                    // (1) find bad indents:
                    if this.indent % defaultIndenting <> 0 then                         
                        // printfn $"bad indent {this.indent} at line {lnNo}"
                        transformers.Insert(lnNo, {from=this.offStart; till=this.offStart+this.indent; act=badIndentAction} )
                                            
                    // (2) find folds:                 
                    
                    if this.indent=this.len then // skip all white lines
                        loopLines prevLnNo prev (lnNo+1)
                    
                    elif this.indent > prev.indent then 
                        if FoldingStack.Count <= maxDepth then 
                            FoldingStack.Push {lineNo=prevLnNo ; info=prev}
                            //printfn $"pushed prevline {prevLnNo} because {this.indent} > {prev.indent} : this.indent > prev.indent"
                            loopLines lnNo this (lnNo+1)
                        else
                            loopLines lnNo this (lnNo+1)
                    
                    elif this.indent < prev.indent && FoldingStack.Count > 0 then 
                        let mutable top = FoldingStack.Peek()
                        //printfn $"on line {lnNo} TRY poped prevline because {this.indent} < {prev.indent}; {top.info.indent} : this.indent < prev.indent; top.info.indent"
                        while this.indent <= top.info.indent  && FoldingStack.Count > 0 do 
                            let st = FoldingStack.Pop()
                            let lineCount = prevLnNo - st.lineNo
                            let nestingLevel = FoldingStack.Count 
                            
                            if (nestingLevel = 0 && lineCount >= minLinesOutside)
                            || (nestingLevel > 0 && lineCount >= minLinesNested ) then
                                //printfn $"on line {lnNo} poped prevline {st.lineNo} to {prevLnNo}"
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
        
        
        match clns.GetLine(1,id) with 
        |ValueNone -> false // did not reach end of code lines
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
    
    let foundBadIndentsEv = new Event<int64>() 

    ///Get foldings at every line that is followed by an indent
    let foldEditor (id:int64) = 
        async{                
            if findFoldings (state.CodeLines, id) then
                foundBadIndentsEv.Trigger(id)
                Folds|> Seff.Util.General.sortInPlaceBy ( fun f -> f.foldStartOff, f.linesInFold) 
                
                if isInitialLoad then                                
                    while foldStatus.WaitingForFileRead do
                        // check like this because reading of file data happens async
                        // ISeffLog.log.PrintfnDebugMsg "waiting to load last code folding status.. "
                        do! Async.Sleep 50
                    let vs = foldStatus.Get(getFilePath())
                    
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                    for i = 0 to Folds.Count-1 do
                        let f = Folds.[i]
                        if f.foldStartOff < f.foldEndOff then // TODO this seems to not always be the case
                            let folded = if  i < vs.Length then  vs.[i]  else false
                            let fs = manager.CreateFolding(f.foldStartOff, f.foldEndOff)
                            fs.Tag <- box f.nestingLevel
                            fs.IsFolded <- folded
                            fs.Title <- textInFoldBox f.linesInFold   
                        else
                            let lno = ed.Document.GetLineByOffset f.foldStartOff
                            ISeffLog.log.PrintfnDebugMsg  $"Failed to manager.CreateFolding for a negative folding from offset {f.foldStartOff} to {f.foldEndOff} on line {lno.LineNumber}"

                    updateCollapseStatus()
                    isInitialLoad <- false
                

                elif state.DocChangedId.Value = id then                    
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context  
                    
                    let folds=ResizeArray<NewFolding>()                                
                    for i=0 to Folds.Count - 1 do
                        if i < Folds.Count then // because folds might get changed on another thread
                            let f = Folds.[i]
                            if f.foldStartOff < f.foldEndOff then // TODO this seems to not always be the case
                                //ISeffLog.log.PrintfnDebugMsg "Foldings from %d to %d  that is  %d lines" f.foldStartOff  f.foldEndOff f.linesInFold
                                let fo = new NewFolding(f.foldStartOff, f.foldEndOff) 
                                fo.Name <- textInFoldBox f.linesInFold
                                folds.Add(fo) //if NewFolding type is created async a waiting symbol appears on top of it
                          
                            else
                                let lno = ed.Document.GetLineByOffset f.foldStartOff
                                ISeffLog.log.PrintfnDebugMsg  $"Failed to make NewFolding for a negative folding from offset {f.foldStartOff} to {f.foldEndOff} on line {lno.LineNumber}"
                            
                    
                    // Existing foldings starting after this offset will be kept even if they don't appear in newFoldings. Use -1 for this parameter if there were no parse errors)
                    let firstErrorOffset = -1 //The first position of a parse error. 
                    manager.UpdateFoldings(folds, firstErrorOffset)
                                
                    // restore state after caret , because state gets lost after an auto complete or multi chracter insertion                             
                    let doc = ed.Document                    
                    let co = ed.CaretOffset
                    for f in manager.AllFoldings do 
                        if f.StartOffset > co then                                         
                            let ln = doc.GetLineByOffset f.StartOffset    
                            match collapseStatus.TryGetValue (doc.GetText(ln)) with 
                            |false , _ -> ()
                            |true , isCollapsed -> 
                                f.IsFolded <- isCollapsed 
                                //let d = iEditor.AvaEdit.Document
                                //let ln = d.GetLineByOffset f.StartOffset
                                //if isCollapsed <> f.IsFolded then 
                                //    if isCollapsed then  ISeffLog.printnColor 200 0 0 $"try collapse {ln.LineNumber}: {d.GetText ln}"
                                //    else                 ISeffLog.printnColor 0 200 0 $"try open {ln.LineNumber}:{d.GetText ln}"
                    //ISeffLog.printnColor 100 100 100 $"---------end of try collapse---------------------"
                    //ISeffLog.log.PrintfnDebugMsg $"Updated {Folds.Count} Foldings "
                    //state.Editor.TextArea.TextView.Redraw()
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
        // set up initial state:
        let vs = state.Config.FoldingStatus.Get(getFilePath())
        for f,s in Seq.zip manager.AllFoldings vs do 
            f.IsFolded <- s
        
        margin.MouseUp.Add (fun e -> 
            updateCollapseStatus()
            state.Config.FoldingStatus.Set(getFilePath(), manager)
            )

    /// runs first part async
    member _.UpdateFoldsAndBadIndents( id) = foldEditor(id)
    
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

