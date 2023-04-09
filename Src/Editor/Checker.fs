namespace Seff.Editor

open System
open System.Collections.Generic
open System.Threading

open Seff.Util.General

open FSharp.Compiler
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.EditorServices

open Seff
open Seff.Model
open Seff.Config

/// Only a single instance of checker exist that is referenced on all editors
type Checker private (config:Config)  = 

    let checkId = ref 0L
    let mutable fsChecker: FSharpChecker Option = None // "you should generally use one global, shared FSharpChecker for everything in an IDE application." from http://fsharp.github.io/FSharp.Compiler.Service/caches.html
    let mutable isFirstCheck = true
    let mutable globalCheckState = FileCheckState.NotStarted

    let entityCache = EntityCache() // used in GetAllEntities method

    let checkingEv          = new Event<IEditor> ()
    let checkedEv           = new Event<IEditor*CheckResults> ()
    let fullCodeAvailableEv = new Event<IEditor> ()
    let firstCheckDoneEv    = new Event<unit>() // to first check file, then start FSI
    

    /// At the end either a event is raised or continuation called if present.
    let checkCode(iEditor:IEditor,  continueOnThreadPool:Option<CheckResults->unit>, stopWaitingForCompletionWindow2: unit-> unit) = 
        let thisId = Interlocked.Increment checkId
        // ISeffLog.log.PrintfnColor 100 100 200 $"C1-checkCode id {thisId}: start"
        globalCheckState <- GettingCode thisId
        iEditor.FileCheckState <- globalCheckState

        checkingEv.Trigger(iEditor) // to show in statusbar
        let doc = iEditor.AvaEdit.Document // access document before starting async        
        async {
            let mutable checkingStoppedEarly2 = true
            //do! Async.Sleep 200 // TODO add lag so that the checker does not run all the time while typing. not needed any more since delayDocChange function
            match fsChecker with
            | Some ch -> ()
            | None ->
                // http://fsharp.github.io/FSharp.Compiler.Service/caches.html    
                Environment.SetEnvironmentVariable ("FCS_ParseFileCacheSize", "5")

                // https://github.com/dotnet/fsharp/blob/main/src/Compiler/Service/service.fsi#L27:
                // projectCacheSize                                      = The optional size of the project checking cache.
                // keepAssemblyContents                                  = Keep the checked contents of projects.
                // keepAllBackgroundResolutions                          = If false, do not keep full intermediate checking results from background checking suitable for returning from 
                //                                                          GetBackgroundCheckResultsForFileInProject. This reduces memory usage.
                // legacyReferenceResolver                               = An optional resolver for legacy MSBuild references
                // tryGetMetadataSnapshot                                = An optional resolver to access the contents of .NET binaries in a memory-efficient way
                // suggestNamesForErrors                                 = Indicate whether name suggestion should be enabled
                // keepAllBackgroundSymbolUses                           = Indicate whether all symbol uses should be kept in background checking
                // enableBackgroundItemKeyStoreAndSemanticClassification = Indicates whether a table of symbol keys should be kept for background compilation
                // enablePartialTypeChecking                             = Indicates whether to perform partial type checking. Cannot be set to true if keepAssmeblyContents is true. 
                //                                                          If set to true, can cause duplicate type-checks when richer information on a file is needed, but can skip background type-checking 
                //                                                          entirely on implementation files with signature files.
                // parallelReferenceResolution                           = Indicates whether to resolve references in parallel.
                // captureIdentifiersWhenParsing                         = When set to true we create a set of all identifiers for each parsed file which can be used to speed up finding references.
                // documentSource  (Experimental)                        = Default: FileSystem. You can use Custom source to provide a function that will return the source for a given file path instead 
                //                                                          of reading it from the file system. Note that with this option the FSharpChecker will also not monitor the file system for file changes. 
                //                                                          It will expect to be notified of changes via the NotifyFileChanged method.
                // useSyntaxTreeCache  (Experimental)                    = Default: true. Indicates whether to keep parsing results in a cache.

                // defaults: https://github.com/dotnet/fsharp/blob/main/src/Compiler/Service/service.fs#LL1339C9-L1357C68
                // keepAssemblyContents =  false
                // keepAllBackgroundResolutions =  true
                // suggestNamesForErrors =  false
                // keepAllBackgroundSymbolUses =  true
                // enableBackgroundItemKeyStoreAndSemanticClassification = false
                // enablePartialTypeChecking =  false
                // captureIdentifiersWhenParsing =  false
                // useSyntaxTreeCache =  true

                // "you should generally use one global, shared FSharpChecker for everything in an IDE application." from http://fsharp.github.io/FSharp.Compiler.Service/caches.html
                let ch = FSharpChecker.Create(suggestNamesForErrors=true) //TODO default options OK?
                fsChecker <- Some ch

            if !checkId = thisId then
                // NOTE just checking only Partial Code till caret with (doc.CreateSnapshot(0, tillOffset).Text) would make the GetDeclarationsList method miss some declarations !!
                let codeInChecker : CodeAsString = (doc.CreateSnapshot().Text) // the only threadsafe way to access the code string

                globalCheckState <- Checking (thisId , codeInChecker)
                iEditor.FileCheckState <- globalCheckState
                
                do! Async.SwitchToContext(FsEx.Wpf.SyncWpf.context)// just for fullCodeAvailableEv event
                if !checkId = thisId then  
                    // ISeffLog.log.PrintfnColor 100 100 200 $"C2-checkCode id {thisId}: fullCodeAvailable"
                    fullCodeAvailableEv.Trigger(iEditor)
                
                do! Async.SwitchToThreadPool()

                let fileFsx = 
                    match iEditor.FilePath with
                    |Deleted fi |SetTo fi ->
                        let n = fi.FullName
                        if not <| n.EndsWith(".fsx",StringComparison.InvariantCultureIgnoreCase) then n + ".fsx" else n // required by FCS, oddly !
                    |NotSet dummyName ->
                        // TODO this name should be unique even for unsaved files !!for caching
                        // Used to differentiate between scripts, to consider each script a separate project.
                        // .fsx file suffix is required by FCS , oddly ! //TODO check if file can contain invalid path characters like *
                        let cl = dummyName.Replace( "*", "")                        
                        if cl.EndsWith(".fsx", StringComparison.InvariantCultureIgnoreCase) then 
                            cl
                        elif cl.EndsWith(".fs", StringComparison.InvariantCultureIgnoreCase) then 
                            cl
                        else
                            cl+".fsx"

                        

                if !checkId = thisId  then
                    let log = config.Log
                    // ISeffLog.log.PrintfnColor 100 100 200 $"C3-checkCode id {thisId}: GetProjectOptionsFromScript"
                    try
                        let sourceText = Text.SourceText.ofString codeInChecker
                        // see https://github.com/dotnet/fsharp/issues/7669 for performance problems
                        
                        // For a given script file, get the FSharpProjectOptions implied by the #load closure.
                        // All files are read from the FileSystem API, except the file being checked.
                        
                        // filename: Used to differentiate between scripts, to consider each script a separate project. Also used in formatted error messages.
                        // source: The source for the file.
                        // previewEnabled: Is the preview compiler enabled.
                        // loadedTimeStamp: Indicates when the script was loaded into the editing environment,
                        //         so that an 'unload' and 'reload' action will cause the script to be considered as a new project,
                        //         so that references are re-resolved.
                        // otherFlags: Other flags for compilation.
                        // useFsiAuxLib: Add a default reference to the FSharp.Compiler.Interactive.Settings library.
                        // useSdkRefs: Use the implicit references from the .NET SDK.
                        // assumeDotNetFramework: Set up compilation and analysis for .NET Framework scripts.
                        // sdkDirOverride: Override the .NET SDK used for default references.
                        // optionsStamp: An optional unique stamp for the options.
                        // userOpName: An optional string used for tracing compiler operations associated with this request.
                        let! options, optionsErr = 
                            fsChecker.Value.GetProjectOptionsFromScript(fileName        = fileFsx
                                                                        ,source            = sourceText
                                                                        ,previewEnabled    = true // // Bug in FCS! if otherFlags argument is given the value here is ignored !
                                                                        //,loadedTimeStamp: DateTime *

                                                                        #if NETFRAMEWORK
                                                                        //https://github.com/fsharp/FsAutoComplete/blob/f176825521215725e5b7ba888d4bb11d1e408e56/src/FsAutoComplete.Core/CompilerServiceInterface.fs#L178
                                                                        ,otherFlags            = [| "--targetprofile:mscorlib"; "--langversion:preview" |]
                                                                        ,useSdkRefs            = false
                                                                        ,assumeDotNetFramework = true
                                                                         
                                                                        #else
                                                                        ,otherFlags            = [| "--targetprofile:netstandard"; "--langversion:preview" |]                                                                          
                                                                        ,useSdkRefs            = true
                                                                        ,assumeDotNetFramework = false
                                                                        #endif

                                                                        //,useFsiAuxLib = true // so that fsi object is available // doesn't work
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
                                // ISeffLog.log.PrintfnColor 100 100 200 $"C4-checkCode id {thisId}: ParseAndCheckFileInProject"
                                let! parseRes , checkAnswer = fsChecker.Value.ParseAndCheckFileInProject(fileFsx, 0, sourceText, options) // can also be done in two  calls   //TODO really use check file in project for scripts??
                                match checkAnswer with
                                | FSharpCheckFileAnswer.Succeeded checkRes ->
                                    // ISeffLog.log.PrintfnColor 100 100 200 $"C5-checkCode id {thisId} = !checkId {!checkId}: FSharpCheckFileAnswer.Succeeded"
                                    if !checkId = thisId  then // this ensures that status gets set to done if no checker has started in the meantime                                        
                                        let res =
                                            {
                                            parseRes = parseRes  
                                            checkRes = checkRes
                                            errors   = ErrorUtil.getBySeverity checkRes
                                            code     = codeInChecker  
                                            checkId  = thisId
                                            editorId = iEditor.Id
                                            }
                                        globalCheckState <- Done res
                                        iEditor.FileCheckState <- globalCheckState                                        
                                        match continueOnThreadPool with
                                        | Some f ->                                            
                                            try
                                                checkingStoppedEarly2 <- false                                               
                                                // ISeffLog.log.PrintfnColor 100 100 200 $"C6-checkCode id {thisId}: continue GetDeclarationListInfo.."
                                                f(res) // calls GetDeclarationListInfo and GetDeclarationListSymbols for finding optional arguments
                                            with
                                                e -> log.PrintfnAppErrorMsg "The continuation after ParseAndCheckFileInProject failed with:\r\n %A" e

                                        | None ->
                                            do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                                            
                                            try
                                                if !checkId = thisId  then                                                
                                                    checkedEv.Trigger(iEditor,res) // to mark statusbar , and highlighting errors
                                                    // ISeffLog.log.PrintfnColor 100 100 200 $"C6-checkCode id {thisId}: ended after OnCheckedForErrors event "
                                                    if isFirstCheck then
                                                        firstCheckDoneEv.Trigger() // to now start FSI
                                                        isFirstCheck <- false
                                            with
                                                e -> log.PrintfnAppErrorMsg "The checked Event after ParseAndCheckFileInProject failed with:\r\n %A" e

                                | FSharpCheckFileAnswer.Aborted  ->
                                    log.PrintfnAppErrorMsg "FSharpChecker.ParseAndCheckFileInProject(filepath, 0, sourceText , options) returned: FSharpCheckFileAnswer.Aborted\r\nFSharpParseFileResults is: %A" parseRes
                                    globalCheckState <- CheckFailed
                                    iEditor.FileCheckState <- globalCheckState
                            with e ->
                                log.PrintfnAppErrorMsg "Error in ParseAndCheckFileInProject Block.\r\n This may be from a Type Provider or you are using another version of FSharpCompilerService.dll than at compile time?"
                                log.PrintfnAppErrorMsg "%A" e
                                log.PrintfnAppErrorMsg "%s" e.Message
                                log.PrintfnAppErrorMsg "InnerException:\r\n%A" e.InnerException
                                if notNull e.InnerException then log.PrintfnAppErrorMsg "%s" e.InnerException.Message
                                globalCheckState <- CheckFailed
                                iEditor.FileCheckState <- globalCheckState
                        else
                            () //ISeffLog.log.PrintfnDebugMsg $"other is running 2: this{thisId} other {!checkId} "

                    with e ->
                            log.PrintfnAppErrorMsg "Error in GetProjectOptionsFromScript Block.\r\nMaybe you are using another version of FSharpCompilerService.dll than at compile time?:"
                            log.PrintfnAppErrorMsg "%A" e
                            log.PrintfnAppErrorMsg "%s" e.Message
                            globalCheckState <- CheckFailed
                            iEditor.FileCheckState <- globalCheckState
            else
                () //ISeffLog.log.PrintfnDebugMsg $"other is running 1: this{thisId} other {!checkId} "
            
            //ISeffLog.log.PrintfnDebugMsg $"checking  id  {thisId} Result: {globalCheckState}."
            if checkingStoppedEarly2 then stopWaitingForCompletionWindow2()
            } |> Async.Start

    static let mutable singleInstance :Checker option  = None
    
    
    //-----------------------------------------------------------------
    //---------------instance  members----------------------------------
    //-----------------------------------------------------------------

    /// every time a new call to the global type checker happens this gets incremented
    /// this happens when the document changes, not for type info requests
    member _.CurrentCheckId = !checkId

    member _.FsChecker = fsChecker

    member _.GlobalCheckState = globalCheckState   

    /// This event is raised on UI thread when a checker session starts.
    [<CLIEvent>] 
    member this.OnChecking = checkingEv.Publish

    /// This event is raised on UI thread
    /// only when checking for errors not when checking for autocomplete
    [<CLIEvent>]
    member this.OnCheckedForErrors = checkedEv.Publish

    /// the async method doc.CreateSnapshot() completed
    [<CLIEvent>] 
    member this.OnFullCodeAvailable = fullCodeAvailableEv.Publish

    /// this event is raised on UI thread
    [<CLIEvent>] 
    member this.OnFirstCheckDone = firstCheckDoneEv.Publish

    member val Fsi  = Fsi.GetOrCreate(config) //  but  Fsi.Initialize() is only called in OnFirstCheckDone
        
    
    /// Triggers Event<FSharpErrorInfo[]> event after calling the continuation
    member this.CheckThenHighlightAndFold (iEditor:IEditor)  =  checkCode (iEditor, None, fun ()->() )

    /// used as optional argument to GetDeclarationListSymbols
    member this.GetAllEntities(res: CheckResults, publicOnly: bool): AssemblySymbol list = 
        let checkResults = res.checkRes
        // from https://github.com/fsharp/FsAutoComplete/blob/fdeca2f5ffc329fad4a3f0a8b75af5aeed192799/src/FsAutoComplete.Core/ParseAndCheckResults.fs#L659
        try
            [ 
                //ISeffLog.log.PrintfnDebugMsg "getAllEntities .." 
                yield! AssemblyContent.GetAssemblySignatureContent AssemblyContentType.Full checkResults.PartialAssemblySignature
                let ctx = checkResults.ProjectContext
                                    
                let assembliesByFileName =
                    ctx.GetReferencedAssemblies()
                    |> List.groupBy (fun asm -> asm.FileName)
                    |> List.rev // if mscorlib.dll is the first then FSC raises exception when we try to get Content.Entities from it.
                                    
                for fileName, signatures in assembliesByFileName do
                let contentType =
                    if publicOnly then
                        AssemblyContentType.Public
                    else
                        AssemblyContentType.Full
                                    
                let content =
                    AssemblyContent.GetAssemblyContent entityCache.Locking contentType fileName signatures
                                    
                yield! content 
            ]
                                
        with e ->
            ISeffLog.log.PrintfnAppErrorMsg "getAllEntities failed with %A" e
            []

    /// Checks for items available for completion    
    member this.GetCompletions (
                                iEditor:IEditor, 
                                pos :PositionInCode, 
                                ifDotSetback, 
                                continueOnUIthread: DeclarationListInfo -> unit, 
                                optArgsDict:Dictionary<string,ResizeArray<OptDefArg>>,
                                stopWaitingForCompletionWindow1: unit-> unit ) = 
        let getSymbolsAndDecls(res:CheckResults) = 
            let thisId = !checkId
            //see https://stackoverflow.com/questions/46980690/f-compiler-service-get-a-list-of-names-visible-in-the-scope
            //and https://github.com/fsharp/FSharp.Compiler.Service/issues/835
            async{
                //ISeffLog.log.PrintfnDebugMsg "*3.1 - GetDeclarationListInfo .."
                let colSetBack = pos.column - ifDotSetback                
                let partLoName = QuickParse.GetPartialLongNameEx(pos.lineToCaret, colSetBack - 1) //TODO is minus one correct ? https://github.com/fsharp/FSharp.Compiler.Service/issues/837
                let mutable checkingStoppedEarly1 = true

                if !checkId = thisId  then
                    //ISeffLog.log.PrintfnDebugMsg "*3.0 - checkRes.GetDeclarationListSymbols..."
                    let symUse = // Symbols are only for finding out if an argument is optional
                        res.checkRes.GetDeclarationListSymbols(
                            Some res.parseRes  // ParsedFileResultsOpt
                            , pos.row          // line
                            , pos.lineToCaret  // lineText
                            , partLoName       // PartialLongName
                            //, (fun () -> this.GetAllEntities(res, true)) // getAllEntities: (unit -> AssemblySymbol list) // TODO use that too like FsAutocomplete does ???   
                            )
                    
                    if !checkId = thisId  then                         
                        let decls = // for auto completion
                            res.checkRes.GetDeclarationListInfo(            //TODO can I take declaration from Symbol list? ( the GetDeclarationListSymbols above) ?
                                  Some res.parseRes  // ParsedFileResultsOpt
                                , pos.row            // line
                                , pos.lineToCaret    // lineText
                                , partLoName         // PartialLongName
                                //, (fun () -> this.GetAllEntities(res, true)) // getAllEntities: (unit -> AssemblySymbol list) // TODO use that too like FsAutocomplete does ???                               
                                //, completionContextAtPos //  TODO use it ?   Completion context for a particular position computed in advance.
                                )

                        if !checkId = thisId  then
                            //ISeffLog.log.PrintfnDebugMsg "*3.2 - GetDeclarationListInfo found %d on lineToCaret:\r\n  '%s'\r\n  QualifyingIdents: %A,  PartialIdent: '%A', lastDotPos: %A" decls.Items.Length pos.lineToCaret  partLoName.QualifyingIdents partLoName.PartialIdent partLoName.LastDotPos
                            if decls.IsError then 
                                ISeffLog.log.PrintfnAppErrorMsg "*ERROR in GetDeclarationListInfo: %A" decls //TODO use log
                            else                                
                                // Find which parameters are optional and set the value on the passed in dictionary.
                                // For adding question marks to optional arguments.
                                // This is still done in Async mode
                                optArgsDict.Clear() //clean up from last time, or keep ?
                                for symbs in symUse do
                                    for symb in symbs do
                                        let opts = TypeInfo.namesOfOptionalArgs(symb, iEditor)
                                        if opts.Count>0 then
                                            optArgsDict.[symb.Symbol.FullName] <- opts                                
                                
                                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                                // while we are waiting no new checker shall be triggered, all typing during waiting  for the checker should just become a  prefilter for the completion window
                                checkingStoppedEarly1 <- false                                
                                continueOnUIthread( decls)
                
                if checkingStoppedEarly1 then stopWaitingForCompletionWindow1() // redundant just for savety if checker exited early 
                } 
            |> Async.StartImmediate // we are on thread pool already
        
        //ISeffLog.log.PrintfnDebugMsg "*3.0 - checkCode .."
        checkCode(iEditor, Some getSymbolsAndDecls, stopWaitingForCompletionWindow1) //TODO can existing parse results be used ? or do they miss the dot so don't show dot completions ?


    member this.DisposeForReseting(iEditor:IEditor) =
        Interlocked.Increment checkId |> ignore
        globalCheckState <- FileCheckState.NotStarted
        checkingEv.Trigger(iEditor) // to update status bar to initializing 
        match fsChecker with
        |None -> ()
        |Some ch -> ch.ClearCache([])
        isFirstCheck <- true // this will trigger a new FSI instance via OnFirstCheckDone event
        fsChecker <- None // a new one will be created then in checkCode()
        
    
    //-----------------------------------------------------------------
    //---------------static members----------------------------------
    //-----------------------------------------------------------------


    /// Create a new Checker and new reset FSI session !!
    static member Reset(config, iEditor:IEditor) = 
        match singleInstance with
        |None ->   ()
        |Some ch ->                
            ch.DisposeForReseting(iEditor)
            ISeffLog.log.PrintfnInfoMsg "New type checker created." 
            ch.CheckThenHighlightAndFold(iEditor) // this wil create a new checker instance, trigger OnFirstCheckDone and reinitalize FSI
            

    /// Ensures only one instance is created
    static member GetOrCreate(config) = 
        match singleInstance with
        |Some ch -> ch
        |None ->            
            let ch = new Checker(config)
            singleInstance <- Some ch            
            ch.OnFirstCheckDone.Add ( fun ()-> ch.Fsi.Initialize() ) // to start fsi when checker is idle after first check
            ch
