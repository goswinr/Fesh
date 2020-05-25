namespace Seff.Config

open Seff
open System
open System.Text
open System.Collections.Generic

           
/// A class to hold the previously loaded assemble refrences for auto completions
type AssemblyReferenceStatistic  (log:ISeffLog, hostInfo:HostingInfo) =
    let writer = SaveWriter(log)
        
    let filePath = hostInfo.GetPathToSaveAppData("AssemblyReferenceStatistic.txt")
        
    let assRefStats = 
        let set = HashSet<string>() 
        async{
            try            
                if IO.File.Exists filePath then 
                    for ln in  IO.File.ReadAllLines filePath do
                        //TODO verify path exists
                        set.Add(ln) |> ignore
            with e -> 
                log.PrintAppErrorMsg "Error load assRefStatsStats: %A" e
            } |> Async.Start 
        set

    let assRefStatsAsString () = 
        let sb = StringBuilder() 
        for v in assRefStats do sb.AppendLine(v.ToString()) |> ignore
        sb.ToString() 

    let filePath = hostInfo.GetPathToSaveAppData("AssemblyReferenceStatistic.txt")
                     
    member this.Get = assRefStats
            
    member this.Save() =
        writer.WriteDelayed (filePath,assRefStatsAsString, 500)
        
    /// used as event delegate in log.print ?
    member this.RecordFromlog = 
        fun (s:string) -> 
            if s.Contains "--> Referenced '" then // e.g.: --> Referenced 'C:\Program Files\Rhino 6\System\RhinoCommon.dll' (file may be locked by F# Interactive process)
                let start = s.IndexOf(''') 
                if start > -1 then 
                    let ende = s.IndexOf(''', start + 2)
                    if ende > start + 3 then
                        let r = s.Substring(start + 1, ende - 1)
                        assRefStats.Add (r)  |> ignore 
   