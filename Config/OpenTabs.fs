namespace Seff.Config

open Seff
open System
open System.IO
open System.Text

/// files that are open when closing the editor window, for next restart
type OpenTabs  (log:ISeffLog, adl:AppDataLocation) = 
    let writer = SaveWriter(log)
    
    let filePath = adl.GetFilePath("CurrentlyOpenFiles.txt")

    let currentTabPreFix =  "*Current tab:* " //a string that can never be part of a filename

    let mutable allFiles:seq<FileInfo option> = Seq.empty

    let mutable currentFile:FileInfo option = None

    let files = 
        let files = ResizeArray()
        try            
            if IO.File.Exists filePath then 
                let lns = IO.File.ReadAllLines filePath 
                if lns.Length > 1 then 
                    let currentFile = (lns |> Seq.head).Replace(currentTabPreFix,"").ToLowerInvariant() // first line is filepath and name for current tab (repeats below)
                    for path in lns |> Seq.skip 1  do 
                        let fi = FileInfo(path)                    
                        if fi.Exists then 
                            let lPath = path.ToLowerInvariant()                            
                            let makeCurrent = lPath = currentFile 
                            files.Add((fi,makeCurrent))            
        with e -> 
            log.PrintAppErrorMsg "Error getFilesfileOnClosingOpen: %s"  e.Message
        files

    let getText() = 
        let curr = if currentFile.IsSome then currentTabPreFix + currentFile.Value.FullName else "*No current tab*"
        let sb = StringBuilder()
        sb.AppendLine(curr) |> ignore // first line is filepath and name for current tab (repeats below)
        for f in allFiles do 
            if f.IsSome then sb.AppendLine(f.Value.FullName) |> ignore   
        sb.ToString()

    member this.Save (currentFileO:FileInfo option , allFilesO: seq<FileInfo option>) =         
        currentFile<-currentFileO
        allFiles<-allFilesO
        writer.WriteDelayed  (filePath, getText ,500)
      
    /// second item in tuple indicates current tab
    member this.Get = files
