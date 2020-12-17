namespace Seff.Config

open Seff
open Seff.Model
open System
open System.Text
open System.Collections.Generic

   
/// A class to hold the statistic of most used toplevel auto completions
type AutoCompleteStatistic  (log:ISeffLog, hostInfo:Hosting) =
    let writer = SaveWriter(log)
    
    let customPriorities = [ // fist item wil have higest prority
        "true"
        "false"
        "printfn"
        "sprintf"
        "eprintfn" 
        "failwithf"
        ]
        


    let  sep = '=' // key value separatur like in ini files
    
    let filePath = hostInfo.GetPathToSaveAppData("AutoCompleteStatistic.txt")
    
    let completionStats = 
        let dict=Collections.Concurrent.ConcurrentDictionary<string,float>() 
        async{
            try            
                if IO.File.Exists filePath then 
                    for ln in  IO.File.ReadAllLines filePath do
                    match ln.Split(sep) with
                    | [|k;v|] -> dict.[k] <- float v // TODO allow for comments? use ini format ??
                    | _       -> log.PrintfnAppErrorMsg "Bad line in CompletionStats file : '%s'" ln                   
            with e -> 
                log.PrintfnAppErrorMsg "Error load fileCompletionStats: %A"   e
            
            customPriorities
            |> List.iteri ( fun i s -> dict.[s] <- 999. - float i  )// decrement priority while iterating  
            
            } |> Async.Start 
        dict

    let completionStatsAsString () = 
        let sb = StringBuilder() 
        for KeyValue(k,v) in completionStats |> Seq.sortByDescending (fun (KeyValue(k,v)) -> v) |> Seq.truncate 500  do // biggets number first, max 500 words
            sb.Append(k).Append(sep).AppendLine(v.ToString()) |> ignore
        sb.ToString() 
     
    member this.Get(key) =
        match completionStats.TryGetValue key with
        |true,i -> i
        |_      -> 0.0
    
    /// increase by 1.0
    member this.Incr(key) =
        match completionStats.TryGetValue key with
        |true,i -> completionStats.[key] <- i +  1.0
        |_      -> completionStats.[key] <- 1.0
    
    member this.Save() =
        writer.WriteDelayed (filePath, completionStatsAsString, 500)