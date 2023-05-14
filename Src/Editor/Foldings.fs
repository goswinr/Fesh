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



(*
type NonStandardIndentColorizer (badInds:ResizeArray<NonStandardIndent>) = 
    inherit Rendering.DocumentColorizingTransformer() 

    let brush =        
        //Color.FromArgb(30uy,255uy,140uy,0uy) // a very light transparent Orange, transparent to show column rulers behind
        Color.FromArgb(50uy,255uy,255uy,0uy) // a very light transparent Yellow, transparent to show column rulers behind
        |> SolidColorBrush
        |> freeze

    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:Document.DocumentLine) = 
        for i in badInds do 
            if i.lineStartOffset = line.Offset then 
                let eOff = i.lineStartOffset + i.badIndent
                if eOff < line.EndOffset then // check needed ! doc my have changed
                    base.ChangeLinePart( i.lineStartOffset, eOff, fun el -> el.TextRunProperties.SetBackgroundBrush(brush))
*)  // DELETE


type Foldings(ed:TextEditor, manager:Folding.FoldingManager, state:InteractionState, getFilePath:unit->FilePath) = 
    
    let badIndentBrush =        
        //Color.FromArgb(30uy,255uy,140uy,0uy) // a very light transparent Orange, transparent to show column rulers behind
        Color.FromArgb(50uy,255uy,255uy,0uy) // a very light transparent Yellow, transparent to show column rulers behind
        |> SolidColorBrush
        |> freeze
    
    let badIndentAction  = Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(badIndentBrush))       
    
    let maxDepth = 1 // maximum amount of nested foldings

    let minLinesOutside = 2 // minimum line count for outer folding

    let minLinesNested = 3 // minimum line count for inner folding

    let minLineCountDiffToOuter = 9 // if inner folding is just 9 line shorter than outer folding don't do it    
    
    let foldStatus = state.Config.FoldingStatus

    let saveFoldingStatus() = foldStatus.Set(getFilePath(),manager)
 
    // the color for folding box is set in SelectedTextHighlighter

    let FoldingStack = Stack<FoldStart>()
    let Folds = ResizeArray<Fold>()
    //let BadIndents = ResizeArray<NonStandardIndent>()

    let mutable isInitialLoad = true

    let defaultIndenting = ed.Options.IndentationSize
    let mutable lastBadIndentSize = 0

    let findFoldings (tx:string) :unit = 

        FoldingStack.Clear() // Collections.Generic.Stack<FoldStart>
        Folds.Clear() // ResizeArray<Fold>  

        let mutable lineNo = 1

        let textLength = tx.Length

        // returns offset of first VisibleChar
        // jumps over empty lines
        let rec findVisibleChar ind off = 
            if  off >= textLength then  // never seems to happen because of 'if en > off then..' check in findFolds
                //ISeffLog.log.PrintfnAppErrorMsg  $"off{off} = textLength"
                { indent = 0; wordStartOff = off-1}
            else
                let  c = tx.[off]
                if   c = ' '   then                        findVisibleChar (ind+1) (off+1) //TODO ignores tabs
                elif c = '\r'  then                        findVisibleChar 0       (off+1)
                elif c = '\n'  then  lineNo <- lineNo + 1; findVisibleChar 0       (off+1)
                else                                       
                    //ISeffLog.log.PrintfnAppErrorMsg  $"first VisibleChar {c}"
                    { indent= ind; wordStartOff=off}

        // returns offset of '\n'
        let rec findLineEnd off = 
            if  off = textLength then  off-1
            else
                if tx.[off] = '\n'  then  off
                else                      findLineEnd (off+1)
        
        let transformers = state.FastColorizer.Transformers
        
        //ISeffLog.log.PrintfnDebugMsg "---------findFolds--------" 
        let rec findFolds ind off = 
            let no = lineNo
            let en = findLineEnd off
            //ISeffLog.log.PrintfnDebugMsg $"---en > off : {en > off }, en = off:{en = off }"

            let le = findVisibleChar 0 en  
           
            if le.indent > ind then
                //ISeffLog.log.PrintfnDebugMsg $"le.indent > ind: offset of first VisibleChar: le.indent={le.indent} (indent {ind}) in line {no} till {lineNo}"
                let nestingLevel = FoldingStack.Count
                if nestingLevel <= maxDepth then
                    let index = Folds.Count
                    Folds.Add  {foldStartOff = -99; foldEndOff = -99 ; linesInFold = -99 ; nestingLevel = nestingLevel} // dummy value to be mutated later, if not mutated it will be filter out in foldEditor function.
                    FoldingStack.Push {indent= ind; lineEndOff = en ; line = no ; indexInFolds = index; nestingLevel = nestingLevel}
                    //ISeffLog.log.PrintfnAppErrorMsg  " line: %d: indent %d start" no ind                

            elif le.indent < ind then
                //ISeffLog.log.PrintfnFsiErrorMsg $"le.indent < ind: offset of first VisibleChar: le.indent={le.indent} (indent {ind}) in line {no} till {lineNo}"                   
                let mutable take = true
                while FoldingStack.Count > 0 && take do
                    let st = FoldingStack.Peek()
                    if st.indent >= le.indent then
                        FoldingStack.Pop()  |> ignore
                        let lines = no - st.line
                        if (st.nestingLevel = 0 && lines >= minLinesOutside)
                        || (st.nestingLevel > 0 && lines >= minLinesNested ) then // only add if block has enough lines outer wise leave dummy inside list

                            let foldStart = st.lineEndOff - 1 // the position of '\n' minus two ( does not work without the minus one)
                            let foldEnd = en - 1 // the position of '\n' minus two
                            Folds.[st.indexInFolds] <- {foldStartOff = foldStart; foldEndOff = foldEnd; linesInFold = lines ;nestingLevel = st.nestingLevel}
                            //ISeffLog.log.PrintfnAppErrorMsg  "line: %d : indent %d end of %d lines " no st.indent lines
                    else
                        take <- false
            
            // check no standart indentation
            // do first
            if lastBadIndentSize > 0 then                 
                //let ln = ed.Document.GetLineByOffset(off) 
                //ISeffLog.printError $"bad indent on line {no}={ln.LineNumber} : {lastBadIndentSize}, confirm LineOffset {ln.Offset}={off}"
                //BadIndents.Add{ badIndent=lastBadIndentSize; lineStartOffset = off-lastBadIndentSize ; lineNo=no }  // DELETE
                let stOff = off-lastBadIndentSize
                transformers.Insert(no, LinePartChange.make(stOff, stOff+lastBadIndentSize, badIndentAction, BadIndent))
                lastBadIndentSize <- 0 
            
            // do last:
            if le.indent % defaultIndenting <> 0 then 
                lastBadIndentSize <- le.indent 

            if en > off then
                findFolds le.indent le.wordStartOff // loop !

        if tx.Length > 0 then // check needed for empty string
            let le = findVisibleChar 0 0
            findFolds le.indent le.wordStartOff
      
    let textInFoldBox(count:int) = sprintf " ... %d folded lines " count

    // save folding id just as its characters length.
    // there is a risk for collision but it is small
    let collapseStatus = Dictionary<int,bool>()

    // get hash of first line
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
    let foldEditor (fullCode:string, id:int64) = 
               
        //ISeffLog.log.PrintfnDebugMsg "folding1: %s" iEditor.FilePath.File
        async{                
            let foldings = 
                findFoldings fullCode  
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
                fs       
                
            if isInitialLoad then                                
                while foldStatus.WaitingForFileRead do
                    // check like this because reading of file data happens async
                    //ISeffLog.log.PrintfnDebugMsg "waiting to load last code folding status.. "
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

                saveFoldingStatus() // so that when new foldings appear they are saved immediately

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
    member _.UpdateFoldsAndBadIndents(fullCode, id) = foldEditor(fullCode, id)
    
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

    // member _.UpdateCollapseStatus() = updateCollapseStatus()  // DELETE

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
    member _.GoToOffsetAndUnfold(offset, length,selectText) =         
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
    
   