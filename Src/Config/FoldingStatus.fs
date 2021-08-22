﻿namespace Seff.Config

open System
open System.Text
open System.Collections.Generic
open System.IO

open AvalonEditB.Folding

open FsEx.Wpf

open Seff
open Seff.Model

   
/// A class to hold the statistic of most used toplevel auto completions
type FoldingStatus ( hostInfo:Hosting, recentlyUsedFiles:RecentlyUsedFiles) =
            
    let  sep = '|' // separator 
    
    let filePath0 = hostInfo.GetPathToSaveAppData("FoldingStatus.txt")
    
    let writer = SaveReadWriter(filePath0,ISeffLog.printError)

    let mutable waitingForFileRead =  true

    let foldingStatus = 
        let dict=Dictionary<string,bool []>() 
        async{
            try            
                if writer.FileExists then
                    for ln in writer.ReadAllLines().Value do
                        match ln.Split(sep) with
                        | [|k;v|] -> 
                            //log.PrintfnDebugMsg "%s for %s" v k
                            let vs = Seq.map ( fun c -> if c = '0' then false elif c = '1' then true else failwithf "bad char %c in FoldingStatus" c) v |> Array.ofSeq
                            dict.[k] <- vs 
                        | _ -> ISeffLog.log.PrintfnAppErrorMsg "Bad line in FoldingStatus file : '%s'" ln 
                waitingForFileRead <- false
            with e ->
                waitingForFileRead <- false
                ISeffLog.log.PrintfnAppErrorMsg "Error load FoldingStatus: %A"   e
            } |> Async.Start
        dict

    let foldingStatusAsString () = 
        let sb = StringBuilder() 
        for KeyValue(k,v) in foldingStatus do
            if recentlyUsedFiles.Contains(k)then // to limit number of files and remove files that dont exist anymore ?
                let vs= v |> Seq.map ( fun b -> if b then "1" else "0") |> String.concat ""
                sb.Append(k).Append(sep).AppendLine(vs) |> ignore
        sb.ToString() 
    
    /// to indicate end of async reading
    member this.WaitingForFileRead = waitingForFileRead

    member this.Get(ed:IEditor) =
        match ed.FilePath with
        | NotSet -> [| |]
        | SetTo fi -> 
            match foldingStatus.TryGetValue fi.FullName with
            |true,vs -> vs
            |_      -> 
                match foldingStatus.TryGetValue fi.Name with
                |true,vs -> vs
                |_      -> [| |]    

    member this.Set(ed:IEditor) = // gets call on every new folds found
        match ed.FilePath with
        | NotSet -> ()
        | SetTo fi -> 
            let vs = [| for f in ed.FoldingManager.AllFoldings do f.IsFolded |]
            let ok, curr = foldingStatus.TryGetValue fi.Name
            if not ok || curr <> vs then // only update if ther are changes or setting is missing.
                foldingStatus.[fi.Name] <- vs
                foldingStatus.[fi.FullName] <- vs
                writer.WriteIfLast (foldingStatusAsString, 800)
        
    member this.Save() =
        writer.WriteIfLast ( foldingStatusAsString, 800)