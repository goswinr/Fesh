namespace Seff.Editor

open System
open System.Threading

open AvalonEditB
open Seff.Util.General

open FSharp.Compiler
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.EditorServices

open Seff
open Seff.Model
open Seff.Config
open Seff.Util.General


/// only a single instance of checker exist that is referenced on all editors
type Checker private (config:Config)  = 

    let log = config.Log

    let mutable checker: FSharpChecker Option = None

    let checkId = ref 0L

    let checkingEv = new Event< IEditor > ()

    let checkedEv = new Event< IEditor > ()

    let fullCodeAvailabeEv = new Event< IEditor > ()

    let mutable isFirstCheck = true

    let firstCheckDoneEv = new Event<unit>() // to first check file, then start FSI

    let mutable globalCheckState = FileCheckState.NotStarted

    /// to check full code use 0 as 'tillOffset', at the end either a event is raised or continuation called if present
    let check(iEditor:IEditor, tillOffset, continueOnThreadPool:Option<CheckResults->unit>) = 
        let thisId = Interlocked.Increment checkId
        //ISeffLog.log.PrintfnDebugMsg $"checking with id  {thisId} ..."
        globalCheckState <- GettingCode thisId
        iEditor.FileCheckState <- globalCheckState

        checkingEv.Trigger(iEditor) // to show in statusbar
        let doc = iEditor.AvaEdit.Document // access document before starting async
        async {
            do! Async.Sleep 200 // TODO add lag so that the checker does not run all the time while typing??
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
                let codeInChecker = 
                    if tillOffset = 0 then  FullCode    (doc.CreateSnapshot().Text)//the only threadsafe way to access the code string
                    else                    PartialCode (doc.CreateSnapshot(0, tillOffset).Text)

                globalCheckState <- Checking (thisId , codeInChecker)
                iEditor.FileCheckState <- globalCheckState

                match codeInChecker with
                |PartialCode _-> ()
                |FullCode _ ->
                    do! Async.SwitchToContext(FsEx.Wpf.SyncWpf.context)
                    if !checkId = thisId then
                        fullCodeAvailabeEv.Trigger(iEditor)
                    do! Async.SwitchToThreadPool()


                let fileFsx = 
                    match iEditor.FilePath with
                    |SetTo fi ->
                        let n = fi.FullName
                        if not <| n.EndsWith(".fsx",StringComparison.InvariantCultureIgnoreCase) then n + ".fsx" else n // required by FCS, oddly !
                    |NotSet -> 
                        // TODO this name should be unique even for unsaved files !!for caching
                        // Used to differentiate between scripts, to consider each script a separate project.
                        "UnSavedFile.fsx" // .fsx file required by FCS , oddly ! //TODO check if file can contain invalid path characters like *

                if !checkId = thisId  then
                    try
                        let sourceText = Text.SourceText.ofString codeInChecker.FsCode
                        // see https://github.com/dotnet/fsharp/issues/7669 for performance problems
                        // <summary>
                        // <para>For a given script file, get the FSharpProjectOptions implied by the #load closure.</para>
                        // <para>All files are read from the FileSystem API, except the file being checked.</para>
                        // </summary>
                        //
                        // <param name="filename">Used to differentiate between scripts, to consider each script a separate project. Also used in formatted error messages.</param>
                        // <param name="source">The source for the file.</param>
                        // <param name="previewEnabled">Is the preview compiler enabled.</param>
                        // <param name="loadedTimeStamp">Indicates when the script was loaded into the editing environment,
                        // so that an 'unload' and 'reload' action will cause the script to be considered as a new project,
                        // so that references are re-resolved.</param>
                        // <param name="otherFlags">Other flags for compilation.</param>
                        // <param name="useFsiAuxLib">Add a default reference to the FSharp.Compiler.Interactive.Settings library.</param>
                        // <param name="useSdkRefs">Use the implicit references from the .NET SDK.</param>
                        // <param name="assumeDotNetFramework">Set up compilation and analysis for .NET Framework scripts.</param>
                        // <param name="sdkDirOverride">Override the .NET SDK used for default references.</param>
                        // <param name="optionsStamp">An optional unique stamp for the options.</param>
                        // <param name="userOpName">An optional string used for tracing compiler operations associated with this request.</param>
                        let! options, optionsErr = 
                                checker.Value.GetProjectOptionsFromScript(filename          = fileFsx
                                                                         ,source            = sourceText
                                                                         ,previewEnabled    = true // or  [| "--langversion:preview" |] 
                                                                         //,loadedTimeStamp: DateTime *

                                                                         #if NETFRAMEWORK
                                                                         ,otherFlags       = [| "--targetprofile:mscorlib" |] //https://github.com/fsharp/FsAutoComplete/blob/f176825521215725e5b7ba888d4bb11d1e408e56/src/FsAutoComplete.Core/CompilerServiceInterface.fs#L178
                                                                         ,useFsiAuxLib = true // so that fsi object is available
                                                                         ,useSdkRefs        = false
                                                                         ,assumeDotNetFramework = true
                                                                         
                                                                         #else
                                                                         ,otherFlags       = [| "--targetprofile:netstandard" |] 
                                                                         ,useFsiAuxLib = true // so that fsi object is available
                                                                         ,useSdkRefs        =true
                                                                         ,assumeDotNetFramework = false
                                                                         #endif

                                                                         //,sdkDirOverride: string *
                                                                         //,optionsStamp: int64 *
                                                                         //,userOpName: string
                                                                         )
                        
                        // Not needed because these errors are reported by ParseAndCheckFileInProject too
                        //for oe in optionsErr do 
                        //    let msg = sprintf "%A" oe |> Util.Str.truncateToMaxLines 3
                        //    ISeffLog.log.PrintfnFsiErrorMsg "Error in GetProjectOptionsFromScript:\r\n%A" msg

                       
                        if !checkId = thisId  then
                            try                                
                                let! parseRes , checkAnswer = checker.Value.ParseAndCheckFileInProject(fileFsx, 0, sourceText, options) // can also be done in two  calls   //TODO really use check file in project for scripts??
                                match checkAnswer with
                                | FSharpCheckFileAnswer.Succeeded checkRes ->
                                    if !checkId = thisId  then // this ensures that status get set to done if no checker has started in the meantime
                                        let res = {parseRes = parseRes;  checkRes = checkRes;  code = codeInChecker ; checkId=thisId }
                                        globalCheckState <- Done res
                                        iEditor.FileCheckState <- globalCheckState

                                        match continueOnThreadPool with
                                        | Some f ->
                                            try
                                                f(res)
                                            with
                                                e -> log.PrintfnAppErrorMsg "The continueation after ParseAndCheckFileInProject failed with:\r\n %A" e

                                        | None ->
                                            do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                                            if !checkId = thisId  then                                                
                                                checkedEv.Trigger(iEditor) // to mark statusbar , and highlighting errors
                                                if isFirstCheck then
                                                    firstCheckDoneEv.Trigger() // to now start FSI
                                                    isFirstCheck <- false

                                | FSharpCheckFileAnswer.Aborted  ->
                                    log.PrintfnAppErrorMsg "FSharpChecker.ParseAndCheckFileInProject(filepath, 0, sourceText , options) returned: FSharpCheckFileAnswer.Aborted\r\nFSharpParseFileResults is: %A" parseRes
                                    globalCheckState <-Failed
                                    iEditor.FileCheckState <- globalCheckState
                            with e ->
                                log.PrintfnAppErrorMsg "Error in ParseAndCheckFileInProject Block.\r\n This may be from a Type Provider or you are using another version of FSharpCompilerService.dll than at compile time?"
                                log.PrintfnAppErrorMsg "%A" e
                                log.PrintfnAppErrorMsg "%s" e.Message
                                log.PrintfnAppErrorMsg "InnerException:\r\n%A" e.InnerException
                                if notNull e.InnerException then log.PrintfnAppErrorMsg "%s" e.InnerException.Message
                                globalCheckState <-Failed
                                iEditor.FileCheckState <- globalCheckState
                        else
                            () //ISeffLog.log.PrintfnDebugMsg $"other is running 2: this{thisId} other {!checkId} "

                    with e ->
                            log.PrintfnAppErrorMsg "Error in GetProjectOptionsFromScript Block.\r\nMaybe you are using another version of FSharpCompilerService.dll than at compile time?:"
                            log.PrintfnAppErrorMsg "%A" e
                            log.PrintfnAppErrorMsg "%s" e.Message
                            globalCheckState <-Failed
                            iEditor.FileCheckState <- globalCheckState
            else
                () //ISeffLog.log.PrintfnDebugMsg $"other is running 1: this{thisId} other {!checkId} "
            
            //ISeffLog.log.PrintfnDebugMsg $"checking  id  {thisId} Result: {globalCheckState}."
            } |> Async.Start


    static let mutable singleInstance :Checker option  = None

    //--------------------public --------------

    /// every time a new call to the global type checker happens this gets incremented
    /// this happens when the documenmt changes, not for type info requests
    member _.CurrentCheckId = !checkId

    member val Fsi  = Fsi.GetOrCreate(config) //  but  Fsi.Initalize() is only called in OnFirstCheckDone

    /// this event is raised on UI thread
    [<CLIEvent>] member this.OnChecking = checkingEv.Publish

    /// the async method doc.CreateSnapshot() completed
    [<CLIEvent>] member this.OnFullCodeAvailabe = fullCodeAvailabeEv.Publish

    /// this event is raised on UI thread
    [<CLIEvent>] member this.OnChecked = checkedEv.Publish

    /// this event is raised on UI thread
    [<CLIEvent>] member this.OnFirstCheckDone = firstCheckDoneEv.Publish

    member this.GlobalCheckState = globalCheckState

    /// ensures only one instance is created
    static member GetOrCreate(config) = 
        match singleInstance with
        |Some ch -> ch
        |None ->
            let ch = new Checker(config)
            singleInstance <- Some ch;
            ch.OnFirstCheckDone.Add ( fun ()-> ch.Fsi.Initalize() ) // to start fsi when checker is idle
            ch

    /// Triggers Event<FSharpErrorInfo[]> event after calling the continuation
    member this.CkeckHighlightAndFold (iEditor:IEditor)  =  check (iEditor, 0,  None)

    /// checks for items available for completion
    member this.GetCompletions (iEditor:IEditor, pos :PositionInCode, ifDotSetback, continueOnUI: DeclarationListInfo*FSharpSymbolUse list list  -> unit) = 
        let getSymbolsAndDecls(res:CheckResults) = 
            let thisId = !checkId
            //see https://stackoverflow.com/questions/46980690/f-compiler-service-get-a-list-of-names-visible-in-the-scope
            //and https://github.com/fsharp/FSharp.Compiler.Service/issues/835
            async{
                let colSetBack = pos.column - ifDotSetback
                let partialLongName = QuickParse.GetPartialLongNameEx(pos.lineToCaret, colSetBack - 1) //- 1) ??TODO is minus one correct ? https://github.com/fsharp/FSharp.Compiler.Service/issues/837
                //log.PrintfnDebugMsg "GetPartialLongNameEx on: '%s' setback: %d is:\r\n%A" pos.lineToCaret colSetBack partialLongName
                //log.PrintfnDebugMsg "GetDeclarationListInfo on: '%s' row: %d, col: %d, colSetBack:%d, ifDotSetback:%d\r\n" pos.lineToCaret pos.row pos.column colSetBack ifDotSetback
                if !checkId = thisId  then
                    let symUse = 
                        res.checkRes.GetDeclarationListSymbols(
                            Some res.parseRes,  // ParsedFileResultsOpt
                            pos.row,            // line
                            pos.lineToCaret ,   // lineText
                            partialLongName     // PartialLongName
                            //( fun _ -> [] )     // getAllEntities: (unit -> AssemblySymbol list)
                            )
                    if !checkId = thisId  then
                        let decls = 
                            res.checkRes.GetDeclarationListInfo(            //TODO take declaration from Symbol list !
                                Some res.parseRes,  // ParsedFileResultsOpt
                                pos.row,            // line
                                pos.lineToCaret ,   // lineText
                                partialLongName     // PartialLongName
                                //( fun _ -> [] )     // getAllEntities: (unit -> AssemblySymbol list)
                                )
                        if !checkId = thisId  then
                            if decls.IsError then log.PrintfnAppErrorMsg "*ERROR in GetDeclarationListInfo: %A" decls //TODO use log
                            else
                                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                                continueOnUI( decls, symUse)
            } |> Async.StartImmediate // we are on thread pool alredeay

        check(iEditor, pos.offset, Some getSymbolsAndDecls) //TODO can existing parse results be used ? or do they miss the dot so dont show dot completions ?
