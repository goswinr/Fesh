﻿namespace Seff.Config

open Seff
open System
open System
open System.Windows
open System.Threading
open System.Text
open System.Collections.Generic
open System.IO

   
    
type DefaultCode  (log:ISeffLog, adl:HostingMode) =
    let writer = SaveWriter(log)
    
    let filePath = adl.GetPathToSaveAppData("DefaultCode.fsx")

    let defaultCodeOnFirstRun =
        [
        "// this is your default code for new files, you can change it by going to the menu: File -> Edit Template File"
        "// or in your local AppData folder: Environment.SpecialFolder.LocalApplicationData/Seff"
        //"tips: // https://panesofglass.github.io/scripting-workshop/#/" 
        //"tips: // http://brandewinder.com/2016/02/06/10-fsharp-scripting-tips/"        
        "open System"
        // "Environment.CurrentDirectory <- __SOURCE_DIRECTORY__"
        ""
        ] 
        |> String.concat Environment.NewLine
    
    member this.FileInfo = FileInfo(filePath)

    ///loads sync
    member this.Get() =            
        try IO.File.ReadAllText filePath
        with _ -> 
            writer.Write(filePath, defaultCodeOnFirstRun)// create file so it can be found and edited manually
            defaultCodeOnFirstRun
            