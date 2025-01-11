namespace Fesh.Config


open System.Text
open System.Collections.Generic
open Fittings
open Fesh.Model
open AvalonEditB


/// A class to hold the folding status for all recently used files
type FoldingStatus ( runContext:RunContext, recentlyUsedFiles:RecentlyUsedFiles) =

    let  sep = '|' // separator

    let filePath0 = runContext.GetPathToSaveAppData("Folding-States.txt")

    let writer = SaveReadWriter(filePath0,IFeshLog.printError)

    let mutable waitingForFileRead =  true

    let foldingStatus =
        let dict=Dictionary<string,bool []>()
        async{
            try
                writer.CreateFileIfMissing("")  |> ignore
                match writer.ReadAllLines() with
                |None -> ()
                |Some lns ->
                    for ln in lns do
                        match ln.Split(sep) with
                        | [|k;v|] ->
                            //log.PrintfnDebugMsg "%s for %s" v k
                            let vs = Seq.map ( fun c -> if c = '0' then false elif c = '1' then true else failwithf "bad char %c in FoldingStatus" c) v |> Array.ofSeq
                            dict.[k] <- vs
                        | _ -> IFeshLog.log.PrintfnAppErrorMsg "Bad line in FoldingStatus file : '%s'" ln
                waitingForFileRead <- false
            with e ->
                waitingForFileRead <- false
                IFeshLog.log.PrintfnIOErrorMsg $"reading folding status failed with\r\n {e}"
            } |> Async.Start
        dict

    let foldingStatusAsString () =
        let sb = StringBuilder()
        for KeyValue(k,v) in foldingStatus do
            if recentlyUsedFiles.Contains(k)then // to limit number of files and remove files that don't exist anymore ?
                let vs= v |> Seq.map ( fun b -> if b then "1" else "0") |> String.concat ""
                sb.Append(k).Append(sep).AppendLine(vs) |> ignore
        sb.ToString()

    /// to indicate end of async reading
    member this.WaitingForFileRead = waitingForFileRead

    member this.Get(path:FilePath) =
        match path with
        | NotSet _ -> [| |]
        | Deleted fi | SetTo fi ->
            match foldingStatus.TryGetValue fi.FullName with
            |true,vs -> vs
            |_      ->
                match foldingStatus.TryGetValue fi.Name with // just in case the file moved folder
                |true,vs -> vs
                |_      -> [| |]

    member this.Set(path:FilePath, manager:Folding.FoldingManager) = // gets call on every new folds found
        match path with
        | NotSet _ -> ()
        | Deleted fi | SetTo fi ->
            let vs = [| for f in manager.AllFoldings do f.IsFolded |]
            let ok, curr = foldingStatus.TryGetValue fi.FullName
            if not ok || curr <> vs then // only update if there are changes or setting is missing.
                foldingStatus.[fi.FullName] <- vs
                foldingStatus.[fi.Name]     <- vs // so that it still works in case the file moves folder
                writer.WriteIfLast (foldingStatusAsString, 800)

    member this.Save() =
        writer.WriteIfLast ( foldingStatusAsString, 800)
