﻿namespace Seff.Config

open Seff
open System
open System
open System.Windows
open System.Threading
open System.Text
open System.Collections.Generic
open System.IO

open FsEx.Wpf

open Seff.Model



type DefaultCode  ( hostInfo:Hosting) = 

    let filePath0 = hostInfo.GetPathToSaveAppData("DefaultCode.fsx")

    let writer = SaveReadWriter(filePath0,ISeffLog.printError)

    let defaultCodeOnFirstRun = 
        [
        "// This is your default code for new files, you can change it by going to the menu: File -> Edit Template File"
        "// Or at " + filePath0
        //"tips: // https://panesofglass.github.io/scripting-workshop/#/"
        //"tips: // http://brandewinder.com/2016/02/06/10-fsharp-scripting-tips/"
        "open System"
        // "Environment.CurrentDirectory <- __SOURCE_DIRECTORY__" // TODO this fails !
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





