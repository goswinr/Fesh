namespace Seff.Editor

open System
open System.Collections.Generic
open System.Windows.Media

open AvalonEditB
open AvalonEditB.Folding
open AvalonLog.Brush
open Seff.Model
open Seff.Config

[<Struct>]
type Fold = {foldStartOff:int; foldEndOff:int; linesInFold: int ; nestingLevel:int}

[<Struct>]
type FoldStart = {indent: int; lineEndOff:int; line: int; indexInFolds:int; nestingLevel:int}

[<Struct>]
type Indent = { indent: int; wordStartOff:int }

[<Struct>]
type NonStandartIndent = { badIndent: int; lineStartOffset:int; lineNo: int }


type NonStandartIndentColorizier (badInds:ResizeArray<NonStandartIndent>) = 
    inherit Rendering.DocumentColorizingTransformer() 

    let brush =        
        //Color.FromArgb(30uy,255uy,140uy,0uy) // a very light transparent Orange, transparanet to show coloum rulers behind
        Color.FromArgb(40uy,255uy,255uy,0uy) // a very light transparent Yellow, transparanet to show coloum rulers behind
        |> SolidColorBrush
        |> freeze

    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:Document.DocumentLine) = 
        for i in badInds do 
            if i.lineStartOffset = line.Offset then 
                let eOff = i.lineStartOffset + i.badIndent
                if eOff < line.EndOffset then // check needed ! doc my have changed
                    base.ChangeLinePart( i.lineStartOffset, eOff, fun el -> el.TextRunProperties.SetBackgroundBrush(brush))


