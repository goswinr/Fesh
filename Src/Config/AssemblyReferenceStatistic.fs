namespace Seff.Config

open Seff.Model
open System
open System.Text
open System.Collections.Generic

           
/// A class to hold the previously loaded assemble refrences for auto completions
type AssemblyReferenceStatistic  (log:ISeffLog, hostInfo:Hosting) =
    let writer = SaveWriter(log)
        
    let filePath = hostInfo.GetPathToSaveAppData("AssemblyReferenceStatistic.txt")
        
    let assRefStats = 
        let set = HashSet<string>() //  full path
        async{
            try            
                if IO.File.Exists filePath then 
                    for ln in  IO.File.ReadAllLines filePath do
                        if IO.File.Exists ln then 
                            set.Add (ln) |> ignore
            with e -> 
                log.PrintfnAppErrorMsg "Error load assRefStatsStats: %A" e
            } |> Async.Start 
        set

    let assRefStatsAsString () = 
        let sb = StringBuilder() 
        for fullPath in assRefStats do sb.AppendLine(fullPath) |> ignore
        sb.ToString() 

    let filePath = hostInfo.GetPathToSaveAppData("AssemblyReferenceStatistic.txt")
                     
    member this.Get = assRefStats
    
    /// Checks if DDl file exists 
    /// Retuens dict of filename and full path
    member this.GetChecked = 
        let D = Dictionary()
        for fullPath in assRefStats do 
            if IO.File.Exists(fullPath) then
                D.[IO.Path.GetFileName fullPath] <- fullPath
        D
           
    member this.Save() =
        writer.WriteDelayed (filePath,assRefStatsAsString, 500)
            
    member this.Add s =  // used as delegate to fsiSession.AssemblyReferenceAdded event
        assRefStats.Add (s)  |> ignore 
   