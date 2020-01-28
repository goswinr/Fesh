namespace Seff

open System
open System.IO
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open ICSharpCode.AvalonEdit



module FsChecker = 
    let mutable private checker : FSharpChecker option = None // delay creation for window startup performance?

    type PositionInCode = { lineToCaret:string  
                            row: int   
                            column: int 
                            offset: int }

    type FsCheckResults = {ok:bool; parseRes:FSharpParseFileResults;  checkRes:FSharpCheckFileResults;  code:string}
    


    /// to check full code use 0 as 'tillOffset'
    /// returns on new thread 
    let fsCheck (tab:FsxTab, doc:Document.TextDocument, tillOffset) : Async<FsCheckResults>= 
        async{                   
            let code = 
                if tillOffset = 0 then doc.CreateSnapshot().Text //the only threadsafe way to acces the code string
                else                   doc.CreateSnapshot(0, tillOffset).Text
            
            let fileFsx = 
                match tab.FileInfo with
                |Some fi -> 
                    let n = fi.FullName
                    if not <| n.EndsWith(".fsx",StringComparison.InvariantCultureIgnoreCase) then n + ".fsx" else n // required by FCS, oddly !
                | None -> "UnSavedFile.fsx" // .fsx file required by FCS , oddly !

                   
            match checker with  
            |Some _ -> ()  
            |None -> 
                let ch = FSharpChecker.Create(suggestNamesForErrors=true) //TODO default options OK? 
                // "you should generally use one global, shared FSharpChecker for everything in an IDE application." from http://fsharp.github.io/FSharp.Compiler.Service/caches.html
                Environment.SetEnvironmentVariable ("FCS_ParseFileCacheSize", "5") // done on startup
                checker <- Some ch
            
            try
                let sourceText = Text.SourceText.ofString code
                let! options, optionsErr = checker.Value.GetProjectOptionsFromScript(fileFsx, sourceText, otherFlags = [| "--langversion:preview" |] ) //TODO really use check file in project for scripts??
                for e in optionsErr do Log.print "*Script Options Error: %A" e
            
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
                try
                    let! parseRes , checkAnswer = checker.Value.ParseAndCheckFileInProject(fileFsx, 0, sourceText, options) // can also be done in two speterate calls   //TODO really use check file in project for scripts??         
                    match checkAnswer with
                    | FSharpCheckFileAnswer.Succeeded checkRes ->   
                        return {ok=true; parseRes=parseRes;  checkRes=checkRes;  code=code}
                    
                    | FSharpCheckFileAnswer.Aborted  ->
                        Log.print "*ParseAndCheckFile code aborted"
                        return {ok=false; parseRes=Unchecked.defaultof<FSharpParseFileResults>;  checkRes=Unchecked.defaultof<FSharpCheckFileResults>;  code=code}                        
                with e ->
                    Log.print "ParseAndCheckFileInProject crashed (varying Nuget versions of FCS ?): %s" e.Message
                    return {ok=false; parseRes=Unchecked.defaultof<FSharpParseFileResults>;  checkRes=Unchecked.defaultof<FSharpCheckFileResults>;  code=code}
            
            with e ->
                    Log.print "GetProjectOptionsFromScript crashed (varying Nuget versions of FCS ?) : %s" e.Message
                    return {ok=false; parseRes=Unchecked.defaultof<FSharpParseFileResults>;  checkRes=Unchecked.defaultof<FSharpCheckFileResults>;  code=code}
            } 
    
    let showChecking (tab:FsxTab, isRunning,checkerId) = //,changedColor) = 
        async {            
            do! Async.Sleep 300            
            if !isRunning && tab.FsCheckerRunning = checkerId  then // || checkerId=0 //in case of completion window
                do! Async.SwitchToContext Sync.syncContext
                tab.Editor.Background <- Appearance.editorBackgroundChecking
            } |> Async.Start

    let checkAndIndicate (tab:FsxTab, tillOffset,checkerId)=
        async{            
            let isRunning = ref true
            showChecking (tab, isRunning, checkerId)
            let! calc = fsCheck (tab, tab.Editor.Document ,tillOffset) |> Async.StartChild
            let! res = calc
            isRunning := false            
            return res
            }

    let getDeclListInfo (parseRes :FSharpParseFileResults, checkRes :FSharpCheckFileResults, pos :PositionInCode, ifDotSetback)  =        
        //see https://stackoverflow.com/questions/46980690/f-compiler-service-get-a-list-of-names-visible-in-the-scope
        //and https://github.com/fsharp/FSharp.Compiler.Service/issues/835
        async{
            let colSetBack = pos.column - ifDotSetback
            let partialLongName = QuickParse.GetPartialLongNameEx(pos.lineToCaret, colSetBack - 1) //- 1) ??TODO is minus one correct ? https://github.com/fsharp/FSharp.Compiler.Service/issues/837
            //Log.print "GetPartialLongNameEx on: '%s' setback: %d is:\r\n%A" pos.lineToCaret colSetBack partialLongName  
            //Log.print "GetDeclarationListInfo on: '%s' row: %d, col: %d, colSetBack:%d, ifDotSetback:%d\r\n" pos.lineToCaret pos.row pos.column colSetBack ifDotSetback          
            let! decls = 
                checkRes.GetDeclarationListInfo(
                    Some parseRes,      // ParsedFileResultsOpt
                    pos.row,            // line                   
                    pos.lineToCaret ,   // lineText
                    partialLongName,    // PartialLongName
                    ( fun _ -> [] )     // getAllEntities: (unit -> AssemblySymbol list) 
                    ) 
            if decls.IsError then Log.print "*ERROR in GetDeclarationListInfo: %A" decls
            return decls
            } 
    
    let getDeclListSymbols (parseRes :FSharpParseFileResults, checkRes :FSharpCheckFileResults, pos :PositionInCode, ifDotSetback)  = 
        //see https://stackoverflow.com/questions/46980690/f-compiler-service-get-a-list-of-names-visible-in-the-scope
        //and https://github.com/fsharp/FSharp.Compiler.Service/issues/835
        async{
            let colSetBack = pos.column - ifDotSetback
            let partialLongName = QuickParse.GetPartialLongNameEx(pos.lineToCaret, colSetBack - 1) //- 1) ??TODO is minus one correct ? https://github.com/fsharp/FSharp.Compiler.Service/issues/837
            //Log.print "GetPartialLongNameEx on: '%s' setback: %d is:\r\n%A" pos.lineToCaret colSetBack partialLongName  
            //Log.print "GetDeclarationListInfo on: '%s' row: %d, col: %d, colSetBack:%d, ifDotSetback:%d\r\n" pos.lineToCaret pos.row pos.column colSetBack ifDotSetback          
            let! decls = 
                checkRes.GetDeclarationListSymbols(
                    Some parseRes,      // ParsedFileResultsOpt
                    pos.row,            // line                   
                    pos.lineToCaret ,   // lineText
                    partialLongName,    // PartialLongName
                    ( fun _ -> [] )     // getAllEntities: (unit -> AssemblySymbol list) 
                    ) 
            //if decls.IsError then Log.print "*ERROR in GetDeclarationListInfo: %A" decls
            return decls
            } 
        
