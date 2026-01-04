namespace Fesh.Editor

open System
open System.Collections.Generic

open Fesh.Model
open Fesh.Util
open Fesh.Util.General

open FSharp.Compiler
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.EditorServices


type ParseCheckRes = {
    parseRes :FSharpParseFileResults
    checkRes :FSharpCheckFileResults
    }

type PositionInCodeEx =
    {
    lineToCaret  :string
    lineIdx      : int
    column       : int
    offset       : int
    setback      : int
    /// to maybe replace some previous characters too upon inserting
    query        : string
    dotBefore    : bool
    partLoName   : PartialLongName
    }
    static member get(pos:PositionInCode) =
        let ln = pos.lineToCaret
        let setback  = Str.lastNonFSharpNameCharPosition ln // to maybe replace some previous characters too
        let dotBefore =
            let i = pos.column - setback - 1
            i >= 0 && i < ln.Length && ln.[i] = '.'
        let ifDotSetback = if dotBefore then setback else 0
        let colSetBack   = pos.column - ifDotSetback
        {
        lineToCaret  = ln
        lineIdx      = pos.lineIdx
        column       = pos.column
        offset       = pos.offset
        setback      = setback     // to maybe replace some previous characters too
        query        = ln.Substring(ln.Length - setback)
        dotBefore    = dotBefore
        partLoName   =
            let lns = QuickParse.GetPartialLongNameEx(ln, colSetBack - 1) //TODO is minus one correct ? https://github.com/fsharp/FSharp.Compiler.Service/issues/837
            if List.isEmpty lns.QualifyingIdents then {lns with QualifyingIdents = [pos.lineToCaret]} else lns
        }

[<RequireQualifiedAccess>]
module FsCheckerUtil =
    open Fesh.Config

    let getFsxFileNameForChecker (filePath:Fesh.Model.FilePath) =
        match filePath with
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

    let getNew () =
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
        // enablePartialTypeChecking                             = Indicates whether to perform partial type checking. Cannot be set to true if keepAssemblyContents is true.
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
        FSharpChecker.Create(suggestNamesForErrors=true) //TODO default options OK?

    let getOptions (fsChecker:FSharpChecker, config:Config, fileFsx ,sourceText, _lineNumber:int ): option<FSharpProjectOptions>=
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
        async {
             try
                let isNet8 = config.RunContext.IsRunningOnDotNetCore
                let! options, _ = // _ = optionsErr
                    fsChecker.GetProjectOptionsFromScript(
                        fileName          = fileFsx
                        ,source            = sourceText
                        ,previewEnabled    = true // // Bug in FCS! if otherFlags argument is given the value here is ignored !
                        //,loadedTimeStamp: DateTime *
                        ,otherFlags            = [| "--targetprofile:" + (if isNet8 then "netstandard" else "mscorlib") ; "--langversion:preview" |]//https://github.com/fsharp/FsAutoComplete/blob/f176825521215725e5b7ba888d4bb11d1e408e56/src/FsAutoComplete.Core/CompilerServiceInterface.fs#L178
                        ,useSdkRefs            = isNet8
                        ,assumeDotNetFramework = not isNet8
                        //,useFsiAuxLib = true // so that fsi object is available // doesn't work
                        //,sdkDirOverride: string *
                        //,optionsStamp: int64 *
                        //,userOpName: string
                        #if !NET8
                        ,caret = Text.Position.mkPos _lineNumber 0// to avoid loading nuget packages while typing them:
                            // used here https://github.com/dotnet/fsharp/blob/1112c72f1344f8a9c79e2f4c197b17016f8d6939/src/Compiler/Driver/ScriptClosure.fs#L306
                            // and https://github.com/dotnet/fsharp/pull/18393
                        #endif
                        )
                //Not needed because these errors are reported by ParseAndCheckFileInProject too
                //for oe in optionsErr do
                //    let msg = sprintf "%A" oe |> Util.Str.truncateToMaxLines 3
                //    IFeshLog.log.PrintfnFsiErrorMsg "Error in GetProjectOptionsFromScript:\r\n%A" msg
                return Some options
             with e ->
                IFeshLog.log.PrintfnAppErrorMsg "Error in GetProjectOptionsFromScript Block.\r\nMaybe you are using another version of FSharpCompilerService.dll than at compile time?:"
                IFeshLog.log.PrintfnAppErrorMsg "%A" e
                IFeshLog.log.PrintfnAppErrorMsg "%s" e.Message
                return None
        } |> Async.RunSynchronously

    let parseAndCheckImpl (fsChecker:FSharpChecker) fileFsx sourceText options: option<ParseCheckRes> =
        async {
            try
                // IFeshLog.log.PrintfnColor 100 100 200 $"C4-checkCode id {thisId}: ParseAndCheckFileInProject"
                let! parseRes , checkAnswer = fsChecker.ParseAndCheckFileInProject(fileFsx, 0, sourceText, options) // can also be done in two  calls   //TODO really use check file in project for scripts??
                match checkAnswer with
                | FSharpCheckFileAnswer.Succeeded checkRes ->
                    // IFeshLog.log.PrintfnColor 100 100 200 $"C5-checkCode id {thisId} = !checkId {!checkId}: FSharpCheckFileAnswer.Succeeded"
                    return Some { parseRes=parseRes; checkRes=checkRes}

                | FSharpCheckFileAnswer.Aborted  ->
                    IFeshLog.log.PrintfnAppErrorMsg "FSharpChecker.ParseAndCheckFileInProject(filepath, 0, sourceText , options) returned: FSharpCheckFileAnswer.Aborted\r\nFSharpParseFileResults is: %A" parseRes
                    return None

            with e ->
                IFeshLog.log.PrintfnAppErrorMsg "Error in ParseAndCheckFileInProject Block.\r\n This may be from a Type Provider or you are using another version of FSharpCompilerService.dll than at compile time?"
                IFeshLog.log.PrintfnAppErrorMsg "%A" e
                IFeshLog.log.PrintfnAppErrorMsg "%s" e.Message
                IFeshLog.log.PrintfnAppErrorMsg "InnerException:\r\n%A" e.InnerException
                if notNull e.InnerException then IFeshLog.log.PrintfnAppErrorMsg "%s" e.InnerException.Message
                return None
        }|> Async.RunSynchronously

