namespace Seff.Config

open Seff
open System
open System
open System.Windows
open System.Threading
open System.Text
open System.Collections.Generic
open System.IO

   
    
type DefaultCode  (log:ISeffLog, hostInfo:HostingInfo) =
    let writer = SaveWriter(log)
    
    let filePath = hostInfo.GetPathToSaveAppData("DefaultCode.fsx")

    let defaultCodeOnFirstRun =
        [
        "// This is your default code for new files, you can change it by going to the menu: File -> Edit Template File"
        "// Or at "+filePath
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
        try 
            IO.File.ReadAllText filePath
        with _ -> 
            writer.Write(filePath, defaultCodeOnFirstRun)// create file so it can be found and edited manually
            defaultCodeOnFirstRun
            