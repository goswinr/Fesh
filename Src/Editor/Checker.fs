namespace Seff.Editor

open Seff

open System
open System.IO
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open ICSharpCode.AvalonEdit
open System.Threading
open Seff.Config
open System.Windows.Threading

type Checker private (config:Config)  = 
    
    let log = config.Log

    let mutable checker: FSharpChecker Option = None
    
    let checkId = ref 0L    

    let checkingEv = new Event< IEditor > () 
    
    let checkedEv = new Event< IEditor > ()

    let fullCodeAvailabeEv = new Event< IEditor > ()

    let mutable isFirstCheck = true
    let firstCheckDoneEv = new Event<unit>() // to first check file, then start FSI

    let mutable status = FileCheckState.NotStarted

    /// to check full code use 0 as 'tillOffset', at the end either a event is raised or continuation called if present
    let check(iEditor:IEditor, tillOffset, continueOnThreadPool:Option<CheckResults->unit>) =         
        log.PrintDebugMsg "***checking  %A in %A " iEditor.FilePath iEditor.CheckState
        let thisId = Interlocked.Increment checkId
        status <- GettingCode thisId        
        checkingEv.Trigger(iEditor) // to show in statusbar
        let doc = iEditor.AvaEdit.Document // access documnet before starting async        
        async { 
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
                    if tillOffset = 0 then  FullCode    (doc.CreateSnapshot().Text)//the only threadsafe way to acces the code string  
                    else                    PartialCode (doc.CreateSnapshot(0, tillOffset).Text)
                
                status <- Checking (thisId , code)   
                
                match code with 
                |PartialCode _-> ()
                |FullCode _ ->                 
                    do! Async.SwitchToContext(Sync.syncContext)
                    fullCodeAvailabeEv.Trigger(iEditor)
                    do! Async.SwitchToThreadPool()

                let fileFsx = 
                    match iEditor.FilePath with
                    |SetTo fi -> 
                        let n = fi.FullName
                        if not <| n.EndsWith(".fsx",StringComparison.InvariantCultureIgnoreCase) then n + ".fsx" else n // required by FCS, oddly !
                    |NotSet -> "UnSavedFile.fsx" // .fsx file required by FCS , oddly ! //TODO check if file can contain invald path characters like *
            
                if !checkId = thisId  then
                    try
                        
                        let sourceText = Text.SourceText.ofString code.Code
                        let! options, optionsErr = checker.Value.GetProjectOptionsFromScript(fileFsx, sourceText, otherFlags = [| "--langversion:preview" |] ) // Gets additional script #load closure information if applicable.
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
                                //log.PrintDebugMsg "checking %A" iEditor.FileInfo
                                let! parseRes , checkAnswer = checker.Value.ParseAndCheckFileInProject(fileFsx, 0, sourceText, options) // can also be done in two  calls   //TODO really use check file in project for scripts?? 
                                match checkAnswer with
                                | FSharpCheckFileAnswer.Succeeded checkRes ->   
                                    if !checkId = thisId  then // this ensures that stat get set to done ich no checker has started in the meantime
                                        let res = {parseRes = parseRes;  checkRes = checkRes;  code = code ; checkId=thisId }
                                        status <- Done res
                                        //iEditor.CheckState <- status                                        
                                        match continueOnThreadPool with
                                        | Some f -> f(res)
                                        | None -> 
                                            do! Async.SwitchToContext Sync.syncContext 
                                            if !checkId = thisId  then 
                                                checkedEv.Trigger(iEditor) // to  mark statusbar , and highlighting errors 
                                                if !checkId = thisId  && isFirstCheck then 
                                                    firstCheckDoneEv.Trigger() //now start FSI
                                                    isFirstCheck <- false
                
                                | FSharpCheckFileAnswer.Aborted  ->
                                    log.PrintAppErrorMsg "*ParseAndCheckFile code aborted"
                                    status <-Failed                                                      
                            with e ->
                                log.PrintAppErrorMsg "Error in ParseAndCheckFileInProject Block.\r\nMaybe you are using another version of  FSharpCompilerService.dll than at compile time?\r\nOr the error is in the continuation.\r\nOr in the event handlers: %A" e
                                status <-Failed                                           
                    with e ->
                            log.PrintAppErrorMsg "Error in GetProjectOptionsFromScript Block.\r\nMaybe you are using another version of  FSharpCompilerService.dll than at compile time?: %A" e
                            status <-Failed                             
                            
            } |> Async.Start
    
    
    static let mutable singleInstance:Checker option  = None

    //--------------------public --------------
        
    /// this event is raised on UI thread    
    [<CLIEvent>] member this.OnChecking = checkingEv.Publish

    /// the async method doc.CreateSnapshot() completed   
    [<CLIEvent>] member this.OnFullCodeAvailabe = fullCodeAvailabeEv.Publish
    
    /// this event is raised on UI thread    
    [<CLIEvent>] member this.OnChecked = checkedEv.Publish
    
    /// this event is raised on UI thread    
    [<CLIEvent>] member this.OnFirstCheckDone = firstCheckDoneEv.Publish

   
    member this.CheckState = status

    /// ensures only one instance is created
    static member GetOrCreate(config) = 
        match singleInstance with 
        |Some ch -> ch
        |None -> 
            let ch = new Checker(config)
            ch.OnFirstCheckDone.Add ( fun () -> Fsi.GetOrCreate(config).Initalize() ) // to start fsi when checker is idle
            singleInstance <- Some ch; 
            ch

    /// Triggers Event<FSharpErrorInfo[]> event after calling the continuation
    member this.CkeckHighlightAndFold (iEditor:IEditor)  =  check (iEditor, 0, None)

    /// checks for items available for completion
    member this.GetCompletions (iEditor:IEditor, pos :PositionInCode, ifDotSetback, continueOnUI: FSharpDeclarationListInfo*FSharpSymbolUse list list  -> unit) =        
        let getSymbolsAndDecls(res:CheckResults) = 
            let thisId = !checkId
            //see https://stackoverflow.com/questions/46980690/f-compiler-service-get-a-list-of-names-visible-in-the-scope
            //and https://github.com/fsharp/FSharp.Compiler.Service/issues/835            
            async{
                let colSetBack = pos.column - ifDotSetback
                let partialLongName = QuickParse.GetPartialLongNameEx(pos.lineToCaret, colSetBack - 1) //- 1) ??TODO is minus one correct ? https://github.com/fsharp/FSharp.Compiler.Service/issues/837
                //log.PrintDebugMsg "GetPartialLongNameEx on: '%s' setback: %d is:\r\n%A" pos.lineToCaret colSetBack partialLongName  
                //log.PrintDebugMsg "GetDeclarationListInfo on: '%s' row: %d, col: %d, colSetBack:%d, ifDotSetback:%d\r\n" pos.lineToCaret pos.row pos.column colSetBack ifDotSetback          
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
                        let! decls = 
                            res.checkRes.GetDeclarationListInfo(            //TODO take declaration from Symbol list !
                                Some res.parseRes,  // ParsedFileResultsOpt
                                pos.row,            // line                   
                                pos.lineToCaret ,   // lineText
                                partialLongName,    // PartialLongName
                                ( fun _ -> [] )     // getAllEntities: (unit -> AssemblySymbol list) 
                                )
                        if !checkId = thisId  then
                            if decls.IsError then log.PrintAppErrorMsg "*ERROR in GetDeclarationListInfo: %A" decls //TODO use log
                            else
                                do! Async.SwitchToContext Sync.syncContext 
                                continueOnUI( decls, symUse)
            } |> Async.StartImmediate // we are on thread pool alredeay    
        
        check(iEditor, pos.offset, Some getSymbolsAndDecls) //TODO can existing parse results be used ? or do they miss the dot so dont show dot completions ?
