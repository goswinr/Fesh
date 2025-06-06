﻿namespace Fesh.Config

open System
open Fittings
open Fesh.Model


type FsiArguments   ( runContext:RunContext) =

    let filePath0 = runContext.GetPathToSaveAppData("FSI-Arguments.txt")
    let writer = SaveReadWriter(filePath0, IFeshLog.printError)

    let defaultArgs =
        if runContext.IsHosted then // dec 2024, F# 9, on net48 hosted in Rhino --multiemit- is needed to enable multiple evaluations, line numbers for errors don't work though.
            [| "first arg must be there but is ignored" ; "--langversion:preview"  ; "--exec"; "--debug+"; "--debug:full" ;"--optimize+" ; "--gui+" ; "--nologo"; "--multiemit-"|]
        else // Standalone for net48 too --multiemit is always there on netCore
            [| "first arg must be there but is ignored" ; "--langversion:preview"  ; "--exec"; "--debug+"; "--debug:full" ;"--optimize+" ; "--gui+" ; "--nologo"; "--multiemit+" |]


    // Standalone with "--multiemit" to have line numbers in error messages see https://github.com/dotnet/fsharp/discussions/13293
    // Hosted without "--multiemit", error line numbers don't work there anyway, and in addition accessing previously emitted assemblies might fail with a TypeLoadException.
    // see: https://fsharp.github.io/fsharp-compiler-docs/fsi-emit.html
    // and https://github.com/dotnet/fsharp/blob/main/src/Compiler/Interactive/fsi.fs#L1171
    // https://github.com/dotnet/fsharp/discussions/13293

    // first arg is ignored: https://github.com/fsharp/FSharp.Compiler.Service/issues/420
    // and  https://github.com/fsharp/FSharp.Compiler.Service/issues/877
    // and  https://github.com/fsharp/FSharp.Compiler.Service/issues/878

    // use "--gui+" to enable winforms event loop ( on by default) check if fixed: https://github.com/dotnet/fsharp/issues/13473

    // use "--exec" instead of "--noninteractive" see https://github.com/dotnet/fsharp/blob/7b46dad60df8da830dcc398c0d4a66f6cdf75cb1/src/Compiler/Interactive/fsi.fs#L937
    // TODO: use --noninteractive flag instead of accessing controlled execution via reflection:
    // https://github.com/dotnet/fsharp/pull/15184

    // "--shadowcopyreferences" is ignored https://github.com/fsharp/FSharp.Compiler.Service/issues/292

    let defaultArgsText = defaultArgs|> String.concat Environment.NewLine


    let get() =
        writer.CreateFileIfMissing(defaultArgsText)  |> ignore
        match writer.ReadAllLines() with
        |None -> defaultArgs
        |Some args ->
            args
            |> Array.map (fun s -> s.Trim())
            |> Array.filter (String.IsNullOrWhiteSpace>>not)
            |> Array.filter (fun a -> a.ToLower() <>  "--quiet") // this argument is managed separately in config.Settings and statusbar

    let mutable args = [||]

    ///loads sync
    member this.Get =
        if Array.isEmpty args then
            args <- get()
            args
        else
            args

    member this.Reload() =
        args <- get()
        args
(*
    note on docs:
    --multiemit  see: https://fsharp.github.io/fsharp-compiler-docs/fsi-emit.html

    https://docs.microsoft.com/en-us/dotnet/fsharp/language-reference/fsharp-interactive-options

    - INPUT FILES -
        --use:<file>                             Use the given file on startup as initial input
        --load:<file>                            #load the given file on startup
        --reference:<file>                       Reference an assembly (Short form: -r)
        --compilertool:<file>                    Reference an assembly or directory containing a design time tool (Short form: -t)
        -- ...                                   Treat remaining arguments as command line arguments, accessed using fsi.CommandLineArgs

    - CODE GENERATION -
        --debug[+|-]                             Emit debug information (Short form: -g)
        --debug:{full|pdbonly|portable|embedded} Specify debugging type: full, portable, embedded, pdbonly. ('pdbonly' is the default if no debuggging type specified and
                                enables attaching a debugger to a running program, 'portable' is a cross-platform format, 'embedded' is a cross-platform
                                format embedded into the output file).
        --optimize[+|-]                          Enable optimizations (Short form: -O)
        --tailcalls[+|-]                         Enable or disable tailcalls
        --deterministic[+|-]                     Produce a deterministic assembly (including module version GUID and timestamp)
        --pathmap:<path=sourcePath;...>          Maps physical paths to source path names output by the compiler
        --crossoptimize[+|-]                     Enable or disable cross-module optimizations

    - ERRORS AND WARNINGS -
        --warnaserror[+|-]                       Report all warnings as errors
        --warnaserror[+|-]:<warn;...>            Report specific warnings as errors
        --warn:<n>                               Set a warning level (0-5)
        --nowarn:<warn;...>                      Disable specific warning messages
        --warnon:<warn;...>                      Enable specific warnings that may be off by default
        --consolecolors[+|-]                     Output warning and error messages in color

    - LANGUAGE -
        --langversion:{?|version|latest|preview} Display the allowed values for language version, specify language version such as 'latest' or 'preview'
        --checked[+|-]                           Generate overflow checks
        --define:<string>                        Define conditional compilation symbols (Short form: -d)
        --mlcompatibility                        Ignore ML compatibility warnings

    - MISCELLANEOUS -
        --nologo                                 Suppress compiler copyright message
        --help                                   Display this usage message (Short form: -?)

    - ADVANCED -
        --codepage:<n>                           Specify the codepage used to read source files
        --utf8output                             Output messages in UTF-8 encoding
        --preferreduilang:<string>               Specify the preferred output language culture name (e.g. es-ES, ja-JP)
        --fullpaths                              Output messages with fully qualified paths
        --lib:<dir;...>                          Specify a directory for the include path which is used to resolve source files and assemblies (Short form: -I)
        --simpleresolution                       Resolve assembly references using directory-based rules rather than MSBuild resolution
        --targetprofile:<string>                 Specify target framework profile of this assembly. Valid values are mscorlib, netcore or netstandard. Default - mscorlib
        --noframework                            Do not reference the default CLI assemblies by default
        --exec                                   Exit fsi after loading the files or running the .fsx script given on the command line
        --gui[+|-]                               Execute interactions on a Windows Forms event loop (on by default)
        --quiet                                  Suppress fsi writing to stdout
        --readline[+|-]                          Support TAB completion in console (on by default)
        --quotations-debug[+|-]                  Emit debug information in quotations
        --shadowcopyreferences[+|-]              Prevents references from being locked by the F# Interactive process
        *)



