namespace Fesh.Config

open System
open System.Text
open System.Globalization
open System.Collections.Concurrent
open Fittings
open Fesh.Model

/// A class to hold the statistic of the most used NuGet packages, for the completion list after  #r "nuget:
/// A package counts as used when it gets picked from the completion list,
/// and when an evaluation of code with its  #r "nuget: ..."  line completed without errors.
type NugetStatistic (runContext:RunContext) =

    let sep = '=' // key value separator like in ini files, NuGet package ids can't contain it

    let filePath0 = runContext.GetPathToSaveAppData("NuGet-Statistic.txt")

    let writer = SaveReadWriter(filePath0, IFeshLog.printError)

    /// Keyed by the lowercase package id, because package ids are case insensitive.
    /// The value has the casing of the latest use.
    let stats =
        let dict = ConcurrentDictionary<string, struct(string * int)>()
        async{
            writer.CreateFileIfMissing("")  |> ignore
            match writer.ReadAllLines() with
            |None -> ()
            |Some lns ->
                for ln in lns do
                    if not (String.IsNullOrWhiteSpace ln) then
                        match ln.Split(sep) with
                        | [|k;v|] ->
                            match Int32.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture) with
                            | true, n -> dict.[k.ToLowerInvariant()] <- struct(k, n)
                            | _       -> IFeshLog.log.PrintfnAppErrorMsg "Bad line in NuGet-Statistic file : '%s'" ln
                        | _ -> IFeshLog.log.PrintfnAppErrorMsg "Bad line in NuGet-Statistic file : '%s'" ln
            } |> Async.Start
        dict

    let statsAsString () =
        let sb = StringBuilder()
        for struct(name, n) in stats.Values |> Seq.sortByDescending (fun struct(_, n) -> n) |> Seq.truncate 500 do // biggest number first, max 500 packages
            sb.Append(name).Append(sep).AppendLine(n.ToString(CultureInfo.InvariantCulture)) |> ignore
        sb.ToString()

    /// All used packages with their count, the most used first.
    member this.Packages : struct(string * int)[] =
        stats.Values |> Seq.toArray |> Array.sortByDescending (fun struct(_, n) -> n)

    /// Increase the count by 1, and keep the casing of this name.
    member this.Incr(name:string) =
        stats.AddOrUpdate(name.ToLowerInvariant(), struct(name, 1), fun _ struct(_, n) -> struct(name, n + 1)) |> ignore

    member this.Save() =
        writer.WriteIfLast ( statsAsString, 500)
