namespace Seff.Config

open System
open System.Text
open System.Collections.Generic

open FsEx.Wpf

open Seff.Model

// TODO replace this with full nuget and local autocompelet on #r "...

/// A class to hold the previously loaded assemble references for auto completions
type AssemblyReferenceStatistic  ( hostInfo:Hosting) = 


    let filePath0 = hostInfo.GetPathToSaveAppData("AssemblyReferenceStatistic.txt")

    let writer = SaveReadWriter(filePath0,ISeffLog.printError)

    let assRefStats = 
        let set = HashSet<string>() //  full path
        async{
            writer.CreateFileIfMissing("")  |> ignore
            match writer.ReadAllLines() with
            |None -> ()
            |Some lns ->
                for ln in lns do
                    if IO.File.Exists ln then
                        set.Add (ln) |> ignore
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
        writer.WriteIfLast (assRefStatsAsString, 500)

    member this.Add s =  // used as delegate to fsiSession.AssemblyReferenceAdded event
        assRefStats.Add (s)  |> ignore

