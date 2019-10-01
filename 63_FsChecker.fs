namespace Seff

open System
open System.IO
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices



module FsChecker = 
    let mutable private checker : FSharpChecker option = None // delay creation for window startup performance?

    type PositionInCode = { lineToCaret:string  
                            row: int   
                            column: int 
                            offset: int }
    
    /// to check full code use 0 as 'tillOffset'
    let check (tab:FsxTab, tillOffset) = 
        async{
            let code = 
                if tillOffset = 0 then tab.Editor.Document.CreateSnapshot().Text //the only threadsafe way to acces the code string
                else                   tab.Editor.Document.CreateSnapshot(0, tillOffset).Text
            
            let fileFsx = 
                match tab.FileInfo with
                |Some fi -> 
                    let n = fi.FullName
                    if not <| n.EndsWith(".fsx",StringComparison.InvariantCultureIgnoreCase) then n + ".fsx" else n // required by FCS, oddly !
                | None -> "UnSavedFile.fsx" // .fsx file required by FCS , oddly !

                   
            let ch = match checker with  |Some c -> c  |None -> FSharpChecker.Create() //TODO default options OK? TODO one checker for serveral files or several checkers ?
            checker <- Some ch
            
            let! options, optionsErr = ch.GetProjectOptionsFromScript(fileFsx, Text.SourceText.ofString code) //TODO really use check file in project for scripts??
            for e in optionsErr do Log.printf "*Script Options Error: %A" e
            
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


            let! parseRes , checkAnswer = ch.ParseAndCheckFileInProject(fileFsx, 0, Text.SourceText.ofString code, options) // can also be done in two speterate calls            
            match checkAnswer with
            | FSharpCheckFileAnswer.Succeeded checkRes ->                 
                return true, parseRes,checkRes
            | FSharpCheckFileAnswer.Aborted  ->
                Log.printf "*ParseAndCheckFile code aborted"
                return false, Unchecked.defaultof<FSharpParseFileResults> ,Unchecked.defaultof<FSharpCheckFileResults>
            } 

    let complete (parseRes :FSharpParseFileResults, checkRes :FSharpCheckFileResults, pos :PositionInCode, ifDotSetback)  =        
        //see https://stackoverflow.com/questions/46980690/f-compiler-service-get-a-list-of-names-visible-in-the-scope
        //and https://github.com/fsharp/FSharp.Compiler.Service/issues/835
        async{
            let colSetBack = pos.column - ifDotSetback
            let partialLongName = QuickParse.GetPartialLongNameEx(pos.lineToCaret, colSetBack - 1) //- 1) ??TODO is minus one correct ? https://github.com/fsharp/FSharp.Compiler.Service/issues/837
            //Log.printf "GetPartialLongNameEx on: '%s' setback: %d is:\r\n%A" pos.lineToCaret colSetBack partialLongName  
            //Log.printf "GetDeclarationListInfo on: '%s' row: %d, col: %d, colSetBack:%d, ifDotSetback:%d\r\n" pos.lineToCaret pos.row pos.column colSetBack ifDotSetback          
            let! decls = 
                checkRes.GetDeclarationListInfo(
                    Some parseRes,      // ParsedFileResultsOpt
                    pos.row,            // line                   
                    pos.lineToCaret ,   // lineText
                    partialLongName,    // PartialLongName
                    ( fun _ -> [] )     // getAllEntities: (unit -> AssemblySymbol list) 
                    ) 
            if decls.IsError then Log.printf "*ERROR in GetDeclarationListInfo: %A" decls
            return decls
            } 
        