/// Only a single checker exist that is referenced on all editors
type Checker private ()  =


    static let mutable fsChecker: FSharpChecker Option = None // "you should generally use one global, shared FSharpChecker for everything in an IDE application." from http://fsharp.github.io/FSharp.Compiler.Service/caches.html

    static let entityCache = EntityCache() // used in GetAllEntities method

    /// for a given method name returns a list of optional argument names
    //static let optArgsDict = Dictionary<string,ResizeArray<OptDefArg>>()

    static let checkingStateEv = new Event<FileCheckState> ()


    static let mutable projectOptions = None: FSharpProjectOptions option

    static let parseAndCheck( state:InteractionState, code:string, filePath:Fesh.Model.FilePath, changeId, caretLineNumber:int): option<ParseCheckRes> =
        match fsChecker with
        | Some _ -> ()
        | None   ->  fsChecker <- Some (FsCheckerUtil.getNew())

        let ok() = state.IsLatestOpt changeId

        Monads.maybe {
            let! _ = ok()
            let  fileFsx    = FsCheckerUtil.getFsxFileNameForChecker filePath
            let! _ = ok()
            let  sourceText = Text.SourceText.ofString code
            let! opts = FsCheckerUtil.getOptions(fsChecker.Value, state.Config, fileFsx, sourceText, caretLineNumber)
            projectOptions <- Some opts
            let! _ = ok()
            let! checkRes = FsCheckerUtil.parseAndCheckImpl fsChecker.Value fileFsx sourceText opts
            let! _ = ok()
            return checkRes
            }



    //-----------------------------------------------------------------
    //---------------static members------------------------------------
    //-----------------------------------------------------------------


    /// This event is raised on UI thread when a checker session starts.
    [<CLIEvent>]
    static member CheckingStateChanged = checkingStateEv.Publish

    // for a given method name returns a list of optional argument names
    // static member OptArgsDict = optArgsDict

    /// Returns None if check failed or was superseded by a newer Document change ID
    static member CheckCode(ed:IEditor, state:InteractionState, code:string, changeId:int64, forCompl:bool, caretLineNumber:int) : option<FullCheckResults> =
        try
            let aCheckingState =
                match ed.FileCheckState with
                | Done res | WaitForCompl res | WaitForErr res -> if forCompl then WaitForCompl res else WaitForErr res
                | NotChecked                                    -> NotChecked

            Fittings.SyncWpf.doSync (fun () -> checkingStateEv.Trigger aCheckingState )
            ed.FileCheckState <- aCheckingState

            match parseAndCheck( state, code, ed.FilePath, changeId, caretLineNumber) with
            |None ->
                //IFeshLog.log.PrintfnDebugMsg $"*parseAndCheck: aborted early, waiting for newer checke state event."
                None
            |Some parseCheckRes ->
                let errs = ErrorUtil.getBySeverity parseCheckRes.checkRes
                let res =
                    {
                    parseRes = parseCheckRes.parseRes
                    checkRes = parseCheckRes.checkRes
                    errors   = errs
                    changeId = changeId
                    editor   = ed.AvaEdit
                    }

                Fittings.SyncWpf.doSync (fun () -> checkingStateEv.Trigger (Done res) )
                ed.FileCheckState <- Done res
                Some res
        with
            | :? TypeLoadException as e ->
                IFeshLog.log.PrintfnAppErrorMsg "TypeLoadException in CheckCode: \r\n%A" e
                IFeshLog.log.PrintfnFsiErrorMsg "This may indicate an assembly version conflict"
                if state.Config.RunContext.IsHosted then
                    let host = state.Config.RunContext.HostName |> Option.defaultValue ""
                    IFeshLog.log.PrintfnFsiErrorMsg $"Please restart the host app {host}. And try again."

                None
            | e ->
                IFeshLog.log.PrintfnAppErrorMsg "Fesh.Editor.Error in CheckCode: \r\n%A" e
                None


    /// Currently unused optional argument to GetDeclarationListSymbols
    /// Completion list would get huge !!!
    static member GetAllEntities(res: FSharpCheckFileResults, publicOnly: bool): AssemblySymbol list =
        // from https://github.com/fsharp/FsAutoComplete/blob/fdeca2f5ffc329fad4a3f0a8b75af5aeed192799/src/FsAutoComplete.Core/ParseAndCheckResults.fs#L659
        try
            [
                //IFeshLog.log.PrintfnDebugMsg "getAllEntities .."
                yield! AssemblyContent.GetAssemblySignatureContent AssemblyContentType.Full res.PartialAssemblySignature
                let ctx = res.ProjectContext

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
            IFeshLog.log.PrintfnAppErrorMsg "getAllEntities failed with %A" e
            []

    /// Checks for items available for completion
    static member GetDeclarations (posX:PositionInCodeEx, res:FullCheckResults ) =
        // IFeshLog.log.PrintfnDebugMsg "*2.0 GetCompletions for:\r\n%A" posX
        // Symbols are only for finding out if an argument is optional
        // let symUse =  res.checkRes.GetDeclarationListSymbols(
        //                  Some res.parseRes  // ParsedFileResultsOpt
        //                  , posX.lineIdx          // line
        //                  , posX.lineToCaret  // lineText
        //                  , posX.partLoName  // PartialLongName
        //                  //, (fun () -> Checker.GetAllEntities(res, true)) // getAllEntities: (unit -> AssemblySymbol list) // TODO use that too like FsAutocomplete does ???
        //                  )

        // for auto completion
        let decls = res.checkRes.GetDeclarationListInfo(
                          Some res.parseRes  // ParsedFileResultsOpt
                        , posX.lineIdx       // line
                        , posX.lineToCaret   // lineText
                        , posX.partLoName    // PartialLongName
                        //, (fun () -> Checker.GetAllEntities(res, true)) // getAllEntities: (unit -> AssemblySymbol list) // TODO use that too like FsAutocomplete does ???
                        //, completionContextAtPos //  TODO use it ?   Completion context for a particular position computed in advance.
                        )

        if decls.IsError then
            IFeshLog.log.PrintfnAppErrorMsg $"*ERROR in GetDeclarationListInfo of {decls.Items.Length} items."  //TODO use log
            None
        else
            // IFeshLog.log.PrintfnDebugMsg $"*2.1 GetCompletions for {posX} found {decls.Items.Length}"
            // Find which parameters are optional and set the value on the passed in dictionary.
            // For adding question marks to optional arguments.
            // This is still done in Async mode
            // optArgsDict.Clear() //clean up from last time, or keep ?
            // for symbs in symUse do
            //     for symb in symbs do
            //         let opts = TypeInfo.namesOfOptionalArgs(symb)
            //         if opts.Count>0 then
            //             optArgsDict.[symb.Symbol.FullName] <- opts



            IFeshLog.log.PrintfnDebugMsg $"*2.1 GetCompletions for {posX} found {decls.Items.Length}"


            Some decls


    /// Create a new Checker
    static member Reset() =
        //DisposeForResetting:
        match fsChecker with
        |None -> ()
        |Some ch ->
            match projectOptions with
            |None -> ()
            |Some po -> ch.ClearCache([po])
        fsChecker <- Some (FsCheckerUtil.getNew()) // TODO this is blocking in Sync ?
        IFeshLog.log.PrintfnInfoMsg "New F# type checker created."