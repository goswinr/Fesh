namespace Seff.Config

open System
open System.IO
open System.Text
open System.Collections.Generic

open FsEx.Wpf

open Seff
open Seff.Model

type FileToOpen = {file:FileInfo; makeCurrent:bool}

/// files that are open when closing the editor window, for next restart
type OpenTabs  (runContext:RunContext, startupArgs:string[]) = 

    let filePath0 = runContext.GetPathToSaveAppData("CurrentlyOpenFiles.txt")
    let writer = SaveReadWriter(filePath0,ISeffLog.printError)

    let currentTabPreFix = "*Current tab:* " //a string that can never be part of a filename

    let mutable allFiles:seq<FileInfo> = Seq.empty

    let mutable currentFile:FilePath = NotSet

    let filesInArgs = startupArgs |> Array.filter File.Exists

    let files = 
        let files = ResizeArray()
        let dup =  HashSet()
        let mutable curr =""
        writer.CreateFileIfMissing("")  |> ignore

        // If ther are file in the startup args only open those, not the previously open files.
        // This is to avoid openinh the same files twice.
        // One instance of Seff might be open with some files.
        // If the user then double clicks another fsx file it would open a new instance of Seff with this fsx file, 
        // but also all the others that are already in the first instance of Seff open.
        if filesInArgs.Length > 0 then 
            // parse startup args
            for path in filesInArgs do                
                let lc = path.ToLowerInvariant()
                curr <- lc
                if not <| dup.Contains (lc) then
                    dup.Add (lc)  |> ignore
                    files.Add (FileInfo path)
        else
            match writer.ReadAllLines() with
            |None -> ()
            |Some lns ->
                if lns.Length > 1 then
                    curr <- lns.[0].Replace(currentTabPreFix,"").ToLowerInvariant() // first line is filepath and name for current tab (repeats below)
                    for path in lns |> Seq.skip 1  do // skip first line of current info
                        let fi = FileInfo(path)
                        if fi.Exists then
                            files.Add fi
                            dup.Add (path.ToLowerInvariant())  |> ignore
        [|
        for fi in files do
            let lowerc = fi.FullName.ToLowerInvariant()
            {file= fi; makeCurrent = lowerc.Equals(curr, StringComparison.Ordinal)}
        |]

    let getText() = 
        let curr = match currentFile with NotSet ->"*No current tab*" |SetTo fi -> currentTabPreFix + fi.FullName
        let sb = StringBuilder()
        sb.AppendLine(curr) |> ignore // first line is filepath and name for current tab (repeats below)
        for f in allFiles do
            sb.AppendLine(f.FullName) |> ignore
        sb.ToString()
    
    /// saves async with delay
    member this.Save (currentFileO:FilePath , allFilesO: seq<FileInfo>) = 
        currentFile<-currentFileO
        allFiles<-allFilesO        
        writer.WriteIfLast (getText ,500)
    
    //saves immediately in sync
    member this.SaveSync (currentFileO:FilePath , allFilesO: seq<FileInfo>) = 
        currentFile<-currentFileO
        allFiles<-allFilesO        
        IO.File.WriteAllText(filePath0, getText(),Text.Encoding.UTF8)


    /// second item in tuple indicates current tab
    /// ensures that ther is only one file to make current
    member this.Get() = files

