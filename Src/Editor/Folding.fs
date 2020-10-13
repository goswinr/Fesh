namespace Seff.Editor

open Seff
open Seff.Util
open System
open System.Windows
open System.Windows.Controls
open System.Windows.Media

open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.Editing
open ICSharpCode.AvalonEdit.Document
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open ICSharpCode.AvalonEdit.Folding
open Seff.Util
open Seff.Util.General
open Seff.Config


    
[<Struct>]
type Fold = {foldStartOff:int; foldEndOff:int; linesInFold: int ; nestingLevel:int}

[<Struct>]
type FoldStart = {indent: int; lineEndOff:int; line: int; indexInFolds:int; nestingLevel:int}

[<Struct>]
type Indent = { indent: int; wordStartOff:int }

type Foldings(ed:TextEditor,checker:Checker,config:Config, edId:Guid) = 
    
    
    let maxDepth = 1 // maximum amount of nested foldings 

    let minLinesOutside = 2 // minimum line count for outer folding 

    let minLinesNested = 3 // minimum line count for inner folding 

    let minLineCountDiffToOuter = 4 // if inner folding is just 4 line shorte than outer folding dont doo it

    let manager = Folding.FoldingManager.Install(ed.TextArea)  // color of margin is set in ColoumRulers.fs

    // color for folding box is set in SelectedTextHighlighter
    
    (*
    /// a hash value to  see if folding state needs updating
    let mutable foldStateHash = 0
    
    /// poor man's hash function
    let getFoldstate (xys: ResizeArray<Fold>) =
        let mutable v = 0
        for f in xys do   
            v <- v +  f.foldStartOff
            v <- v + (f.foldEndOff <<< 16)
        v *)
    
    let FoldingStack = Collections.Generic.Stack<FoldStart>()
    let Folds = ResizeArray<Fold>()


    let mutable isIntialLoad = true

    let findFolds (tx:string) =
    
        FoldingStack.Clear()
        Folds.Clear()
        //let FoldingStack = Collections.Generic.Stack<FoldStart>()
        //let Folds = ResizeArray<Fold>()

        let mutable lineNo = 1
    
        // returns offset of first letter
        // jumps over empty lines
        let rec findLetter ind off =
            if  off = tx.Length then  { indent = 0; wordStartOff = off-1}
            else 
                let c = tx.[off]
                if   c = ' '   then                        findLetter (ind+1) (off+1) //TODO ignores tabs
                elif c = '\r'  then                        findLetter 0       (off+1)        
                elif c = '\n'  then  lineNo <- lineNo + 1; findLetter 0       (off+1)        
                else                 { indent= ind; wordStartOff=off}
        
        // returns offset of '\n'
        let rec findLineEnd off =
            if  off = tx.Length then  off-1
            else 
                if tx.[off] = '\n'  then  off
                else                      findLineEnd (off+1)  
    
    
        let rec findFolds ind off =         
            let no = lineNo
            let en = findLineEnd off
            if en > off then 
                let le = findLetter 0 en
                //printfn "le.indent: %i (ind %d)  in line %d" le.indent ind no
            
                if le.indent = ind then 
                    findFolds le.indent le.wordStartOff
                
                elif le.indent > ind then 
                    let nestingLevel = FoldingStack.Count
                    if nestingLevel <= maxDepth then 
                        let index = Folds.Count
                        Folds.Add  {foldStartOff = -99; foldEndOff = -99 ; linesInFold = -99 ; nestingLevel = nestingLevel} // dummy value to be mutated later 
                        FoldingStack.Push {indent= ind; lineEndOff = en ; line = no ; indexInFolds = index; nestingLevel = nestingLevel}
                        //printfn " line: %d: indent %d start" no ind
                    findFolds le.indent le.wordStartOff
            
                elif le.indent < ind then 
                    //eprintfn "%A" St
                    let mutable take = true
                    while FoldingStack.Count > 0 && take do                
                        let st = FoldingStack.Peek()
                        if st.indent >= le.indent then 
                            FoldingStack.Pop()  |> ignore 
                            let lines = no - st.line
                            if (st.nestingLevel = 0 && lines >= minLinesOutside)
                            || (st.nestingLevel > 0 && lines >= minLinesNested ) then // only add if block has enoug lines outer wise leave dummy inside list                                
                                
                                let foldStart = st.lineEndOff - 1 // the position of '\n' minus two ( does not work without the minus one)
                                let foldEnd = en - 1 // the position of '\n' minus two
                                Folds.[st.indexInFolds] <- {foldStartOff = foldStart; foldEndOff = foldEnd; linesInFold = lines ;nestingLevel = st.nestingLevel}
                                //eprintfn "line: %d : indent %d end of %d lines " no st.indent lines
                        else
                            take <- false            
                    findFolds le.indent le.wordStartOff        
    
    
        let le = findLetter 0 0
        findFolds le.indent le.wordStartOff
        Folds


    ///Get foldings at every line that is followed by an indent
    let foldEditor (iEditor:IEditor) =        
        //config.Log.PrintDebugMsg "folding: %s %A = %A" iEditor.FilePath.File edId iEditor.Id
        if edId=iEditor.Id then // will be called on each tab, to skips updating  if it is not current editor
            //config.Log.PrintDebugMsg "folding1: %s" iEditor.FilePath.File
            async{            
                match iEditor.FileCheckState.FullCodeAndId with
                | NoCode ->()
                | CodeID (code,checkId) ->                    
                    let foldings = 
                        let ffs = findFolds code
                        let l = ffs.Count-1
                        let fs = ResizeArray(l)
                        let mutable lastOuter = {foldStartOff = -99; foldEndOff = -99 ; linesInFold = -99 ; nestingLevel = -99}
                        for i=0 to l do
                            let f = ffs.[i]
                            if f.foldEndOff > 0 then // filter out to short blocks that are leeft as dummy
                                if  f.nestingLevel = 0 then 
                                    lastOuter <- f
                                    fs.Add f
                                elif f.linesInFold + minLineCountDiffToOuter > lastOuter.linesInFold then // filter out inner blocks that are almost the size of the outer block
                                    fs.Add f 
                        fs
                        
                    //let state = getFoldstate foldings
                    //if state = foldStateHash then 
                        //() // no chnages in folding
                    //else
                        //foldStateHash <- state

                    if foldings.Count>0 then 
                        do! Async.SwitchToContext Sync.syncContext
                        match iEditor.FileCheckState.SameIdAndFullCode(checker.GlobalCheckState) with
                        | NoCode -> ()
                        | CodeID _ -> 
                            if isIntialLoad then 
                                while config.FoldingStatus.WaitingForFileRead do
                                    config.Log.PrintDebugMsg "waiting to load last code folding status.. "
                                    do! Async.Sleep 50
                                let vs = config.FoldingStatus.Get(iEditor)                                
                                for i=0 to foldings.Count-1 do
                                    let f = foldings.[i]
                                    let folded = if  i < vs.Length then  vs.[i]  else false          
                                    let fs = manager.CreateFolding(f.foldStartOff, f.foldEndOff)
                                    fs.IsFolded <- folded
                                    fs.Title <- sprintf " ... %d Lines " f.linesInFold                                        
                                isIntialLoad <- false

                            else
                                let folds=ResizeArray<NewFolding>()
                                for f in foldings do                                 
                                    //config.Log.PrintDebugMsg "Foldings from %d to %d  that is  %d lines" f.foldStartOff  f.foldEndOff f.linesInFold
                                    let fo = new NewFolding(f.foldStartOff, f.foldEndOff)                                
                                    fo.Name <- sprintf " ... %d Lines " f.linesInFold
                                    folds.Add(fo) //if NewFolding type is created async a waiting symbol apears on top of it 
                            
                                let firstErrorOffset = -1 //The first position of a parse error. Existing foldings starting after this offset will be kept even if they don't appear in newFoldings. Use -1 for this parameter if there were no parse errors) 
                                manager.UpdateFoldings(folds,firstErrorOffset)
                                
                } |>  Async.Start       
    
    
    do        
        checker.OnFullCodeAvailabe.Add foldEditor // will add an event for each new tab, foldEditor skips updating  if it is not current editor
        
        // event for tracking folding status is attached in editor.setup()

    member this.SetState(ied:IEditor) =
        let vs = config.FoldingStatus.Get(ied)     
        for f,s in Seq.zip manager.AllFoldings vs do f.IsFolded <- s

    member this.Manager = manager

    member this.Margin = 
        ed.TextArea.LeftMargins
        |> Seq.tryFind ( fun m -> m :? Folding.FoldingMargin )
        |> Option.defaultWith (fun () -> failwithf "Failed to find Folding.FoldingMargin")
        :?> Folding.FoldingMargin

    member this.ExpandAll() = for f in manager.AllFoldings do f.IsFolded <- false
    
    member this.CollapseAll() = for f in manager.AllFoldings do f.IsFolded <- true

    static member val private EventIsSetUp = false with get, set // so the event OnFullCodeAvailabe is only attached once to checker

    

    //let firstErrorOffset = -1 //The first position of a parse error. Existing foldings starting after this offset will be kept even if they don't appear in newFoldings. Use -1 for this parameter if there were no parse errors)                    
    //manager.UpdateFoldings(foldings,firstErrorOffset)
    
    // or walk AST ?

    //let visitDeclarations decls = 
    //  for declaration in decls do
    //    match declaration with
    //    | SynModuleDecl.Let(isRec, bindings, range) ->
    //        // Let binding as a declaration is similar to let binding
    //        // as an expression (in visitExpression), but has no body
    //        for binding in bindings do
    //          let (Binding(access, kind, inlin, mutabl, attrs, xmlDoc, data, pat, retInfo, body, range, sp)) = binding
    //          log.PrintDebugMsg "Binding: %A  from %d to %d:" kind  range.StartLine range.EndLine             
    //    | _ -> printfn " - not supported declaration: %A" declaration

    //match parseRes.ParseTree with 
    //|None -> ()
    //|Some tree ->  
    //match tree with
    //    | ParsedInput.ImplFile(implFile) ->
    //        // Extract declarations and walk over them
    //        let (ParsedImplFileInput(fn, script, name, _, _, modulesOrNss, _)) = implFile
    //        for moduleOrNs in modulesOrNss do
    //            let (SynModuleOrNamespace(lid, isRec, isMod, decls, xml, attrs, sao, range)) = moduleOrNs
    //            log.PrintDebugMsg "Namespace or module: %A : %A from %d to %d" lid isMod range.StartLine range.EndLine   
    //            visitDeclarations decls
    //    | _ -> failwith "F# Interface file (*.fsi) not supported."


  