type Foldings(ed:TextEditor, checker:Checker, config:Config, edId:Guid) = 

    let maxDepth = 1 // maximum amount of nested foldings

    let minLinesOutside = 2 // minimum line count for outer folding

    let minLinesNested = 3 // minimum line count for inner folding

    let minLineCountDiffToOuter = 9 // if inner folding is just 9 line shorter than outer folding don't do it

    let manager = Folding.FoldingManager.Install(ed.TextArea)  // color of margin is set in ColoumRulers.fs

    // color for folding box is set in SelectedTextHighlighter

    let FoldingStack = Stack<FoldStart>()
    let Folds = ResizeArray<Fold>()
    let BadIndents = ResizeArray<NonStandartIndent>()

    let mutable isIntialLoad = true

    let defaultIndenting = ed.Options.IndentationSize
    let mutable lastBadIndentSize = 0

    let findFoldings (tx:string) :unit = 

        FoldingStack.Clear() // Collections.Generic.Stack<FoldStart>
        Folds.Clear() // ResizeArray<Fold>       
        BadIndents.Clear()

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
            
            // check no standart indenrtation
            // do first
            if lastBadIndentSize > 0 then                 
                //let ln = ed.Document.GetLineByOffset(off) 
                //ISeffLog.printError $"bad indent on line {no}={ln.LineNumber} : {lastBadIndentSize}, confirm LineOfset {ln.Offset}={off}"
                BadIndents.Add{ badIndent=lastBadIndentSize; lineStartOffset = off-lastBadIndentSize ; lineNo=no }
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

    // get hash of firts line
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


    ///Get foldings at every line that is followed by an indent
    let foldEditor (iEditor:IEditor) = 
        //config.Log.PrintfnDebugMsg "folding: %s %A = %A" iEditor.FilePath.File edId iEditor.Id
        if edId=iEditor.Id then // foldEditor will be called on each tab, to update only current editor check id
            //ISeffLog.log.PrintfnDebugMsg "folding1: %s" iEditor.FilePath.File
            async{
                match iEditor.FileCheckState.CodeAndId with
                | NoCode ->()
                | CodeID (code,checkId) ->
                    let foldings = 
                        findFoldings code                        
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

                    if checker.CurrentCheckId = checkId then
                        do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                        match iEditor.FileCheckState.SameIdAndFullCode(checker.GlobalCheckState) with
                        | NoCode -> ()
                        | CodeID _ ->
                            if isIntialLoad then                                
                                while config.FoldingStatus.WaitingForFileRead do
                                    // check like this because reading of file data happens async
                                    ISeffLog.log.PrintfnDebugMsg "waiting to load last code folding status.. "
                                    do! Async.Sleep 50
                                let vs = config.FoldingStatus.Get(iEditor)
                                for i = 0 to foldings.Count-1 do
                                    let f = foldings.[i]
                                    let folded = if  i < vs.Length then  vs.[i]  else false
                                    let fs = manager.CreateFolding(f.foldStartOff, f.foldEndOff)
                                    fs.Tag <- box f.nestingLevel
                                    fs.IsFolded <- folded
                                    fs.Title <- textInFoldBox f.linesInFold
                                updateCollapseStatus()
                                isIntialLoad <- false

                            else
                                let folds=ResizeArray<NewFolding>()
                                
                                for f in foldings do
                                    //ISeffLog.log.PrintfnDebugMsg "Foldings from %d to %d  that is  %d lines" f.foldStartOff  f.foldEndOff f.linesInFold
                                    let fo = new NewFolding(f.foldStartOff, f.foldEndOff) 
                                    fo.Name <- textInFoldBox f.linesInFold
                                    folds.Add(fo) //if NewFolding type is created async a waiting symbol appears on top of it

                                // Existing foldings starting after this offset will be kept even if they don't appear in newFoldings. Use -1 for this parameter if there were no parse errors)
                                let firstErrorOffset = -1 //The first position of a parse error. 
                                manager.UpdateFoldings(folds, firstErrorOffset)
                                
                                // restore state after caret , because state gets lost after an auto complete                                
                                let co = iEditor.AvaEdit.CaretOffset
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

                                config.FoldingStatus.Set(iEditor) // so that when new foldings appear they are saved immediately

                } |>  Async.Start


    do
        checker.OnFullCodeAvailable.Add foldEditor // will add an event for each new tab, foldEditor skips updating if it is not current editor
        // event for tracking folding status via mouse up in margin is attached in editor.setup()
    
    
    // UNUSED : ?
    // Because when the full text gets replaced ( eg via git branch change).
    // manager.UpdateFoldings(..) cannot remember old locations and keep state
    // member this.SetToDoOneFullReload() =  isIntialLoad <- true

    member this.InitState(ied:IEditor) = 
        let vs = config.FoldingStatus.Get(ied)
        for f,s in Seq.zip manager.AllFoldings vs do f.IsFolded <- s

    member this.Manager = manager

    member this.BadIndentations = BadIndents

    member this.Margin = 
        ed.TextArea.LeftMargins
        |> Seq.tryFind ( fun m -> m :? Folding.FoldingMargin )
        |> Option.defaultWith (fun () -> failwithf "Failed to find Folding.FoldingMargin")
        :?> Folding.FoldingMargin

    member this.UpdateCollapseStatus() = updateCollapseStatus() 

    static member ExpandAll(ied:IEditor, folds:Foldings, config:Config) = 
        for f in ied.FoldingManager.AllFoldings do
            f.IsFolded <- false
        folds.UpdateCollapseStatus()
        config.FoldingStatus.Set(ied) // so that they are saved immediately

    static member CollapseAll(ied:IEditor, folds:Foldings, config:Config) = 
        for f in ied.FoldingManager.AllFoldings do
            f.IsFolded <- true
        folds.UpdateCollapseStatus()
        config.FoldingStatus.Set(ied) // so that they are saved immediately

    static member CollapsePrimary(ied:IEditor, folds:Foldings, config:Config) = 
        for f in ied.FoldingManager.AllFoldings do            
            match f.Tag with // cast might fail ??
             | :? int as tag ->
                    if tag  = 0 then // nestingLevel
                        f.IsFolded <- true
             | _ -> // because only foldins at opening get a tag, not later ones
                let ln = ied.AvaEdit.Document.GetLineByOffset(f.StartOffset)    
                let st = ied.AvaEdit.Document.GetCharAt(ln.Offset)
                if st<> ' ' then 
                    f.IsFolded <- true             
        folds.UpdateCollapseStatus()
        config.FoldingStatus.Set(ied) // so that they are saved immediately
    
    /// open any foldings if required and optionally select at location
    static member GoToOffsetAndUnfold(offset, length, ied:IEditor, folds:Foldings, config:Config, selectText) =         
        let mutable unfoldedOneOrMore = false
        for fold in ied.FoldingManager.GetFoldingsContaining(offset) do
            if fold.IsFolded then
                fold.IsFolded <- false
                unfoldedOneOrMore <- true
        let ln = ied.AvaEdit.Document.GetLineByOffset(offset)
        ied.AvaEdit.ScrollTo(ln.LineNumber,1)
        //ied.AvaEdit.CaretOffset<- loc.EndOffset // done by ied.AvaEdit.Select too
        if selectText then 
            ied.AvaEdit.Select(offset, length)
        else 
            ied.AvaEdit.CaretOffset<-offset 

        if unfoldedOneOrMore then
            folds.UpdateCollapseStatus()
            config.FoldingStatus.Set(ied) // so that they are saved immediately
    
   