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



type Foldings(ed:TextEditor,checker:Checker) = 
    
    let minLinesForFold = 1

    let manager = Folding.FoldingManager.Install(ed.TextArea)  
        
    /// a hash value to  see if folding state needs updating
    let mutable foldStateHash = 0
    
    /// poor man's hash function
    let getFoldstate (xys: ResizeArray<int*int*int>) =
        let mutable v = 0
        for x,y,_ in xys do   
            v <- v + x
            v <- v + (y<<<16)
        v
    
    ///Get foldings at every line that is followed by an indent
    let foldEd (iEditor:IEditor) =
        eprintfn "folding: %A" iEditor.FilePath
        async{            
            match iEditor.CheckState.FullCodeAndId with
            | NoCode ->()
            | CodeID (code,id0) ->                
                // TODO compute update only for visible areas not allcode?
                let foldings=ResizeArray<int*int*int>()
                let lns = code.Split([|"\r\n"|],StringSplitOptions.None) // TODO better iterate without allocating an array of lines  
                let mutable currLnEndOffset = 0
                let mutable foldStartOfset = -1
                let mutable foldStartLine = -1
                let mutable lastNotBlankLineEndOffset = -1
                let mutable lastNotBlankLineNum = 0

                for lni, ln in Seq.indexed lns do 
                    let lnNum = lni+1
                    currLnEndOffset <- currLnEndOffset + ln.Length + 2
                    let notBlank = not (String.isJustSpaceCharsOrEmpty ln)  
                    if notBlank && ln.Length>0 then                         
                        let firstChar = ln.[0]
                        if firstChar <> ' ' then  
                        
                            //test for open folds
                            if foldStartOfset > 0 then    
                                if foldStartLine <= lastNotBlankLineNum - minLinesForFold then                             
                                
                                    let foldEnd = lastNotBlankLineEndOffset - 2 //-2 to skip over line break 
                                    //log.PrintDebugMsg "Folding from  line %d to %d : Offset %d to %d" foldStartLine lastNotBlankLineNum foldStartOfset foldEnd
                                    let foldedlines = lastNotBlankLineNum - foldStartLine
                                    let f = foldStartOfset, foldEnd,  foldedlines
                                    foldings.Add f                            
                                    foldStartOfset <- -1
                                    foldStartLine  <- -1
                                else
                                    foldStartOfset <- -1
                                    foldStartLine  <- -1
                        
                            //on then same line a new fold might open
                            if foldStartOfset < 0 then                                                   
                                foldStartLine <- lnNum
                                foldStartOfset <- currLnEndOffset-2//-2 to skip over line break
                        lastNotBlankLineEndOffset <- currLnEndOffset
                        lastNotBlankLineNum <- lnNum
                  
            
                //close last folding
                if foldStartOfset > 0 then                  
                    let foldEnd = lastNotBlankLineEndOffset - 2 //-2 to skip over line break
                    //log.PrintDebugMsg "Last Folding from  line %d to end : Offset %d to %d" foldStartLine  foldStartOfset foldEnd
                    let foldedlines = lastNotBlankLineNum - foldStartLine
                    let f = foldStartOfset, foldEnd , foldedlines
                    foldings.Add f                   
            
                let state = getFoldstate foldings
                if state = foldStateHash then 
                    () // noch chnages in folding
                else
                    foldStateHash <- state
                    do! Async.SwitchToContext Sync.syncContext
                    match iEditor.CheckState.FullCodeAndId with
                    | NoCode ->()
                    | CodeID (_,id) ->
                        if id = id0 then
                            let folds=ResizeArray<NewFolding>()
                            for st,en,length in foldings do 
                                let f = new NewFolding(st,en)
                                f.Name <- sprintf " ... %d Lines " length
                                folds.Add(f) //if new folding type is created async a waiting symbol apears on top of it 
                            let firstErrorOffset = -1 //The first position of a parse error. Existing foldings starting after this offset will be kept even if they don't appear in newFoldings. Use -1 for this parameter if there were no parse errors) 
                            manager.UpdateFoldings(folds,firstErrorOffset)
            } |>  Async.Start       
        
        
    do        
        checker.OnFullCodeAvailabe.Add foldEd

    member this.Manager = manager

    member this.ExpandAll() = for f in manager.AllFoldings do f.IsFolded <- false
    
    member this.CollapseAll() = for f in manager.AllFoldings do f.IsFolded <- true

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


  