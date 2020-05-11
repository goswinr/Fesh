namespace Seff.Editor

open Seff
open System
open System.IO
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open ICSharpCode.AvalonEdit
open System.Threading
    
type PositionInCode = { lineToCaret:string  
                        row: int   
                        column: int 
                        offset: int }

type CheckResults = {   parseRes:FSharpParseFileResults;  
                        checkRes:FSharpCheckFileResults;  
                        code:string
                        tillOffset:int}

type Checker (log:ISeffLog) = 
    
    let mutable checker: FSharpChecker Option = None
    
    let mutable results : CheckResults Option = None
    
    let checkId = ref 0L
    let getInfoId = ref 0L

    let checkingEv = new Event<unit>()
    
    let checkedEv = new Event<FSharpErrorInfo[]>()

    /// to check full code use 0 as 'tillOffset'
    let check(ed:Editor, fileInfo:FileInfo option, tillOffset, continueOnThreadPool:CheckResults->unit) = 
        checkingEv.Trigger()
        async {            
            let thisId = Interlocked.Increment checkId

            match checker with 
            | Some ch -> ()
            | None ->             
                // http://fsharp.github.io/FSharp.Compiler.Service/caches.html
                // https://github.com/fsharp/FSharp.Compiler.Service/blob/71272426d0e554e0bac32ad349bbd9f5fa8a3be9/src/fsharp/service/service.fs#L35
                Environment.SetEnvironmentVariable ("FCS_ParseFileCacheSize", "5") 
                // "you should generally use one global, shared FSharpChecker for everything in an IDE application." from http://fsharp.github.io/FSharp.Compiler.Service/caches.html 
                let ch = FSharpChecker.Create(suggestNamesForErrors=true) //TODO default options OK?
                checker <- Some ch
            
            if !checkId = thisId then
                let code = 
                    if tillOffset = 0 then ed.AvaEdit.Document.CreateSnapshot().Text //the only threadsafe way to acces the code string
                    else                   ed.AvaEdit.Document.CreateSnapshot(0, tillOffset).Text
            
                let fileFsx = 
                    match fileInfo with
                    |Some fi -> 
                        let n = fi.FullName
                        if not <| n.EndsWith(".fsx",StringComparison.InvariantCultureIgnoreCase) then n + ".fsx" else n // required by FCS, oddly !
                    | None -> "UnSavedFile.fsx" // .fsx file required by FCS , oddly ! //TODO check if file can contain invald path characters like *
            
                if !checkId = thisId  then
                    try
                        let sourceText = Text.SourceText.ofString code
                        let! options, optionsErr = checker.Value.GetProjectOptionsFromScript(fileFsx, sourceText, otherFlags = [| "--langversion:preview" |] ) //TODO really use check file in project for scripts??
                        for e in optionsErr do log.PrintAppErrorMsg "ERROR in GetProjectOptionsFromScript: %A" e //TODO make lo print
        
                        // "filename">The name of the file in the project whose source is being checked
                        // "fileversion">An integer that can be used to indicate the version of the file. This will be returned by TryGetRecentCheckResultsForFile when looking up the file.
                        // "source">The full source for the file.
                        // "options">The options for the project or script.
                        // "textSnapshotInfo">
                        //     An item passed back to 'hasTextChangedSinceLastTypecheck' (from some calls made on 'FSharpCheckFileResults') to help determine if 
                        //     an approximate intellisense resolution is inaccurate because a range of text has changed. This 
                        //     can be used to marginally increase accuracy of intellisense results in some situations.
                        // "userOpName">An optional string used for tracing compiler operations associated with this request.
                        (*
                        https://github.com/dotnet/fsharp/issues/7669
                        let parsingOptions = 
                              {{ SourceFiles = [|"/tmp.fsx"|]
                                ConditionalCompilationDefines = []
                                ErrorSeverityOptions = 
                                                     { WarnLevel = 3
                                                       GlobalWarnAsError = false
                                                       WarnOff = []
                                                       WarnOn = []
                                                       WarnAsError = []
                                                       WarnAsWarn = [] }
                                IsInteractive = false
                                LightSyntax = None
                                CompilingFsLib = false
                                IsExe = false }}
                                CompilingFsLib: false
                                ConditionalCompilationDefines: Length = 0
                                ErrorSeverityOptions: {{ WarnLevel = 3
                                GlobalWarnAsError = false
                                WarnOff = []
                                WarnOn = []
                                WarnAsError = []
                                WarnAsWarn = [] }}
                                IsExe: false
                                IsInteractive: false
                                LightSyntax: null
                                SourceFiles: {string[1]}
                                *)
                        if !checkId = thisId  then
                            try
                                let! parseRes , checkAnswer = checker.Value.ParseAndCheckFileInProject(fileFsx, 0, sourceText, options) // can also be done in two speterate calls   //TODO really use check file in project for scripts??         
                                match checkAnswer with
                                | FSharpCheckFileAnswer.Succeeded checkRes ->   
                                    if !checkId = thisId  then
                                        let res = {parseRes=parseRes;  checkRes=checkRes;  code=code; tillOffset=code.Length}
                                        results <- Some res 
                                        continueOnThreadPool(res)
                                        if !checkId = thisId  then
                                            do! Async.SwitchToContext Sync.syncContext 
                                            checkedEv.Trigger(checkRes.Errors) // TODO or trigger event first ? //TODO all eventstrigger in Sync ?
                
                                | FSharpCheckFileAnswer.Aborted  ->
                                    log.PrintAppErrorMsg "*ParseAndCheckFile code aborted"
                                    results <- None                       
                            with e ->
                                log.PrintAppErrorMsg "ParseAndCheckFileInProject crashed (maybe you are using another version of  FSharpCompilerService.dll than at compile time): %s" e.Message
                                results <- None         
                    with e ->
                            log.PrintAppErrorMsg "GetProjectOptionsFromScript crashed (maybe you are using another version of  FSharpCompilerService.dll than at compile time): %s" e.Message
                            results <- None 
            } |> Async.Start
    
    [<CLIEvent>]
    member this.OnChecking = checkingEv.Publish
    
    [<CLIEvent>]
    member this.OnChecked = checkingEv.Publish

    /// Triggers Event<FSharpErrorInfo[]> event
    member this.Ckeck (ed:Editor,fileInfo:FileInfo Option)  = check(ed, fileInfo, 0, ignore)

    //member this.CkeckTill( ed:Editor, fileInfo:FileInfo Option, tillOffset )  = check(ed, fileInfo, tillOffset, ignore)    

    member this.GetDeclListInfo (ed:Editor, fileInfo:FileInfo Option, pos :PositionInCode, ifDotSetback, continueOnUI: FSharpDeclarationListInfo -> unit)  =        
        let thisId = !checkId
        let getDecls(res:CheckResults) = 
            //see https://stackoverflow.com/questions/46980690/f-compiler-service-get-a-list-of-names-visible-in-the-scope
            //and https://github.com/fsharp/FSharp.Compiler.Service/issues/835            
            async{
                let colSetBack = pos.column - ifDotSetback
                let partialLongName = QuickParse.GetPartialLongNameEx(pos.lineToCaret, colSetBack - 1) //- 1) ??TODO is minus one correct ? https://github.com/fsharp/FSharp.Compiler.Service/issues/837
                //log.Print "GetPartialLongNameEx on: '%s' setback: %d is:\r\n%A" pos.lineToCaret colSetBack partialLongName  
                //log.Print "GetDeclarationListInfo on: '%s' row: %d, col: %d, colSetBack:%d, ifDotSetback:%d\r\n" pos.lineToCaret pos.row pos.column colSetBack ifDotSetback          
                if !checkId = thisId  then 
                    let! decls = 
                        res.checkRes.GetDeclarationListInfo(
                            Some res.parseRes,  // ParsedFileResultsOpt
                            pos.row,            // line                   
                            pos.lineToCaret ,   // lineText
                            partialLongName,    // PartialLongName
                            ( fun _ -> [] )     // getAllEntities: (unit -> AssemblySymbol list) 
                            )
                    if !checkId = thisId  then         
                        do! Async.SwitchToContext Sync.syncContext 
                        if decls.IsError then log.PrintAppErrorMsg "*ERROR in GetDeclarationListInfo: %A" decls //TODO use log
                        else continueOnUI( decls)
            } |> Async.StartImmediate 
        
        if !checkId = thisId  && results.IsSome && results.Value.tillOffset >= pos.offset then 
            getDecls(results.Value) 
        else 
            check(ed, fileInfo, pos.offset, getDecls)
    
    member this.GetDeclListSymbols (ed:Editor, fileInfo:FileInfo Option, pos :PositionInCode, ifDotSetback, continueOnUI:FSharpSymbolUse list list -> unit)  =        
        let thisId = !checkId
        let getSymbols(res:CheckResults) = 
            //see https://stackoverflow.com/questions/46980690/f-compiler-service-get-a-list-of-names-visible-in-the-scope
            //and https://github.com/fsharp/FSharp.Compiler.Service/issues/835            
            async{
                let colSetBack = pos.column - ifDotSetback
                let partialLongName = QuickParse.GetPartialLongNameEx(pos.lineToCaret, colSetBack - 1) //- 1) ??TODO is minus one correct ? https://github.com/fsharp/FSharp.Compiler.Service/issues/837
                //log.Print "GetPartialLongNameEx on: '%s' setback: %d is:\r\n%A" pos.lineToCaret colSetBack partialLongName  
                //log.Print "GetDeclarationListInfo on: '%s' row: %d, col: %d, colSetBack:%d, ifDotSetback:%d\r\n" pos.lineToCaret pos.row pos.column colSetBack ifDotSetback          
                if !checkId = thisId  then 
                    let! symUse = 
                        res.checkRes.GetDeclarationListSymbols(
                            Some res.parseRes,  // ParsedFileResultsOpt
                            pos.row,            // line                   
                            pos.lineToCaret ,   // lineText
                            partialLongName,    // PartialLongName
                            ( fun _ -> [] )     // getAllEntities: (unit -> AssemblySymbol list) 
                            )
                    if !checkId = thisId  then         
                        do! Async.SwitchToContext Sync.syncContext 
                        continueOnUI( symUse)
            
            } |> Async.StartImmediate 
    
        if !checkId = thisId  && results.IsSome && results.Value.tillOffset >= pos.offset then 
            getSymbols(results.Value) 
        else 
            check(ed, fileInfo, pos.offset, getSymbols)
