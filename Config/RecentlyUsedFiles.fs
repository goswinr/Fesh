namespace Seff.Config

open Seff
open System
open System.Text
open System.IO

   
type RecentlyUsedFiles  (log:ISeffLog, adl:AppDataLocation) =
    let writer = SaveWriter(log)
        
    let filePath = adl.GetFilePath("RecentlyUsedFiles.txt")
        
    let recentFilesChangedEv = new Event<unit>()
        
    let recentFilesStack = 
        // TODO this could be done async too?
        
        //let stack = new Collections.Concurrent.ConcurrentStack<FileInfo>()// might contain even files that dont exist(on a currently detached drive)
        let stack = Collections.Generic.Stack<FileInfo>()
        try            
            //if IO.File.Exists filePath then // do this check only when creating menu items
            for ln in  IO.File.ReadAllLines filePath |> Seq.rev do
                stack.Push(FileInfo(ln)) |> ignore                    
        with e -> 
            log.PrintAppErrorMsg "Error load RecentlyUsedFiles: %s"   e.Message
                  
        stack   

        
    /// the maximum number of recxent files to be saved
    /// the amount of files in the recently used menu can be controlled separetly
    let maxCount = 70       
                

    let getStringRaiseEvent() = 
        let sb = StringBuilder()
        for fi in recentFilesStack |> Seq.truncate maxCount do   // iteration starts at top element of stack
            sb.AppendLine(fi.FullName)  |> ignore
        recentFilesChangedEv.Trigger()  //this event will be triggered 1000 ms after new tabs are created
        sb.ToString()    

    member this.Save(fi:FileInfo) =         
            recentFilesStack.Push fi 
            writer.WriteDelayed(filePath, getStringRaiseEvent, 1000)
        
    /// the first elemnt in this array the top of stack
    member this.Get() = Array.ofSeq recentFilesStack

   
    [<CLIEvent>]
    /// this even is raised from UI thread 
    /// used to update Menu
    member this.OnRecentFilesChanged = recentFilesChangedEv.Publish 