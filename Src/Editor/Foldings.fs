﻿namespace Seff.Editor

open System

open Seff
open Seff.Model
open Seff.Util
open Seff.Util.General
open Seff.Config

open AvalonEditB
open AvalonEditB.Folding


    
[<Struct>]
type Fold = {foldStartOff:int; foldEndOff:int; linesInFold: int ; nestingLevel:int}

[<Struct>]
type FoldStart = {indent: int; lineEndOff:int; line: int; indexInFolds:int; nestingLevel:int}

[<Struct>]
type Indent = { indent: int; wordStartOff:int }

type Foldings(ed:TextEditor, checker:Checker, config:Config, edId:Guid) = 
    
    
    let maxDepth = 1 // maximum amount of nested foldings 

    let minLinesOutside = 2 // minimum line count for outer folding 

    let minLinesNested = 3 // minimum line count for inner folding 

    let minLineCountDiffToOuter = 9 // if inner folding is just 9 line shorter than outer folding dont do it


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

    let findFoldings (tx:string) =
    
        FoldingStack.Clear()
        Folds.Clear()
        //let FoldingStack = Collections.Generic.Stack<FoldStart>()
        //let Folds = ResizeArray<Fold>()

        let mutable lineNo = 1
    
        // returns offset of first VisibleChar
        // jumps over empty lines
        let rec findVisibleChar ind off =
            if  off = tx.Length then  { indent = 0; wordStartOff = off-1}
            else 
                let c = tx.[off]
                if   c = ' '   then                        findVisibleChar (ind+1) (off+1) //TODO ignores tabs
                elif c = '\r'  then                        findVisibleChar 0       (off+1)        
                elif c = '\n'  then  lineNo <- lineNo + 1; findVisibleChar 0       (off+1)        
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
                let le = findVisibleChar 0 en
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
    
        if tx.Length > 0 then // scheck neded for empty string            
            let le = findVisibleChar 0 0
            findFolds le.indent le.wordStartOff
        Folds


    ///Get foldings at every line that is followed by an indent
    let foldEditor (iEditor:IEditor) =        
        //config.Log.PrintfnDebugMsg "folding: %s %A = %A" iEditor.FilePath.File edId iEditor.Id
        if edId=iEditor.Id then // will be called on each tab, to skips updating  if it is not current editor
            //config.Log.PrintfnDebugMsg "folding1: %s" iEditor.FilePath.File
            async{            
                match iEditor.FileCheckState.FullCodeAndId with
                | NoCode ->()
                | CodeID (code,checkId) -> 

                    let foldings = 
                        let ffs = findFoldings code
                        let l = ffs.Count-1
                        let fs = ResizeArray(max 0 l)// would be -1 if no foldings
                        let mutable lastOuter = {foldStartOff = -99; foldEndOff = -99 ; linesInFold = -99 ; nestingLevel = -99}
                        for i=0 to l do
                            let f = ffs.[i]
                            if f.foldEndOff > 0 then // filter out to short blocks that are leeft as dummy
                                if  f.nestingLevel = 0 then 
                                    lastOuter <- f
                                    fs.Add f
                                elif f.linesInFold + minLineCountDiffToOuter < lastOuter.linesInFold then // filter out inner blocks that are almost the size of the outer block
                                    fs.Add f 
                        fs
                        
                    
                    if foldings.Count>0 then 
                        do! Async.SwitchToContext Sync.syncContext
                        match iEditor.FileCheckState.SameIdAndFullCode(checker.GlobalCheckState) with
                        | NoCode -> ()
                        | CodeID _ -> 
                            if isIntialLoad then 
                                while config.FoldingStatus.WaitingForFileRead do
                                    // check like this because reading of file data happens async 
                                    config.Log.PrintfnDebugMsg "waiting to load last code folding status.. "
                                    do! Async.Sleep 50
                                let vs = config.FoldingStatus.Get(iEditor)                                
                                for i=0 to foldings.Count-1 do
                                    let f = foldings.[i]
                                    let folded = if  i < vs.Length then  vs.[i]  else false          
                                    let fs = manager.CreateFolding(f.foldStartOff, f.foldEndOff)
                                    fs.Tag <- box f.nestingLevel
                                    fs.IsFolded <- folded
                                    fs.Title <- sprintf " ... %d folded lines " f.linesInFold                                        
                                isIntialLoad <- false

                            else
                                let folds=ResizeArray<NewFolding>()
                                for f in foldings do                                 
                                    //config.Log.PrintfnDebugMsg "Foldings from %d to %d  that is  %d lines" f.foldStartOff  f.foldEndOff f.linesInFold
                                    let fo = new NewFolding(f.foldStartOff, f.foldEndOff)                                
                                    fo.Name <- sprintf " ... %d folded lines " f.linesInFold
                                    folds.Add(fo) //if NewFolding type is created async a waiting symbol apears on top of it 
                            
                                let firstErrorOffset = -1 //The first position of a parse error. Existing foldings starting after this offset will be kept even if they don't appear in newFoldings. Use -1 for this parameter if there were no parse errors) 
                                manager.UpdateFoldings(folds,firstErrorOffset)
                                config.FoldingStatus.Set(iEditor) // so that when new flodings apear they are saved immedeatly
                                
                } |>  Async.Start       
    
    
    do        
        checker.OnFullCodeAvailabe.Add foldEditor // will add an event for each new tab, foldEditor skips updating  if it is not current editor        
        // event for tracking folding status via mouse up in margin is attached in editor.setup()

    member this.SetState(ied:IEditor) =
        let vs = config.FoldingStatus.Get(ied)     
        for f,s in Seq.zip manager.AllFoldings vs do f.IsFolded <- s

    member this.Manager = manager

    member this.Margin = 
        ed.TextArea.LeftMargins
        |> Seq.tryFind ( fun m -> m :? Folding.FoldingMargin )
        |> Option.defaultWith (fun () -> failwithf "Failed to find Folding.FoldingMargin")
        :?> Folding.FoldingMargin

    static member ExpandAll(ied:IEditor, config:Config) = 
        for f in ied.FoldingManager.AllFoldings do 
            f.IsFolded <- false
        config.FoldingStatus.Set(ied) // so that they are saved immedeatly
    
    static member CollapseAll(ied:IEditor, config:Config) = 
        for f in ied.FoldingManager.AllFoldings do 
            f.IsFolded <- true
        config.FoldingStatus.Set(ied) // so that they are saved immedeatly
    
    static member CollapsePrimary(ied:IEditor, config:Config) = 
        for f in ied.FoldingManager.AllFoldings do 
            if unbox f.Tag = 0 then // nestingLevel
                f.IsFolded <- true
        config.FoldingStatus.Set(ied) // so that they are saved immedeatly