﻿namespace Fesh.Config

open System
open System.IO
open Fittings
open Fesh.Model


type DefaultCode  (runContext:RunContext) =

    let filePath0 = runContext.GetPathToSaveAppData("Default-Code-for-New-Files.fsx")

    let writer = SaveReadWriter(filePath0,IFeshLog.printError)

    let defaultCodeOnFirstRun =
            [
            "// This is your default code for new files."
            "// You can change it by going to the menu: File -> Edit Template File"
            "// The default code is saved at at " + filePath0
            //"tips: // https://panesofglass.github.io/scripting-workshop/#/"
            //"tips: // http://brandewinder.com/2016/02/06/10-fsharp-scripting-tips/"
            match runContext.DefaultCode with
            | Some dc -> dc
            | None ->
                "#r \"nuget: Fesher\" // for printfn in color"
                ""
                "open System"
                "open Fesher"
                ""
                "Printfn.red \"Hello, Fesh!\""
            ""
            ""
            ]
            |> String.concat Environment.NewLine

    do
        if writer.FileDoesNotExists then
            writer.WriteAsync( defaultCodeOnFirstRun)// create file so it can be found and edited manually

    member this.FileInfo = FileInfo(filePath0)

    ///loads sync
    member this.Get() =
        writer.CreateFileIfMissing(defaultCodeOnFirstRun)  |> ignore // create file so it can be found and edited manually
        match writer.ReadAllText() with
        |None -> defaultCodeOnFirstRun
        |Some code -> code





