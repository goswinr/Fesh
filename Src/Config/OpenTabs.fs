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
type OpenTabs  (hostInfo:Hosting, startupArgs:string[]) = 
    
    let filePath0 = hostInfo.GetPathToSaveAppData("CurrentlyOpenFiles.txt")
    let writer = SaveReadWriter(filePath0,ISeffLog.printError)

    let currentTabPreFix =  "*Current tab:* " //a string that can never be part of a filename

    let mutable allFiles:seq<FileInfo> = Seq.empty

    let mutable currentFile:FilePath = NotSet

    let files = 
        let files = ResizeArray()
        let dup =  HashSet()
        let mutable curr ="" 
        writer.CreateFileIfMissing("")  |> ignore        
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

        // parse startup args
        for path in startupArgs do
            let fi = FileInfo(path)
            if fi.Exists then 
                let lc = path.ToLowerInvariant()
                curr <- lc
                if not <| dup.Contains (lc) then 
                    dup.Add (lc)  |> ignore
                    files.Add fi      
        
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

    member this.Save (currentFileO:FilePath , allFilesO: seq<FileInfo>) =         
        currentFile<-currentFileO
        allFiles<-allFilesO
        //log.PrintfnDebugMsg "Save tabs %A, curent %A" allFiles currentFile
        writer.WriteIfLast  ( getText ,500)
      
    /// second item in tuple indicates current tab
    /// ensures that ther is only one file to make current
    member this.Get() = files
      