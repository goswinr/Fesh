namespace Seff.Config

open Seff
open Seff.Model
open System
open System.Text
open System.Collections.Generic
open System.IO
open ICSharpCode.AvalonEdit.Folding
   
/// A class to hold the statistic of most used toplevel auto completions
type FoldingStatus (log:ISeffLog, hostInfo:Hosting, recentlyUsedFiles:RecentlyUsedFiles) =
    
    let writer = SaveWriter(log)
        
    let  sep = '|' // separator 
    
    let filePath = hostInfo.GetPathToSaveAppData("FoldingStatus.txt")
    
    let mutable waitingForFileRead =  true

    let foldingStatus = 
        let dict=Dictionary<string,bool []>() 
        async{
            try            
                if IO.File.Exists filePath then 
                    for ln in  IO.File.ReadAllLines filePath do
                    match ln.Split(sep) with
                    | [|k;v|] -> 
                        //log.PrintfnDebugMsg "%s for %s" v k
                        let vs = Seq.map ( fun c -> if c = '0' then false elif c = '1' then true else failwithf "bad char %c in FoldingStatus" c) v |> Array.ofSeq
                        dict.[k] <- vs 
                    | _ -> log.PrintfnAppErrorMsg "Bad line in FoldingStatus file : '%s'" ln 
                waitingForFileRead <- false
            with e -> 
                waitingForFileRead <- false
                log.PrintfnAppErrorMsg "Error load FoldingStatus: %A"   e
            } |> Async.Start
        dict

    let foldingStatusAsString () = 
        let sb = StringBuilder() 
        for KeyValue(k,v) in foldingStatus do
            if recentlyUsedFiles.Contains(k)then 
                let vs= v |> Seq.map ( fun b -> if b then "1" else "0") |> String.concat ""
                sb.Append(k).Append(sep).AppendLine(vs) |> ignore
        sb.ToString() 
    
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

    member this.Set(ed:IEditor) =
        match ed.FilePath with
        | NotSet -> ()
        | SetTo fi -> 
            let vs = [| for f in ed.FoldingManager.AllFoldings do f.IsFolded |]
            foldingStatus.[fi.FullName] <- vs
            foldingStatus.[fi.Name] <- vs
            writer.WriteDelayed (filePath, foldingStatusAsString, 800)
        
    member this.Save() =
        writer.WriteDelayed (filePath, foldingStatusAsString, 800)