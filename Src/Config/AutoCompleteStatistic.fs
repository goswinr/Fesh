namespace Seff.Config

open System
open System.Text
open System.Collections.Generic

open FsEx.Wpf

open Seff
open Seff.Model

/// A class to hold the statistic of most used toplevel auto completions
type AutoCompleteStatistic  ( runContext:RunContext) = 

    let customPriorities = [
        // first item wil have highest priority
        "true"
        "false"
        "printfn"
        "sprintf"
        "eprintfn"
        "failwithf"
        ]

    let  sep = '=' // key value separator like in ini files

    let filePath0 = runContext.GetPathToSaveAppData("AutoCompleteStatistic.txt")

    let writer = SaveReadWriter(filePath0,ISeffLog.printError)

    let completionStats = 
        let dict=Collections.Concurrent.ConcurrentDictionary<string,float>()
        async{
            writer.CreateFileIfMissing("")  |> ignore
            match writer.ReadAllLines() with
            |None -> ()
            |Some lns ->
                for ln in lns do
                    match ln.Split(sep) with
                    | [|k;v|] -> dict.[k] <- float v // TODO allow for comments? use ini format ??
                    | _       -> ISeffLog.log.PrintfnAppErrorMsg "Bad line in CompletionStats file : '%s'" ln

            // add custom priorities
            customPriorities |> List.iteri ( fun i s -> dict.[s] <- 999. - float i  )// decrement priority while iterating

            } |> Async.Start
        dict

    let completionStatsAsString () = 
        let sb = StringBuilder()
        for KeyValue(k,v) in completionStats |> Seq.sortByDescending (fun (KeyValue(k,v)) -> v) |> Seq.truncate 500  do // biggest number first, max 500 words
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
        writer.WriteIfLast ( completionStatsAsString, 500)
