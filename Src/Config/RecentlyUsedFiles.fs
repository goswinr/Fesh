namespace Seff.Config

open Seff
open System
open System.Text
open System.IO
open System.Collections.ObjectModel

   
type RecentlyUsedFiles  (log:ISeffLog, hostInfo:HostingInfo) =
    let writer = SaveWriter(log)
        
    let filePath = hostInfo.GetPathToSaveAppData("RecentlyUsedFiles.txt")
        
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
            log.PrintInfoMsg "No recently used files found. (This is expected on first use of the App)"  
                  
        stack   

        
    /// the maximum number of recxent files to be saved
    /// the amount of files in the recently used menu can be controlled separetly in menu.fs
    let maxCount = 70       
                

    let getStringRaiseEvent() = 
        let sb = StringBuilder()
        let Dup = Collections.Generic.HashSet()
        let k = ref 0
        for fi in recentFilesStack  do   // iteration starts at top element of stack
            incr k
            if !k < maxCount then 
                if not <| Dup.Contains fi.FullName then 
                    sb.AppendLine(fi.FullName)  |> ignore
                    Dup.Add fi.FullName  |> ignore 

        recentFilesChangedEv.Trigger()  //this event will be triggered 1000 ms after new tabs are created
        sb.ToString()    

    member this.AddAndSave(fi:FileInfo) =         
        recentFilesStack.Push fi 
        //log.PrintDebugMsg "add recent file %s" fi.FullName
        writer.WriteDelayed(filePath, getStringRaiseEvent, 1000)

    member this.Save() =         
        writer.WriteDelayed(filePath, getStringRaiseEvent, 1000)


    /// does not save 
    member this.Add(fi:FileInfo) =         
        recentFilesStack.Push fi
        
    /// the first elemnt in this array the top of stack
    member this.Get() = Array.ofSeq recentFilesStack

   
    [<CLIEvent>]
    /// this even is raised from UI thread 
    /// used to update Menu
    member this.OnRecentFilesChanged = recentFilesChangedEv.Publish 