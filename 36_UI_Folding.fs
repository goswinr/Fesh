namespace Seff

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

module FsFolding = 
    
    /// a hash value to  see if folding state needs updating
    let mutable FoldStateHash = 0
    

    let getFoldstate (xys: ResizeArray<int*int>) =
        let mutable v = 0
        for x,y in xys do   
            v <- v + x
            v <- v + (y>>>16)
        v

    let minLinesForFold = 1

    let get (tab:FsxTab, code:string) : Async<unit> =
        async{ 
            do! Async.SwitchToThreadPool()
            let foldings=ResizeArray<int*int>()
            let lns = code.Split([|"\r\n"|],StringSplitOptions.None)
            
            let mutable currLnEndOffset = 0
            let mutable foldStartOfset = -1
            let mutable foldStartLine = -1
            let mutable lastNotBlankLineEndOffset = -1
            let mutable lastNotBlankLineNum = 0

            for lni, ln in Seq.indexed lns do 
                let lnNum = lni+1
                currLnEndOffset <- currLnEndOffset + ln.Length + 2
                let notBlank = not (StringUtil.isJustSpaceCharsOrEmpty ln)  
                if notBlank && ln.Length>0 then                         
                    let firstChar = ln.[0]
                    if firstChar <> ' ' then  
                        
                        //test for open folds
                        if foldStartOfset > 0 then    
                            if foldStartLine <= lastNotBlankLineNum - minLinesForFold then                             
                                
                                let foldEnd = lastNotBlankLineEndOffset - 2 //-2 to skip over line break 
                                //Log.printf "Folding from  line %d to %d : Offset %d to %d" foldStartLine lastNotBlankLineNum foldStartOfset foldEnd
                                let f = foldStartOfset, foldEnd
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
                //Log.printf "Last Folding from  line %d to end : Offset %d to %d" foldStartLine  foldStartOfset foldEnd
                let f = foldStartOfset, foldEnd
                foldings.Add f
                   
            
            let state = getFoldstate foldings
            if state = FoldStateHash then 
                tab.Foldings <- None
            else
                FoldStateHash <- state
                tab.Foldings<- Some foldings
            do! Async.SwitchToContext Sync.syncContext
            }

    
    
    // or  walk AST ?

    //let visitDeclarations decls = 
    //  for declaration in decls do
    //    match declaration with
    //    | SynModuleDecl.Let(isRec, bindings, range) ->
    //        // Let binding as a declaration is similar to let binding
    //        // as an expression (in visitExpression), but has no body
    //        for binding in bindings do
    //          let (Binding(access, kind, inlin, mutabl, attrs, xmlDoc, data, pat, retInfo, body, range, sp)) = binding
    //          Log.printf "Binding: %A  from %d to %d:" kind  range.StartLine range.EndLine             
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
    //            Log.printf "Namespace or module: %A : %A from %d to %d" lid isMod range.StartLine range.EndLine   
    //            visitDeclarations decls
    //    | _ -> failwith "F# Interface file (*.fsi) not supported."